import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { api } from '../api';
import { AuthContext } from '../auth';
import { utilisateur } from '../test/fabrique';
import { equipesTest, modeleTexte, valeursMep, variablesCatalogue } from '../test/textes';
import type { MepResume } from '../types';
import { ToastProvider } from '../ui/Toast';
import { TooltipProvider } from '../ui/Tooltip';
import ModeleTexteEditeurPage, { grouperVariables } from './ModeleTexteEditeurPage';
import ModelesTextePage from './ModelesTextePage';

vi.mock('../api', () => ({
  api: {
    equipes: vi.fn(),
    modelesTexte: vi.fn(),
    modeleTexte: vi.fn(),
    variablesModelesTexte: vi.fn(),
    variablesTexte: vi.fn(),
    meps: vi.fn(),
    creerModeleTexte: vi.fn(),
    modifierModeleTexte: vi.fn(),
    retablirModeleTexte: vi.fn(),
    supprimerModeleTexte: vi.fn(),
    modifierCommunicationEquipe: vi.fn(),
  },
}));

function rendre(chemin: string, estAdmin = false) {
  render(
    <MemoryRouter initialEntries={[chemin]}>
      <AuthContext.Provider value={{ utilisateur: utilisateur({ estAdmin, equipeIds: ['eq1'] }) } as never}>
        <ToastProvider>
          <TooltipProvider>
            <Routes>
              <Route path="/parametres/textes" element={<ModelesTextePage />} />
              <Route path="/parametres/textes/:id" element={<ModeleTexteEditeurPage />} />
            </Routes>
          </TooltipProvider>
        </ToastProvider>
      </AuthContext.Provider>
    </MemoryRouter>,
  );
}

const resume = {
  id: 'm1',
  outil: 'Portail',
  versionCible: '2.0',
  dateMep: null,
  creeLe: '2026-10-01T08:00:00Z',
} as MepResume;

