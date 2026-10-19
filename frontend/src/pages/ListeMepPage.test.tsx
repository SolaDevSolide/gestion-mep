import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { api } from '../api';
import type { MepResume } from '../types';
import { telecharger } from '../telechargement';
import { mepResume, utilisateur } from '../test/fabrique';
import ListeMepPage from './ListeMepPage';

vi.mock('../api', () => ({
  api: { meps: vi.fn(), equipes: vi.fn(), creerMep: vi.fn(), catalogueSystemes: vi.fn(), utilisateurs: vi.fn() },
}));
vi.mock('../telechargement', () => ({ telecharger: vi.fn() }));

const resume = (p: Partial<MepResume>): MepResume => mepResume(p);

const BDD = { catalogueId: 'bdd', nom: 'BDD', environnements: [{ nom: 'Production', production: true }], echecs: 0 };
const API = { catalogueId: 'api', nom: 'API', environnements: [{ nom: 'Recette', production: false }], echecs: 0 };

/** Adresse courante, pour vérifier que les filtres y sont écrits. */
function Adresse() {
  const { search } = useLocation();
  return <output aria-label="adresse">{search}</output>;
}

function rendre(adresse = '/') {
  render(
    <MemoryRouter initialEntries={[adresse]}>
      <Routes>
        <Route
          path="/"
          element={
            <>
              <ListeMepPage />
              <Adresse />
            </>
          }
        />
        <Route path="/parametres/systemes/:id" element={<p>Fiche du système</p>} />
      </Routes>
    </MemoryRouter>,
  );
}

const equipeExploit = {
  id: 'exploit',
  nom: 'Exploitation',
  description: '',
  membreIds: [],
  creeeLe: '2026-10-01T08:00:00Z',
  contactSupport: '',
  signature: '',
};

