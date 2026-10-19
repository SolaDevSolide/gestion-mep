import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { api } from '../../api';
import { AuthContext } from '../../auth';
import { MepContext } from '../../mepContext';
import { mep, utilisateur } from '../../test/fabrique';
import { equipesTest, modeleTexte, valeursMep, variablesCatalogue } from '../../test/textes';
import type { Mep } from '../../types';
import { ToastProvider } from '../../ui/Toast';
import { TooltipProvider } from '../../ui/Tooltip';
import { CommunicationSection } from './CommunicationSection';

vi.mock('../../api', () => ({
  api: {
    modifierCommunication: vi.fn(),
    equipes: vi.fn(),
    variablesModelesTexte: vi.fn(),
    variablesTexte: vi.fn(),
    modelesTexte: vi.fn(),
  },
}));

function rendre(m: Mep, estAdmin = false) {
  const run = vi.fn(async (action?: () => Promise<unknown>) => {
    await action?.();
    return true;
  });
  render(
    <MemoryRouter>
      <AuthContext.Provider value={{ utilisateur: utilisateur({ estAdmin }) } as never}>
        <MepContext.Provider value={{ mep: m, run } as never}>
          <ToastProvider>
            <TooltipProvider>
              <CommunicationSection />
            </TooltipProvider>
          </ToastProvider>
        </MepContext.Provider>
      </AuthContext.Provider>
    </MemoryRouter>,
  );
  return { run };
}

const enPreparation = (p: Partial<Mep> = {}) =>
  mep({
    phase: 'Preparation',
    version: 7,
    revisions: { ...mep().revisions, communication: 3 },
    droits: { ...mep().droits, peutEditerContenu: true, peutModifierPlan: true },
    ...p,
  });

describe('Préparation › Communication', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(api.equipes).mockResolvedValue(equipesTest);
    vi.mocked(api.variablesModelesTexte).mockResolvedValue(variablesCatalogue);
    vi.mocked(api.variablesTexte).mockResolvedValue(valeursMep);
    vi.mocked(api.modelesTexte).mockResolvedValue([
      modeleTexte(),
      modeleTexte({ id: 'mt2', nom: 'Demande de MEP', usage: 'DemandeMep', corps: 'Demande {{reference_demande}}' }),
    ]);
    vi.mocked(api.modifierCommunication).mockResolvedValue(mep());
  });

  it('enregistre les champs facultatifs avec la révision de la section (#96)', async () => {
    const { run } = rendre(enPreparation());
    await userEvent.type(screen.getByLabelText('Référence de la demande'), 'CHG-42');
    await userEvent.click(screen.getByRole('radio', { name: 'Élevé' }));
    await userEvent.click(screen.getByRole('checkbox', { name: /Validée par les utilisateurs/ }));
    await userEvent.type(screen.getByLabelText('Validée par'), 'Claire');
    await userEvent.click(screen.getByRole('button', { name: 'Enregistrer' }));
    await waitFor(() => expect(run).toHaveBeenCalled());
    expect(api.modifierCommunication).toHaveBeenCalledWith(
      'm1',
      expect.objectContaining({
        revision: 3,
        referenceDemande: 'CHG-42',
        niveauRisque: 'Eleve',
        valideeParUtilisateurs: true,
        valideePar: 'Claire',
      }),
    );
  });

  it('reprend l’indisponibilité prévue pour la durée de l’arrêt applicatif', () => {
    rendre(enPreparation({ indisponibiliteMinutes: null }));
    expect(screen.getByText('non renseignée')).toBeInTheDocument();
  });

  it('après le Go, le pilote modifie encore ; les autres lisent seulement', () => {
    const execution = mep({ phase: 'Execution' });
    rendre({ ...execution, droits: { ...execution.droits, estPilote: true } });
    expect(screen.getByLabelText('Référence de la demande')).toBeEnabled();
    expect(screen.getByText(/le pilote complète encore la communication/)).toBeInTheDocument();
  });

  it('lecture seule après le Go pour qui n’est pas pilote, et pour tous une fois la MEP clôturée', () => {
    rendre(mep({ phase: 'Execution' }));
    expect(screen.getByLabelText('Référence de la demande')).toBeDisabled();
    expect(screen.queryByRole('button', { name: 'Enregistrer' })).toBeNull();
  });

  it('affiche le texte rempli, signale les manques et copie le texte simple en un clic', async () => {
    const ecrire = vi.fn(async () => undefined);
    Object.defineProperty(navigator, 'clipboard', { value: { writeText: ecrire }, configurable: true });
    rendre(enPreparation());
    const texte = await screen.findByLabelText('Texte rempli');
    expect(within(texte).getByText('Plus rapide')).toBeInTheDocument();
    expect(screen.getByText(/Une information manque/)).toHaveTextContent('indisponibilité prévue');
    // Équipe proposée : celle du pilote.
    expect(screen.getByRole('option', { name: 'Exploitation (équipe du pilote)' })).toBeInTheDocument();

    await userEvent.click(screen.getByRole('radio', { name: 'Texte simple' }));
    await userEvent.click(screen.getByRole('button', { name: 'Copier le texte simple' }));
    await waitFor(() => expect(ecrire).toHaveBeenCalled());
    expect(ecrire).toHaveBeenCalledWith(
      'Bonjour,\n\nCe qui change\n- Plus rapide\n- Plus simple\n\nIndisponibilité : [Indisponibilité prévue à compléter]\n\nL’équipe Exploitation',
    );
    expect(await screen.findByText('Texte simple copié.')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Demande de MEP' }));
    expect(within(screen.getByLabelText('Texte rempli')).getByText('Demande CHG-1')).toBeInTheDocument();
  });

  it('change d’équipe pour prendre ses modèles', async () => {
    rendre(enPreparation());
    await screen.findByLabelText('Texte rempli');
    await userEvent.selectOptions(screen.getByLabelText('Équipe'), 'eq2');
    await waitFor(() => expect(api.modelesTexte).toHaveBeenLastCalledWith('eq2'));
    expect(api.variablesTexte).toHaveBeenLastCalledWith('m1', 'eq2');
  });
});
