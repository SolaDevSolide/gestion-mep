import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ClotureDialog } from './ClotureDialog';

function rendre(props: Partial<React.ComponentProps<typeof ClotureDialog>> = {}) {
  const onConfirmer = vi.fn().mockResolvedValue(true);
  render(<ClotureDialog open onOpenChange={vi.fn()} repli={false} bilanActive onConfirmer={onConfirmer} {...props} />);
  return { onConfirmer };
}

describe('ClotureDialog', () => {
  it('exige « ce qui a marché » et « incidents » quand le bilan est actif', async () => {
    const { onConfirmer } = rendre();
    const clore = screen.getByRole('button', { name: 'Clôturer la MEP' });
    expect(clore).toBeDisabled();
    await userEvent.type(screen.getByLabelText(/Ce qui a marché/), 'Tout');
    expect(clore).toBeDisabled();
    await userEvent.type(screen.getByLabelText(/Incidents/), 'Rien à signaler');
    expect(clore).toBeEnabled();
    await userEvent.type(screen.getByLabelText('Actions'), 'Automatiser');
    await userEvent.click(clore);
    expect(onConfirmer).toHaveBeenCalledWith({ ceQuiAMarche: 'Tout', incidents: 'Rien à signaler', actions: 'Automatiser' });
  });

  it('n’affiche aucun champ quand le bilan est désactivé', async () => {
    const { onConfirmer } = rendre({ bilanActive: false });
    expect(screen.queryByLabelText(/Ce qui a marché/)).toBeNull();
    await userEvent.click(screen.getByRole('button', { name: 'Clôturer la MEP' }));
    expect(onConfirmer).toHaveBeenCalledTimes(1);
  });

  it('adapte le libellé après un retour arrière', () => {
    rendre({ repli: true });
    expect(screen.getByRole('button', { name: 'Service rétabli : clôturer' })).toBeDisabled();
  });
});
