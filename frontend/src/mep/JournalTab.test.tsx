import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { api } from '../api';
import { telecharger } from '../telechargement';
import JournalTab from './JournalTab';

vi.mock('../api', async (importOriginal) => {
  const { api: vraie } = await importOriginal<{ api: typeof api }>();
  return { api: { journal: vi.fn(), testsUnitaires: vi.fn(), urlExportJournal: vraie.urlExportJournal } };
});
vi.mock('../telechargement', () => ({ telecharger: vi.fn() }));

describe('JournalTab', () => {
  it('exporte en Excel les événements affichés, avec les filtres en cours', async () => {
    vi.mocked(api.testsUnitaires).mockResolvedValue({ faits: 0, nonFaits: 0, verifications: 0, tests: [] });
    vi.mocked(api.journal).mockResolvedValue([
      {
        id: 2,
        quand: '2026-10-09T20:10:00Z',
        auteur: 'Alice',
        type: 'etape.reussie',
        detail: 'E0001 réussie',
        auteurLogin: 'alice',
      },
      {
        id: 1,
        quand: '2026-10-09T20:00:00Z',
        auteur: 'Bob',
        type: 'mep.creee',
        detail: 'MEP créée',
        auteurLogin: null,
      },
    ]);
    render(<JournalTab mepId="m1" tick={0} />);
    expect(await screen.findByText('E0001 réussie')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Exporter en Excel' }));
    expect(telecharger).toHaveBeenLastCalledWith('/api/meps/m1/journal/excel');

    await userEvent.selectOptions(screen.getByLabelText('Filtrer par type d’événement'), 'etape');
    await userEvent.type(screen.getByLabelText('Rechercher dans le journal'), 'E0001');
    expect(screen.queryByText('MEP créée')).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Exporter en Excel' }));
    expect(telecharger).toHaveBeenLastCalledWith('/api/meps/m1/journal/excel?type=etape&recherche=E0001');
  });
});
