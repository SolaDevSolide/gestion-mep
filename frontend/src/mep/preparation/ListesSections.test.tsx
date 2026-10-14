import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { api } from '../../api';
import { MepContext } from '../../mepContext';
import { mep, utilisateur } from '../../test/fabrique';
import { TooltipProvider } from '../../ui/Tooltip';
import { PersonnesSection } from './ListesSections';

vi.mock('../../api', () => ({
  api: {
    utilisateurs: vi.fn(),
    equipes: vi.fn(async () => []),
    ajouterParticipant: vi.fn(),
    modifierParticipant: vi.fn(),
    catalogueSystemes: vi.fn(async () => []),
  },
}));

function rendre() {
  vi.mocked(api.utilisateurs).mockResolvedValue([
    utilisateur({ id: 'u7', login: 'dana', nomAffiche: 'Dana', role: 'DBA', contact: 'dana@x.fr' }),
  ]);
  const run = vi.fn(async (action?: () => Promise<unknown>) => {
    await action?.();
    return true;
  });
  const m = mep({ phase: 'Preparation', droits: { ...mep().droits, peutEditerContenu: true, peutModifierPlan: true } });
  render(
    <MepContext.Provider value={{ mep: m, run } as never}>
      <TooltipProvider>
        <PersonnesSection />
      </TooltipProvider>
    </MepContext.Provider>,
  );
  return { run };
}

describe('Personnes : ajout depuis un compte', () => {
  beforeEach(() => vi.clearAllMocks());

  it('le champ Compte vient en premier', async () => {
    rendre();
    await userEvent.click(screen.getByRole('button', { name: 'Ajouter' }));
    const champs = (await screen.findAllByRole('dialog'))[0].querySelectorAll('select, input');
    expect(champs[0]).toHaveAccessibleName('Compte');
  });

  it('choisir un compte pré-remplit nom, rôle et contact depuis son profil, modifiables ensuite', async () => {
    const { run } = rendre();
    await userEvent.click(screen.getByRole('button', { name: 'Ajouter' }));
    await screen.findByRole('option', { name: /Dana/ });
    await userEvent.selectOptions(screen.getByLabelText('Compte'), 'u7');
    expect(screen.getByLabelText(/^Nom/)).toHaveValue('Dana');
    expect(screen.getByLabelText('Rôle')).toHaveValue('DBA');
    expect(screen.getByLabelText('Contact')).toHaveValue('dana@x.fr');
    await userEvent.clear(screen.getByLabelText('Rôle'));
    await userEvent.type(screen.getByLabelText('Rôle'), 'Pilote');
    const boutons = screen.getAllByRole('button', { name: 'Ajouter' });
    await userEvent.click(boutons[boutons.length - 1]);
    await waitFor(() => expect(run).toHaveBeenCalled());
    expect(api.ajouterParticipant).toHaveBeenCalledWith('m1', {
      nom: 'Dana',
      role: 'Pilote',
      contact: 'dana@x.fr',
      utilisateurId: 'u7',
    });
  });

  it('filtre les comptes par équipe et ne propose plus les comptes désactivés', async () => {
    vi.mocked(api.equipes).mockResolvedValueOnce([
      {
        id: 'e1',
        nom: 'Exploitation',
        description: '',
        membreIds: ['u7'],
        creeeLe: '2026-10-01T08:00:00Z',
        contactSupport: '',
        signature: '',
      },
    ]);
    vi.mocked(api.utilisateurs).mockResolvedValue([
      utilisateur({ id: 'u7', login: 'dana', nomAffiche: 'Dana', equipeIds: ['e1'] }),
      utilisateur({ id: 'u8', login: 'eric', nomAffiche: 'Éric' }),
      utilisateur({ id: 'u9', login: 'parti', nomAffiche: 'Parti', actif: false }),
    ]);
    const run = vi.fn(async () => true);
    const m = mep({
      phase: 'Preparation',
      droits: { ...mep().droits, peutEditerContenu: true, peutModifierPlan: true },
    });
    render(
      <MepContext.Provider value={{ mep: m, run } as never}>
        <TooltipProvider>
          <PersonnesSection />
        </TooltipProvider>
      </MepContext.Provider>,
    );
    await userEvent.click(screen.getByRole('button', { name: 'Ajouter' }));
    await screen.findByRole('option', { name: /Éric/ });
    expect(screen.queryByRole('option', { name: /Parti/ })).toBeNull();
    await userEvent.selectOptions(screen.getByLabelText('Équipe'), 'e1');
    expect(screen.getByRole('option', { name: /Dana/ })).toBeInTheDocument();
    expect(screen.queryByRole('option', { name: /Éric/ })).toBeNull();
  });
});