describe('ListeMepPage', () => {
  beforeEach(() => {
    window.localStorage.clear();
    vi.mocked(api.equipes).mockResolvedValue([]);
    vi.mocked(api.catalogueSystemes).mockResolvedValue([
      {
        id: 'bdd',
        nom: 'BDD',
        description: 'Base',
        cible: '',
        actif: true,
        environnements: [{ nom: 'Production', cible: '', production: true }],
      } as never,
    ]);
    vi.mocked(api.utilisateurs).mockResolvedValue([
      utilisateur({ id: 'alice', login: 'alice', nomAffiche: 'Alice' }),
      utilisateur({ id: 'bob', login: 'bob', nomAffiche: 'Bob' }),
    ]);
  });

  it('ne réutilise pas un bouton de l’en-tête d’une vue à l’autre (pas de transition de couleur)', async () => {
    vi.mocked(api.meps).mockResolvedValue([]);
    rendre();
    const depuisModele = screen.getByRole('button', { name: /Nouvelle MEP depuis un modèle/ });
    await userEvent.click(screen.getByRole('radio', { name: /Modèles/ }));
    const nouveauModele = (await screen.findAllByRole('button', { name: /Nouveau modèle/ }))[0];
    // Même position dans l'en-tête, mais un autre élément : sans clé, React lui changeait seulement sa variante.
    expect(nouveauModele).not.toBe(depuisModele);
    expect(depuisModele).not.toBeInTheDocument();
  });

  it('filtre les MEP par équipe et trouve une personne par l’un de ses comptes reliés', async () => {
    vi.mocked(api.equipes).mockResolvedValue([equipeExploit]);
    vi.mocked(api.meps).mockResolvedValue([
      resume({ id: 'a', outil: 'Portail', equipeIds: ['exploit'], personnes: ['Jean Dupont', 'jean.dupont@ent.fr'] }),
      resume({ id: 'b', outil: 'Facturation', personnes: ['Claire'] }),
    ]);
    rendre();
    expect(await screen.findByText('Facturation')).toBeInTheDocument();

    await userEvent.click(await screen.findByRole('button', { name: /^Équipe/ }));
    await userEvent.click(screen.getByRole('menuitemcheckbox', { name: /Exploitation/ }));
    expect(screen.getAllByText('Portail').length).toBeGreaterThan(0);
    expect(screen.queryByText('Facturation')).not.toBeInTheDocument();
    expect(screen.getByLabelText('adresse')).toHaveTextContent('?equipe=exploit');
    await userEvent.keyboard('{Escape}');

    await userEvent.click(screen.getAllByRole('button', { name: 'Effacer le filtre Équipe' })[0]);
    await userEvent.type(screen.getByLabelText('Rechercher une MEP'), 'jean.dupont');
    expect(screen.getAllByText('Portail').length).toBeGreaterThan(0);
    expect(screen.queryByText('Facturation')).not.toBeInTheDocument();
  });

  it('lit les filtres de l’adresse, montre un tableau et compte les résultats', async () => {
    vi.mocked(api.meps).mockResolvedValue([
      resume({
        id: 'a',
        outil: 'Stocks',
        etat: 'AvecIncidents',
        phase: 'Cloturee',
        systemes: [{ ...BDD, echecs: 1 }, API],
        echecsSurvenus: 1,
      }),
      resume({ id: 'b', outil: 'Paie', systemes: [API] }),
    ]);
    rendre('/?systeme=bdd');
    const tableau = await screen.findByRole('table', { name: /Mises en production trouvées/ });
    expect(within(tableau).getByText('Stocks')).toBeInTheDocument();
    expect(within(tableau).queryByText('Paie')).not.toBeInTheDocument();
    expect(within(tableau).getByText('Avec incidents')).toBeInTheDocument();
    expect(screen.getByText(/sur 2/).parentElement).toHaveTextContent('1 MEP sur 2');
    expect(screen.getByRole('button', { name: /^Système/ })).toHaveTextContent('BDD');
    expect(within(tableau).getByRole('link', { name: 'Rapport de fin de Stocks' })).toHaveAttribute(
      'href',
      '/meps/a/rapport',
    );
  });

  it('retient la présentation choisie à la main', async () => {
    vi.mocked(api.meps).mockResolvedValue([resume({ id: 'a', outil: 'Stocks' })]);
    rendre();
    expect(await screen.findByText('Stocks')).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('radio', { name: 'Tableau' }));
    expect(screen.getByRole('table')).toBeInTheDocument();
    expect(window.localStorage.getItem('liste-meps.affichage')).toBe('"tableau"');
  });

  it('une puce de système ouvre son historique', async () => {
    vi.mocked(api.meps).mockResolvedValue([resume({ id: 'a', outil: 'Stocks', systemes: [BDD] })]);
    rendre();
    await userEvent.click(await screen.findByRole('button', { name: /BDD : historique et MEP de ce système/ }));
    await userEvent.click(screen.getByRole('menuitem', { name: /Historique des MEP/ }));
    expect(await screen.findByText('Fiche du système')).toBeInTheDocument();
  });

  it('sans résultat, propose de retirer un critère avec le nombre de MEP retrouvées', async () => {
    vi.mocked(api.meps).mockResolvedValue([
      resume({ id: 'a', outil: 'Stocks', jour: '2026-09-01', systemes: [BDD] }),
      resume({ id: 'b', outil: 'Paie', jour: '2026-10-10' }),
    ]);
    rendre('/?systeme=bdd&du=2026-10-01');
    expect(await screen.findByText('Aucun résultat')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Sans le système (1)' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Exporter en Excel' })).toBeDisabled();
    await userEvent.click(screen.getByRole('button', { name: 'Sans la période (1)' }));
    expect(screen.getAllByText('Stocks').length).toBeGreaterThan(0);
    expect(screen.getByLabelText('adresse')).toHaveTextContent('?systeme=bdd');
  });

  it('exporte en Excel la liste affichée, avec ses filtres', async () => {
    vi.mocked(api.meps).mockResolvedValue([
      resume({ id: 'a', outil: 'Portail', phase: 'Execution', etat: 'Execution' }),
    ]);
    rendre();
    expect(await screen.findByText('Portail')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Exporter en Excel' }));
    expect(telecharger).toHaveBeenLastCalledWith('/api/meps/excel');

    await userEvent.type(screen.getByLabelText('Rechercher une MEP'), 'portail');
    await userEvent.click(screen.getByRole('button', { name: /^État/ }));
    await userEvent.click(screen.getByRole('menuitemcheckbox', { name: /^Exécution/ }));
    await userEvent.keyboard('{Escape}');
    await userEvent.click(screen.getByRole('button', { name: 'Exporter en Excel' }));
    expect(telecharger).toHaveBeenLastCalledWith('/api/meps/excel?recherche=portail&etat=execution');

    // Modèles : seuls la recherche et l'équipe s'appliquent.
    await userEvent.click(screen.getByRole('radio', { name: /Modèles/ }));
    await userEvent.clear(screen.getByLabelText('Rechercher une MEP'));
    await userEvent.click(await screen.findByRole('button', { name: 'Exporter en Excel' }));
    expect(telecharger).toHaveBeenLastCalledWith('/api/meps/excel?modele=true');
  });

  it('signale les MEP qui touchent la production', async () => {
    vi.mocked(api.meps).mockResolvedValue([
      resume({ id: 'a', outil: 'Portail', toucheProduction: true }),
      resume({ id: 'b', outil: 'Recette seule' }),
    ]);
    rendre();
    const portail = (await screen.findByText('Portail')).closest('li') ?? document.body;
    expect(portail).toHaveTextContent(/Production/);
    const recette = screen.getByText('Recette seule').closest('li') ?? document.body;
    expect(recette).not.toHaveTextContent(/Production/);
  });

  it('montre les MEP reportées et annulées (#93), l’annulée sans barre de progression', async () => {
    vi.mocked(api.meps).mockResolvedValue([
      resume({ id: 'r', outil: 'Notes de frais', etat: 'Reportee', reporteeAu: '2026-10-28' }),
      resume({
        id: 'x',
        outil: 'Signature',
        phase: 'Annulee',
        etat: 'Annulee',
        annuleeLe: '2026-10-19T14:02:00Z',
        nbEtapes: 5,
        nbReussies: 2,
      }),
    ]);
    rendre();
    const reportee = (await screen.findByText('Notes de frais')).closest('li') ?? document.body;
    expect(reportee).toHaveTextContent('Reportée · 28/10/2026');
    const annulee = screen.getByText('Signature').closest('li') ?? document.body;
    expect(annulee).toHaveTextContent('Annulée le 19/10/2026');
    expect(annulee).toHaveTextContent('2/5 étapes réussies avant l’annulation');
    expect(within(annulee as HTMLElement).queryByRole('img', { name: /progression/i })).toBeNull();
  });
});

