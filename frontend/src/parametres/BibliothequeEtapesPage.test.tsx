import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { api } from '../api';
import { AuthContext } from '../auth';
import { utilisateur } from '../test/fabrique';
import type { ModeleEtape } from '../types';
import { TooltipProvider } from '../ui/Tooltip';
import BibliothequeEtapesPage from './BibliothequeEtapesPage';

const modele = (p: Partial<ModeleEtape> & Pick<ModeleEtape, 'id' | 'action'>): ModeleEtape => ({
  systemeCatalogueIds: ['c-bdd'],
  type: 'Standard',
  criticite: 'Moderee',
  dureeEstimeeMinutes: 10,
  instructions: '```\npg_dump appli\n```',
  resultatAttendu: 'Fichier créé',
  fin: 'TestUnitaire',
  sauvegardeEmplacement: '',
  sauvegardeMethodeRecuperation: '',
  resolutionIds: [],
  utilisations: 0,
  creeLe: '2026-01-01T00:00:00Z',
  version: 2,
  modifieLe: '2026-02-01T00:00:00Z',
  modifieParId: 'u2',
  modifiePar: 'Bob',
  ...p,
});

const MODELES = [
  modele({
    id: 'm1',
    action: 'Sauvegarde complète de la base',
    type: 'Sauvegarde',
    utilisations: 14,
    sauvegardeEmplacement: '/sauvegardes',
  }),
  modele({ id: 'm2', action: 'Exécution des scripts de migration', utilisations: 19 }),
  modele({ id: 'm3', action: 'Contrôle de bout en bout', systemeCatalogueIds: ['c-bdd', 'c-web'], utilisations: 3 }),
  modele({ id: 'm4', action: 'Purge du cache', systemeCatalogueIds: ['c-web'] }),
];

vi.mock('../api', () => ({
  api: {
    etapesModeles: vi.fn(),
    catalogueSystemes: vi.fn(async () => [
      { id: 'c-web', nom: 'WEB', description: 'Portail public', cible: '', actif: true },
      { id: 'c-bdd', nom: 'BDD', description: 'Base PostgreSQL', cible: '', actif: true },
      { id: 'c-ldap', nom: 'LDAP', description: '', cible: '', actif: true },
    ]),
    resolutions: vi.fn(async () => []),
    creerEtapeModele: vi.fn(),
    modifierEtapeModele: vi.fn(),
    supprimerEtapeModele: vi.fn(),
  },
}));

function rendre(estAdmin = false, url = '/parametres/etapes') {
  render(
    <MemoryRouter initialEntries={[url]}>
      <AuthContext.Provider value={{ utilisateur: utilisateur({ estAdmin }) } as never}>
        <TooltipProvider>
          <Routes>
            <Route path="/parametres/etapes" element={<BibliothequeEtapesPage />} />
          </Routes>
        </TooltipProvider>
      </AuthContext.Provider>
    </MemoryRouter>,
  );
}

const titres = () => screen.getAllByRole('heading', { level: 2 }).map((h) => h.textContent);

