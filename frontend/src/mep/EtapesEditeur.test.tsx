import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { api } from '../api';
import { MepContext } from '../mepContext';
import { etape, mep } from '../test/fabrique';
import type { Mep } from '../types';
import { TooltipProvider } from '../ui/Tooltip';
import { EtapesEditeur } from './EtapesEditeur';

vi.mock('../api', () => ({
  api: {
    catalogueSystemes: vi.fn(async () => []),
    resolutions: vi.fn(async () => []),
    modifierEtape: vi.fn(async () => ({})),
    ajouterEtape: vi.fn(async () => ({ etapes: [] })),
    placerEtape: vi.fn(async () => ({})),
    supprimerEtape: vi.fn(async () => ({})),
    supprimerSysteme: vi.fn(async () => ({})),
    ajouterSystemeDepuisCatalogue: vi.fn(async () => ({})),
  },
}));

const droitsEdition = { estPilote: true, estApprobateur: false, estResponsableTechnique: false, peutModifierPlan: true, peutEditerContenu: true, participantId: 'p3' };

function planExemple(p: Partial<Mep> = {}): Mep {
  return mep({
    phase: 'Preparation',
    droits: droitsEdition,
    etapes: [
      etape({ id: 'e1', code: 'E0001', action: 'Sauvegarde BDD', ligne: 0, ordre: 0, type: 'Sauvegarde' }),
      etape({ id: 'e2', code: 'E0002', action: 'Bascule des données', ligne: 0, ordre: 1, pointDeNonRetour: true }),
      etape({ id: 'e3', code: 'E0003', action: 'Contrôle final', ligne: 1, ordre: 0, dependances: ['e1'] }),
    ],
    ...p,
  });
}

function rendre(m: Mep) {
  const run = vi.fn(async (action: () => Promise<unknown>) => { await action(); return true; });
  render(
    <MemoryRouter>
      <TooltipProvider>
        <MepContext.Provider value={{ mep: m, run }}>
          <EtapesEditeur plan="Deploiement" />
        </MepContext.Provider>
      </TooltipProvider>
    </MemoryRouter>,
  );
  return { run };
}

const carte = (nom: RegExp) => screen.getByRole('button', { name: nom });

