import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { AuthContext } from '../../auth';
import { MepContext } from '../../mepContext';
import { mep, utilisateur } from '../../test/fabrique';
import type { Approbation, Mep } from '../../types';
import { TooltipProvider } from '../../ui/Tooltip';
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
  render(
    <AuthContext.Provider value={{ utilisateur: utilisateur({ id: moi }) } as never}>
      <MepContext.Provider value={{ mep: m, run: vi.fn() }}>
        <TooltipProvider>
          <ApprobationsBloc />
        </TooltipProvider>
      </MepContext.Provider>
    </AuthContext.Provider>,
  );
}

describe('ApprobationsBloc : séparation des tâches (#94)', () => {
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
