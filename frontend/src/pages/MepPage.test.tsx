import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { createMemoryRouter, RouterProvider, useLocation } from 'react-router-dom';
import { api } from '../api';
import { AuthContext } from '../auth';
import { useReportDirty } from '../hooks';
import { GardeModifications } from '../layout/GardeModifications';
import { ZonePresence } from '../presence';
import { SESSION_ACTIVE, etape, mep, utilisateur } from '../test/fabrique';
import type { Mep } from '../types';
import { ToastProvider } from '../ui/Toast';
import { TooltipProvider } from '../ui/Tooltip';
import MepPage from './MepPage';

vi.mock('../api', () => ({
  ApiError: class extends Error {
    status = 0;
  },
  api: {
    mep: vi.fn(),
    declarerPresence: vi.fn(),
    connexionsMep: vi.fn(async () => ({ envois: [], echecs: [], peutEnvoyer: false })),
  },
}));

// Les onglets eux-mêmes ont leurs propres tests : ici, des remplaçants légers.
function Formulaire() {
  const [valeur, setValeur] = useState('');
  useReportDirty(valeur !== '', 'Intervention');
  return <input aria-label="Objet" value={valeur} onChange={(e) => setValeur(e.target.value)} />;
}
vi.mock('../mep/PreparationTab', () => ({
  default: () => (
    <ZonePresence cle="preparation:intervention">
      <Formulaire />
    </ZonePresence>
  ),
}));
vi.mock('../mep/EtapesTab', () => ({ default: () => <p>Contenu Étapes</p> }));
vi.mock('../mep/RetourArriereTab', () => ({ default: () => <p>Contenu Retour arrière</p> }));
vi.mock('../mep/RecetteTab', () => ({ default: () => <p>Contenu Recette</p> }));
vi.mock('../mep/ExecutionTab', () => ({ default: () => <p>Contenu Exécution</p> }));
vi.mock('../mep/JournalTab', () => ({ default: () => <p>Contenu Journal</p> }));
vi.mock('../mep/ensemble/VueEnsembleTab', () => ({ default: () => <p>Contenu Vue d’ensemble</p> }));

/** Flux SSE simulé : la page ouvre `/api/meps/{id}/flux?session=…`. */
class FauxFlux {
  static CLOSED = 2;
  static instances: FauxFlux[] = [];
  readyState = 1;
  private ecouteurs = new Map<string, ((ev: Event) => void)[]>();
  constructor(public url: string) {
    FauxFlux.instances.push(this);
  }
  addEventListener(type: string, f: (ev: Event) => void) {
    this.ecouteurs.set(type, [...(this.ecouteurs.get(type) ?? []), f]);
  }
  close() {
    this.readyState = FauxFlux.CLOSED;
  }
  emettre(type: string, data?: unknown) {
    const ev = data === undefined ? new Event(type) : new MessageEvent(type, { data: JSON.stringify(data) });
    for (const f of this.ecouteurs.get(type) ?? []) f(ev);
  }
}

function Adresse() {
  const { search } = useLocation();
  return <p data-testid="adresse">{search}</p>;
}

const moi = utilisateur({ id: 'u-alice', nomAffiche: 'Alice Durand' });

async function rendre(m: Mep, adresse = '/meps/m1') {
  vi.mocked(api.mep).mockResolvedValue(m);
  const routeur = createMemoryRouter(
    [
      {
        path: '/meps/:id',
        element: (
          <GardeModifications>
            <MepPage />
            <Adresse />
          </GardeModifications>
        ),
      },
    ],
    { initialEntries: [adresse] },
  );
  render(
    <AuthContext.Provider
      value={{
        utilisateur: moi,
        chargement: false,
        connecter: vi.fn(),
        deconnecter: vi.fn(),
        majUtilisateur: vi.fn(),
        ...SESSION_ACTIVE,
      }}
    >
      <TooltipProvider>
        <ToastProvider>
          <RouterProvider router={routeur} />
        </ToastProvider>
      </TooltipProvider>
    </AuthContext.Provider>,
  );
  await screen.findByRole('tablist', { name: 'Sections de la MEP' });
}

