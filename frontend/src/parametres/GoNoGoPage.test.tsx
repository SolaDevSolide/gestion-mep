import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { api } from '../api';
import { AuthContext } from '../auth';
import { utilisateur } from '../test/fabrique';
import type { CritereGoNoGo } from '../types';
import { TooltipProvider } from '../ui/Tooltip';
import GoNoGoPage from './GoNoGoPage';

const critere = (id: string, libelle: string, ordre: number, actif = true): CritereGoNoGo => ({
  id,
  libelle,
  ordre,
  actif,
});

// Volontairement dans le désordre : la page les présente par ordre.
const CRITERES = [
  critere('c3', 'Fenêtre de maintenance confirmée', 3, false),
  critere('c1', 'Équipe présente et joignable', 1),
  critere('c2', 'Sauvegarde vérifiée', 2),
];

vi.mock('../api', () => ({
  api: {
    criteresGoNoGo: vi.fn(),
    parametres: vi.fn(),
    modifierParametres: vi.fn(async () => ({})),
    creerCritere: vi.fn(async () => ({})),
    modifierCritere: vi.fn(async () => ({})),
    supprimerCritere: vi.fn(async () => undefined),
  },
}));

function rendre(estAdmin = true) {
  render(
    <AuthContext.Provider value={{ utilisateur: utilisateur({ estAdmin }) } as never}>
      <TooltipProvider>
        <GoNoGoPage />
      </TooltipProvider>
    </AuthContext.Provider>,
  );
}

const lignes = () => within(screen.getByRole('list')).getAllByRole('listitem');

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(api.criteresGoNoGo).mockResolvedValue(CRITERES);
  vi.mocked(api.parametres).mockResolvedValue({ approbationsMinimum: 1 });
});

