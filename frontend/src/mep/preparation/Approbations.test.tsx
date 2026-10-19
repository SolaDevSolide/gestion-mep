import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { AuthContext } from '../../auth';
import { MepContext } from '../../mepContext';
import { mep, utilisateur } from '../../test/fabrique';
import type { Approbation, Mep } from '../../types';
import { TooltipProvider } from '../../ui/Tooltip';
import { api } from '../../api';
import { ApprobationsBloc } from './Approbations';

// Comptes : Claire (u1), Bob (u2, suppléant), Alice (u3, pilote), Dave (u4, hors MEP) et un second compte de Bob (u5).
const comptes = [
  utilisateur({ id: 'u1', login: 'claire', nomAffiche: 'Claire' }),
  utilisateur({ id: 'u2', login: 'bob', nomAffiche: 'Bob' }),
  utilisateur({ id: 'u3', login: 'alice', nomAffiche: 'Alice' }),
  utilisateur({ id: 'u4', login: 'dave', nomAffiche: 'Dave' }),
  utilisateur({ id: 'u5', login: 'bob.admin', nomAffiche: 'Bob', comptePrincipalId: 'u2' }),
];

vi.mock('../../api', () => ({
  api: {
    utilisateurs: vi.fn(() => Promise.resolve(comptes)),
    journal: vi.fn(() => Promise.resolve([])),
    equipes: vi.fn(() => Promise.resolve([])),
    decider: vi.fn(),
    modifierApprobateurs: vi.fn(),
  },
}));

const approbation = (p: Partial<Approbation>): Approbation => ({
  id: 'a1',
  utilisateurId: 'u4',
  nom: 'Dave',
  decision: null,
  commentaire: '',
  le: null,
  empechement: null,
  ...p,
});

function rendre(p: Partial<Mep>, moi = 'u3') {
  const m = mep({
    phase: 'Preparation',
    piloteId: 'p3',
    suppleantIds: ['p2'],
    droits: {
      ...mep().droits,
      peutEditerContenu: true,
      peutModifierPlan: true,
      estPilote: moi === 'u3',
      estApprobateur: true,
    },
    ...p,
  });
  const run = vi.fn(async (action: () => Promise<unknown>) => {
    await action();
    return true;
  });
  render(
    <AuthContext.Provider value={{ utilisateur: utilisateur({ id: moi }) } as never}>
      <MepContext.Provider value={{ mep: m, run }}>
        <TooltipProvider>
          <ApprobationsBloc />
        </TooltipProvider>
      </MepContext.Provider>
    </AuthContext.Provider>,
  );
  return { run };
}

beforeAll(() => {
  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe() {}
      unobserve() {}
      disconnect() {}
    },
  );
  Element.prototype.scrollIntoView ??= () => undefined;
});

beforeEach(() => {
  vi.clearAllMocks();
});

