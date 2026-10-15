import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { api } from '../api';
import { MepContext } from '../mepContext';
import { etape, mep } from '../test/fabrique';
import type { Mep, ModeleEtape, ResolutionType } from '../types';
import { TooltipProvider } from '../ui/Tooltip';
import { EtapesEditeur } from './EtapesEditeur';
import { apiGraphe } from './graphe/apiGraphe';
import { changerModeEspace } from './graphe/EspaceEtape';
import { simulerReactFlow } from '../test/reactFlow';

const res = (id: string, titre: string, systemeCatalogueIds: string[], symptomes = ''): ResolutionType => ({
  id,
  titre,
  symptomes,
  diagnostic: '',
  systemeCatalogueIds,
  corrections: [],
  verificationInstruction: '',
  verificationAttendu: '',
  actif: true,
  version: 1,
  modifieeLe: '2026-10-10T10:00:00Z',
  modifieeParId: null,
  modifieeParNom: null,
  utilisations: 0,
  creeeLe: '2026-01-01T00:00:00Z',
});

vi.mock('../api', () => ({
  api: {
    catalogueSystemes: vi.fn(async () => [
      { id: 'c-api', nom: 'API', description: 'Service commandes', cible: '', actif: true },
      { id: 'c-web', nom: 'WEB', description: 'Portail public', cible: '', actif: true },
    ]),
    resolutions: vi.fn(async () => [
      res('r1', 'API : le service ne démarre pas', ['c-api'], 'GET /sante renvoie 503'),
      res('r2', 'WEB : purge du cache CDN refusée', ['c-web'], 'Publication refusée (403)'),
      res('r3', 'Procédure générale de redémarrage', []),
    ]),
    modifierEtape: vi.fn(async () => ({})),
    supprimerEtape: vi.fn(async () => ({})),
    supprimerSysteme: vi.fn(async () => ({})),
    ajouterSystemeDepuisCatalogue: vi.fn(async () => ({})),
    etapesModeles: vi.fn(async () => []),
    etapeModeleDepuisEtape: vi.fn(),
  },
}));

vi.mock('./graphe/apiGraphe', () => ({
  apiGraphe: {
    creerEtape: vi.fn(async () => ({ etapes: [] })),
    insererModele: vi.fn(async () => ({ etapes: [] })),
    deplacerEtape: vi.fn(async () => ({})),
    lier: vi.fn(async () => ({})),
    delier: vi.fn(async () => ({})),
    creerSection: vi.fn(async () => ({})),
    placerSection: vi.fn(async () => ({})),
  },
  apiVerrou: { prendre: vi.fn(), relacher: vi.fn(async () => undefined), liste: vi.fn(async () => []) },
}));

beforeAll(() => simulerReactFlow());

const droitsEdition = {
  estPilote: true,
  estApprobateur: false,
  estResponsableTechnique: false,
  peutModifierPlan: true,
  peutEditerContenu: true,
  participantId: 'p3',
};

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
  return (
    <p>
      Destination : {l.pathname}
      {l.search}
    </p>
  );
}

function rendre(m: Mep) {
  const run = vi.fn(async (action: () => Promise<unknown>) => {
    await action();
    return true;
  });
  const arbre = (mm: Mep) => (
    <MemoryRouter initialEntries={['/meps/m1']}>
      <TooltipProvider>
        <MepContext.Provider value={{ mep: mm, run }}>
          <Routes>
            <Route path="/meps/:id" element={<EtapesEditeur plan="Deploiement" />} />
            <Route path="*" element={<Destination />} />
          </Routes>
        </MepContext.Provider>
      </TooltipProvider>
    </MemoryRouter>
  );
  const { rerender } = render(arbre(m));
  return { run, rerendre: (mm: Mep) => rerender(arbre(mm)) };
}

const carte = (nom: RegExp) => screen.getByRole('button', { name: nom });
/** Choix « Étape vierge » du menu de création ouvert par un « + », le bouton Étape ou le menu d'un lien. */
const vierge = async () => userEvent.click(await screen.findByRole('menuitem', { name: 'Étape vierge' }));
/** Lien atteint au clavier : Entrée ouvre son menu (comme un clic sur son tracé). */
const ouvrirLien = async (nom: string) => {
  const lien = await screen.findByRole('group', { name: nom });
  fireEvent.keyDown(lien, { key: 'Enter' });
  await screen.findByRole('menu', { name: 'Actions du lien' });
  return lien;
};

