import { act, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { api } from '../api';
import { ouvrirFlux } from '../flux';
import { etape, mep } from '../test/fabrique';
import type { Suivi } from '../types';
import SuiviPage from './SuiviPage';

vi.mock('../api', () => ({ api: { suivi: vi.fn() } }));
vi.mock('../flux', () => ({ ouvrirFlux: vi.fn(() => () => undefined) }));

const go = { par: 'Alice', le: '2026-10-16T13:00:00Z' };

function suivi(p: Partial<Suivi> = {}): Suivi {
  return {
    enCours: [
      mep({ id: 'calme', outil: 'Calme', phase: 'Execution', demarrageAutorise: go }),
      mep({
        id: 'echec',
        outil: 'Paiement',
        phase: 'Execution',
        demarrageAutorise: go,
        etapes: [
          etape({ id: 'x', code: 'E0005', etat: 'Echec' }),
          etape({ id: 'y', code: 'E0006', dependances: ['x'] }),
        ],
      }),
      mep({
        id: 'repli',
        outil: 'Facturation',
        phase: 'RetourArriere',
        demarrageAutorise: go,
        retourArriere: { le: '2026-10-16T14:00:00Z', par: null, motif: 'Échéance dépassée' },
      }),
    ],
    prevues: [
      {
        id: 'p1',
        outil: 'Annuaire',
        versionCible: '2.0',
        type: 'Complete',
        date: '2099-01-01',
        heure: '20:00:00',
        debut: '2099-01-01T19:00:00Z',
        fuseauHoraire: 'Europe/Paris',
        pilote: 'Bob',
      },
    ],
    prochaine: null,
    cloturees: [
      {
        id: 'c1',
        outil: 'Stocks',
        versionCible: '6.3',
        type: 'Complete',
        issue: 'Reussie',
        clotureeLe: '2026-10-16T08:00:00Z',
        pilote: 'Alice',
      },
    ],
    maintenant: '2026-10-16T15:00:00Z',
    ...p,
  };
}

function rendre() {
  render(
    <MemoryRouter>
      <SuiviPage />
    </MemoryRouter>,
  );
}

describe('SuiviPage (#61)', () => {
  beforeEach(() => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
  });
  afterEach(() => {
    vi.useRealTimers();
  });

  it('range les retours arrière puis les échecs d’abord, et donne les chiffres du moment', async () => {
    vi.mocked(api.suivi).mockResolvedValue(suivi());
    rendre();
    const liste = await screen.findByRole('list', { name: 'MEP en cours' });
    const cartes = within(liste)
      .getAllByRole('listitem')
      .filter((li) => li.parentElement === liste);
    expect(cartes.map((c) => c.getAttribute('aria-label'))).toEqual([
      'Facturation → 2.0',
      'Paiement → 2.0',
      'Calme → 2.0',
    ]);
    expect(within(cartes[1]).getByText(/en échec/)).toHaveTextContent('E0005 en échec · bloque 1');
    expect(screen.getByText('MEP en cours').parentElement).toHaveTextContent('3');
    expect(screen.getByText('Échecs à traiter').parentElement).toHaveTextContent('1');
    expect(screen.getByText('Retours arrière').parentElement).toHaveTextContent('1');
    // Prévues et clôturées : un lien vers chaque MEP, et vers le rapport des clôturées.
    expect(screen.getByRole('link', { name: 'Annuaire → 2.0' })).toHaveAttribute('href', '/meps/p1');
    expect(screen.getByRole('link', { name: /Rapport/ })).toHaveAttribute('href', '/meps/c1/rapport');
    expect(screen.getByText('Prochaine MEP').parentElement).toHaveTextContent('Aucune MEP annoncée');
  });

  it('tient la place du tableau de bord par un squelette jusqu’à la réponse, sans « Aucune MEP » provisoire', async () => {
    let repondre: (s: Suivi) => void = () => undefined;
    vi.mocked(api.suivi).mockReturnValue(new Promise<Suivi>((r) => (repondre = r)));
    rendre();
    expect(screen.getByRole('status', { name: 'Chargement du suivi…' })).toBeInTheDocument();
    expect(screen.queryByText('Aucune MEP en cours')).not.toBeInTheDocument();
    act(() => repondre(suivi()));
    expect(await screen.findByRole('list', { name: 'MEP en cours' })).toBeInTheDocument();
    expect(screen.queryByRole('status', { name: 'Chargement du suivi…' })).not.toBeInTheDocument();
  });

  it('relit le tableau de bord quand le flux signale des modifications, en les regroupant', async () => {
    vi.mocked(api.suivi).mockResolvedValue(suivi({ enCours: [] }));
    rendre();
    expect(await screen.findByText('Aucune MEP en cours')).toBeInTheDocument();
    expect(ouvrirFlux).toHaveBeenCalledWith('/api/suivi/flux', expect.any(Function), expect.any(Function));
    const surMaj = vi.mocked(ouvrirFlux).mock.calls[0][1];
    vi.mocked(api.suivi).mockResolvedValue(suivi());
    act(() => {
      surMaj(1);
      surMaj(2);
      surMaj(3);
    });
    await act(async () => {
      await vi.advanceTimersByTimeAsync(2000);
    });
    expect(api.suivi).toHaveBeenCalledTimes(2);
    expect(await screen.findByRole('list', { name: 'MEP en cours' })).toBeInTheDocument();
  });
});