const adresse = () => screen.getByTestId('adresse').textContent;

const enPreparation = (p: Partial<Mep> = {}) =>
  mep({
    phase: 'Preparation',
    etapes: [
      etape({ id: 'e1', code: 'E1' }),
      etape({ id: 'e2', code: 'E2' }),
      etape({ id: 'r1', code: 'R1', plan: 'RetourArriere' }),
    ],
    ...p,
  });

beforeEach(() => {
  FauxFlux.instances = [];
  vi.stubGlobal('EventSource', FauxFlux);
  vi.mocked(api.declarerPresence).mockResolvedValue(undefined);
});
afterEach(() => {
  vi.unstubAllGlobals();
  vi.clearAllMocks();
});

describe('MepPage : onglets', () => {
  it('Vue d’ensemble, Préparation, Plan, Exécution, Journal ; Plan a trois sous-onglets avec leur nombre d’éléments', async () => {
    await rendre(enPreparation());
    const onglets = within(screen.getByRole('tablist', { name: 'Sections de la MEP' })).getAllByRole('tab');
    expect(onglets.map((o) => o.textContent)).toEqual([
      'Vue d’ensemble',
      'Préparation',
      'Plan',
      'Exécution',
      'Journal',
    ]);
    // Pas encore lancée : pas de pastille « en direct ».
    expect(screen.queryByRole('img', { name: 'En direct' })).toBeNull();
    expect(screen.getByRole('tab', { name: 'Préparation' })).toHaveAttribute('aria-selected', 'true');
    expect(screen.queryByRole('tablist', { name: 'Parties du plan' })).toBeNull();

    await userEvent.click(screen.getByRole('tab', { name: /^Plan/ }));
    const parties = screen.getByRole('tablist', { name: 'Parties du plan' });
    expect(
      within(parties)
        .getAllByRole('tab')
        .map((o) => o.textContent),
    ).toEqual(['Étapes2', 'Retour arrière1', 'Recette0']);
    expect(screen.getByText('Contenu Étapes')).toBeInTheDocument();
    expect(adresse()).toBe('?onglet=plan&plan=etapes');

    await userEvent.click(within(parties).getByRole('tab', { name: /Retour arrière/ }));
    expect(screen.getByText('Contenu Retour arrière')).toBeInTheDocument();
    expect(adresse()).toBe('?onglet=plan&plan=retour');
  });

  it('pendant l’exécution, qui n’exécute aucune étape arrive sur la vue d’ensemble, en direct (#61)', async () => {
    await rendre(mep({ phase: 'Execution', etapes: [etape({ id: 'e1', code: 'E1', responsableId: 'p2' })] }));
    expect(screen.getByRole('tab', { name: /Vue d’ensemble/ })).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByRole('img', { name: 'En direct' })).toBeInTheDocument();
    expect(screen.getByText('Contenu Vue d’ensemble')).toBeInTheDocument();
  });

  it('un modèle n’a pas de vue d’ensemble', async () => {
    await rendre(enPreparation({ estModele: true }), '/meps/m1?onglet=ensemble');
    expect(screen.queryByRole('tab', { name: /Vue d’ensemble/ })).toBeNull();
    expect(screen.getByRole('tab', { name: 'Préparation' })).toHaveAttribute('aria-selected', 'true');
  });

  it('sans plan de retour arrière, Plan n’a pas de sous-onglet Retour arrière', async () => {
    await rendre(enPreparation({ retourArriereActive: false }), '/meps/m1?onglet=plan&plan=retour');
    const parties = screen.getByRole('tablist', { name: 'Parties du plan' });
    expect(
      within(parties)
        .getAllByRole('tab')
        .map((o) => o.textContent),
    ).toEqual(['Étapes2', 'Recette0']);
    expect(screen.getByText('Contenu Étapes')).toBeInTheDocument();
  });

  it('un modèle n’a pas d’onglet Exécution', async () => {
    await rendre(enPreparation({ estModele: true }));
    expect(screen.queryByRole('tab', { name: 'Exécution' })).toBeNull();
  });

  it('les anciennes adresses ?onglet=recette mènent à Plan › Recette', async () => {
    await rendre(enPreparation(), '/meps/m1?onglet=recette');
    expect(screen.getByRole('tab', { name: /^Plan/ })).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByRole('tab', { name: /Recette/ })).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByText('Contenu Recette')).toBeInTheDocument();
    await waitFor(() => expect(adresse()).toBe('?onglet=plan&plan=recette'));
  });

  it('pendant la recette, la MEP s’ouvre sur Plan › Recette', async () => {
    await rendre(enPreparation({ phase: 'Recette' }));
    const parties = screen.getByRole('tablist', { name: 'Parties du plan' });
    expect(within(parties).getByRole('tab', { name: /Recette/ })).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByText('Contenu Recette')).toBeInTheDocument();
  });

  it('changer d’onglet avec une saisie non enregistrée demande confirmation', async () => {
    await rendre(enPreparation());
    await userEvent.type(screen.getByLabelText('Objet'), 'Nouvel objet');
    await userEvent.click(screen.getByRole('tab', { name: 'Journal' }));
    const fenetre = await screen.findByRole('dialog', { name: /Quitter sans enregistrer/ });
    expect(fenetre).toHaveTextContent('Intervention');
    await userEvent.click(screen.getByRole('button', { name: 'Rester' }));
    expect(screen.getByLabelText('Objet')).toHaveValue('Nouvel objet');
    expect(screen.queryByText('Contenu Journal')).toBeNull();

    await userEvent.click(screen.getByRole('tab', { name: 'Journal' }));
    await userEvent.click(await screen.findByRole('button', { name: 'Quitter sans enregistrer' }));
    expect(await screen.findByText('Contenu Journal')).toBeInTheDocument();
  });
});