describe('GoNoGoPage : critères de go / no-go et approbations minimum', () => {
  it('lecture seule : critères dans l’ordre, inactif barré, aucune action ni saisie', async () => {
    rendre(false);
    expect(await screen.findByText('Équipe présente et joignable')).toBeInTheDocument();
    expect(lignes().map((l) => l.textContent)).toEqual([
      '1Équipe présente et joignableActif',
      '2Sauvegarde vérifiéeActif',
      '3Fenêtre de maintenance confirméeInactif',
    ]);
    expect(screen.getByText('Fenêtre de maintenance confirmée')).toHaveClass('line-through');
    expect(screen.queryByRole('button', { name: 'Nouveau critère' })).toBeNull();
    expect(screen.queryByRole('button', { name: /^Monter / })).toBeNull();
    expect(screen.queryByRole('button', { name: /^Supprimer / })).toBeNull();
    expect(await screen.findByLabelText('Approbations minimum')).toBeDisabled();
    expect(screen.getByLabelText('Approbations minimum')).toHaveValue(1);
    expect(screen.getByText(/seuls les administrateurs peuvent modifier ce paramètre/)).toBeInTheDocument();
  });

  it('approbations minimum : enregistrable seulement si la valeur change et reste un entier positif ou nul', async () => {
    rendre();
    const champ = await screen.findByLabelText('Approbations minimum');
    await waitFor(() => expect(champ).toBeEnabled());
    // Le premier « Enregistrer » est celui des approbations (avant la liste des critères).
    const enregistrer = screen.getByRole('button', { name: 'Enregistrer' });
    expect(enregistrer).toBeDisabled();
    await userEvent.clear(champ);
    await userEvent.type(champ, '-1');
    expect(enregistrer).toBeDisabled();
    await userEvent.clear(champ);
    await userEvent.type(champ, '2');
    expect(enregistrer).toBeEnabled();
    await userEvent.click(enregistrer);
    expect(api.modifierParametres).toHaveBeenCalledWith({ approbationsMinimum: 2 });
    await waitFor(() => expect(api.parametres).toHaveBeenCalledTimes(2));
  });

  it('nouveau critère : libellé obligatoire, espaces retirés, liste relue', async () => {
    rendre();
    await screen.findByText('Équipe présente et joignable');
    await userEvent.click(screen.getByRole('button', { name: 'Nouveau critère' }));
    const dialogue = await screen.findByRole('dialog', { name: 'Nouveau critère' });
    const enregistrer = within(dialogue).getByRole('button', { name: 'Enregistrer' });
    expect(enregistrer).toBeDisabled();
    await userEvent.type(within(dialogue).getByLabelText('Libellé *'), '  Astreinte prévenue  ');
    await userEvent.click(enregistrer);
    expect(api.creerCritere).toHaveBeenCalledWith({ libelle: 'Astreinte prévenue', actif: true });
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect(api.criteresGoNoGo).toHaveBeenCalledTimes(2);
  });

  it('refus du serveur : le message reste dans le dialogue, rien n’est fermé', async () => {
    vi.mocked(api.creerCritere).mockRejectedValueOnce(new Error('Ce critère existe déjà.'));
    rendre();
    await screen.findByText('Équipe présente et joignable');
    await userEvent.click(screen.getByRole('button', { name: 'Nouveau critère' }));
    const dialogue = await screen.findByRole('dialog', { name: 'Nouveau critère' });
    await userEvent.type(within(dialogue).getByLabelText('Libellé *'), 'Sauvegarde vérifiée');
    await userEvent.click(within(dialogue).getByRole('button', { name: 'Enregistrer' }));
    expect(await within(dialogue).findByRole('alert')).toHaveTextContent('Ce critère existe déjà.');
    expect(screen.getByRole('dialog', { name: 'Nouveau critère' })).toBeInTheDocument();
  });

  it('modifier un critère : le dialogue reprend son libellé et son état', async () => {
    rendre();
    await userEvent.click(await screen.findByRole('button', { name: 'Modifier Fenêtre de maintenance confirmée' }));
    const dialogue = await screen.findByRole('dialog', { name: 'Modifier le critère' });
    expect(within(dialogue).getByLabelText('Libellé *')).toHaveValue('Fenêtre de maintenance confirmée');
    await userEvent.click(within(dialogue).getByRole('checkbox', { name: /Actif/ }));
    await userEvent.click(within(dialogue).getByRole('button', { name: 'Enregistrer' }));
    expect(api.modifierCritere).toHaveBeenCalledWith('c3', {
      libelle: 'Fenêtre de maintenance confirmée',
      actif: true,
    });
  });

  it('monter un critère : seuls les deux critères échangés sont renumérotés', async () => {
    rendre();
    expect(await screen.findByRole('button', { name: 'Monter Équipe présente et joignable' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Descendre Fenêtre de maintenance confirmée' })).toBeDisabled();
    await userEvent.click(screen.getByRole('button', { name: 'Monter Sauvegarde vérifiée' }));
    expect(api.modifierCritere).toHaveBeenCalledTimes(2);
    expect(api.modifierCritere).toHaveBeenCalledWith('c2', { libelle: 'Sauvegarde vérifiée', ordre: 1, actif: true });
    expect(api.modifierCritere).toHaveBeenCalledWith('c1', {
      libelle: 'Équipe présente et joignable',
      ordre: 2,
      actif: true,
    });
    await waitFor(() => expect(api.criteresGoNoGo).toHaveBeenCalledTimes(2));
  });

  it('désactiver un critère garde son libellé et sa place', async () => {
    rendre();
    await userEvent.click(await screen.findByRole('button', { name: 'Désactiver Sauvegarde vérifiée' }));
    expect(api.modifierCritere).toHaveBeenCalledWith('c2', { libelle: 'Sauvegarde vérifiée', ordre: 2, actif: false });
    expect(screen.getByRole('button', { name: 'Activer Fenêtre de maintenance confirmée' })).toBeInTheDocument();
  });

  it('supprimer demande confirmation', async () => {
    rendre();
    await userEvent.click(await screen.findByRole('button', { name: 'Supprimer Sauvegarde vérifiée' }));
    const dialogue = await screen.findByRole('dialog', { name: 'Supprimer « Sauvegarde vérifiée » ?' });
    expect(dialogue).toHaveTextContent('Les MEP déjà lancées gardent leur copie des critères cochés.');
    expect(api.supprimerCritere).not.toHaveBeenCalled();
    await userEvent.click(within(dialogue).getByRole('button', { name: 'Supprimer' }));
    expect(api.supprimerCritere).toHaveBeenCalledWith('c2');
  });

  it('aucun critère : le pilote peut donner le Go directement', async () => {
    vi.mocked(api.criteresGoNoGo).mockResolvedValue([]);
    rendre();
    expect(await screen.findByText('Aucun critère')).toBeInTheDocument();
    expect(screen.getByText('Sans critère, le pilote peut donner le Go directement.')).toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: 'Nouveau critère' })).toHaveLength(2);
  });
});
