import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { api } from '../api';
import { MepContext } from '../mepContext';
import { etape, mep } from '../test/fabrique';
import type { Mep, ResolutionType } from '../types';
import { TooltipProvider } from '../ui/Tooltip';
import { EtapesEditeur } from './EtapesEditeur';

const res = (id: string, titre: string, systemeCatalogueId: string | null, symptomes = ''): ResolutionType => ({
  id, titre, symptomes, diagnostic: '', systemeCatalogueId, corrections: [], verificationInstruction: '', verificationAttendu: '', actif: true, version: 1, modifieeLe: '2026-10-10T10:00:00Z',
});

vi.mock('../api', () => ({
  api: {
    catalogueSystemes: vi.fn(async () => [
      { id: 'c-api', nom: 'API', description: 'Service commandes', cible: '', actif: true },
      { id: 'c-web', nom: 'WEB', description: 'Portail public', cible: '', actif: true },
    ]),
    resolutions: vi.fn(async () => [
      res('r1', 'API : le service ne démarre pas', 'c-api', 'GET /sante renvoie 503'),
      res('r2', 'WEB : purge du cache CDN refusée', 'c-web', 'Publication refusée (403)'),
      res('r3', 'Procédure générale de redémarrage', null),
    ]),
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
  const base = mep().systemes[0];
  return mep({
    phase: 'Preparation',
    droits: droitsEdition,
    systemes: [{ ...base, catalogueId: 'c-api' }],
    etapes: [
      etape({ id: 'e1', code: 'E0001', action: 'Sauvegarde BDD', ligne: 0, ordre: 0, type: 'Sauvegarde' }),
      etape({ id: 'e2', code: 'E0002', action: 'Bascule des données', ligne: 0, ordre: 1, pointDeNonRetour: true }),
      etape({ id: 'e3', code: 'E0003', action: 'Contrôle final', ligne: 1, ordre: 0, dependances: ['e1'] }),
    ],
    ...p,
  });
}

function Destination() {
  const l = useLocation();
  return <p>Destination : {l.pathname}{l.search}</p>;
}

function rendre(m: Mep) {
  const run = vi.fn(async (action: () => Promise<unknown>) => { await action(); return true; });
  render(
    <MemoryRouter initialEntries={['/meps/m1']}>
      <TooltipProvider>
        <MepContext.Provider value={{ mep: m, run }}>
          <Routes>
            <Route path="/meps/:id" element={<EtapesEditeur plan="Deploiement" />} />
            <Route path="*" element={<Destination />} />
          </Routes>
        </MepContext.Provider>
      </TooltipProvider>
    </MemoryRouter>,
  );
  return { run };
}

const carte = (nom: RegExp) => screen.getByRole('button', { name: nom });
const onglet = (nom: RegExp) => userEvent.click(screen.getByRole('tab', { name: nom }));

describe('EtapesEditeur : graphe en préparation', () => {
  beforeEach(() => { vi.clearAllMocks(); window.localStorage.clear(); });

  it('affiche les lignes, la barrière du point de non-retour, les blocs fantômes et les contrôles de vue', async () => {
    rendre(planExemple());
    await waitFor(() => expect(api.resolutions).toHaveBeenCalled());
    expect(carte(/^E0001 Sauvegarde BDD/)).toBeInTheDocument();
    expect(screen.getByRole('note')).toHaveTextContent('⛔ E0002 : après ce point, plus de retour arrière');
    expect(screen.getByRole('button', { name: 'Ajouter une étape sur la ligne 1' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Nouvelle ligne/ })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Lien E0001 vers E0003' })).toBeInTheDocument();
    const vue = screen.getByRole('toolbar', { name: 'Vue du graphe' });
    await userEvent.click(within(vue).getByRole('button', { name: 'Zoomer' }));
    expect(within(vue).getByRole('button', { name: /Zoom 120 %/ })).toBeInTheDocument();
    await userEvent.click(within(vue).getByRole('button', { name: /revenir à 100 %/ }));
    expect(within(vue).getByRole('button', { name: /Zoom 100 %/ })).toBeInTheDocument();
  });

  it('Entrée sur une fiche ouvre le panneau ; un préalable s’ajoute depuis l’onglet Liens', async () => {
    rendre(planExemple());
    carte(/^E0003/).focus();
    await userEvent.keyboard('{Enter}');
    expect(screen.getByLabelText('Action')).toHaveValue('Contrôle final');
    const panneau = screen.getByRole('complementary', { name: 'Détail de la sélection' });
    await onglet(/Liens/);
    expect(within(panneau).getByRole('button', { name: 'Retirer le préalable E0001' })).toBeInTheDocument();
    await userEvent.selectOptions(within(panneau).getByLabelText('Ajouter un préalable'), 'e2');
    expect(api.modifierEtape).toHaveBeenCalledWith('m1', 'e3', expect.objectContaining({ dependances: ['e1', 'e2'], action: 'Contrôle final', systemeIds: ['s1'] }));
  });

  it('retire un préalable depuis l’onglet Liens', async () => {
    rendre(planExemple());
    await userEvent.click(carte(/^E0003/));
    await onglet(/Liens/);
    await userEvent.click(screen.getByRole('button', { name: 'Retirer le préalable E0001' }));
    expect(api.modifierEtape).toHaveBeenCalledWith('m1', 'e3', expect.objectContaining({ dependances: [] }));
  });

  it('enregistre la fiche (systèmes multiples, durée, instructions en blocs) ; indicateur de saisie', async () => {
    const base = mep().systemes[0];
    rendre(planExemple({ systemes: [base, { ...base, id: 's2', nom: 'BDD' }] }));
    await userEvent.click(carte(/^E0001/));
    await userEvent.click(screen.getByRole('button', { name: 'BDD', pressed: false }));
    expect(screen.getByRole('status')).toHaveTextContent('Modifications non enregistrées');
    const duree = screen.getByLabelText('Durée estimée (min)');
    await userEvent.clear(duree);
    await userEvent.type(duree, '20');
    await onglet(/Instructions/);
    await userEvent.click(screen.getByRole('button', { name: 'Ajouter un bloc code' }));
    await userEvent.type(screen.getByLabelText('Instructions, bloc 2 (code)'), 'pg_dump clients');
    await userEvent.click(screen.getByRole('button', { name: 'Enregistrer' }));
    expect(api.modifierEtape).toHaveBeenCalledWith('m1', 'e1', expect.objectContaining({
      systemeIds: ['s1', 's2'], dureeEstimeeMinutes: 20, type: 'Sauvegarde',
      instructions: '```\nsystemctl restart api\n```\n\n```\npg_dump clients\n```',
    }));
  });

  it('onglets : compteurs et pastille d’anomalie', async () => {
    rendre(planExemple({ etapes: [etape({ id: 'e1', code: 'E0001', ligne: 0, ordre: 0, criticite: null, resultatAttendu: '' })] }));
    await userEvent.click(carte(/^E0001/));
    expect(screen.getByRole('tab', { name: /Liens\s*0/ })).toBeInTheDocument();
    expect(screen.getByRole('img', { name: /À compléter : la criticité/ })).toBeInTheDocument();
    expect(screen.getByRole('img', { name: /À compléter : le résultat attendu/ })).toBeInTheDocument();
  });

  it('résolutions : recherche sans accents, même système en tête, Entrée pour lier', async () => {
    rendre(planExemple());
    await userEvent.click(carte(/^E0003/));
    await onglet(/Résolutions/);
    const liste = await screen.findByRole('listbox', { name: 'Résolutions trouvées' });
    // Filtre présélectionné sur le système de l'étape (API) : WEB est masqué, la fiche « tous systèmes » reste.
    expect(within(liste).getAllByRole('option').map((o) => o.textContent)).toEqual([
      expect.stringContaining('API : le service ne démarre pas'), expect.stringContaining('Procédure générale'),
    ]);
    await userEvent.click(screen.getByRole('button', { name: 'Tous' }));
    const champ = screen.getByRole('combobox', { name: 'Ajouter une résolution' });
    await userEvent.type(champ, 'refusee 403');
    expect(within(liste).getAllByRole('option')).toHaveLength(1);
    expect(within(liste).getByText('refusée', { selector: 'mark' })).toBeInTheDocument();
    await userEvent.keyboard('{Enter}');
    expect(screen.getByRole('button', { name: /Retirer la résolution WEB : purge du cache CDN refusée/ })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /Résolutions\s*1/ })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Enregistrer' }));
    expect(api.modifierEtape).toHaveBeenCalledWith('m1', 'e3', expect.objectContaining({ resolutionIds: ['r2'] }));
  });

  it('résolutions : les correspondances masquées par le filtre de systèmes sont proposées', async () => {
    rendre(planExemple());
    await userEvent.click(carte(/^E0003/));
    await onglet(/Résolutions/);
    await userEvent.type(await screen.findByRole('combobox', { name: 'Ajouter une résolution' }), 'purge');
    await userEvent.click(screen.getByRole('button', { name: /1 autre résultat dans d’autres systèmes/ }));
    expect(within(screen.getByRole('listbox')).getByRole('option')).toHaveTextContent('WEB : purge du cache CDN refusée');
  });

  it('résolutions : créer une fiche avec le texte cherché comme titre', async () => {
    rendre(planExemple());
    await userEvent.click(carte(/^E0003/));
    await onglet(/Résolutions/);
    await userEvent.type(await screen.findByRole('combobox', { name: 'Ajouter une résolution' }), 'LDAP muet');
    await userEvent.click(screen.getByRole('button', { name: /Créer une nouvelle fiche de résolution « LDAP muet »/ }));
    expect(await screen.findByText('Destination : /parametres/resolutions/nouvelle?mep=m1&etape=e3&titre=LDAP+muet')).toBeInTheDocument();
  });

  it('suppression : confirmation intégrée au panneau', async () => {
    rendre(planExemple());
    await userEvent.click(carte(/^E0003/));
    await userEvent.click(screen.getByRole('button', { name: 'Supprimer l’étape' }));
    const confirmation = screen.getByRole('alertdialog', { name: 'Supprimer E0003' });
    await userEvent.click(within(confirmation).getByRole('button', { name: 'Annuler' }));
    expect(api.supprimerEtape).not.toHaveBeenCalled();
    await userEvent.click(screen.getByRole('button', { name: 'Supprimer l’étape' }));
    await userEvent.click(screen.getByRole('button', { name: 'Supprimer définitivement' }));
    expect(api.supprimerEtape).toHaveBeenCalledWith('m1', 'e3');
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