describe('Bibliothèque d’étapes', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(api.etapesModeles).mockResolvedValue(MODELES);
  });

  it('s’ouvre sur le choix du système : ordre alphabétique, nombre d’étapes et répartition par type', async () => {
    rendre();
    const bdd = await screen.findByRole('link', { name: 'BDD : 3 étapes' });
    expect(bdd).toHaveTextContent('2 normales · 1 sauvegarde');
    expect(bdd).toHaveTextContent('Base PostgreSQL');
    const liens = screen.getAllByRole('link', { name: /: \d+ étape/ }).map((l) => l.getAttribute('aria-label'));
    // Une étape qui touche plusieurs systèmes compte dans chacun.
    expect(liens).toEqual(['BDD : 3 étapes', 'LDAP : 0 étape', 'WEB : 2 étapes']);
    expect(bdd).toHaveAttribute('href', '/parametres/etapes?systeme=BDD');
  });

  it('recherche « BDD sauvegarde » depuis l’accueil : système reconnu, mot cherché dans le nom ou le type', async () => {
    rendre();
    await screen.findByRole('link', { name: 'BDD : 3 étapes' });
    await userEvent.type(screen.getByLabelText('Rechercher dans la bibliothèque'), 'BDD sauvegarde');
    const lecture = screen.getByRole('status');
    expect(lecture).toHaveTextContent('1 étape modèle');
    expect(lecture).toHaveTextContent('système BDD');
    expect(titres()).toEqual(['Sauvegarde complète de la base']);
  });

  it('bibliothèque d’un système : triée par utilisations, filtre par type, compteur sur chaque carte', async () => {
    rendre(false, '/parametres/etapes?systeme=BDD');
    expect(await screen.findByRole('heading', { level: 1 })).toHaveTextContent('Bibliothèque d’étapes · BDD');
    await waitFor(() =>
      expect(titres()).toEqual([
        'Exécution des scripts de migration',
        'Sauvegarde complète de la base',
        'Contrôle de bout en bout',
      ]),
    );
    expect(screen.getByText('19 MEP')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /tous les systèmes/ })).toHaveAttribute('href', '/parametres/etapes');
    await userEvent.selectOptions(screen.getByLabelText('Filtrer par type d’étape'), 'Sauvegarde');
    expect(titres()).toEqual(['Sauvegarde complète de la base']);
  });

  it('crée une étape modèle depuis la bibliothèque d’un système (système proposé, jamais de point de non-retour)', async () => {
    vi.mocked(api.creerEtapeModele).mockResolvedValue(modele({ id: 'm9', action: 'Vacuum' }));
    rendre(false, '/parametres/etapes?systeme=BDD');
    await userEvent.click(await screen.findByRole('button', { name: 'Nouvelle étape modèle' }));
    const dialogue = await screen.findByRole('dialog');
    expect(within(dialogue).getByRole('button', { name: 'BDD', pressed: true })).toBeInTheDocument();
    await userEvent.type(within(dialogue).getByLabelText('Nom de l’étape modèle *'), 'Vacuum');
    await userEvent.click(within(dialogue).getByRole('button', { name: 'WEB', pressed: false }));
    await userEvent.selectOptions(within(dialogue).getByLabelText('Type d’étape'), 'Sauvegarde');
    await userEvent.type(within(dialogue).getByLabelText('Emplacement de la sauvegarde'), '/sauv');
    await userEvent.click(within(dialogue).getByRole('button', { name: 'Enregistrer' }));
    await waitFor(() =>
      expect(api.creerEtapeModele).toHaveBeenCalledWith(
        expect.objectContaining({
          action: 'Vacuum',
          systemeCatalogueIds: ['c-web', 'c-bdd'],
          type: 'Sauvegarde',
          criticite: 'Moderee',
          sauvegardeEmplacement: '/sauv',
        }),
      ),
    );
    expect(api.creerEtapeModele).toHaveBeenCalledWith(
      expect.not.objectContaining({ pointDeNonRetour: expect.anything() }),
    );
  });

  it('refuse une étape modèle sans système', async () => {
    rendre();
    await userEvent.click(await screen.findByRole('button', { name: 'Nouvelle étape modèle' }));
    const dialogue = await screen.findByRole('dialog');
    await userEvent.type(within(dialogue).getByLabelText('Nom de l’étape modèle *'), 'Vacuum');
    await userEvent.click(within(dialogue).getByRole('button', { name: 'Enregistrer' }));
    expect(within(dialogue).getByRole('alert')).toHaveTextContent('Choisissez au moins un système du catalogue.');
    expect(api.creerEtapeModele).not.toHaveBeenCalled();
  });

  it('duplique (nouvelle étape modèle) et modifie (avec la version)', async () => {
    vi.mocked(api.creerEtapeModele).mockResolvedValue(modele({ id: 'm9', action: 'copie' }));
    vi.mocked(api.modifierEtapeModele).mockResolvedValue(modele({ id: 'm2', action: 'x' }));
    rendre(false, '/parametres/etapes?systeme=BDD');
    await userEvent.click(await screen.findByRole('button', { name: 'Dupliquer Exécution des scripts de migration' }));
    let dialogue = await screen.findByRole('dialog');
    expect(within(dialogue).getByLabelText('Nom de l’étape modèle *')).toHaveValue(
      'Exécution des scripts de migration (copie)',
    );
    await userEvent.click(within(dialogue).getByRole('button', { name: 'Enregistrer' }));
    await waitFor(() =>
      expect(api.creerEtapeModele).toHaveBeenCalledWith(
        expect.objectContaining({
          action: 'Exécution des scripts de migration (copie)',
          systemeCatalogueIds: ['c-bdd'],
        }),
      ),
    );
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());

    await userEvent.click(screen.getByRole('button', { name: 'Modifier Exécution des scripts de migration' }));
    dialogue = await screen.findByRole('dialog');
    await userEvent.click(within(dialogue).getByRole('button', { name: 'Enregistrer' }));
    await waitFor(() =>
      expect(api.modifierEtapeModele).toHaveBeenCalledWith(
        'm2',
        expect.objectContaining({ version: 2, action: 'Exécution des scripts de migration' }),
      ),
    );
  });

  it('seul un administrateur supprime une étape modèle', async () => {
    rendre(false, '/parametres/etapes?systeme=WEB');
    await screen.findByText('Purge du cache');
    expect(screen.queryByRole('button', { name: /Supprimer/ })).toBeNull();
  });

  it('un administrateur supprime après confirmation', async () => {
    vi.mocked(api.supprimerEtapeModele).mockResolvedValue(undefined);
    rendre(true, '/parametres/etapes?systeme=WEB');
    await userEvent.click(await screen.findByRole('button', { name: 'Supprimer Purge du cache' }));
    const confirmation = await screen.findByRole('dialog');
    await userEvent.click(within(confirmation).getByRole('button', { name: 'Supprimer' }));
    await waitFor(() => expect(api.supprimerEtapeModele).toHaveBeenCalledWith('m4'));
  });
});
