import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { api } from '../api';
import { MepContext } from '../mepContext';
import { etape, mep } from '../test/fabrique';
import type { Mep, ModeleEtape, ResolutionType } from '../types';
import { TooltipProvider } from '../ui/Tooltip';
import { EtapesEditeur } from './EtapesEditeur';
import { changerModeEspace } from './graphe/EspaceEtape';

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
    ajouterEtape: vi.fn(async () => ({ etapes: [] })),
    placerEtape: vi.fn(async () => ({})),
    supprimerEtape: vi.fn(async () => ({})),
    supprimerSysteme: vi.fn(async () => ({})),
    ajouterSystemeDepuisCatalogue: vi.fn(async () => ({})),
    etapesModeles: vi.fn(async () => []),
    insererEtapeModele: vi.fn(async () => ({ etapes: [] })),
    etapeModeleDepuisEtape: vi.fn(),
  },
}));

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

describe('EtapesEditeur : graphe en préparation', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    window.localStorage.clear();
    changerModeEspace('plein');
  });

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

  it('Entrée sur une fiche ouvre l’espace d’édition ; un préalable s’ajoute', async () => {
    rendre(planExemple());
    carte(/^E0003/).focus();
    await userEvent.keyboard('{Enter}');
    expect(screen.getByLabelText('Action')).toHaveValue('Contrôle final');
    const panneau = screen.getByRole('dialog');
    expect(within(panneau).getByRole('button', { name: 'Retirer le préalable E0001' })).toBeInTheDocument();
    await userEvent.selectOptions(within(panneau).getByLabelText('Ajouter un préalable'), 'e2');
    expect(api.modifierEtape).toHaveBeenCalledWith(
      'm1',
      'e3',
      expect.objectContaining({ dependances: ['e1', 'e2'], action: 'Contrôle final', systemeIds: ['s1'] }),
    );
  });

  it('un lecteur d’écran ouvre une fiche (clic sans pointeur)', () => {
    rendre(planExemple());
    fireEvent.click(carte(/^E0003/));
    expect(screen.getByLabelText('Action')).toHaveValue('Contrôle final');
  });

  it('Alt + flèches déplacent une fiche au clavier', async () => {
    rendre(planExemple());
    carte(/^E0001/).focus();
    await userEvent.keyboard('{Alt>}{ArrowRight}{/Alt}');
    expect(api.placerEtape).toHaveBeenLastCalledWith('m1', 'e1', 0, 1, false);
    expect(screen.queryByRole('dialog')).toBeNull();
    await userEvent.keyboard('{Alt>}{ArrowDown}{/Alt}');
    expect(api.placerEtape).toHaveBeenLastCalledWith('m1', 'e1', 1, 0, false);
    carte(/^E0003/).focus();
    await userEvent.keyboard('{Alt>}{ArrowUp}{/Alt}');
    expect(api.placerEtape).toHaveBeenLastCalledWith('m1', 'e3', 0, 0, false);
    // Impossible (déjà en tête de ligne, ou seule sur la dernière ligne) : rien n'est envoyé.
    vi.mocked(api.placerEtape).mockClear();
    carte(/^E0001/).focus();
    await userEvent.keyboard('{Alt>}{ArrowLeft}{/Alt}');
    carte(/^E0003/).focus();
    await userEvent.keyboard('{Alt>}{ArrowDown}{/Alt}');
    expect(api.placerEtape).not.toHaveBeenCalled();
  });

  it('Alt + Maj + ↑ / ↓ mettent une fiche sur une ligne à elle', async () => {
    rendre(planExemple());
    carte(/^E0002/).focus();
    await userEvent.keyboard('{Alt>}{Shift>}{ArrowDown}{/Shift}{/Alt}');
    expect(api.placerEtape).toHaveBeenLastCalledWith('m1', 'e2', 1, 0, true);
    carte(/^E0003/).focus();
    await userEvent.keyboard('{Alt>}{Shift>}{ArrowUp}{/Shift}{/Alt}');
    expect(api.placerEtape).toHaveBeenLastCalledWith('m1', 'e3', 0, 0, true);
  });

  it('un seul déplacement à la fois : une touche répétée pendant l’enregistrement est ignorée', async () => {
    let finir: () => void = () => undefined;
    vi.mocked(api.placerEtape).mockImplementationOnce(
      () => new Promise<Mep>((ok) => (finir = () => ok(planExemple()))),
    );
    rendre(planExemple());
    carte(/^E0001/).focus();
    await userEvent.keyboard('{Alt>}{ArrowDown}{/Alt}');
    await userEvent.keyboard('{Alt>}{ArrowDown}{/Alt}');
    await userEvent.keyboard('{Alt>}{ArrowRight}{/Alt}');
    expect(api.placerEtape).toHaveBeenCalledTimes(1);
    finir();
    await waitFor(() => expect(screen.getByText(/E0001 déplacée : liens recréés/)).toBeInTheDocument());
    carte(/^E0001/).focus();
    await userEvent.keyboard('{Alt>}{ArrowRight}{/Alt}');
    expect(api.placerEtape).toHaveBeenCalledTimes(2);
  });

  it('fiche modifiée par ailleurs pendant la saisie : avertissement avant d’écraser', async () => {
    const m = planExemple();
    const { rerendre } = rendre(m);
    await userEvent.click(carte(/^E0003/));
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
    await userEvent.click(carte(/^E0003/));
    await userEvent.click(screen.getByRole('button', { name: 'Retirer le préalable E0001' }));
    expect(api.modifierEtape).toHaveBeenCalledWith('m1', 'e3', expect.objectContaining({ dependances: [] }));
  });

  it('enregistre la fiche (systèmes multiples, durée, instructions en blocs) ; indicateur de saisie', async () => {
    const base = mep().systemes[0];
    rendre(planExemple({ systemes: [base, { ...base, id: 's2', nom: 'BDD' }] }));
    await userEvent.click(carte(/^E0001/));
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
    await userEvent.click(carte(/^E0001/));
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
    await userEvent.click(carte(/^E0001/));
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
    await userEvent.click(carte(/^E0003/));
    await userEvent.click(screen.getByRole('button', { name: 'Afficher en panneau latéral' }));
    expect(window.localStorage.getItem('mep.espace.mode')).toBe('panneau');
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    // Panneau : le graphe reste utilisable (pas de surcouche modale), une autre fiche s'ouvre d'un clic.
    await userEvent.click(screen.getAllByRole('button', { name: /^E0001/ })[0]);
    expect(screen.getByLabelText('Action')).toHaveValue('Sauvegarde BDD');
    await userEvent.click(screen.getByRole('button', { name: 'Afficher en plein écran' }));
    expect(window.localStorage.getItem('mep.espace.mode')).toBe('plein');
  });

  it('fermer avec des modifications non enregistrées : garde', async () => {
    rendre(planExemple());
    await userEvent.click(carte(/^E0003/));
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
    await userEvent.click(carte(/^E0003/));
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
    await userEvent.click(carte(/^E0003/));
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
    await userEvent.click(carte(/^E0003/));
    await userEvent.type(await screen.findByRole('combobox', { name: 'Ajouter une résolution' }), 'purge');
    await userEvent.click(screen.getByRole('button', { name: /1 autre dans d’autres systèmes/ }));
    expect(within(screen.getByRole('listbox')).getByRole('option')).toHaveTextContent(
      'WEB : purge du cache CDN refusée',
    );
  });

  it('résolutions : créer une fiche avec le texte cherché comme titre', async () => {
    rendre(planExemple());
    await userEvent.click(carte(/^E0003/));
    await userEvent.type(await screen.findByRole('combobox', { name: 'Ajouter une résolution' }), 'LDAP muet');
    await userEvent.click(screen.getByRole('button', { name: /Nouvelle fiche de résolution « LDAP muet »/ }));
    expect(
      await screen.findByText('Destination : /parametres/resolutions/nouvelle?mep=m1&etape=e3&titre=LDAP+muet'),
    ).toBeInTheDocument();
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

  it('double clic sur « Nouvelle ligne » : une seule étape créée', async () => {
    let finir: () => void = () => undefined;
    vi.mocked(api.ajouterEtape).mockImplementationOnce(
      () => new Promise<Mep>((ok) => (finir = () => ok(planExemple()))),
    );
    rendre(planExemple());
    await userEvent.dblClick(screen.getByRole('button', { name: /Nouvelle ligne/ }));
    await userEvent.click(screen.getByRole('button', { name: 'Ajouter une étape sur la ligne 1' }));
    expect(api.ajouterEtape).toHaveBeenCalledTimes(1);
    await act(async () => finir());
    await userEvent.click(screen.getByRole('button', { name: /Nouvelle ligne/ }));
    expect(api.ajouterEtape).toHaveBeenCalledTimes(2);
  });

  it('ajouter une étape pendant une saisie : la saisie n’est pas perdue, l’espace demande quoi en faire', async () => {
    const m = planExemple();
    const nouvelle = etape({ id: 'e4', code: 'E0004', action: '', ligne: 0, ordre: 2 });
    const apres = { ...m, etapes: [...m.etapes, nouvelle] };
    vi.mocked(api.ajouterEtape).mockResolvedValueOnce(apres);
    // Panneau latéral : le graphe reste utilisable pendant la saisie.
    changerModeEspace('panneau');
    const { rerendre } = rendre(m);
    await userEvent.click(carte(/^E0003/));
    await userEvent.type(screen.getByLabelText('Action'), ' bis');
    await userEvent.click(screen.getByRole('button', { name: 'Ajouter une étape sur la ligne 1' }));
    expect(api.ajouterEtape).toHaveBeenCalledWith('m1', 'Deploiement', 0);
    rerendre(apres);
    expect(await screen.findByRole('alertdialog', { name: 'Modifications non enregistrées' })).toBeInTheDocument();
    expect(screen.getByLabelText('Action')).toHaveValue('Contrôle final bis');
    await userEvent.click(screen.getByRole('button', { name: 'Changer d’étape sans enregistrer' }));
    // La nouvelle étape s'ouvre, le focus sur son action.
    expect(screen.getByLabelText('Action')).toHaveValue('');
    expect(screen.getByLabelText('Action')).toHaveFocus();
  });

  it('retrait d’un système utilisé par des étapes : refusé avec un message', async () => {
    rendre(planExemple());
    await userEvent.click(screen.getByRole('button', { name: 'Retirer API de la MEP' }));
    expect(screen.getByText(/API est utilisé par E0001, E0002, E0003/)).toBeInTheDocument();
    expect(api.supprimerSysteme).not.toHaveBeenCalled();
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

    await userEvent.click(carte(/^E0001/));
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

  it('menu des plus utilisées pour les systèmes de la MEP ; la copie flotte, puis se pose sur une ligne', async () => {
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
    expect(api.insererEtapeModele).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: /Poser ici, sur une nouvelle ligne/ })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Poser l’étape sur la ligne 1' }));
    expect(api.insererEtapeModele).toHaveBeenCalledWith('m1', 'ma', 'Deploiement', 0, undefined, false);
    expect(api.ajouterEtape).not.toHaveBeenCalled();
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
    expect(screen.getByRole('button', { name: 'Ajouter une étape sur la ligne 1' })).toBeInTheDocument();
    expect(api.insererEtapeModele).not.toHaveBeenCalled();
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
    await userEvent.click(screen.getByRole('button', { name: /Poser ici, sur une nouvelle ligne/ }));
    expect(api.insererEtapeModele).toHaveBeenCalledWith('m1', 'mb', 'Deploiement', undefined, undefined, false);
  });

  it('« Transformer en étape modèle » depuis l’espace d’une étape, nom modifiable', async () => {
    vi.mocked(api.etapeModeleDepuisEtape).mockResolvedValue(modeleEtape({ id: 'md', action: 'Contrôle type' }));
    rendre(planExemple());
    await userEvent.click(carte(/^E0002/));
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
    await userEvent.click(carte(/^E0003/));
    expect(await screen.findByText(/Copie de l’étape modèle « Redémarrage de l’API »/)).toBeInTheDocument();
  });
});