describe('MepPage : présence', () => {
  it('affiche les autres personnes présentes et l’élément qu’elles modifient', async () => {
    await rendre(enPreparation());
    const flux = FauxFlux.instances.at(-1)!;
    expect(flux.url).toMatch(/^\/api\/meps\/m1\/flux\?session=\w+$/);
    act(() =>
      flux.emettre('presence', [
        { utilisateurId: 'u-alice', nom: 'Alice Durand', elements: [] },
        { utilisateurId: 'u-bob', nom: 'Bob Martin', elements: ['preparation:intervention'] },
      ]),
    );
    const presents = screen.getByRole('group', { name: 'Personnes sur cette MEP' });
    expect(presents).toHaveTextContent('Sur cette MEP :');
    expect(within(presents).getByRole('img', { name: 'Bob Martin (modifie)' })).toBeInTheDocument();
    expect(within(presents).queryByRole('img', { name: /Alice/ })).toBeNull();
    expect(screen.getByText('Bob modifie')).toBeInTheDocument();

    act(() => flux.emettre('presence', [{ utilisateurId: 'u-alice', nom: 'Alice Durand', elements: [] }]));
    expect(screen.queryByText('Bob modifie')).toBeNull();
    expect(screen.queryByRole('group', { name: 'Personnes sur cette MEP' })).toBeNull();
  });

  it('déclare l’élément sur lequel on travaille, et le redéclare à la réouverture du flux', async () => {
    await rendre(enPreparation());
    const flux = FauxFlux.instances.at(-1)!;
    const session = new URL(flux.url, 'http://x').searchParams.get('session');
    act(() => flux.emettre('open'));
    await userEvent.click(screen.getByLabelText('Objet'));
    await waitFor(() =>
      expect(api.declarerPresence).toHaveBeenLastCalledWith('m1', session, ['preparation:intervention']),
    );
    const appels = vi.mocked(api.declarerPresence).mock.calls.length;
    act(() => flux.emettre('open'));
    await waitFor(() => expect(vi.mocked(api.declarerPresence).mock.calls.length).toBe(appels + 1));

    await userEvent.click(screen.getByRole('tab', { name: 'Journal' }));
    await waitFor(() => expect(api.declarerPresence).toHaveBeenLastCalledWith('m1', session, []));
  });
});
