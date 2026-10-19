import { fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { api } from '../../api';
import { MepContext } from '../../mepContext';
import { mep } from '../../test/fabrique';
import type { Mep } from '../../types';
import { TooltipProvider } from '../../ui/Tooltip';
import { IdentiteSection } from './IdentiteSection';

vi.mock('../../api', () => ({
  api: { modifierIdentite: vi.fn(), changerTypeMep: vi.fn(), modifierEcheance: vi.fn() },
}));

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

  it('est coché par défaut, avec une échéance facultative affichée dans le fuseau de la MEP', () => {
    rendre();
    expect(caseRepli()).toBeChecked();
    const echeance = screen.getByLabelText('Échéance de retour arrière');
    // 23 h 30 UTC le 9 octobre = 1 h 30 le 10 à Paris (heure d'été).
    expect(within(echeance).getByText('10/10/2026 01:30 (Europe/Paris)')).toBeInTheDocument();
    expect(
      within(echeance).getByText(/en recette, elle est seulement signalée et le pilote décide/),
    ).toBeInTheDocument();
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
    expect(screen.getByLabelText('Échéance de retour arrière')).toBeInTheDocument();
  });

  it('un modèle garde la case mais n’a pas d’échéance', () => {
    rendre({ estModele: true });
    expect(caseRepli()).toBeChecked();
    expect(screen.queryByLabelText(/Échéance de retour arrière/)).not.toBeInTheDocument();
  });
});

describe('IdentiteSection : modifier l’échéance (#94)', () => {
  beforeEach(() => vi.mocked(api.modifierEcheance).mockReset());
  const droitsEcheance = { ...droitsEdition, peutEditerContenu: false, peutModifierEcheance: true };

  it('sans le droit, l’échéance se lit sans bouton', () => {
    rendre({ phase: 'Execution', droits: { ...droitsEdition, peutEditerContenu: false } });
    expect(screen.queryByRole('button', { name: /échéance/ })).not.toBeInTheDocument();
  });

  it('se saisit à l’heure du fuseau de la MEP, avec un motif obligatoire', async () => {
    rendre({ phase: 'Execution', droits: droitsEcheance });
    await userEvent.click(screen.getByRole('button', { name: 'Modifier l’échéance' }));
    const dialogue = screen.getByRole('dialog', { name: 'Échéance de retour arrière' });
    expect(within(dialogue).getByText(/déclenche le retour arrière automatiquement/)).toBeInTheDocument();
    const saisie = within(dialogue).getByLabelText('Nouvelle échéance (heure de Europe/Paris)');
    expect(saisie).toHaveValue('2026-10-10T01:30');
    const enregistrer = within(dialogue).getByRole('button', { name: 'Enregistrer l’échéance' });
    expect(enregistrer).toBeDisabled(); // inchangée
    fireEvent.change(saisie, { target: { value: '2026-10-10T03:00' } });
    expect(enregistrer).toBeDisabled(); // motif manquant
    await userEvent.type(within(dialogue).getByLabelText('Motif (obligatoire)'), 'Migration plus longue');
    await userEvent.click(enregistrer);
    expect(api.modifierEcheance).toHaveBeenCalledWith('m1', {
      revision: 0,
      echeanceRepli: '2026-10-10T01:00:00.000Z',
      motif: 'Migration plus longue',
    });
  });

  it('se retire avec un motif ; en recette, le dialogue rappelle que le pilote décide', async () => {
    rendre({ phase: 'Recette', droits: droitsEcheance });
    await userEvent.click(screen.getByRole('button', { name: 'Modifier l’échéance' }));
    const dialogue = screen.getByRole('dialog', { name: 'Échéance de retour arrière' });
    expect(within(dialogue).getByText(/seulement signalée : le pilote décide/)).toBeInTheDocument();
    await userEvent.click(within(dialogue).getByRole('checkbox', { name: /Retirer l’échéance/ }));
    await userEvent.type(within(dialogue).getByLabelText('Motif (obligatoire)'), 'Plus nécessaire');
    await userEvent.click(within(dialogue).getByRole('button', { name: 'Enregistrer l’échéance' }));
    expect(api.modifierEcheance).toHaveBeenCalledWith('m1', {
      revision: 0,
      echeanceRepli: null,
      motif: 'Plus nécessaire',
    });
  });

  it('la première saisie en préparation n’exige pas de motif', async () => {
    rendre({ echeanceRepli: null, droits: { ...droitsEdition, peutModifierEcheance: true } });
    await userEvent.click(screen.getByRole('button', { name: 'Fixer l’échéance' }));
    const dialogue = screen.getByRole('dialog', { name: 'Échéance de retour arrière' });
    fireEvent.change(within(dialogue).getByLabelText(/Nouvelle échéance/), { target: { value: '2026-10-23T23:00' } });
    expect(within(dialogue).getByLabelText('Motif (facultatif)')).toBeInTheDocument();
    await userEvent.click(within(dialogue).getByRole('button', { name: 'Enregistrer l’échéance' }));
    expect(api.modifierEcheance).toHaveBeenCalledWith('m1', {
      revision: 0,
      echeanceRepli: '2026-10-23T21:00:00.000Z',
      motif: '',
    });
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
