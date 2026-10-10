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

const conforme = (ordre: number, instruction: string) => ({
  ordre,
  instruction,
  resultatAttendu: 'ok',
  resultat: 'Conforme' as const,
  observation: 'fait',
  par: 'Claire',
  le: '2026-10-09T21:05:00Z',
});
const progression = () => within(screen.getByRole('list', { name: 'Progression de la résolution' }));

describe('ResolutionDeroule', () => {
  it('montre la progression et la première correction à faire, en tête, avec son action', () => {
    rendre(resolution());
    expect(progression().getByText('Correction 1/2').closest('[aria-current]')).toHaveAttribute('aria-current', 'step');
    expect(progression().getByText('Vérification finale')).toBeInTheDocument();
    const courante = screen.getByRole('group', { name: 'Correction 1 sur 2 : à faire maintenant' });
    expect(within(courante).getByRole('button', { name: 'Conforme' })).toBeEnabled();
    expect(screen.getByText('Correction 2 sur 2 : à venir')).toBeInTheDocument();
    expect(screen.getByText(/Vérification finale : après les corrections/)).toBeInTheDocument();
    expect(screen.getByText('Diagnostic', { selector: 'p' })).toBeInTheDocument();
    expect(screen.getAllByText('curl -i http://localhost/sante').length).toBeGreaterThan(0);
  });

  it('valide une correction conforme sans observation', async () => {
    const actions = rendre(resolution());
    await userEvent.click(screen.getByRole('button', { name: 'Conforme' }));
    expect(actions.controlerCorrection).toHaveBeenCalledWith(expect.objectContaining({ id: 'r1' }), 1, 'Conforme', '');
  });

  it('Échec : demande d’abord le constat, puis confirme (retour arrière)', async () => {
    const actions = rendre(resolution());
    await userEvent.click(screen.getByRole('button', { name: 'Échec' }));
    expect(screen.getByRole('alert')).toHaveTextContent('Décrivez ce que vous avez constaté');
    expect(actions.controlerCorrection).not.toHaveBeenCalled();
    await userEvent.type(screen.getByLabelText(/Ce que vous constatez/), 'Le service ne repart pas');
    await userEvent.click(screen.getByRole('button', { name: 'Échec' }));
    expect(screen.getByText('Cela déclenchera le retour arrière.')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: /Échec : déclencher le retour arrière/ }));
    expect(actions.controlerCorrection).toHaveBeenCalledWith(
      expect.objectContaining({ id: 'r1' }),
      1,
      'Echec',
      'Le service ne repart pas',
    );
  });

  it('après une correction conforme : la suivante passe en tête, la première est cochée', () => {
    const r = resolution();
    r.corrections[0] = conforme(1, '```\nsystemctl restart api\n```');
    rendre(r);
    expect(screen.getByRole('group', { name: 'Correction 2 sur 2 : à faire maintenant' })).toBeInTheDocument();
    expect(progression().getByText('Correction 2/2').closest('[aria-current]')).toHaveAttribute('aria-current', 'step');
    expect(
      within(screen.getByRole('list', { name: 'Étapes de la résolution' })).getByText(/Conforme/),
    ).toBeInTheDocument();
  });

  it('vérification finale : le constat est obligatoire, signalé au clic', async () => {
    const r = resolution();
    r.corrections = [conforme(1, 'a'), conforme(2, 'b')];
    const actions = rendre(r);
    const bloc = screen.getByRole('group', { name: 'Vérification finale : à faire maintenant' });
    await userEvent.click(within(bloc).getByRole('button', { name: 'Conforme' }));
    expect(within(bloc).getByRole('alert')).toHaveTextContent('Indiquez ce que vous avez constaté');
    expect(actions.controlerVerification).not.toHaveBeenCalled();
    await userEvent.type(within(bloc).getByLabelText(/Ce que vous constatez/), 'HTTP 200 reçu');
    await userEvent.click(within(bloc).getByRole('button', { name: 'Conforme' }));
    expect(actions.controlerVerification).toHaveBeenCalledWith(
      expect.objectContaining({ id: 'r1' }),
      'Conforme',
      'HTTP 200 reçu',
    );
  });

  it('sans droit : pas de bouton, explication', () => {
    rendre(resolution(), false);
    expect(screen.queryByRole('button', { name: 'Conforme' })).toBeNull();
    expect(screen.getByText(/Seul l’exécutant de l’étape ou le pilote/)).toBeInTheDocument();
  });
});
