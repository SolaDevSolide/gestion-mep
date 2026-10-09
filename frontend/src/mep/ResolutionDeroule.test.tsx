import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ResolutionDeroule, type ActionsResolution } from './ResolutionDeroule';
import { resolution } from '../test/fabrique';
import type { ExecutionResolution } from '../types';

function rendre(r: ExecutionResolution, peutAgir = true) {
  const actions: ActionsResolution = { controlerCorrection: vi.fn(), controlerVerification: vi.fn() };
  render(<ResolutionDeroule resolution={r} peutAgir={peutAgir} actions={actions} />);
  return actions;
}

const conforme = (ordre: number, instruction: string) => ({ ordre, instruction, resultatAttendu: 'ok', resultat: 'Conforme' as const, observation: 'fait', par: 'Claire', le: '2026-10-09T21:05:00Z' });

describe('ResolutionDeroule', () => {
  it('affiche le diagnostic puis la première correction à faire, les suivantes à venir', () => {
    rendre(resolution());
    expect(screen.getByText('Diagnostic', { selector: 'p' })).toBeInTheDocument();
    expect(screen.getAllByText('curl -i http://localhost/sante').length).toBeGreaterThan(0);
    expect(screen.getByText('Correction 1 sur 2')).toBeInTheDocument();
    expect(screen.getByText('Correction 2 sur 2 : à venir')).toBeInTheDocument();
    expect(screen.getByText(/Vérification finale : après les corrections/)).toBeInTheDocument();
  });

  it('valide une correction conforme sans observation', async () => {
    const actions = rendre(resolution());
    await userEvent.click(screen.getByRole('button', { name: 'Conforme' }));
    expect(actions.controlerCorrection).toHaveBeenCalledWith(expect.objectContaining({ id: 'r1' }), 1, 'Conforme', '');
  });

  it('exige une observation pour l’échec et prévient du retour arrière', async () => {
    const actions = rendre(resolution());
    const echec = screen.getByRole('button', { name: 'Échec' });
    expect(echec).toBeDisabled();
    expect(screen.getByText(/déclenche automatiquement le retour arrière/)).toBeInTheDocument();
    await userEvent.type(screen.getByLabelText(/Observation/), 'Le service ne repart pas');
    await userEvent.click(echec);
    expect(screen.getByText('Cela déclenchera le retour arrière.')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: /Échec : déclencher le retour arrière/ }));
    expect(actions.controlerCorrection).toHaveBeenCalledWith(expect.objectContaining({ id: 'r1' }), 1, 'Echec', 'Le service ne repart pas');
  });

  it('passe à la correction suivante quand la première est conforme', () => {
    const r = resolution();
    r.corrections[0] = conforme(1, 'systemctl restart api');
    rendre(r);
    expect(screen.getByText('Correction 2 sur 2')).toBeInTheDocument();
    expect(screen.queryByText('Correction 1 sur 2')).toBeNull();
    expect(screen.getByText('Correction 1', { exact: false, selector: 'span' })).toBeInTheDocument();
  });

  it('la vérification finale exige une observation même pour Conforme', async () => {
    const r = resolution();
    r.corrections = [conforme(1, 'a'), conforme(2, 'b')];
    const actions = rendre(r);
    const bloc = screen.getByText('Vérification finale').closest('div')!.parentElement!;
    const conformeBtn = within(bloc).getByRole('button', { name: 'Conforme' });
    expect(conformeBtn).toBeDisabled();
    await userEvent.type(screen.getByLabelText(/Observation/), 'HTTP 200 reçu');
    await userEvent.click(conformeBtn);
    expect(actions.controlerVerification).toHaveBeenCalledWith(expect.objectContaining({ id: 'r1' }), 'Conforme', 'HTTP 200 reçu');
  });

  it('un non-exécutant ne peut rien valider', () => {
    rendre(resolution(), false);
    expect(screen.getByRole('button', { name: 'Conforme' })).toBeDisabled();
    expect(screen.getByText(/Seul l’exécutant/)).toBeInTheDocument();
  });
});
