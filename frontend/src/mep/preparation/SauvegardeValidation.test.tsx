import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { api } from '../../api';
import { MepContext } from '../../mepContext';
import { mep } from '../../test/fabrique';
import type { Mep, Systeme } from '../../types';
import { TooltipProvider } from '../../ui/Tooltip';
import { SauvegardeSection, ValidationSection } from './SauvegardeValidation';

vi.mock('../../api', () => ({ api: { modifierSauvegardeSysteme: vi.fn(), modifierSauvegarde: vi.fn(), criteresGoNoGo: vi.fn(async () => []) } }));
vi.mock('./Approbations', () => ({ ApprobationsBloc: () => null }));

function rendre(m: Mep, composant: React.ReactNode) {
  const run = vi.fn(async (action?: () => Promise<unknown>) => { await action?.(); return true; });
  render(<MepContext.Provider value={{ mep: m, run } as never}><TooltipProvider>{composant}</TooltipProvider></MepContext.Provider>);
  return { run };
}

const systeme = (id: string, nom: string, sauvegarde: Partial<Systeme['sauvegarde']> = {}): Systeme => ({
  ...mep().systemes[0], id, nom, sauvegarde: { statut: 'ARenseigner', moment: 'AvantMep', contenu: '', justification: '', ...sauvegarde },
});
const droitsEdition = { ...mep().droits, peutEditerContenu: true, peutModifierPlan: true };

describe('Sauvegarde par système', () => {
  it('liste un bloc par système de la MEP et signale ceux à renseigner', () => {
    rendre(mep({ systemes: [systeme('s1', 'BDD'), systeme('s2', 'API', { statut: 'Requise' })], droits: droitsEdition }), <SauvegardeSection />);
    const lignes = within(screen.getByRole('list', { name: 'Sauvegarde par système' })).getAllByRole('listitem', { name: /^Sauvegarde de / });
    expect(lignes).toHaveLength(2);
    expect(lignes[0]).toHaveTextContent('BDD doit-il être sauvegardé');
    expect(lignes[1]).not.toHaveTextContent('doit-il être sauvegardé');
  });

  it('« Non requise » exige une justification avant d’enregistrer', async () => {
    const { run } = rendre(mep({ systemes: [systeme('s1', 'BDD')], droits: droitsEdition }), <SauvegardeSection />);
    const ligne = screen.getByRole('listitem', { name: 'Sauvegarde de BDD' });
    await userEvent.click(within(ligne).getByRole('radio', { name: 'Non requise' }));
    const enregistrer = within(ligne).getByRole('button', { name: 'Enregistrer' });
    expect(enregistrer).toBeDisabled();
    await userEvent.type(within(ligne).getByLabelText(/Justification/), 'Service sans état');
    expect(enregistrer).toBeEnabled();
    await userEvent.click(enregistrer);
    await waitFor(() => expect(run).toHaveBeenCalledTimes(1));
    expect(api.modifierSauvegardeSysteme).toHaveBeenCalledWith('m1', 's1', { statut: 'NonRequise', moment: 'AvantMep', contenu: '', justification: 'Service sans état', version: 1 });
  });

  it('« Requise » demande le moment et le contenu ; « pendant la MEP » renvoie vers une étape de type Sauvegarde', async () => {
    const { run } = rendre(mep({
      systemes: [systeme('s1', 'BDD')], droits: droitsEdition,
      erreursPlan: ['Sauvegarde de BDD pendant la MEP : ajouter une étape de déploiement de type Sauvegarde sur ce système.'],
    }), <SauvegardeSection />);
    const ligne = screen.getByRole('listitem', { name: 'Sauvegarde de BDD' });
    await userEvent.click(within(ligne).getByRole('radio', { name: 'Requise' }));
    await userEvent.type(within(ligne).getByLabelText('Quoi et comment ?, bloc 1 (texte)'), 'pg_dump');
    await userEvent.click(within(ligne).getByRole('radio', { name: 'Pendant la MEP' }));
    expect(within(ligne).getByText('Aucune étape de sauvegarde sur ce système')).toBeInTheDocument();
    await userEvent.click(within(ligne).getByRole('button', { name: 'Enregistrer' }));
    await waitFor(() => expect(run).toHaveBeenCalled());
    expect(api.modifierSauvegardeSysteme).toHaveBeenCalledWith('m1', 's1', { statut: 'Requise', moment: 'PendantMep', contenu: 'pg_dump', justification: '', version: 1 });
  });

  it('bandeau d’avertissement quand aucun système n’est sauvegardé', () => {
    rendre(mep({ systemes: [systeme('s1', 'BDD', { statut: 'NonRequise', justification: 'x' })] }), <SauvegardeSection />);
    expect(screen.getByText('Aucun système n’est sauvegardé')).toBeInTheDocument();
  });

  it('pas de bandeau si au moins un système est sauvegardé', () => {
    rendre(mep({ systemes: [systeme('s1', 'BDD', { statut: 'NonRequise', justification: 'x' }), systeme('s2', 'API', { statut: 'Requise' })] }), <SauvegardeSection />);
    expect(screen.queryByText('Aucun système n’est sauvegardé')).toBeNull();
  });

  it('n’affiche plus le mode global ni les cases structure / données / configuration', () => {
    rendre(mep({ systemes: [systeme('s1', 'BDD')], droits: droitsEdition }), <SauvegardeSection />);
    expect(screen.queryByText('Quand la sauvegarde est-elle faite ?')).toBeNull();
    expect(screen.queryByRole('checkbox', { name: 'Structure' })).toBeNull();
    expect(screen.getByRole('checkbox', { name: /Restauration testée/ })).toBeInTheDocument();
  });

  it('hors préparation : lecture seule', () => {
    rendre(mep({ systemes: [systeme('s1', 'BDD', { statut: 'Requise' })] }), <SauvegardeSection />);
    expect(screen.getByRole('radio', { name: 'Requise' })).toBeDisabled();
  });
});

describe('Validation : démarrage autorisé', () => {
  it('explique que le Go coche la case et propose « Donner le Go → » au pilote en préparation', () => {
    rendre(mep({ phase: 'Preparation', droits: { ...droitsEdition, estPilote: true } }), <ValidationSection />);
    expect(screen.getByText('Se coche automatiquement au Go, donné par le pilote ou un suppléant.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Donner le Go →' })).toBeInTheDocument();
  });

  it('pas de lien pour un non-pilote', () => {
    rendre(mep({ phase: 'Preparation', droits: droitsEdition }), <ValidationSection />);
    expect(screen.queryByRole('button', { name: 'Donner le Go →' })).toBeNull();
  });

  it('le lien ouvre le dialogue Go / no-go', async () => {
    rendre(mep({ phase: 'Preparation', droits: { ...droitsEdition, estPilote: true } }), <ValidationSection />);
    await userEvent.click(screen.getByRole('button', { name: 'Donner le Go →' }));
    expect(await screen.findByRole('dialog')).toBeInTheDocument();
  });
});