describe('EtapesEditeur : graphe en préparation', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    window.localStorage.clear();
    changerModeEspace('plein');
  });

  it('graphe libre : cartes, liens, création rapide et contrôles de vue', async () => {
    rendre(planExemple());
    await waitFor(() => expect(api.resolutions).toHaveBeenCalled());
    expect(carte(/^E0001 Sauvegarde BDD/)).toBeInTheDocument();
    expect(await screen.findByRole('group', { name: 'Lien E0001 vers E0003' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Ajouter une étape après E0001' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Ajouter une étape en parallèle de E0001' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /^Étape$/ })).toBeInTheDocument();
    expect(screen.getByRole('combobox', { name: 'Aller à une étape (code)' })).toBeInTheDocument();
    const vue = screen.getByRole('toolbar', { name: 'Vue du graphe' });
    expect(within(vue).getByRole('button', { name: 'Ajuster à l’écran' })).toBeInTheDocument();
    expect(within(vue).getByRole('button', { name: /Zoom \d+ %/ })).toBeInTheDocument();
  });

  it('Entrée sur une fiche ouvre l’espace d’édition ; un préalable s’ajoute', async () => {
    rendre(planExemple());
    carte(/^E0003/).focus();
    await userEvent.keyboard('{Enter}');
    expect(screen.getByLabelText('Action')).toHaveValue('Contrôle final');
    const panneau = screen.getByRole('dialog');
    expect(within(panneau).getByRole('button', { name: 'Retirer le préalable E0001' })).toBeInTheDocument();
    await userEvent.selectOptions(within(panneau).getByLabelText('Ajouter un préalable'), 'e2');
    expect(apiGraphe.lier).toHaveBeenCalledWith('m1', 'e2', 'e3');
  });

  it('un lecteur d’écran ouvre une fiche (clic sans pointeur)', () => {
    rendre(planExemple());
    fireEvent.click(carte(/^E0003/));
    expect(screen.getByLabelText('Action')).toHaveValue('Contrôle final');
  });

  it('création rapide : après une carte, en parallèle, isolée ; chaque fois « Étape vierge » ou « Depuis un modèle »', async () => {
    rendre(planExemple());
    await userEvent.click(screen.getByRole('button', { name: 'Ajouter une étape après E0001' }));
    const menu = await screen.findByRole('menu', { name: 'Créer une étape' });
    expect(within(menu).getByText('Nouvelle étape après E0001')).toBeInTheDocument();
    expect(within(menu).getByText('Depuis un modèle')).toBeInTheDocument();
    expect(within(menu).getByRole('menuitem', { name: 'Parcourir la bibliothèque…' })).toBeInTheDocument();
    expect(apiGraphe.creerEtape).not.toHaveBeenCalled();
    await vierge();
    expect(apiGraphe.creerEtape).toHaveBeenLastCalledWith('m1', 'Deploiement', { apres: 'e1' }, null);
    await userEvent.click(screen.getByRole('button', { name: 'Ajouter une étape en parallèle de E0002' }));
    await vierge();
    expect(apiGraphe.creerEtape).toHaveBeenLastCalledWith('m1', 'Deploiement', { paralleleA: 'e2' }, null);
    await userEvent.click(screen.getByRole('button', { name: /^Étape$/ }));
    await vierge();
    expect(apiGraphe.creerEtape).toHaveBeenLastCalledWith('m1', 'Deploiement', {}, null);
  });

  it('insertion sur un lien : son menu propose « Insérer une étape » (vierge ou depuis un modèle)', async () => {
    rendre(planExemple());
    await ouvrirLien('Lien E0001 vers E0003');
    const menu = screen.getByRole('menu', { name: 'Actions du lien' });
    expect(within(menu).getByText('E0001 → E0003')).toBeInTheDocument();
    expect(within(menu).getByText('Insérer une étape sur ce lien')).toBeInTheDocument();
    await vierge();
    expect(apiGraphe.creerEtape).toHaveBeenLastCalledWith(
      'm1',
      'Deploiement',
      { entre: { de: 'e1', vers: 'e3' } },
      null,
    );
  });

  it('une seule création à la fois : un double clic sur « + » ne crée qu’une étape', async () => {
    let finir: () => void = () => undefined;
    vi.mocked(apiGraphe.creerEtape).mockImplementationOnce(
      () => new Promise<Mep>((ok) => (finir = () => ok(planExemple()))),
    );
    rendre(planExemple());
    await userEvent.click(screen.getByRole('button', { name: /^Étape$/ }));
    await userEvent.dblClick(await screen.findByRole('menuitem', { name: 'Étape vierge' }));
    await userEvent.click(screen.getByRole('button', { name: 'Ajouter une étape après E0001' }));
    await vierge();
    expect(apiGraphe.creerEtape).toHaveBeenCalledTimes(1);
    await act(async () => finir());
    await userEvent.click(screen.getByRole('button', { name: /^Étape$/ }));
    await vierge();
    expect(apiGraphe.creerEtape).toHaveBeenCalledTimes(2);
  });

  it('fiche modifiée par ailleurs pendant la saisie : avertissement avant d’écraser', async () => {
    const m = planExemple();
    const { rerendre } = rendre(m);
    fireEvent.click(carte(/^E0003/));
    await userEvent.type(screen.getByLabelText('Action'), ' bis');
    // Un collègue modifie le résultat attendu ; la MEP est rechargée par une autre action.
    rerendre({
      ...m,
      version: m.version + 1,
      etapes: m.etapes.map((e) =>
        e.id === 'e3' ? { ...e, revision: e.revision + 1, resultatAttendu: 'Revu par Bob' } : e,
      ),
    });
    expect(screen.getByRole('alert')).toHaveTextContent('Modifié pendant votre saisie');
    expect(screen.getByLabelText('Action')).toHaveValue('Contrôle final bis');
    expect(screen.getByRole('button', { name: 'Enregistrer' })).toBeDisabled();
    await userEvent.click(screen.getByRole('button', { name: 'Garder ma saisie' }));
    await userEvent.click(screen.getByRole('button', { name: 'Enregistrer' }));
    expect(api.modifierEtape).toHaveBeenCalledWith(
      'm1',
      'e3',
      expect.objectContaining({ action: 'Contrôle final bis', revision: m.etapes[2].revision + 1 }),
    );
  });

  it('retire un préalable', async () => {
    rendre(planExemple());
    fireEvent.click(carte(/^E0003/));
    await userEvent.click(screen.getByRole('button', { name: 'Retirer le préalable E0001' }));
    expect(apiGraphe.delier).toHaveBeenCalledWith('m1', 'e1', 'e3');
  });

  it('enregistre la fiche (systèmes multiples, durée, instructions en blocs) ; indicateur de saisie', async () => {
    const base = mep().systemes[0];
    rendre(planExemple({ systemes: [base, { ...base, id: 's2', nom: 'BDD' }] }));
    fireEvent.click(carte(/^E0001/));
    await userEvent.click(screen.getByRole('button', { name: 'BDD', pressed: false }));
    expect(screen.getByRole('status')).toHaveTextContent('Non enregistré');
    const duree = screen.getByLabelText('Durée (min)');
    await userEvent.clear(duree);
    await userEvent.type(duree, '20');
    await userEvent.click(screen.getByRole('button', { name: 'Ajouter un bloc code' }));
    await userEvent.type(screen.getByLabelText('Instructions, bloc 2 (code)'), 'pg_dump clients');
    await userEvent.click(screen.getByRole('button', { name: 'Enregistrer' }));
    expect(api.modifierEtape).toHaveBeenCalledWith(
      'm1',
      'e1',
      expect.objectContaining({
        systemeIds: ['s1', 's2'],
        dureeEstimeeMinutes: 20,
        type: 'Sauvegarde',
        instructions: '```\nsystemctl restart api\n```\n\n```\npg_dump clients\n```',
      }),
    );
  });

  it('fin de l’étape et sauvegarde : emplacement et méthode de récupération réclamés pour une étape de sauvegarde', async () => {
    rendre(planExemple());
    fireEvent.click(carte(/^E0001/));
    const espace = screen.getByRole('dialog');
    expect(
      within(espace).getByText(/À compléter : l’emplacement de la sauvegarde, la méthode de récupération\./),
    ).toBeInTheDocument();
    expect(within(espace).getByRole('radio', { name: 'Test unitaire' })).toBeChecked();
    expect(within(espace).getByText(/l’exécutant saisira le résultat constaté/)).toBeInTheDocument();
    await userEvent.click(within(espace).getByRole('radio', { name: 'Simple vérification' }));
    await userEvent.type(within(espace).getByLabelText(/Emplacement de la sauvegarde/), '/sauvegardes/clients.sql');
    await userEvent.type(within(espace).getByLabelText(/Méthode de récupération/), 'psql < clients.sql');
    expect(within(espace).queryByText(/À compléter/)).toBeNull();
    await userEvent.click(within(espace).getByRole('button', { name: 'Enregistrer' }));
    expect(api.modifierEtape).toHaveBeenCalledWith(
      'm1',
      'e1',
      expect.objectContaining({
        fin: 'Verification',
        sauvegardeEmplacement: '/sauvegardes/clients.sql',
        sauvegardeMethodeRecuperation: 'psql < clients.sql',
      }),
    );
  });

  it('tout est visible sans onglets : fiche, instructions, liens, résolutions ; points à compléter résumés', async () => {
    rendre(
      planExemple({
        etapes: [
          etape({
            id: 'e1',
            code: 'E0001',
            ligne: 0,
            ordre: 0,
            criticite: null,
            resultatAttendu: '',
            dureeEstimeeMinutes: null,
          }),
        ],
      }),
    );
    fireEvent.click(carte(/^E0001/));
    const espace = screen.getByRole('dialog');
    expect(within(espace).queryByRole('tab')).toBeNull();
    expect(within(espace).queryByLabelText('Responsable')).toBeNull();
    for (const t of [
      'Systèmes et environnements',
      'Instructions exactes',
      'Résultat attendu',
      'Attend (0)',
      'Débloque (0)',
      'Résolutions en cas d’échec (0)',
    ]) {
      expect(within(espace).getByRole('heading', { name: t })).toBeInTheDocument();
    }
    expect(within(espace).getByText(/À compléter : la criticité, le résultat attendu\./)).toBeInTheDocument();
  });

  it('bascule plein écran / panneau latéral, mémorisée', async () => {
    rendre(planExemple());
    fireEvent.click(carte(/^E0003/));
    await userEvent.click(screen.getByRole('button', { name: 'Afficher en panneau latéral' }));
    expect(window.localStorage.getItem('mep.espace.mode')).toBe('panneau');
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    // Panneau : le graphe reste utilisable (pas de surcouche modale), une autre fiche s'ouvre d'un clic.
    fireEvent.click(screen.getAllByRole('button', { name: /^E0001/ })[0]);
    expect(screen.getByLabelText('Action')).toHaveValue('Sauvegarde BDD');
    await userEvent.click(screen.getByRole('button', { name: 'Afficher en plein écran' }));
    expect(window.localStorage.getItem('mep.espace.mode')).toBe('plein');
  });

  it('fermer avec des modifications non enregistrées : garde', async () => {
    rendre(planExemple());
    fireEvent.click(carte(/^E0003/));
    await userEvent.type(screen.getByLabelText('Action'), ' bis');
    await userEvent.keyboard('{Escape}');
    expect(screen.getByRole('alertdialog', { name: 'Modifications non enregistrées' })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Continuer' }));
    expect(screen.getByLabelText('Action')).toHaveValue('Contrôle final bis');
    await userEvent.click(screen.getByRole('button', { name: 'Fermer' }));
    await userEvent.click(screen.getByRole('button', { name: 'Fermer sans enregistrer' }));
    expect(screen.queryByRole('dialog')).toBeNull();
    expect(api.modifierEtape).not.toHaveBeenCalled();
  });

  it('ouvrir une autre étape avec des modifications non enregistrées : garde, puis changement', async () => {
    rendre(planExemple());
    fireEvent.click(carte(/^E0003/));
    await userEvent.type(screen.getByLabelText('Action'), ' bis');
    fireEvent.click(carte(/^E0001/));
    expect(screen.getByRole('alertdialog', { name: 'Modifications non enregistrées' })).toBeInTheDocument();
    expect(screen.getByLabelText('Action')).toHaveValue('Contrôle final bis');
    await userEvent.click(screen.getByRole('button', { name: 'Continuer' }));
    expect(screen.queryByRole('alertdialog')).toBeNull();
    fireEvent.click(carte(/^E0001/));
    await userEvent.click(screen.getByRole('button', { name: 'Changer d’étape sans enregistrer' }));
    expect(screen.getByLabelText('Action')).not.toHaveValue('Contrôle final bis');
    expect(api.modifierEtape).not.toHaveBeenCalled();
  });

  it('résolutions : recherche sans accents, même système en tête, Entrée pour lier', async () => {
    rendre(planExemple());
    fireEvent.click(carte(/^E0003/));
    const liste = await screen.findByRole('listbox', { name: 'Résolutions trouvées' });
    // Filtre présélectionné sur le système de l'étape (API) : WEB est masqué, la fiche « tous systèmes » reste.
    expect(
      within(liste)
        .getAllByRole('option')
        .map((o) => o.textContent),
    ).toEqual([
      expect.stringContaining('API : le service ne démarre pas'),
      expect.stringContaining('Procédure générale'),
    ]);
    await userEvent.click(screen.getByRole('button', { name: 'Tous' }));
    const champ = screen.getByRole('combobox', { name: 'Ajouter une résolution' });
    await userEvent.type(champ, 'refusee 403');
    expect(within(liste).getAllByRole('option')).toHaveLength(1);
    expect(within(liste).getByText('refusée', { selector: 'mark' })).toBeInTheDocument();
    await userEvent.keyboard('{Enter}');
    expect(
      screen.getByRole('button', { name: /Retirer la résolution WEB : purge du cache CDN refusée/ }),
    ).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Résolutions en cas d’échec (1)' })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Enregistrer' }));
    expect(api.modifierEtape).toHaveBeenCalledWith('m1', 'e3', expect.objectContaining({ resolutionIds: ['r2'] }));
  });

  it('résolutions : les correspondances masquées par le filtre de systèmes sont proposées', async () => {
    rendre(planExemple());
    fireEvent.click(carte(/^E0003/));
    await userEvent.type(await screen.findByRole('combobox', { name: 'Ajouter une résolution' }), 'purge');
    await userEvent.click(screen.getByRole('button', { name: /1 autre dans d’autres systèmes/ }));
    expect(within(screen.getByRole('listbox')).getByRole('option')).toHaveTextContent(
      'WEB : purge du cache CDN refusée',
    );
  });

  it('résolutions : créer une fiche avec le texte cherché comme titre', async () => {
    rendre(planExemple());
    fireEvent.click(carte(/^E0003/));
    await userEvent.type(await screen.findByRole('combobox', { name: 'Ajouter une résolution' }), 'LDAP muet');
    await userEvent.click(screen.getByRole('button', { name: /Nouvelle fiche de résolution « LDAP muet »/ }));
    expect(
      await screen.findByText('Destination : /parametres/resolutions/nouvelle?mep=m1&etape=e3&titre=LDAP+muet'),
    ).toBeInTheDocument();
  });

  it('suppression : confirmation intégrée au panneau', async () => {
    rendre(planExemple());
    fireEvent.click(carte(/^E0003/));
    await userEvent.click(screen.getByRole('button', { name: 'Supprimer l’étape' }));
    const confirmation = screen.getByRole('alertdialog', { name: 'Supprimer E0003' });
    await userEvent.click(within(confirmation).getByRole('button', { name: 'Annuler' }));
    expect(api.supprimerEtape).not.toHaveBeenCalled();
    await userEvent.click(screen.getByRole('button', { name: 'Supprimer l’étape' }));
    await userEvent.click(screen.getByRole('button', { name: 'Supprimer définitivement' }));
    expect(api.supprimerEtape).toHaveBeenCalledWith('m1', 'e3');
  });

  it('un lien choisi ouvre son menu : « Supprimer le lien »', async () => {
    rendre(planExemple());
    // Plus de bouton au survol : les actions passent par le menu du lien.
    expect(screen.queryByRole('button', { name: 'Insérer une étape sur ce lien' })).not.toBeInTheDocument();
    await ouvrirLien('Lien E0001 vers E0003');
    await userEvent.click(screen.getByRole('menuitem', { name: 'Supprimer le lien' }));
    expect(apiGraphe.delier).toHaveBeenCalledWith('m1', 'e1', 'e3');
  });

  it('touche Suppr sur un lien sélectionné', async () => {
    rendre(planExemple());
    const lien = await ouvrirLien('Lien E0001 vers E0003');
    fireEvent.keyDown(lien, { key: 'Delete' });
    expect(apiGraphe.delier).toHaveBeenCalledWith('m1', 'e1', 'e3');
  });

  it('ajouter une étape pendant une saisie : la saisie n’est pas perdue, l’espace demande quoi en faire', async () => {
    const m = planExemple();
    const nouvelle = etape({ id: 'e4', code: 'E0004', action: '', ligne: 0, ordre: 2 });
    const apres = { ...m, etapes: [...m.etapes, nouvelle] };
    vi.mocked(apiGraphe.creerEtape).mockResolvedValueOnce(apres);
    // Panneau latéral : le graphe reste utilisable pendant la saisie.
    changerModeEspace('panneau');
    const { rerendre } = rendre(m);
    fireEvent.click(carte(/^E0003/));
    await userEvent.type(screen.getByLabelText('Action'), ' bis');
    await userEvent.click(screen.getByRole('button', { name: 'Ajouter une étape après E0001' }));
    await vierge();
    expect(apiGraphe.creerEtape).toHaveBeenCalledWith('m1', 'Deploiement', { apres: 'e1' }, null);
    rerendre(apres);
    expect(await screen.findByRole('alertdialog', { name: 'Modifications non enregistrées' })).toBeInTheDocument();
    expect(screen.getByLabelText('Action')).toHaveValue('Contrôle final bis');
    await userEvent.click(screen.getByRole('button', { name: 'Changer d’étape sans enregistrer' }));
    // La nouvelle étape s'ouvre, le focus sur son action.
    expect(screen.getByLabelText('Action')).toHaveValue('');
    expect(screen.getByLabelText('Action')).toHaveFocus();
  });

  it('pas de barre des systèmes au-dessus du plan (ils se gèrent en Préparation) ; bac des sections dans le cadre', () => {
    rendre(planExemple());
    expect(screen.queryByRole('button', { name: 'Retirer API de la MEP' })).not.toBeInTheDocument();
    const bac = screen.getByRole('complementary', { name: 'Bac des sections' });
    expect(bac.closest('.graphe-libre')).not.toBeNull();
    expect(screen.getByRole('button', { name: 'Plan en plein écran' })).toBeInTheDocument();
  });

  it('environnements par système : pastilles sur la fiche (barrée si retirée du système), choix dans l’édition', async () => {
    const base = mep().systemes[0];
    rendre(
      planExemple({
        systemes: [
          { ...base, id: 's1', nom: 'API', environnements: [{ nom: 'Production', production: true }] },
          {
            ...base,
            id: 's2',
            nom: 'BDD',
            environnements: [
              { nom: 'Préproduction', production: false },
              { nom: 'Production', production: true },
            ],
          },
          { ...base, id: 's3', nom: 'WEB', environnements: [{ nom: 'Production', production: true }] },
        ],
        etapes: [
          etape({
            id: 'e1',
            code: 'E0001',
            action: 'Exemple',
            ligne: 0,
            ordre: 0,
            systemeIds: ['s1'],
            environnements: [
              { systemeId: 's1', nom: 'Production' },
              { systemeId: 's1', nom: 'Recette' },
            ],
          }),
        ],
      }),
    );
    // Sur la fiche du graphe : « Prod » en orange, « Recette » barrée (n'est plus choisie pour API).
    const retiree = screen.getByTitle(/Recette : n’est plus choisi pour ce système/);
    expect(retiree).toHaveClass('line-through');
    expect(screen.getByTitle('Production')).toHaveTextContent('Prod');

    fireEvent.click(carte(/^E0001/));
    const espace = screen.getByRole('dialog');
    // BDD a deux environnements : on choisit ; WEB n'en a qu'un : coché d'office.
    await userEvent.click(within(espace).getAllByRole('button', { name: 'BDD', pressed: false })[0]);
    expect(within(espace).getByRole('button', { name: 'BDD : Préproduction', pressed: false })).toBeInTheDocument();
    await userEvent.click(within(espace).getByRole('button', { name: 'BDD : Préproduction' }));
    await userEvent.click(within(espace).getAllByRole('button', { name: 'WEB', pressed: false })[0]);
    expect(within(espace).getByRole('button', { name: 'WEB : Production', pressed: true })).toBeInTheDocument();
    await userEvent.click(within(espace).getByRole('button', { name: /^API : Recette/ }));
    await userEvent.click(within(espace).getByRole('button', { name: 'Enregistrer' }));
    expect(api.modifierEtape).toHaveBeenCalledWith(
      'm1',
      'e1',
      expect.objectContaining({
        systemeIds: ['s1', 's2', 's3'],
        environnements: [
          { systemeId: 's1', nom: 'Production' },
          { systemeId: 's2', nom: 'Préproduction' },
          { systemeId: 's3', nom: 'Production' },
        ],
      }),
    );
  });

  it('lecture seule : ni « + », ni poignées actives, ni bac modifiable ; panneau en lecture', async () => {
    rendre(planExemple({ phase: 'Execution', droits: { ...droitsEdition, peutEditerContenu: false } }));
    expect(screen.queryByRole('button', { name: /^Étape$/ })).toBeNull();
    expect(screen.queryByRole('button', { name: /Ajouter une étape après/ })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Nouvelle section' })).toBeNull();
    expect(document.querySelector('.react-flow__handle.connectable')).toBeNull();
    fireEvent.click(carte(/^E0003/));
    await waitFor(() => expect(screen.getByRole('heading', { name: 'Contrôle final' })).toBeInTheDocument());
    expect(screen.queryByRole('button', { name: 'Enregistrer' })).toBeNull();
  });
});

