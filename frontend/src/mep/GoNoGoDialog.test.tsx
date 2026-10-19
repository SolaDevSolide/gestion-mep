import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { GoNoGoDialog } from './GoNoGoDialog';
import type { CritereGoNoGo } from '../types';

const criteres: CritereGoNoGo[] = [
  { id: 'c1', libelle: 'Équipe présente et joignable', ordre: 1, actif: true },
  { id: 'c2', libelle: 'Sauvegarde disponible', ordre: 2, actif: true },
];

function rendre(props: Partial<React.ComponentProps<typeof GoNoGoDialog>> = {}) {
  const onGo = vi.fn().mockResolvedValue(true);
  const onNoGo = vi.fn().mockResolvedValue(true);
  const onOpenChange = vi.fn();
  render(<GoNoGoDialog open onOpenChange={onOpenChange} criteres={criteres} onGo={onGo} onNoGo={onNoGo} {...props} />);
  return { onGo, onNoGo, onOpenChange };
}

describe('GoNoGoDialog', () => {
  it('désactive le Go tant que tous les critères ne sont pas cochés', async () => {
    const { onGo } = rendre();
    const go = screen.getByRole('button', { name: /Go : passer en exécution/ });
    expect(go).toBeDisabled();
    await userEvent.click(screen.getByRole('checkbox', { name: 'Équipe présente et joignable' }));
    expect(go).toBeDisabled();
    await userEvent.click(screen.getByRole('checkbox', { name: 'Sauvegarde disponible' }));
    expect(go).toBeEnabled();
    await userEvent.click(go);
    expect(onGo).toHaveBeenCalledWith(['c1', 'c2']);
  });

  it('autorise le Go directement quand aucun critère n’est actif', () => {
    rendre({ criteres: [] });
    expect(screen.getByRole('button', { name: /Go : passer en exécution/ })).toBeEnabled();
  });

  it('exige un motif pour le no-go', async () => {
    const { onNoGo } = rendre();
    await userEvent.click(screen.getByRole('button', { name: 'No-go' }));
    const confirmer = screen.getByRole('button', { name: 'Confirmer le no-go' });
    expect(confirmer).toBeDisabled();
    await userEvent.type(screen.getByLabelText(/Motif du no-go/), '  Équipe BDD injoignable ');
    expect(confirmer).toBeEnabled();
    await userEvent.click(confirmer);
    expect(onNoGo).toHaveBeenCalledWith('Équipe BDD injoignable', undefined);
  });

  it('propose de reporter la MEP avec le no-go, avec une date visée facultative (#93)', async () => {
    const { onNoGo } = rendre();
    await userEvent.click(screen.getByRole('button', { name: 'No-go' }));
    await userEvent.type(screen.getByLabelText(/Motif du no-go/), 'Gel des changements');
    expect(screen.queryByLabelText(/Date visée/)).toBeNull();
    await userEvent.click(screen.getByRole('checkbox', { name: /Reporter la MEP/ }));
    await userEvent.type(screen.getByLabelText(/Date visée/), '2026-11-02');
    await userEvent.click(screen.getByRole('button', { name: 'Confirmer le no-go et reporter' }));
    expect(onNoGo).toHaveBeenCalledWith('Gel des changements', { dateVisee: '2026-11-02' });
  });

  it('signale les anomalies du plan', () => {
    rendre({ erreursPlan: ['Il faut 1 approbation(s).'] });
    expect(screen.getByText('1 point(s) à corriger dans le plan')).toBeInTheDocument();
  });
});