describe('ListeMepPage : MEP simplifiée', () => {
  beforeEach(() => {
    vi.mocked(api.equipes).mockResolvedValue([]);
    vi.mocked(api.catalogueSystemes).mockResolvedValue([]);
    vi.mocked(api.utilisateurs).mockResolvedValue([]);
    vi.mocked(api.creerMep).mockReset();
  });

  it('crée une MEP du type choisi (complète par défaut)', async () => {
    vi.mocked(api.meps).mockResolvedValue([]);
    vi.mocked(api.creerMep).mockResolvedValue({ id: 'n' } as never);
    rendre();
    await userEvent.click((await screen.findAllByRole('button', { name: /^Nouvelle MEP$/ }))[0]);
    expect(screen.getByRole('radio', { name: /MEP complète/ })).toBeChecked();
    await userEvent.click(screen.getByRole('radio', { name: /MEP simplifiée/ }));
    await userEvent.type(screen.getByLabelText('Outil ou application'), 'Correctif');
    await userEvent.click(screen.getByRole('button', { name: 'Créer la MEP' }));
    expect(api.creerMep).toHaveBeenCalledWith('Correctif', '', false, 'Simplifiee');
  });

  it('pas de choix de type pour un modèle', async () => {
    vi.mocked(api.meps).mockResolvedValue([]);
    rendre();
    await userEvent.click(screen.getByRole('radio', { name: /Modèles/ }));
    await userEvent.click((await screen.findAllByRole('button', { name: /Nouveau modèle/ }))[0]);
    expect(screen.getByRole('dialog', { name: 'Nouveau modèle' })).toBeInTheDocument();
    expect(screen.queryByRole('radio', { name: /MEP simplifiée/ })).not.toBeInTheDocument();
  });

  it('repère les MEP simplifiées dans la liste, sans version cible', async () => {
    vi.mocked(api.meps).mockResolvedValue([
      resume({ id: 'a', outil: 'Portail', versionCible: '2.0' }),
      resume({ id: 'b', outil: 'Correctif', type: 'Simplifiee', versionCible: '9.9' }),
    ]);
    rendre();
    expect(await screen.findByText('Correctif')).toBeInTheDocument();
    expect(screen.getAllByText('MEP simplifiée')).toHaveLength(1);
    expect(screen.getByText('2.0')).toBeInTheDocument();
    expect(screen.queryByText('9.9')).not.toBeInTheDocument();
  });
});
