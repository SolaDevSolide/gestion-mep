import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { api } from '../api';
import { AuthContext } from '../auth';
import { utilisateur } from '../test/fabrique';
import type { ModeleRecette } from '../types';
import { TooltipProvider } from '../ui/Tooltip';
import BibliothequeRecettesPage from './BibliothequeRecettesPage';

const modele = (
  id: string,
  titre: string,
  systemeCatalogueId: string,
  instructions: string,
  utilisations = 0,
  creeLe = '2026-01-01T00:00:00Z',
): ModeleRecette => ({
  id,
  titre,
  systemeCatalogueId,
  etapes: [{ ordre: 1, instructions, resultatAttendu: 'OK' }],
  version: 3,
  modifieLe: '2026-10-01T10:00:00Z',
  modifieParId: 'u2',
  modifiePar: 'Bob',
  utilisations,
  creeLe,
});

vi.mock('../api', () => ({
  api: {
    modelesRecette: vi.fn(),
    catalogueSystemes: vi.fn(async () => [
      { id: 'c-web', nom: 'WEB', description: '', cible: '', actif: true },
      { id: 'c-bdd', nom: 'BDD', description: '', cible: '', actif: true },
    ]),
    creerModeleRecette: vi.fn(),
    modifierModeleRecette: vi.fn(),
    supprimerModeleRecette: vi.fn(),
  },
}));

function rendre(estAdmin = false, url = '/parametres/recettes') {
  render(
    <MemoryRouter initialEntries={[url]}>
      <AuthContext.Provider value={{ utilisateur: utilisateur({ estAdmin }) } as never}>
        <TooltipProvider>
          <BibliothequeRecettesPage />
        </TooltipProvider>
      </AuthContext.Provider>
    </MemoryRouter>,
  );
}

