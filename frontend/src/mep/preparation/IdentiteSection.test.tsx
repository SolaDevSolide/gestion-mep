import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { api } from '../../api';
import { MepContext } from '../../mepContext';
import { mep } from '../../test/fabrique';
import type { Mep } from '../../types';
import { TooltipProvider } from '../../ui/Tooltip';
import { IdentiteSection } from './IdentiteSection';

vi.mock('../../api', () => ({ api: { modifierIdentite: vi.fn(), changerTypeMep: vi.fn() } }));

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

describe('IdentiteSection : MEP simplifiée', () => {
  beforeEach(() => vi.mocked(api.changerTypeMep).mockReset());

  const simplifiee = (p: Partial<Mep> = {}) =>
    rendre({ type: 'Simplifiee', versionActuelle: '', versionCible: '', suppleantIds: ['p1', 'p2'], ...p });

  it('masque les versions et rappelle que les suppléants sont toutes les autres personnes', () => {
    simplifiee();
    expect(screen.queryByLabelText('Version cible')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Version actuelle')).not.toBeInTheDocument();
    const suppleants = screen.getByLabelText('Suppléants');
    expect(within(suppleants).getByText('Toutes les autres personnes de la MEP (2)')).toBeInTheDocument();
    expect(within(suppleants).queryByRole('combobox')).not.toBeInTheDocument();
  });

  it('passe en complète après confirmation', async () => {
    simplifiee();
    await userEvent.click(screen.getByRole('button', { name: 'Passer en MEP complète' }));
    const dialogue = screen.getByRole('dialog', { name: 'Passer en MEP complète ?' });
    expect(within(dialogue).getByText(/Les 2 autres personnes restent suppléantes/)).toBeInTheDocument();
    expect(within(dialogue).getByText(/inscrit au journal/)).toBeInTheDocument();
    await userEvent.click(within(dialogue).getByRole('button', { name: 'Passer en MEP complète' }));
    expect(api.changerTypeMep).toHaveBeenCalledWith('m1', { version: 1, type: 'Complete' });
  });

  it('une personne sans droit de pilotage ne change pas le type', () => {
    simplifiee({ droits: { ...droitsEdition, estPilote: false } });
    expect(screen.getByText('MEP simplifiée')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Passer en MEP complète' })).not.toBeInTheDocument();
  });
});

describe('IdentiteSection : passer une MEP complète en simplifiée', () => {
  beforeEach(() => vi.mocked(api.changerTypeMep).mockReset());

  it('avertit de ce qui sera supprimé et exige de cocher avant de confirmer', async () => {
    rendre({ versionActuelle: '1.0', versionCible: '2.0' });
    expect(screen.getByLabelText('Version cible')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Passer en MEP simplifiée' }));
    const dialogue = screen.getByRole('dialog', { name: 'Passer en MEP simplifiée ?' });
    expect(within(dialogue).getByText(/les versions actuelle et cible \(1\.0 → 2\.0\)/)).toBeInTheDocument();
    expect(within(dialogue).getByText(/les criticités des étapes/)).toBeInTheDocument();
    expect(within(dialogue).getByText(/les résolutions liées aux étapes/)).toBeInTheDocument();
    const confirmer = within(dialogue).getByRole('button', { name: 'Supprimer et passer en simplifiée' });
    expect(confirmer).toBeDisabled();
    await userEvent.click(within(dialogue).getByRole('checkbox', { name: /J’ai compris/ }));
    await userEvent.click(confirmer);
    expect(api.changerTypeMep).toHaveBeenCalledWith('m1', { version: 1, type: 'Simplifiee' });
  });

  it('n’apparaît pas pour un modèle', () => {
    rendre({ estModele: true });
    expect(screen.queryByRole('button', { name: 'Passer en MEP simplifiée' })).not.toBeInTheDocument();
  });

  it('n’apparaît pas pour un responsable technique qui n’est pas pilote', () => {
    rendre({ droits: { ...droitsEdition, estPilote: false } });
    expect(screen.queryByText('MEP complète')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Passer en MEP simplifiée' })).not.toBeInTheDocument();
  });
});
