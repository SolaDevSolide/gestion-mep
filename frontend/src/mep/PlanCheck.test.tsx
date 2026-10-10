import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { PlanCheck } from './PlanCheck';
import { mep } from '../test/fabrique';

describe('PlanCheck', () => {
  it('affiche les avertissements à part des anomalies, sans les compter comme anomalies', async () => {
    render(
      <PlanCheck
        mep={mep({
          erreursPlan: ['Renseigner le pilote.'],
          avertissements: ['Aucun responsable technique renseigné.'],
        })}
      />,
    );
    const bouton = screen.getByRole('button', { name: 'Vérifier le plan : 1 anomalie(s), 1 avertissement(s)' });
    await userEvent.click(bouton);
    expect(screen.getByRole('region', { name: 'Anomalies' })).toHaveTextContent('Renseigner le pilote.');
    const avert = screen.getByRole('region', { name: 'Avertissements' });
    expect(avert).toHaveTextContent('Aucun responsable technique renseigné.');
    expect(screen.getByRole('region', { name: 'Anomalies' })).not.toHaveTextContent('Aucun responsable technique');
  });

  it('plan sans anomalie mais avec avertissement : le plan reste complet', async () => {
    render(
      <PlanCheck
        mep={mep({
          avertissements: [
            'Alice est à la fois responsable technique et pilote : prévoir un suppléant qui connaît le plan.',
          ],
        })}
      />,
    );
    await userEvent.click(screen.getByRole('button', { name: /Vérifier le plan/ }));
    expect(screen.getByText('Le plan est complet : aucune anomalie.')).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Avertissements' })).toHaveTextContent('prévoir un suppléant');
  });

  it('rien d’autre qu’un plan complet sans avertissement', async () => {
    render(<PlanCheck mep={mep()} />);
    await userEvent.click(screen.getByRole('button', { name: /Vérifier le plan/ }));
    expect(screen.queryByRole('region', { name: 'Avertissements' })).toBeNull();
  });
});