const modeleEtape = (p: Partial<ModeleEtape> & Pick<ModeleEtape, 'id' | 'action'>): ModeleEtape => ({
  systemeCatalogueIds: ['c-api'],
  type: 'Standard',
  criticite: 'Moderee',
  dureeEstimeeMinutes: 5,
  instructions: 'systemctl restart api',
  resultatAttendu: 'Service actif',
  fin: 'TestUnitaire',
  sauvegardeEmplacement: '',
  sauvegardeMethodeRecuperation: '',
  resolutionIds: [],
  utilisations: 0,
  creeLe: '2026-01-01T00:00:00Z',
  version: 1,
  modifieLe: '2026-01-01T00:00:00Z',
  modifieParId: null,
  modifiePar: null,
  ...p,
});

const MODELES = [
  modeleEtape({ id: 'ma', action: 'Redémarrage de l’API', utilisations: 5 }),
  modeleEtape({ id: 'mb', action: 'Purge du cache', systemeCatalogueIds: ['c-web'], utilisations: 9 }),
  modeleEtape({ id: 'mc', action: 'Contrôle de santé', utilisations: 1 }),
];

describe('EtapesEditeur : étapes modèles', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    window.localStorage.clear();
    changerModeEspace('plein');
    vi.mocked(api.etapesModeles).mockResolvedValue(MODELES);
  });

  it('menu des plus utilisées pour les systèmes de la MEP ; la copie flotte, puis se pose après une carte', async () => {
    rendre(planExemple());
    await waitFor(() => expect(api.etapesModeles).toHaveBeenCalled());
    await userEvent.click(screen.getByRole('button', { name: /Étape modèle/ }));
    const menu = await screen.findByRole('menu');
    const items = within(menu)
      .getAllByRole('menuitem')
      .map((i) => i.textContent);
    // WEB n'est pas dans la MEP : « Purge du cache » n'est pas proposée.
    expect(items[0]).toContain('Redémarrage de l’API');
    expect(items[1]).toContain('Contrôle de santé');
    expect(items.some((t) => t?.includes('Purge du cache'))).toBe(false);
    await userEvent.click(within(menu).getByRole('menuitem', { name: /Redémarrage de l’API/ }));

    const flottante = await screen.findByRole('group', {
      name: /E0004, copie de l’étape modèle « Redémarrage de l’API »/,
    });
    expect(flottante).toHaveTextContent('À placer');
    expect(apiGraphe.insererModele).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: 'Poser à part' })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Poser la copie après E0001' }));
    expect(apiGraphe.insererModele).toHaveBeenCalledWith('m1', 'ma', 'Deploiement', { apres: 'e1' }, null);
    expect(apiGraphe.creerEtape).not.toHaveBeenCalled();
  });

  it('Échap annule l’insertion : rien n’est écrit', async () => {
    rendre(planExemple());
    await waitFor(() => expect(api.etapesModeles).toHaveBeenCalled());
    await userEvent.click(screen.getByRole('button', { name: /Étape modèle/ }));
    await userEvent.click(await screen.findByRole('menuitem', { name: /Contrôle de santé/ }));
    await screen.findByRole('group', { name: /à placer/ });
    await waitFor(() => expect(screen.queryByRole('menu')).toBeNull());
    fireEvent.keyDown(document.body, { key: 'Escape' });
    await waitFor(() => expect(screen.queryByRole('group', { name: /à placer/ })).toBeNull());
    expect(screen.getByRole('button', { name: 'Ajouter une étape après E0001' })).toBeInTheDocument();
    expect(apiGraphe.insererModele).not.toHaveBeenCalled();
  });

  it('« Parcourir la bibliothèque » : systèmes de la MEP d’abord, aperçu, avertissement si un système manque', async () => {
    rendre(planExemple());
    await waitFor(() => expect(api.etapesModeles).toHaveBeenCalled());
    await userEvent.click(screen.getByRole('button', { name: 'Parcourir la bibliothèque' }));
    const dialogue = await screen.findByRole('dialog');
    expect(within(dialogue).getByRole('button', { name: 'API : 2 étapes' })).toBeInTheDocument();
    expect(within(dialogue).queryByRole('button', { name: /^WEB/ })).toBeNull();
    await userEvent.click(within(dialogue).getByRole('button', { name: 'les afficher' }));
    await userEvent.click(within(dialogue).getByRole('button', { name: 'WEB : 1 étape' }));
    await userEvent.click(within(dialogue).getByRole('button', { name: /Purge du cache/ }));
    expect(within(dialogue).getByText(/Le système WEB de ce modèle n’est pas dans la MEP/)).toBeInTheDocument();
    await userEvent.click(within(dialogue).getByRole('button', { name: 'Insérer une copie' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    await userEvent.click(screen.getByRole('button', { name: 'Poser à part' }));
    expect(apiGraphe.insererModele).toHaveBeenCalledWith('m1', 'mb', 'Deploiement', {}, null);
  });

  it('« Transformer en étape modèle » depuis l’espace d’une étape, nom modifiable', async () => {
    vi.mocked(api.etapeModeleDepuisEtape).mockResolvedValue(modeleEtape({ id: 'md', action: 'Contrôle type' }));
    rendre(planExemple());
    fireEvent.click(carte(/^E0002/));
    await userEvent.click(await screen.findByRole('button', { name: 'Transformer en étape modèle' }));
    const dialogue = await screen.findByRole('dialog', { name: 'Transformer E0002 en étape modèle ?' });
    expect(within(dialogue).getByText(/E0002 est un point de non-retour dans cette MEP/)).toBeInTheDocument();
    const nom = within(dialogue).getByLabelText('Nom de l’étape modèle *');
    await userEvent.clear(nom);
    await userEvent.type(nom, 'Contrôle type');
    await userEvent.click(within(dialogue).getByRole('button', { name: 'Créer l’étape modèle' }));
    await waitFor(() => expect(api.etapeModeleDepuisEtape).toHaveBeenCalledWith('m1', 'e2', 'Contrôle type'));
  });

  it('une copie d’étape modèle le dit dans son espace, pas sur la fiche du graphe', async () => {
    const plan = planExemple();
    plan.etapes[2] = { ...plan.etapes[2], modeleEtapeId: 'ma' };
    rendre(plan);
    await waitFor(() => expect(api.etapesModeles).toHaveBeenCalled());
    expect(carte(/^E0003/)).not.toHaveTextContent(/modèle/i);
    fireEvent.click(carte(/^E0003/));
    expect(await screen.findByText(/Copie de l’étape modèle « Redémarrage de l’API »/)).toBeInTheDocument();
  });
});
