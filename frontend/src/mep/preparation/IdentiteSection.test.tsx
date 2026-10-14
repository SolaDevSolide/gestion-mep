import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { api } from '../../api';
import { MepContext } from '../../mepContext';
import { mep } from '../../test/fabrique';
import type { Mep } from '../../types';
import { TooltipProvider } from '../../ui/Tooltip';
import { IdentiteSection } from './IdentiteSection';

vi.mock('../../api', () => ({ api: { modifierIdentite: vi.fn() } }));

const droitsEdition = { ...mep().droits, peutEditerContenu: true, peutModifierPlan: true, estPilote: true };

function rendre(p: Partial<Mep> = {}) {
  const run = vi.fn(async (action?: () => Promise<unknown>) => {
    await action?.();
    return true;
  });
  render(
    <MepContext.Provider value={{ mep: mep({ phase: 'Preparation', droits: droitsEdition, ...p }), run } as never}>
      <TooltipProvider>
        <IdentiteSection />
      </TooltipProvider>
    </MepContext.Provider>,
  );
  return { run };
}

const caseRepli = () => screen.getByRole('checkbox', { name: /Plan de retour arrière/ });

describe('IdentiteSection : plan de retour arrière', () => {
  beforeEach(() => vi.mocked(api.modifierIdentite).mockReset());

  it('est coché par défaut, avec une échéance facultative', () => {
    rendre();
    expect(caseRepli()).toBeChecked();
    expect(screen.getByLabelText('Échéance de retour arrière (facultative)')).toBeInTheDocument();
    expect(screen.getByText(/Sans échéance : ni compte à rebours ni déclenchement automatique/)).toBeInTheDocument();
  });

  it('demande confirmation avant de désactiver ; annuler garde le plan', async () => {
    rendre();
    await userEvent.click(caseRepli());
    const dialogue = screen.getByRole('dialog', { name: 'Désactiver le plan de retour arrière ?' });
    expect(within(dialogue).getByText(/L’onglet Retour arrière est masqué/)).toBeInTheDocument();
    expect(within(dialogue).getByText(/ne réclame plus de plan ni d’échéance/)).toBeInTheDocument();
    expect(within(dialogue).getByText(/Aucun déclenchement automatique/)).toBeInTheDocument();
    expect(within(dialogue).queryByText(/journal/i)).not.toBeInTheDocument();
    await userEvent.click(within(dialogue).getByRole('button', { name: 'Annuler' }));
    expect(caseRepli()).toBeChecked();
  });

  it('désactive après confirmation, masque l’échéance et l’enregistre', async () => {
    rendre();
    await userEvent.click(caseRepli());
    await userEvent.click(screen.getByRole('button', { name: 'Désactiver' }));
    expect(caseRepli()).not.toBeChecked();
    expect(screen.queryByLabelText(/Échéance de retour arrière/)).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Enregistrer' }));
    expect(api.modifierIdentite).toHaveBeenCalledWith('m1', expect.objectContaining({ retourArriereActive: false }));
  });

  it('se réactive sans confirmation', async () => {
    rendre({ retourArriereActive: false });
    expect(caseRepli()).not.toBeChecked();
    await userEvent.click(caseRepli());
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(caseRepli()).toBeChecked();
    expect(screen.getByLabelText('Échéance de retour arrière (facultative)')).toBeInTheDocument();
  });

  it('un modèle garde la case mais n’a pas d’échéance', () => {
    rendre({ estModele: true });
    expect(caseRepli()).toBeChecked();
    expect(screen.queryByLabelText(/Échéance de retour arrière/)).not.toBeInTheDocument();
  });
});