describe('EtapesEditeur : graphe en préparation', () => {
  beforeEach(() => vi.clearAllMocks());

  it('affiche les lignes, la barrière du point de non-retour et les blocs fantômes', async () => {
    rendre(planExemple());
    await waitFor(() => expect(api.resolutions).toHaveBeenCalled());
    expect(carte(/^E0001 Sauvegarde BDD/)).toBeInTheDocument();
    expect(screen.getByRole('note')).toHaveTextContent('⛔ E0002 : après ce point, plus de retour arrière');
    expect(screen.getByRole('button', { name: 'Ajouter une étape sur la ligne 1' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Nouvelle ligne/ })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Lien E0001 vers E0003' })).toBeInTheDocument();
  });

  it('Entrée sur une fiche ouvre le panneau ; un préalable s’ajoute depuis le panneau', async () => {
    rendre(planExemple());
    carte(/^E0003/).focus();
    await userEvent.keyboard('{Enter}');
    expect(screen.getByLabelText('Action')).toHaveValue('Contrôle final');
    const panneau = screen.getByRole('complementary', { name: 'Détail de la sélection' });
    expect(within(panneau).getByRole('button', { name: 'Retirer le préalable E0001' })).toBeInTheDocument();
    await userEvent.selectOptions(within(panneau).getByLabelText('Ajouter un préalable'), 'e2');
    expect(api.modifierEtape).toHaveBeenCalledWith('m1', 'e3', expect.objectContaining({ dependances: ['e1', 'e2'], action: 'Contrôle final', systemeIds: ['s1'] }));
  });

  it('retire un préalable depuis le panneau', async () => {
    rendre(planExemple());
    await userEvent.click(carte(/^E0003/));
    await userEvent.click(screen.getByRole('button', { name: 'Retirer le préalable E0001' }));
    expect(api.modifierEtape).toHaveBeenCalledWith('m1', 'e3', expect.objectContaining({ dependances: [] }));
  });

  it('enregistre la fiche (systèmes multiples, durée, instructions en blocs)', async () => {
    const base = mep().systemes[0];
    rendre(planExemple({ systemes: [base, { ...base, id: 's2', nom: 'BDD' }] }));
    await userEvent.click(carte(/^E0001/));
    await userEvent.click(screen.getByRole('button', { name: 'BDD', pressed: false }));
    const duree = screen.getByLabelText('Durée estimée (min)');
    await userEvent.clear(duree);
    await userEvent.type(duree, '20');
    await userEvent.click(screen.getByRole('button', { name: 'Bloc code' }));
    await userEvent.type(screen.getByLabelText('Instructions, bloc 2 (code)'), 'pg_dump clients');
    await userEvent.click(screen.getByRole('button', { name: 'Enregistrer' }));
    expect(api.modifierEtape).toHaveBeenCalledWith('m1', 'e1', expect.objectContaining({
      systemeIds: ['s1', 's2'], dureeEstimeeMinutes: 20, type: 'Sauvegarde',
      instructions: '```\nsystemctl restart api\n```\n\n```\npg_dump clients\n```',
    }));
  });

  it('cliquer sur un lien le sélectionne ; il se supprime depuis le panneau', async () => {
    rendre(planExemple());
    await userEvent.click(screen.getByRole('button', { name: 'Lien E0001 vers E0003' }));
    expect(screen.getByText('Lien sélectionné')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Supprimer le lien' }));
    expect(api.modifierEtape).toHaveBeenCalledWith('m1', 'e3', expect.objectContaining({ dependances: [] }));
  });

  it('touche Suppr sur un lien sélectionné', async () => {
    rendre(planExemple());
    const lien = screen.getByRole('button', { name: 'Lien E0001 vers E0003' });
    lien.focus();
    await userEvent.keyboard('{Enter}');
    await userEvent.keyboard('{Delete}');
    expect(api.modifierEtape).toHaveBeenCalledWith('m1', 'e3', expect.objectContaining({ dependances: [] }));
  });

  it('les blocs fantômes créent une étape (ligne existante ou nouvelle ligne)', async () => {
    rendre(planExemple());
    await userEvent.click(screen.getByRole('button', { name: 'Ajouter une étape sur la ligne 2' }));
    expect(api.ajouterEtape).toHaveBeenLastCalledWith('m1', 'Deploiement', 1);
    await userEvent.click(screen.getByRole('button', { name: /Nouvelle ligne/ }));
    expect(api.ajouterEtape).toHaveBeenLastCalledWith('m1', 'Deploiement', undefined);
  });

  it('retrait d’un système utilisé par des étapes : refusé avec un message', async () => {
    rendre(planExemple());
    await userEvent.click(screen.getByRole('button', { name: 'Retirer API de la MEP' }));
    expect(screen.getByText(/API est utilisé par E0001, E0002, E0003/)).toBeInTheDocument();
    expect(api.supprimerSysteme).not.toHaveBeenCalled();
  });

  it('lecture seule : ni fantômes, ni ronds, ni liens cliquables ; panneau en lecture', async () => {
    rendre(planExemple({ phase: 'Execution', droits: { ...droitsEdition, peutEditerContenu: false } }));
    expect(screen.queryByRole('button', { name: /Nouvelle ligne/ })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Lien E0001 vers E0003' })).toBeNull();
    expect(document.querySelector('[data-port]')).toBeNull();
    await userEvent.click(carte(/^E0003/));
    await waitFor(() => expect(screen.getByRole('heading', { name: 'Contrôle final' })).toBeInTheDocument());
    expect(screen.queryByRole('button', { name: 'Enregistrer' })).toBeNull();
  });
});
