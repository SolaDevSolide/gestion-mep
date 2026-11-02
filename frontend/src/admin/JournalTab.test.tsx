import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import type * as moduleApi from '../api';
import { api } from '../api';
import type { EvenementAdmin } from '../types';
import JournalTab from './JournalTab';

vi.mock('../api', async (original) => ({
  ApiError: (await original<typeof moduleApi>()).ApiError,
  api: { adminJournal: vi.fn() },
}));

const ev = (id: number, type: string, detail: string): EvenementAdmin => ({
  id,
  quand: '2026-11-02T10:42:00Z',
  auteur: 'Alice',
  auteurLogin: 'alice',
  type,
  detail,
});

describe('Journal d’administration', () => {
  it('ouvert depuis « Jetons d’API », il ne montre que les jetons, jusqu’à ce qu’on retire le filtre', async () => {
    vi.mocked(api.adminJournal).mockResolvedValue([
      ev(3, 'jeton.cree', 'Jeton personnel « Grafana » (gmep_r8Fq2x0A) créé'),
      ev(2, 'compte.cree', 'Compte Bob créé'),
      ev(1, 'jeton.revoque', 'Jeton personnel « Ancien script » révoqué'),
    ]);
    const user = userEvent.setup();
    render(
      <MemoryRouter initialEntries={['/admin?onglet=journal&filtre=jetons']}>
        <JournalTab />
      </MemoryRouter>,
    );
    expect(await screen.findByText('Jeton créé')).toBeInTheDocument();
    expect(screen.getByText('Jeton révoqué')).toBeInTheDocument();
    expect(screen.queryByText('Compte créé')).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Retirer le filtre « Jetons »' }));
    expect(await screen.findByText('Compte créé')).toBeInTheDocument();
  });
});