describe('Paramètres › Modèles de texte', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(api.equipes).mockResolvedValue(equipesTest);
    vi.mocked(api.variablesModelesTexte).mockResolvedValue(variablesCatalogue);
    vi.mocked(api.variablesTexte).mockResolvedValue(valeursMep);
    vi.mocked(api.meps).mockResolvedValue([resume]);
    vi.mocked(api.modelesTexte).mockResolvedValue([
      modeleTexte({ modifie: true, modifiePar: 'Claire', modifieParId: 'u1' }),
      modeleTexte({ id: 'mt2', nom: 'Rappel le jour J', usage: 'Rappel' }),
      modeleTexte({
        id: 'mt3',
        nom: 'Prévenir l’astreinte',
        usage: 'Autre',
        fourni: false,
        objet: 'MEP {{application}}',
      }),
    ]);
  });

  it('présente les modèles de l’équipe : fournis non supprimables, rétablissables une fois modifiés', async () => {
    rendre('/parametres/textes');
    expect(await screen.findByText('Rappel le jour J')).toBeInTheDocument();
    expect(api.modelesTexte).toHaveBeenCalledWith('eq1');
    expect(screen.getAllByText('Fourni par défaut')).toHaveLength(2);
    expect(screen.getByText('Texte fourni, jamais modifié')).toBeInTheDocument();
    expect(screen.getByText(/Modifié par Claire le/)).toBeInTheDocument();
    expect(screen.getByText('support@exemple.invalid')).toBeInTheDocument();
    expect(screen.getByText('L’équipe Exploitation')).toBeInTheDocument();

    expect(screen.getByRole('button', { name: 'Rétablir Annonce aux utilisateurs' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Rétablir Rappel le jour J' })).toBeNull();
    expect(screen.queryByRole('button', { name: /Supprimer (Annonce|Rappel)/ })).toBeNull();
    expect(screen.getByRole('button', { name: 'Supprimer Prévenir l’astreinte' })).toBeInTheDocument();

    vi.mocked(api.retablirModeleTexte).mockResolvedValue(modeleTexte());
    await userEvent.click(screen.getByRole('button', { name: 'Rétablir Annonce aux utilisateurs' }));
    await userEvent.click(
      within(await screen.findByRole('dialog')).getByRole('button', { name: 'Rétablir le texte fourni' }),
    );
    await waitFor(() => expect(api.retablirModeleTexte).toHaveBeenCalledWith('mt1'));
  });

  it('recherche dans le nom, l’objet et le texte', async () => {
    rendre('/parametres/textes');
    await screen.findByText('Rappel le jour J');
    await userEvent.type(screen.getByLabelText('Rechercher un modèle'), 'astreinte');
    expect(screen.queryByText('Rappel le jour J')).toBeNull();
    expect(screen.getByText('Prévenir l’astreinte')).toBeInTheDocument();
  });

  it('règle le contact support et la signature de l’équipe', async () => {
    vi.mocked(api.modifierCommunicationEquipe).mockResolvedValue({ ...equipesTest[0], signature: 'L’exploitation' });
    rendre('/parametres/textes');
    await screen.findByText('Rappel le jour J');
    await userEvent.click(screen.getByRole('button', { name: 'Modifier' }));
    const dialogue = await screen.findByRole('dialog');
    await userEvent.type(within(dialogue).getByLabelText('Signature'), 'L’exploitation');
    await userEvent.click(within(dialogue).getByRole('button', { name: 'Enregistrer' }));
    await waitFor(() =>
      expect(api.modifierCommunicationEquipe).toHaveBeenCalledWith('eq1', {
        contactSupport: 'support@exemple.invalid',
        signature: 'L’exploitation',
      }),
    );
  });
});

describe('Éditeur d’un modèle de texte', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(api.equipes).mockResolvedValue(equipesTest);
    vi.mocked(api.variablesModelesTexte).mockResolvedValue(variablesCatalogue);
    vi.mocked(api.variablesTexte).mockResolvedValue(valeursMep);
    vi.mocked(api.meps).mockResolvedValue([resume]);
    vi.mocked(api.modeleTexte).mockResolvedValue(modeleTexte({ corps: 'Bonjour' }));
  });

  it('groupe les variables et les filtre par une recherche multi-termes', () => {
    expect(grouperVariables(variablesCatalogue, '').map((g) => g.groupe)).toEqual([
      'MEP',
      'Date et heure',
      'Communication',
      'Équipe',
    ]);
    expect(
      grouperVariables(variablesCatalogue, 'reference ticket').flatMap((g) => g.variables.map((v) => v.cle)),
    ).toEqual(['reference_demande']);
    expect(grouperVariables(variablesCatalogue, 'date heure')).toHaveLength(1);
  });

  it('insère une variable à l’emplacement du curseur et met à jour l’aperçu sur la MEP choisie', async () => {
    rendre('/parametres/textes/mt1');
    const texte = await screen.findByLabelText('Texte');
    expect(texte).toHaveValue('Bonjour');
    await userEvent.type(screen.getByLabelText('Rechercher une variable'), 'référence');
    expect(screen.queryByRole('button', { name: 'Application' })).toBeNull();
    const zone = texte as HTMLTextAreaElement;
    fireEvent.focus(zone);
    zone.setSelectionRange(7, 7);
    await userEvent.click(screen.getByRole('button', { name: 'Référence de la demande' }));
    expect(zone).toHaveValue('Bonjour{{reference_demande}}');
    expect(within(screen.getByRole('region', { name: 'Aperçu' })).getByText('BonjourCHG-1')).toBeInTheDocument();
    expect(api.variablesTexte).toHaveBeenCalledWith('m1', 'eq1');

    vi.mocked(api.modifierModeleTexte).mockResolvedValue(modeleTexte());
    await userEvent.click(screen.getByRole('button', { name: 'Enregistrer' }));
    await waitFor(() =>
      expect(api.modifierModeleTexte).toHaveBeenCalledWith(
        'mt1',
        expect.objectContaining({ version: 1, corps: 'Bonjour{{reference_demande}}' }),
      ),
    );
  });

  it('signale une variable inconnue et les informations absentes de la MEP', async () => {
    vi.mocked(api.modeleTexte).mockResolvedValue(modeleTexte({ corps: '{{aplication}} {{indisponibilite}}' }));
    rendre('/parametres/textes/mt1');
    expect(await screen.findByText(/Variable inconnue/)).toHaveTextContent('{{aplication}}');
    expect(await screen.findByText(/Non renseigné dans cette MEP/)).toHaveTextContent('indisponibilité prévue');
  });

  it('un modèle d’une autre équipe se consulte sans se modifier', async () => {
    vi.mocked(api.modeleTexte).mockResolvedValue(modeleTexte({ peutModifier: false }));
    rendre('/parametres/textes/mt1');
    expect(await screen.findByLabelText('Texte')).toBeDisabled();
    expect(screen.queryByRole('button', { name: 'Enregistrer' })).toBeNull();
    expect(screen.queryByLabelText('Rechercher une variable')).toBeNull();
  });
});
