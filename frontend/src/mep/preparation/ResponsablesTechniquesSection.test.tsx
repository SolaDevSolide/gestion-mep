import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MepContext } from '../../mepContext';
import { mep } from '../../test/fabrique';
import { TooltipProvider } from '../../ui/Tooltip';
import { ResponsablesTechniquesSection } from './ResponsablesTechniquesSection';
import type { Mep } from '../../types';

vi.mock('../../api', () => ({ api: { modifierResponsablesTechniques: vi.fn() } }));

function rendre(m: Mep) {
  const run = vi.fn(async () => true);
  render(<MepContext.Provider value={{ mep: m, run } as never}><TooltipProvider><ResponsablesTechniquesSection /></TooltipProvider></MepContext.Provider>);
  return { run };
}

describe('Section Responsables techniques', () => {
  it('liste les responsables avec périmètre, systèmes, disponibilité et contact', () => {
    rendre(mep({
      responsablesTechniques: [{ participantId: 'p2', systemeIds: ['s1'], perimetre: 'API de facturation', disponibilite: 'Absent', contactJourJ: '06 00 00 00 00' }],
    }));
    const ligne = screen.getByRole('listitem');
    expect(ligne).toHaveTextContent('Bob');
    expect(ligne).toHaveTextContent('Absent');
    expect(ligne).toHaveTextContent('API de facturation');
    expect(ligne).toHaveTextContent('06 00 00 00 00');
    expect(within(ligne).getByLabelText('Systèmes développés')).toHaveTextContent('API');
  });

  it('lecture seule sans droit d’édition : pas de bouton Ajouter', () => {
    rendre(mep());
    expect(screen.queryByRole('button', { name: /Ajouter/ })).toBeNull();
    expect(screen.getByText('Aucun responsable technique')).toBeInTheDocument();
  });

  it('ajoute un responsable technique : un participant déjà responsable n’est plus proposé', async () => {
    const m = mep({
      responsablesTechniques: [{ participantId: 'p2', systemeIds: [], perimetre: '', disponibilite: 'Joignable', contactJourJ: '' }],
      droits: { ...mep().droits, peutEditerContenu: true, peutModifierPlan: true },
    });
    const { run } = rendre(m);
    await userEvent.click(screen.getByRole('button', { name: /Ajouter/ }));
    const select = screen.getByLabelText(/Participant/);
    expect(within(select).queryByRole('option', { name: /Bob/ })).toBeNull();
    expect(within(select).getByRole('option', { name: /Claire/ })).toBeInTheDocument();
    await userEvent.type(screen.getByLabelText('Contact le jour J'), '0102');
    await userEvent.click(screen.getByRole('radio', { name: 'Absent' }));
    const boutons = screen.getAllByRole('button', { name: 'Ajouter' });
    await userEvent.click(boutons[boutons.length - 1]);
    expect(run).toHaveBeenCalledTimes(1);
  });
});