describe('Bibliothèque de recettes', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(api.modelesRecette).mockResolvedValue([
      modele('r1', 'Connexion au portail', 'c-web', 'Ouvrir le portail', 2, '2026-02-01T00:00:00Z'),
      modele('r2', 'Droits applicatifs', 'c-bdd', '```\npsql -c "SELECT 1"\n```', 4),
      modele('r3', 'Affichage sur mobile', 'c-web', 'Ouvrir le portail sur un téléphone', 5),
      modele('r4', 'Portail en anglais', 'c-web', 'Changer de langue', 2, '2026-01-15T00:00:00Z'),
    ]);
  });

  it('s’ouvre sur le choix du système, par ordre alphabétique, avec le nombre de modèles', async () => {
    rendre();
    const bdd = await screen.findByRole('link', { name: 'BDD : 1 modèle' });
    const web = screen.getByRole('link', { name: 'WEB : 3 modèles' });
    expect(bdd.compareDocumentPosition(web) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(web).toHaveAttribute('href', '/parametres/recettes?systeme=WEB');
    expect(screen.queryByText('Connexion au portail')).toBeNull();
  });

  it('cherche dans tous les systèmes : un nom de système filtre, les autres mots portent sur le titre', async () => {
    rendre();
    await screen.findByRole('link', { name: 'WEB : 3 modèles' });
    await userEvent.type(screen.getByLabelText('Rechercher dans la bibliothèque'), 'web portail');
    expect(screen.getByRole('status')).toHaveTextContent('2 modèles');
    const titres = screen.getAllByRole('heading', { level: 2 }).map((h) => h.textContent);
    // Même nombre d'utilisations : le plus ancien d'abord.
    expect(titres).toEqual(['Portail en anglais', 'Connexion au portail']);
    await userEvent.clear(screen.getByLabelText('Rechercher dans la bibliothèque'));
    await userEvent.type(screen.getByLabelText('Rechercher dans la bibliothèque'), 'téléphone');
    expect(screen.getByRole('status')).toHaveTextContent('0 modèle');
  });

  it('présente la bibliothèque d’un système, triée par utilisations, avec le compteur', async () => {
    rendre(false, '/parametres/recettes?systeme=WEB');
    expect(await screen.findByRole('heading', { level: 1 })).toHaveTextContent('Bibliothèque de recettes · WEB');
    const titres = screen.getAllByRole('heading', { level: 2 }).map((h) => h.textContent);
    expect(titres).toEqual(['Affichage sur mobile', 'Portail en anglais', 'Connexion au portail']);
    expect(screen.getByText('5 MEP')).toBeInTheDocument();
    expect(screen.queryByText('Droits applicatifs')).toBeNull();
    expect(screen.getAllByText('Modifié par Bob le', { exact: false })).toHaveLength(3);
    expect(screen.getAllByText('1. Ouvrir le portail', { exact: false })).toHaveLength(2);
  });

  it('crée un modèle depuis la bibliothèque d’un système, système proposé', async () => {
    vi.mocked(api.creerModeleRecette).mockResolvedValue(modele('r5', 'Santé API', 'c-web', 'x'));
    rendre(false, '/parametres/recettes?systeme=WEB');
    await screen.findByText('Connexion au portail');
    await userEvent.click(screen.getByRole('button', { name: 'Nouveau modèle' }));
    const dialogue = await screen.findByRole('dialog');
    expect(within(dialogue).getByLabelText('Système du catalogue *')).toHaveValue('c-web');
    await userEvent.type(within(dialogue).getByLabelText('Titre du modèle *'), 'Santé API');
    await userEvent.click(within(dialogue).getByRole('button', { name: 'Ajouter une étape de test' }));
    await userEvent.type(within(dialogue).getByLabelText('Déroulé du test, étape 1, bloc 1 (texte)'), 'Appeler /sante');
    await userEvent.type(within(dialogue).getByLabelText('Résultat attendu, étape 1'), 'Réponse 200');
    await userEvent.click(within(dialogue).getByRole('button', { name: 'Enregistrer' }));
    await waitFor(() =>
      expect(api.creerModeleRecette).toHaveBeenCalledWith({
        titre: 'Santé API',
        systemeCatalogueId: 'c-web',
        etapes: [{ instructions: 'Appeler /sante', resultatAttendu: 'Réponse 200' }],
      }),
    );
  });

  it('exige le système d’un nouveau modèle créé depuis l’accueil', async () => {
    rendre();
    await screen.findByRole('link', { name: 'WEB : 3 modèles' });
    await userEvent.click(screen.getByRole('button', { name: 'Nouveau modèle' }));
    const dialogue = await screen.findByRole('dialog');
    await userEvent.type(within(dialogue).getByLabelText('Titre du modèle *'), 'Santé API');
    await userEvent.click(within(dialogue).getByRole('button', { name: 'Enregistrer' }));
    expect(within(dialogue).getByRole('alert')).toHaveTextContent('Choisissez le système du catalogue');
  });

  it('modifie un modèle avec sa version', async () => {
    vi.mocked(api.modifierModeleRecette).mockResolvedValue(modele('r1', 'Connexion', 'c-web', 'x'));
    rendre(false, '/parametres/recettes?systeme=WEB');
    await userEvent.click(await screen.findByRole('button', { name: 'Modifier Connexion au portail' }));
    const dialogue = await screen.findByRole('dialog');
    await userEvent.click(within(dialogue).getByRole('button', { name: 'Enregistrer' }));
    await waitFor(() =>
      expect(api.modifierModeleRecette).toHaveBeenCalledWith('r1', {
        version: 3,
        titre: 'Connexion au portail',
        systemeCatalogueId: 'c-web',
        etapes: [{ instructions: 'Ouvrir le portail', resultatAttendu: 'OK' }],
      }),
    );
  });

  it('seul un administrateur peut supprimer un modèle', async () => {
    rendre(false, '/parametres/recettes?systeme=WEB');
    await screen.findByText('Connexion au portail');
    expect(screen.queryByRole('button', { name: /Supprimer Connexion/ })).toBeNull();
  });
});
