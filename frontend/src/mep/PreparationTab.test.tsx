import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { api, ApiError } from '../api';
import { AuthContext } from '../auth';
import { MepContext } from '../mepContext';
import { oublierReferentiels } from '../referentiels';
import { mep, utilisateur } from '../test/fabrique';
import type { CompteAnnuaire } from '../types';
import { TooltipProvider } from '../ui/Tooltip';
import PreparationTab from './PreparationTab';

vi.mock('../api', () => ({
  api: {
    utilisateurs: vi.fn(),
    equipes: vi.fn(async () => []),
    catalogueSystemes: vi.fn(async () => []),
    objetsCatalogue: vi.fn(async () => []),
    journal: vi.fn(async () => []),
    variablesModelesTexte: vi.fn(async () => []),
    variablesTexte: vi.fn(async () => ({ equipeId: null, equipeProposeeId: null, valeurs: [] })),
    modelesTexte: vi.fn(async () => []),
    urlExportPreparation: (id: string) => `/api/meps/${id}/preparation/excel`,
    urlObjetsExcel: () => '',
  },
  ApiError: class ErreurApi extends Error {
    status: number;
    erreurs: string[] = [];
    requete = '';
    date = new Date();
    constructor(status: number, message: string) {
      super(message);
      this.status = status;
    }
  },
}));

const SESSION = { utilisateur: utilisateur({ id: 'u3' }) } as never;

function rendre() {
  const m = mep({
    phase: 'Preparation',
    droits: { ...mep().droits, peutEditerContenu: true, peutModifierPlan: true },
  });
  const ctx = { mep: m, run: vi.fn() };
  render(
    <AuthContext.Provider value={SESSION}>
      <MemoryRouter>
        <MepContext.Provider value={ctx}>
          <TooltipProvider>
            <PreparationTab />
          </TooltipProvider>
        </MepContext.Provider>
      </MemoryRouter>
    </AuthContext.Provider>,
  );
}

/** Promesse résolue à la main : la réponse des comptes arrive quand le test le décide. */
function differee<T>() {
  let resoudre: (v: T) => void = () => undefined;
  const promesse = new Promise<T>((r) => {
    resoudre = r;
  });
  return { promesse, resoudre };
}

describe('Onglet Préparation : un squelette, puis tout d’un coup', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    oublierReferentiels();
  });

  it('affiche le sommaire tout de suite et les sections ensemble, une fois les listes arrivées', async () => {
    const comptes = differee<CompteAnnuaire[]>();
    vi.mocked(api.utilisateurs).mockReturnValue(comptes.promesse);
    rendre();
    expect(screen.getByRole('navigation', { name: 'Sections de la préparation' })).toBeInTheDocument();
    expect(screen.getByRole('status', { name: 'Chargement de la préparation…' })).toBeInTheDocument();
    expect(screen.queryByText('Personnes', { selector: 'h2 *, h2' })).not.toBeInTheDocument();

    comptes.resoudre([]);
    await waitFor(() =>
      expect(screen.queryByRole('status', { name: 'Chargement de la préparation…' })).not.toBeInTheDocument(),
    );
    // Une seule lecture des listes communes pour toutes les sections.
    expect(api.utilisateurs).toHaveBeenCalledTimes(1);
    expect(api.equipes).toHaveBeenCalledTimes(1);
    expect(api.catalogueSystemes).toHaveBeenCalledTimes(1);
    expect(api.journal).toHaveBeenCalledTimes(1);
  });

  it('une liste en erreur n’empêche pas l’affichage : sa section montre l’erreur et « Réessayer »', async () => {
    vi.mocked(api.utilisateurs)
      .mockRejectedValueOnce(new ApiError(502, 'Erreur 502.'))
      .mockResolvedValue([utilisateur({ id: 'u7', nomAffiche: 'Dana' })]);
    rendre();
    const alerte = await screen.findByText(/Comptes non chargés : le serveur ne répond pas \(502\)/);
    // Les autres sections sont là.
    expect(screen.queryByRole('status', { name: 'Chargement de la préparation…' })).not.toBeInTheDocument();
    const ligne = alerte.closest('[role="alert"]') as HTMLElement;
    await userEvent.click(within(ligne).getByRole('button', { name: 'Réessayer' }));
    await waitFor(() => expect(screen.queryByText(/Comptes non chargés/)).not.toBeInTheDocument());
    expect(api.utilisateurs).toHaveBeenCalledTimes(2);
  });
});