describe('ApprobationsBloc : séparation des tâches (#94)', () => {
  it('ne propose ni le pilote, ni un suppléant, ni un responsable technique, comptes reliés compris', async () => {
    rendre({
      responsablesTechniques: [
        { participantId: 'p1', systemeIds: [], perimetre: '', disponibilite: 'Joignable', contactJourJ: '' },
      ],
    });
    expect(
      screen.getByText(
        /Le pilote, ses suppléants et les responsables techniques \(comptes reliés compris\) sont exclus/,
      ),
    ).toBeInTheDocument();
    await userEvent.click(await screen.findByRole('button', { name: 'Approbateurs' }));
    const liste = screen.getByRole('listbox');
    expect(within(liste).getByText('Dave')).toBeInTheDocument();
    for (const nom of ['Alice', 'Bob', 'Claire']) expect(within(liste).queryByText(nom)).toBeNull();
  });

  it('un approbateur devenu suppléant reste affiché avec la raison, sans bouton pour décider', async () => {
    rendre(
      {
        approbations: [
          approbation({
            id: 'a1',
            utilisateurId: 'u2',
            nom: 'Bob',
            decision: 'Approuvee',
            le: '2026-10-08T10:00:00Z',
            empechement: 'suppléant du pilote',
          }),
        ],
      },
      'u2',
    );
    expect(
      await screen.findByText(/Bob ne peut pas approuver \(suppléant du pilote\) : sa décision ne compte pas\./),
    ).toBeInTheDocument();
    expect(screen.getByText(/Le retirer des approbateurs pour permettre le Go\./)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Approuver' })).toBeNull();
  });

  it('un approbateur sans empêchement décide normalement', async () => {
    rendre({ approbations: [approbation({})] }, 'u4');
    expect(await screen.findByRole('button', { name: 'Approuver' })).toBeInTheDocument();
    expect(screen.queryByText(/ne peut pas approuver/)).toBeNull();
  });
});

const evenement = (id: number, type: string) => ({
  id,
  quand: '2026-10-08T10:00:00Z',
  auteur: 'Alice',
  type,
  detail: '',
  auteurLogin: 'alice',
});

describe('ApprobationsBloc : décider, désigner', () => {
  it('approuver : la décision part avec la version de la MEP, le commentaire est facultatif', async () => {
    const { run } = rendre({ version: 7, approbations: [approbation({})] }, 'u4');
    await userEvent.click(await screen.findByRole('button', { name: 'Approuver' }));
    const dialogue = await screen.findByRole('dialog', { name: 'Approuver la MEP' });
    expect(within(dialogue).getByLabelText('Commentaire (facultatif)')).toBeInTheDocument();
    await userEvent.type(within(dialogue).getByLabelText('Commentaire (facultatif)'), 'Relu, RAS.');
    await userEvent.click(within(dialogue).getByRole('button', { name: 'Approuver' }));
    expect(api.decider).toHaveBeenCalledWith('m1', 7, 'Approuvee', 'Relu, RAS.');
    expect(run).toHaveBeenCalledWith(expect.any(Function), 'MEP approuvée.');
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
  });

  it('refuser exige un commentaire, envoyé sans espaces autour', async () => {
    rendre({ approbations: [approbation({})] }, 'u4');
    await userEvent.click(await screen.findByRole('button', { name: 'Refuser' }));
    const dialogue = await screen.findByRole('dialog', { name: 'Refuser la MEP' });
    const refuser = within(dialogue).getByRole('button', { name: 'Refuser' });
    expect(refuser).toBeDisabled();
    await userEvent.type(within(dialogue).getByLabelText('Commentaire (obligatoire)'), '   ');
    expect(refuser).toBeDisabled();
    await userEvent.type(within(dialogue).getByLabelText('Commentaire (obligatoire)'), 'La base n’est pas couverte. ');
    expect(refuser).toBeEnabled();
    await userEvent.click(refuser);
    expect(api.decider).toHaveBeenCalledWith('m1', 1, 'Refusee', 'La base n’est pas couverte.');
  });

  it('compteur des approbations requises, statut de chacun et refus à traiter', async () => {
    rendre({
      approbationsMinimum: 2,
      approbations: [
        approbation({ id: 'a1', decision: 'Approuvee', le: '2026-10-08T10:00:00Z' }),
        approbation({
          id: 'a2',
          utilisateurId: 'u1',
          nom: 'Claire',
          decision: 'Refusee',
          commentaire: 'Plan de retour arrière incomplet',
          le: '2026-10-08T11:00:00Z',
        }),
      ],
    });
    expect(await screen.findByText('1/2 approbations requises')).toBeInTheDocument();
    const decisions = screen.getByRole('list', { name: 'Décisions des approbateurs' });
    const [dave, claire] = within(decisions).getAllByRole('listitem');
    expect(dave).toHaveTextContent('Approuvée');
    expect(claire).toHaveTextContent('Refusée');
    expect(claire).toHaveTextContent('« Plan de retour arrière incomplet »');
    // Le pilote ne décide pas : aucun bouton pour lui.
    expect(screen.queryByRole('button', { name: 'Approuver' })).toBeNull();
    expect(screen.getByText('Refus à traiter').parentElement).toHaveTextContent(
      'Claire : Plan de retour arrière incomplet',
    );
  });

  it('sans approbateur : le nombre exigé avant le Go', async () => {
    rendre({ approbationsMinimum: 2 });
    expect(
      await screen.findByText('Aucun approbateur désigné : 2 approbations exigées avant le Go.'),
    ).toBeInTheDocument();
  });

  it('« Approbations à refaire » après une modification qui a effacé les décisions', async () => {
    vi.mocked(api.journal).mockResolvedValueOnce([
      evenement(2, 'approbations.reinitialisees'),
      evenement(1, 'approbation.donnee'),
    ]);
    rendre({ approbations: [approbation({})] }, 'u4');
    expect(await screen.findByRole('status')).toHaveTextContent('Approbations à refaire');
  });

  it('dernière décision postérieure à l’effacement : pas d’avis « à refaire »', async () => {
    vi.mocked(api.journal).mockResolvedValueOnce([
      evenement(3, 'approbation.donnee'),
      evenement(2, 'approbations.reinitialisees'),
    ]);
    rendre({ approbations: [approbation({})] }, 'u4');
    await screen.findByRole('button', { name: 'Approuver' });
    await waitFor(() => expect(api.journal).toHaveBeenCalled());
    expect(screen.queryByText('Approbations à refaire')).toBeNull();
  });

  it('plan incomplet : décision impossible, les raisons sont listées', async () => {
    rendre(
      {
        approbations: [approbation({})],
        erreursPlan: ['L’étape E0002 n’a pas d’exécutant.', 'Il faut 1 approbation avant le Go.'],
      },
      'u4',
    );
    expect(await screen.findByRole('button', { name: 'Approuver' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Refuser' })).toBeDisabled();
    const alerte = screen.getByText('Plan incomplet : décision impossible pour l’instant').parentElement!;
    expect(alerte).toHaveTextContent('L’étape E0002 n’a pas d’exécutant.');
    // Le manque d'approbations n'empêche pas d'approuver.
    expect(alerte).not.toHaveTextContent('Il faut 1 approbation');
  });

  it('désigner un approbateur puis enregistrer ; les décisions déjà prises seront effacées', async () => {
    rendre({
      approbations: [
        approbation({
          id: 'a1',
          utilisateurId: 'u1',
          nom: 'Claire',
          decision: 'Approuvee',
          le: '2026-10-08T10:00:00Z',
        }),
      ],
    });
    await userEvent.click(await screen.findByRole('button', { name: 'Approbateurs' }));
    await userEvent.click(within(screen.getByRole('listbox')).getByRole('option', { name: /Dave/ }));
    await userEvent.keyboard('{Escape}');
    expect(screen.getByText('Les décisions déjà prises seront effacées.')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Enregistrer les approbateurs' }));
    expect(api.modifierApprobateurs).toHaveBeenCalledWith('m1', 0, ['u1', 'u4']);
  });
});
