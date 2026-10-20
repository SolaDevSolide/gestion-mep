import '@xyflow/react/dist/base.css';
import {
  ReactFlow,
  ReactFlowProvider,
  ViewportPortal,
  useReactFlow,
  type NodeChange,
  type Connection,
  applyNodeChanges,
} from '@xyflow/react';
import { Map as IconeCarte, Maximize, ZoomIn, ZoomOut } from 'lucide-react';
import { createPortal } from 'react-dom';
import {
  useCallback,
  useEffect,
  useLayoutEffect,
  useMemo,
  useRef,
  useState,
  type KeyboardEvent,
  type PointerEvent as PointerEventReact,
  type ReactNode,
} from 'react';
import type { Etape, Mep, ModeleEtape, TypePlan } from '../../types';
import { BoutonIcone } from '../../ui/Button';
import { cn } from '../../ui/cn';
import type { Deplacement, PlaceEtape } from './apiGraphe';
import { AvisRangement } from './AvisRangement';
import { BarreOutilsGraphe } from './BarreOutilsGraphe';
import { CarteEtape, type ModeGraphe } from './CarteEtape';
import {
  chercherCible,
  fantomePour,
  type Boite,
  type CarteDepot,
  type CercleDepot,
  type Fantome,
  type Geometrie,
} from './cibleDepot';
import { deplacementPour, interpreterConnexion, placePour, type CibleDepot } from './connexions';
import { ContexteBac, ContexteGraphe, type BacGraphe, type ValeurGraphe } from './contexteGraphe';
import { EtapeFlottante } from './EtapesModeles';
import { LiensEnMouvement, MarqueursLiens, TYPES_LIENS } from './LienGraphe';
import { MenuLabel } from '../../ui/Menu';
import { ContenuMenuLien, EntreesCreation, MenuAuPoint, type ChoixCreation } from './MenusGraphe';
import { MINI_CARTE, MiniCarte, useMiniCarte } from './MiniCarte';
import { construireModele, idSection, type LienModele } from './modeleGraphe';
import { TYPES_NOEUDS, type DonneesNoeud } from './NoeudsGraphe';
import { GLISSABLE, noeudsDepuis, Z, ZOOM_MAX, ZOOM_MIN, type LienRf, type NoeudRf } from './noeudsRf';
import { CARTE, tailleSectionRepliee, GRILLE, noeudAuCentre, type Taille } from './rangement';
import { liensAuPoint } from './routage';
import { sautsSansJonctions } from './routageBordure';
import { SqueletteGraphe } from './SqueletteGraphe';
import { infoSection, type Section } from './sections';
import { useHauteurDisponible } from './hauteurDisponible';
import { hoteMesure, mesuresManquantes, PREFIXE_MESURE, tailleRetenue } from './mesuresGraphe';
import { useMontageAnime } from './useMontageAnime';
import { hautCollant, largeurTiroir, MARGE_CADRE } from './petitEcran';
import { mouvementReduit } from '../../ui/mouvement';
import { useRangement } from './useRangement';

const PAS_ZOOM = 1.2;

/** Actions d'édition du graphe ; absentes en lecture ou en exécution. */
export interface ActionsGraphe {
  /** Création rapide (ou pose de la copie d'une étape modèle) : après une carte, sur un lien, en parallèle, isolée. */
  ajouter?: (place: PlaceEtape) => void;
  /** Carte lâchée sur un lien (insertion) ou sur une carte (après elle) ; le serveur en déduit la section. */
  deplacer?: (id: string, d: Deplacement) => void;
  /**
   * Réorganisation gauche-droite d'un niveau : `avant`, les éléments du niveau (étapes et sections) dans l'ordre actuel ;
   * `apres`, les mêmes dans le nouvel ordre. Aucun lien ne change.
   */
  ordonner?: (avant: string[], apres: string[]) => void;
  /** Lien tracé entre deux poignées : `vers` attendra `de` (étapes ou cercles). */
  lier?: (de: string, vers: string) => void;
  supprimerLien?: (l: LienModele) => void;
  ouvrirCercle?: (cercleId: string) => void;
  /** Ouvre la vue d'édition d'une section (et, si donnée, la fiche d'une de ses étapes). */
  editerSection?: (sectionId: string, etapeId?: string) => void;
  /** Carte lâchée sur le bac : l'étape quitte le graphe (ses suivants attendent ses préalables) pour « Étapes à placer ». */
  retirerEtape?: (etapeId: string) => void;
  /** Section lâchée sur le bac (ou « Retirer du plan ») : elle y retourne, ses cercles perdent leurs liens extérieurs. */
  retirerSection?: (sectionId: string) => void;
  /** Étape du bac glissée dans le graphe : sur un lien, après une carte ou un cercle, en parallèle, ou à part (`{}`). */
  placerEtape?: (etapeId: string, place: PlaceEtape) => void;
}

interface Props {
  mep: Mep;
  plan: TypePlan;
  /** Étapes du plan. */
  etapes: Etape[];
  /** Sections du plan. */
  sections: Section[];
  mode: ModeGraphe;
  modifiable?: boolean;
  descriptions: Map<string, string>;
  /** Étape sélectionnée (son espace est ouvert). */
  selection: string | null;
  onSelection: (id: string | null) => void;
  /** Vue d'édition d'une section : seules ses étapes et ses cercles. */
  sectionVue?: string | null;
  deplies: ReadonlySet<string>;
  onBasculer: (sectionId: string) => void;
  /** Gel demandé par l'écran (saisie en cours dans l'espace d'une étape). */
  gel?: boolean;
  /** Change après une action de la personne : le rangement suit aussitôt, même gelé. */
  jeton?: number;
  actions?: ActionsGraphe;
  onMessage?: (message: string) => void;
  estEstompee?: (e: Etape) => boolean;
  /** Copie d'une étape modèle pas encore posée : elle flotte, on la glisse sur un lien ou une carte, ou on clique un « + ». */
  flottante?: { etape: Etape; onAnnuler: () => void } | null;
  /** Contrôles de gauche de la barre d'outils (étapes modèles). */
  enTete?: ReactNode;
  /** Contrôles ajoutés à droite de la barre d'outils (Tout replier, Étape…). */
  outils?: ReactNode;
  /** Panneau latéral intérieur, à droite du graphe (bac des sections), avec son propre défilement ; il lit le dépôt et le
   * glisser de ses étapes par `useBacGraphe`. */
  lateral?: ReactNode;
  /** Espace d'une étape ouvert en panneau à droite : le plein écran lui laisse sa place. */
  retraitDroit?: boolean;
  /** Bandeau d'informations dans le cadre, sous la barre d'outils (résumé, messages, en-tête de la section éditée). */
  bandeau?: ReactNode;
  /** Plein écran tenu par l'écran (il reste actif quand on entre dans une section ou qu'on en sort). */
  plein?: boolean;
  onPlein?: (plein: boolean) => void;
  /** « Optimiser les liens » : liens (tels que le serveur les connaît) qui seront retirés, en rouge en attendant la confirmation. */
  retraits?: ReadonlyArray<{ de: string; vers: string }> | null;
  /** Vue d'ensemble (#61) : cartes et sections colorées par état, étapes bloquées ; mini-carte masquée par défaut. */
  ensemble?: { bloquees: ReadonlyMap<string, Etape[]> } | null;
  /** Création depuis une étape modèle, proposée avec « Étape vierge » dans chaque menu de création. */
  creation?: {
    /** Les plus utilisées pour les systèmes de la MEP. */
    modeles: ModeleEtape[];
    depuisModele: (place: PlaceEtape, m: ModeleEtape) => void;
    /** Ouvre la bibliothèque ; l'étape choisie sera posée à cette place. */
    parcourir: (place: PlaceEtape) => void;
  };
  /**
   * Vue compacte (fenêtre de moins de 800 px de haut ; sans effet en plein écran) : cadre à la hauteur de la fenêtre moins
   * 24 px, la page défile jusqu'à lui quand on ouvre une section ou une étape ; barre d'outils d'une ligne avec `resume`.
   */
  compact?: boolean;
  /** Vue compacte : résumé du plan et messages, dans la barre d'outils. */
  resume?: ReactNode;
  /** Menu « ⋯ » de la barre d'outils (export, aide). */
  menu?: ReactNode;
  /**
   * Vue compacte : étape ouverte en tiroir par-dessus le graphe, sous le bandeau. `emplacement` reçoit l'élément où l'écran
   * affiche le tiroir (`EmplacementTiroir`) ; `ouvert` : une étape y est ouverte (le graphe se recentre sur sa carte).
   */
  tiroir?: { emplacement: (el: HTMLElement | null) => void; ouvert: boolean } | null;
}

/** Menu ouvert sur le graphe : celui d'un lien (choisir parmi des liens fusionnés, insérer, supprimer) ou de création. */
type MenuOuvert =
  | { type: 'lien'; point: { x: number; y: number }; ids: string[] }
  | { type: 'creation'; point: { x: number; y: number }; place: PlaceEtape; ancre: string | null; titre: string };

/** Point du pointeur (souris ou doigt) d'un événement de glisser. */
function pointDe(ev: MouseEvent | TouchEvent): { x: number; y: number } {
  if ('touches' in ev) {
    const t = ev.touches[0] ?? ev.changedTouches[0];
    return { x: t?.clientX ?? 0, y: t?.clientY ?? 0 };
  }
  return { x: ev.clientX, y: ev.clientY };
}

/** Graphe libre (React Flow + rangement ELK) : cartes, sections, cercles ; création rapide, glisser par liens. */
export function GrapheLibre(props: Props) {
  return (
    <ReactFlowProvider>
      <Graphe {...props} />
    </ReactFlowProvider>
  );
}

/** Rangée (même niveau, même cadre) du nœud glissé : ordre actuel, centres et bande verticale, en coordonnées du graphe. */
interface Rangee {
  id: string;
  ids: string[];
  boites: { x: number; largeur: number }[];
  haut: number;
  bas: number;
}

function Graphe(props: Props) {
  const {
    mep,
    plan,
    etapes,
    sections,
    mode,
    modifiable = false,
    descriptions,
    selection,
    onSelection,
    sectionVue = null,
    deplies,
    onBasculer,
    gel = false,
    jeton = 0,
    actions = {},
    onMessage,
    estEstompee,
    flottante = null,
    enTete,
    outils,
    lateral,
    retraitDroit = false,
    bandeau,
    creation,
    retraits = null,
    resume,
    menu: menuPlus,
    tiroir = null,
  } = props;
  const edition = mode === 'edition' && modifiable;
  const placement = edition && !!flottante;
  const rf = useReactFlow<NoeudRf, LienRf>();

  const parId = useMemo(() => new Map(etapes.map((e) => [e.id, e])), [etapes]);
  const parSection = useMemo(() => new Map(sections.map((s) => [s.id, s])), [sections]);
  const etapesParSection = useMemo(() => {
    const m = new Map<string, Etape[]>();
    for (const e of etapes) {
      const sid = infoSection(e).sectionId;
      if (sid) m.set(sid, [...(m.get(sid) ?? []), e]);
    }
    return m;
  }, [etapes]);
  const modele = useMemo(
    () => construireModele({ etapes, sections, deplies, sectionVue }),
    [etapes, sections, deplies, sectionVue],
  );
  const noeudsModele = useMemo(() => new Map(modele.noeuds.map((n) => [n.id, n])), [modele]);
  const modeleParId = useRef(noeudsModele);
  modeleParId.current = noeudsModele;

  // ---------- Cadre : hauteur de la fenêtre, plein écran.
  const cadre = useRef<HTMLDivElement>(null);
  const [pleinLocal, setPleinLocal] = useState(false);
  const plein = props.plein ?? pleinLocal;
  const onPlein = props.onPlein;
  const setPlein = useCallback((v: boolean) => (onPlein ? onPlein(v) : setPleinLocal(v)), [onPlein]);
  const compact = !!props.compact && !plein;
  const tiroirOuvert = compact && !!tiroir?.ouvert;
  const hauteur = useHauteurDisponible(cadre, !plein, compact);
  useEffect(() => {
    if (!plein) return undefined;
    const echap = (ev: globalThis.KeyboardEvent) => {
      // Échap ferme d'abord ce qui est ouvert par-dessus (espace d'une étape, dialogue, menu).
      if (ev.key !== 'Escape' || ev.defaultPrevented || document.querySelector('[role="dialog"], [role="menu"]'))
        return;
      setPlein(false);
    };
    window.addEventListener('keydown', echap);
    return () => window.removeEventListener('keydown', echap);
  }, [plein, setPlein]);
  const [miniCarte, basculerMiniCarte] = useMiniCarte(!!props.ensemble);

  // ---------- Gestes en cours : gel du rangement (protection 1).
  const [glisse, setGlisse] = useState<string | null>(null);
  const [trace, setTrace] = useState(false);
  const [cible, setCible] = useState<CibleDepot | null>(null);
  const [tailles, setTailles] = useState<ReadonlyMap<string, Taille>>(() => new Map());
  const taillesRef = useRef(tailles);
  taillesRef.current = tailles;
  const gele = gel || glisse !== null || trace;
  // Graphe montré seulement une fois rangé (tailles mesurées, vue d'arrivée posée) : avant, un squelette ; jamais de carte
  // à une place provisoire. Même chose en changeant de vue (plan ↔ section) : l'ancienne vue disparaît aussitôt, la nouvelle
  // apparaît en fondu une fois rangée.
  const contexte = `${plan}|${sectionVue ?? ''}`;
  const [pret, setPret] = useState(false);
  const [contexteAffiche, setContexteAffiche] = useState(contexte);
  if (contexteAffiche !== contexte) {
    setContexteAffiche(contexte);
    setPret(false);
  }
  // Graphe affiché : un repli, un dépli ou une étape reçue n'est rangé qu'une fois les nouveaux nœuds mesurés (en
  // invisible), en un seul rangement définitif ; d'ici là, le graphe affiché reste tel quel, liens compris.
  const { range, enAttente, indisponible, reessayer } = useRangement(modele, tailles, gele, jeton, pret);
  // Rangement impossible : le graphe se montre aussitôt à ses places actuelles, avec le message « Réessayer ».
  const montre = pret || indisponible !== null;
  // Vue compacte : ouvrir une section ou une étape fait défiler la page jusqu'au cadre (12 px au-dessus) ; l'en-tête de la
  // MEP reste à un défilement. Encore une fois quand le graphe paraît : ce qui est au-dessus a pu changer de hauteur.
  useEffect(() => {
    if (!compact || (!sectionVue && !tiroirOuvert)) return undefined;
    const image = window.requestAnimationFrame(() => {
      const el = cadre.current;
      if (!el) return;
      const haut = el.getBoundingClientRect().top - hautCollant() - MARGE_CADRE;
      if (Math.abs(haut) > 1)
        window.scrollTo({ top: window.scrollY + haut, behavior: mouvementReduit() ? 'auto' : 'smooth' });
    });
    return () => window.cancelAnimationFrame(image);
  }, [compact, sectionVue, tiroirOuvert, montre]);
  const rangeRef = useRef(range);
  rangeRef.current = range;

  // ---------- Nœuds affichés : ceux du dernier rangement appliqué, animés vers leur place (`useMontageAnime`).
  const [noeuds, setNoeuds] = useState<NoeudRf[]>([]);
  const noeudsRef = useRef<NoeudRf[]>([]);
  noeudsRef.current = noeuds;
  const modeleAffiche = range?.modele ?? modele;
  // Rangement impossible (cas anormal) : le graphe se montre quand même.
  useEffect(() => {
    if (pret) return undefined;
    const t = window.setTimeout(() => setPret(true), 6000);
    return () => window.clearTimeout(t);
  }, [pret, contexte]);
  // Dernier nœud touché (« + », dépôt) : ancre de la vue quand il n'y a pas de sélection.
  const derniereAncre = useRef<string | null>(null);
  // Section qu'on vient de déplier : la vue la montre en entier, dans le même mouvement que le dépli.
  const aMontrer = useRef<{ id: string; jusqua: number } | null>(null);
  const zone = useRef<HTMLDivElement>(null);

  const ancreCourante = useCallback(
    (avant: ReadonlyMap<string, { x: number; y: number }>) => {
      const candidats = [selection, cible?.type === 'carte' ? cible.id : null, derniereAncre.current];
      for (const c of candidats) {
        if (!c) continue;
        if (avant.has(c)) return c;
        const e = parId.get(c);
        const sid = e && infoSection(e).sectionId;
        if (sid && avant.has(idSection(sid))) return idSection(sid);
      }
      const el = zone.current;
      if (!el) return null;
      const vp = rf.getViewport();
      const centre = { x: (el.clientWidth / 2 - vp.x) / vp.zoom, y: (el.clientHeight / 2 - vp.y) / vp.zoom };
      return noeudAuCentre(avant, tailles, centre);
    },
    [selection, cible, parId, rf, tailles],
  );

  /** Marges de la vue : la mini-carte, en bas à gauche, ne couvre pas de carte après « Ajuster ». */
  const marges = () => ({
    haut: GRILLE,
    bas: GRILLE + (miniCarte ? MINI_CARTE.hauteur + 12 : 0),
  });

  /** Vue d'arrivée : tout le graphe s'il tient lisiblement, sinon le haut du graphe, centré, à 80 % au moins. */
  const vueInitiale = () => {
    const el = zone.current;
    const noeudsRf = rf.getNodes().filter((n) => !n.id.startsWith(PREFIXE_MESURE));
    if (!el || noeudsRf.length === 0) return;
    // Par identifiant : mesures et positions internes de React Flow, à jour même avant le rendu suivant.
    const b = rf.getNodesBounds(noeudsRf.map((n) => n.id));
    const m = marges();
    const hauteurUtile = el.clientHeight - m.haut - m.bas;
    const ajuste = Math.min(1, (el.clientWidth - 2 * GRILLE) / b.width, hauteurUtile / b.height);
    const z = Math.max(0.8, ajuste);
    void rf.setViewport({
      x: el.clientWidth / 2 - (b.x + b.width / 2) * z,
      y: m.haut + (ajuste >= 0.8 ? hauteurUtile / 2 - (b.y + b.height / 2) * z : -b.y * z),
      zoom: z,
    });
  };
  /**
   * Centre la vue sur une carte (vue d'arrivée, « Aller à… », étape ouverte en tiroir) : dans la partie du graphe que le
   * tiroir laisse visible quand il est ouvert.
   */
  const centrerSur = (id: string, options: { zoom: number; duration?: number }) => {
    const n = rf.getInternalNode(id);
    if (!n) return false;
    const p = n.internals.positionAbsolute;
    const el = zone.current;
    const cache = tiroirOuvert && el ? Math.min(largeurTiroir(), el.clientWidth / 2) : 0;
    void rf.setCenter(p.x + CARTE.largeur / 2 + cache / 2 / options.zoom, p.y + CARTE.hauteur / 2, options);
    return true;
  };
  const ajuster = () => {
    const m = marges();
    void rf.fitView({
      nodes: rf.getNodes().filter((n) => !n.id.startsWith(PREFIXE_MESURE)),
      padding: { top: `${m.haut}px`, x: `${GRILLE}px`, bottom: `${m.bas}px` },
      maxZoom: 1,
      duration: 250,
    });
  };

  // ---------- Nœuds de mesure : copie invisible de chaque nœud pas encore mesuré (intérieur d'une section qu'on déplie,
  // section qu'on replie, étape reçue), posée à l'intérieur d'un nœud affiché ; sa taille sert au rangement définitif.
  const manquants = useMemo(() => mesuresManquantes(modele, tailles), [modele, tailles]);
  const affiches = new Set(noeuds.map((n) => n.id));
  const premierAffiche = noeuds.find((n) => !n.parentId)?.id ?? null;
  const planMesures = manquants.map((n) => ({ n, hote: hoteMesure(n, affiches, premierAffiche) }));
  const cleMesures = planMesures.map((m) => `${m.n.id}@${m.hote ?? ''}`).join('|');
  const noeudsMesure = useMemo<NoeudRf[]>(
    () =>
      planMesures.map(({ n, hote }) => {
        const copie: NoeudRf = {
          id: PREFIXE_MESURE + n.id,
          type: n.nature,
          position: { x: 0, y: 0 },
          data: { etapeId: n.etapeId, sectionId: n.sectionId, cercleId: n.cercleId },
          draggable: false,
          selectable: false,
          connectable: false,
          focusable: false,
          className: 'noeud-mesure',
          zIndex: Z.cadre,
        };
        if (hote) copie.parentId = hote;
        if (n.nature === 'section') copie.style = { width: tailleSectionRepliee(n).largeur };
        return copie;
      }),
    // Nœuds à mesurer et leurs hôtes seulement : des objets stables, sinon React Flow les mesurerait à chaque image.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [cleMesures],
  );
  const mesuresRef = useRef(noeudsMesure);
  mesuresRef.current = noeudsMesure;
  // Sections dont le repli ou le dépli attend ses mesures et son rangement : leur bouton le signale (après un court délai).
  const cleEnCours = modele.noeuds
    .filter((n) => (n.nature === 'section' || n.nature === 'cadre') && n.sectionId)
    .filter((n) => {
      const affiche = noeuds.find((x) => x.id === n.id);
      return !!affiche && affiche.type !== n.nature;
    })
    .map((n) => n.sectionId)
    .join(',');
  const sectionsEnCours = useMemo(() => new Set(cleEnCours ? cleEnCours.split(',') : []), [cleEnCours]);
  const cleRepli = modele.noeuds
    .filter((n) => n.nature === 'section' && noeuds.some((x) => x.id === n.id && x.type === 'cadre'))
    .map((n) => n.sectionId)
    .join(',');
  const sectionsRepli = useMemo(() => new Set(cleRepli ? cleRepli.split(',') : []), [cleRepli]);

  const fait = useCallback(
    (l: LienModele) => {
      if (mode !== 'execution') return false;
      const de = l.logique?.de;
      if (de && parId.has(de)) return parId.get(de)?.etat === 'Reussie';
      const src = l.source.startsWith('s:') ? (etapesParSection.get(l.source.slice(2)) ?? []) : [parId.get(l.source)];
      return src.length > 0 && src.every((e) => e?.etat === 'Reussie');
    },
    [mode, parId, etapesParSection],
  );
  const { rangeVisible, anime, liensMouvement, arrivee, finArrivee, noterMesures } = useMontageAnime({
    range,
    modele,
    edition,
    parId,
    sectionVue,
    contexte,
    noeudsRef,
    setNoeuds,
    taillesRef,
    zone,
    aMontrer,
    ancreCourante,
    fait,
  });

  // Vue d'arrivée : après le rendu qui a donné à React Flow les nœuds rangés et mesurés.
  useEffect(() => {
    if (!arrivee) return;
    finArrivee();
    if (!selection || !centrerSur(selection, { zoom: 1 })) vueInitiale();
    setPret(true);
    // Une fois par arrivée : la sélection et les marges sont lues à ce moment.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [arrivee]);

  const onNodesChange = useCallback(
    (changes: NodeChange<NoeudRf>[]) => {
      setNoeuds((n) => applyNodeChanges(changes, n));
      const dims = changes.filter(
        (c): c is Extract<NodeChange<NoeudRf>, { type: 'dimensions' }> => c.type === 'dimensions' && !!c.dimensions,
      );
      if (dims.length === 0) return;
      // Nœuds montés invisibles avant un mouvement : quand tous sont mesurés, le mouvement part.
      noterMesures(dims);
      // Taille retenue d'après le nœud tel qu'il est à l'écran au moment de la mesure (pas d'après l'état React, qui peut
      // déjà avoir changé) : voir `tailleRetenue`.
      const lues = dims.flatMap((c) => {
        const el = zone.current?.querySelector<HTMLElement>(`.react-flow__node[data-id="${CSS.escape(c.id)}"]`);
        const id = c.id.startsWith(PREFIXE_MESURE) ? c.id.slice(PREFIXE_MESURE.length) : c.id;
        const genre = el ? /react-flow__node-(\w+)/.exec(el.className)?.[1] : undefined;
        const t = c.dimensions && tailleRetenue(genre, modeleParId.current.get(id), !!el?.style.height, c.dimensions);
        return t ? [{ id, t }] : [];
      });
      if (lues.length === 0) return;
      setTailles((avant) => {
        let change = false;
        const apres = new Map(avant);
        for (const { id, t } of lues) {
          const a = avant.get(id);
          if (a && Math.abs(a.largeur - t.largeur) < 4 && Math.abs(a.hauteur - t.hauteur) < 4) continue;
          apres.set(id, t);
          change = true;
        }
        return change ? apres : avant;
      });
    },
    [noterMesures],
  );

  // ---------- Liens affichés : tracé d'ELK, sauts aux croisements, mise en avant.
  const [lienSelectionne, setLienSelectionne] = useState<string | null>(null);
  // Liens sous le pointeur : plusieurs quand le pointeur est sur un tronçon partagé (liens fusionnés).
  const [survol, setSurvol] = useState<readonly string[]>([]);
  const [menu, setMenu] = useState<MenuOuvert | null>(null);
  // Lien sous le pointeur ou le focus dans le menu de choix (liens fusionnés) : mis en avant seul.
  const [candidat, setCandidat] = useState<string | null>(null);
  const liensParId = useMemo(() => new Map(modeleAffiche.liens.map((l) => [l.id, l])), [modeleAffiche]);
  // Liens dessinés : ceux du rangement affiché (pas d'un rangement qui attend son mouvement), pour qu'ils aillent
  // toujours avec les nœuds à l'écran.
  const modeleLiens = rangeVisible?.modele ?? modeleAffiche;
  const sauts = useMemo(() => sautsSansJonctions(rangeVisible?.rangement.routes ?? new Map()), [rangeVisible]);
  // Dans le plan, l'intérieur d'une section dépliée (ses étapes, ses cercles) se lit seulement : on le modifie dans la vue
  // de la section. Les cercles restent reliables depuis le dehors.
  const parentDe = useMemo(() => new Map(modeleAffiche.noeuds.map((n) => [n.id, n.parent ?? null])), [modeleAffiche]);
  const interieur = useCallback((id: string) => !!parentDe.get(id), [parentDe]);
  const lienInterne = useCallback((l: LienModele) => interieur(l.source) && interieur(l.cible), [interieur]);
  const lienModifiable = useCallback(
    (l: LienModele) => edition && !!l.logique && !lienInterne(l),
    [edition, lienInterne],
  );
  const poser = useCallback(
    (l: LienModele) => {
      if (!l.logique) return;
      derniereAncre.current = l.cible;
      actions.ajouter?.({ entre: l.logique });
    },
    [actions],
  );
  const code = useCallback((id: string) => parId.get(id)?.code ?? '?', [parId]);
  const nomNoeud = useCallback(
    (n: string) =>
      n.startsWith('s:')
        ? `section ${parSection.get(n.slice(2))?.titre ?? ''}`
        : n.startsWith('c:')
          ? `cercle ${sections.flatMap((s) => s.ports).find((c) => c.id === n.slice(2))?.titre ?? ''}`
          : code(n),
    [code, parSection, sections],
  );
  const libelleLien = useCallback(
    (l: LienModele) => `Lien ${nomNoeud(l.source)} vers ${nomNoeud(l.cible)}`,
    [nomNoeud],
  );
  const flecheLien = useCallback((l: LienModele) => `${nomNoeud(l.source)} → ${nomNoeud(l.cible)}`, [nomNoeud]);
  const enAvant = useMemo<ValeurGraphe['enAvant']>(() => {
    if (glisse) return null;
    const visesMenu = menu?.type === 'lien' ? (candidat ? [candidat] : menu.ids) : [];
    const actifs = (visesMenu.length ? visesMenu : survol.length ? survol : lienSelectionne ? [lienSelectionne] : [])
      .map((id) => liensParId.get(id))
      .filter((l): l is LienModele => !!l);
    if (actifs.length)
      return {
        liens: new Set(actifs.map((l) => l.id)),
        noeuds: new Set(actifs.flatMap((l) => [l.source, l.cible])),
      };
    if (!selection) return null;
    const ids = modeleAffiche.liens.filter((x) => x.source === selection || x.cible === selection).map((x) => x.id);
    return ids.length ? { liens: new Set(ids), noeuds: new Set([selection]) } : null;
  }, [glisse, menu, candidat, survol, lienSelectionne, liensParId, selection, modeleAffiche]);
  const aRetirer = useMemo(() => new Set((retraits ?? []).map((r) => `${r.de}>${r.vers}`)), [retraits]);
  const liens = useMemo<LienRf[]>(
    () =>
      modeleLiens.liens.map((l) => ({
        id: l.id,
        type: 'lien',
        source: l.source,
        sourceHandle: l.sourcePoignee,
        target: l.cible,
        targetHandle: l.ciblePoignee,
        // Survol et sélection sont calculés sur les tracés (tronçons partagés compris), pas par React Flow.
        selectable: false,
        // Atteignable au clavier (Tab puis Entrée ouvre son menu).
        focusable: true,
        selected: lienSelectionne === l.id,
        ariaLabel: libelleLien(l),
        zIndex:
          enAvant?.liens.has(l.id) || (l.logique && aRetirer.has(`${l.logique.de}>${l.logique.vers}`))
            ? Z.lienEnAvant
            : Z.lien,
        data: {
          lien: l,
          route: rangeVisible?.rangement.routes.get(l.id),
          sauts: sauts.get(l.id),
          fait: fait(l),
          onPoser: placement && lienModifiable(l) ? poser : undefined,
          retire: !!l.logique && aRetirer.has(`${l.logique.de}>${l.logique.vers}`),
        },
      })),
    [
      modeleLiens,
      rangeVisible,
      sauts,
      lienSelectionne,
      enAvant,
      fait,
      placement,
      lienModifiable,
      poser,
      libelleLien,
      aRetirer,
    ],
  );

  /** Liens dont le tracé passe sous ce point de l'écran (tolérance de quelques pixels, quel que soit le zoom). */
  const liensSous = useCallback(
    (x: number, y: number): string[] => {
      const routes = rangeRef.current?.rangement.routes;
      if (!routes) return [];
      const p = rf.screenToFlowPosition({ x, y });
      return liensAuPoint(routes, p, 7 / rf.getViewport().zoom).filter((id) => liensParId.has(id));
    },
    [rf, liensParId],
  );
  /** Le pointeur est-il sur le fond (ou le fond d'un cadre déplié), là où un lien peut être visé ? */
  const surLeFond = (el: EventTarget | null) => {
    if (!(el instanceof Element)) return false;
    const noeud = el.closest('.react-flow__node');
    if (noeud) return noeud.classList.contains('react-flow__node-cadre');
    return !!el.closest('.react-flow__pane, .react-flow__edges');
  };
  const survoler = (ev: PointerEventReact<HTMLDivElement>) => {
    if (glisse || trace || menu || ev.buttons) return;
    const ids = surLeFond(ev.target) ? liensSous(ev.clientX, ev.clientY) : [];
    setSurvol((a) => (a.length === ids.length && a.every((x, i) => x === ids[i]) ? a : ids));
  };

  // ---------- Menus : celui d'un lien et ceux de création (« Étape vierge » ou « Depuis un modèle »).
  const choixCreation = (place: PlaceEtape, ancre: string | null): ChoixCreation | undefined =>
    creation && actions.ajouter
      ? {
          modeles: creation.modeles,
          onVierge: () => {
            derniereAncre.current = ancre;
            actions.ajouter?.(place);
          },
          onModele: (m) => {
            derniereAncre.current = ancre;
            creation.depuisModele(place, m);
          },
          onParcourir: () => creation.parcourir(place),
        }
      : undefined;
  /** Création à une place : menu « Étape vierge / Depuis un modèle », sauf quand une copie attend d'être posée. */
  const demanderCreation = (place: PlaceEtape, ancre: string | null, el: Element | null, titre: string) => {
    if (!actions.ajouter) return;
    if (placement || !creation) {
      derniereAncre.current = ancre;
      actions.ajouter(place);
      return;
    }
    const r = el?.getBoundingClientRect();
    setMenu({ type: 'creation', point: r ? { x: r.left, y: r.bottom } : { x: 0, y: 0 }, place, ancre, titre });
  };
  const demanderCreationRef = useRef(demanderCreation);
  demanderCreationRef.current = demanderCreation;
  /** Clic sur le fond : sélectionne le lien visé (ou propose de choisir parmi ceux qui passent là) et ouvre son menu. */
  const cliquerFond = (ev: { clientX: number; clientY: number; target: EventTarget | null }) => {
    const ids = surLeFond(ev.target) ? liensSous(ev.clientX, ev.clientY) : [];
    if (ids.length === 0) {
      setLienSelectionne(null);
      return;
    }
    if (placement) {
      const l = ids.map((id) => liensParId.get(id)).find((x): x is LienModele => !!x && lienModifiable(x));
      if (l) poser(l);
      return;
    }
    setSurvol([]);
    setCandidat(null);
    if (ids.length === 1) setLienSelectionne(ids[0]);
    // En exécution ou en lecture, un clic met le lien en avant ; le menu sert seulement à choisir parmi des liens fusionnés.
    if (edition || ids.length > 1) setMenu({ type: 'lien', point: { x: ev.clientX, y: ev.clientY }, ids });
  };
  /** Contenu du menu ouvert. */
  const contenuMenu = (m: MenuOuvert): ReactNode => {
    if (m.type === 'creation') {
      const choix = choixCreation(m.place, m.ancre);
      return (
        choix && (
          <>
            <MenuLabel>{m.titre}</MenuLabel>
            <EntreesCreation choix={choix} />
          </>
        )
      );
    }
    const choisis = m.ids.map((id) => liensParId.get(id)).filter((l): l is LienModele => !!l);
    if (choisis.length > 1)
      return (
        <ContenuMenuLien
          titre={`${choisis.length} liens passent ici : lequel ?`}
          candidats={choisis.map((l) => ({ id: l.id, libelle: flecheLien(l) }))}
          onCandidat={setCandidat}
          onChoisirLien={(id) => {
            setLienSelectionne(id);
            setCandidat(null);
            if (edition) setMenu({ ...m, ids: [id] });
            else setMenu(null);
          }}
        />
      );
    const l = choisis[0];
    if (!l) return null;
    const sectionInterne = lienInterne(l) ? parentDe.get(l.source) : null;
    const note = !edition
      ? undefined
      : sectionInterne
        ? 'Lien à l’intérieur d’une section : il se modifie dans la vue de la section.'
        : !l.logique
          ? 'Lien d’une section repliée : dépliez-la ou ouvrez-la pour agir sur un lien précis.'
          : undefined;
    return (
      <ContenuMenuLien
        titre={flecheLien(l)}
        note={note}
        onOuvrirSection={
          sectionInterne && actions.editerSection
            ? () => actions.editerSection?.(sectionInterne.replace(/^s:/, ''))
            : undefined
        }
        creation={lienModifiable(l) && l.logique ? choixCreation({ entre: l.logique }, l.cible) : undefined}
        onSupprimer={
          lienModifiable(l) && actions.supprimerLien
            ? () => {
                actions.supprimerLien?.(l);
                setLienSelectionne(null);
              }
            : undefined
        }
      />
    );
  };

  // ---------- Glisser une carte : cible sous le pointeur (lien, carte, cercle, ou place dans sa rangée), aperçu, dépôt.
  // La géométrie (cartes, cercles, tracés) est relevée au début du geste : rien n'est lu dans le DOM pendant le mouvement.
  const geometrie = useRef<{ geo: Geometrie; boites: Map<string, Boite> } | null>(null);
  const releverGeometrie = useCallback(
    (exclu: string | null) => {
      const boites = new Map<string, Boite>();
      const cartes: CarteDepot[] = [];
      const cercles: CercleDepot[] = [];
      const parNoeud = new Map(modeleAffiche.noeuds.map((n) => [n.id, n]));
      for (const n of rf.getNodes()) {
        const i = rf.getInternalNode(n.id);
        if (!i) continue;
        const b: Boite = {
          x: i.internals.positionAbsolute.x,
          y: i.internals.positionAbsolute.y,
          largeur: i.measured.width ?? 0,
          hauteur: i.measured.height ?? 0,
        };
        boites.set(n.id, b);
        if (n.id === exclu) continue;
        const m = parNoeud.get(n.id);
        // L'intérieur d'une section dépliée ne se modifie que dans sa vue ; après un cercle de sortie, on est dehors.
        if (n.type === 'etape' && parId.has(n.id) && !interieur(n.id)) cartes.push({ id: n.id, ...b });
        else if (n.type === 'cercle' && m?.cercleId && (!interieur(n.id) || m.sens === 'Sortie'))
          cercles.push({ id: m.cercleId, noeud: n.id, ...b });
        else if (n.type === 'section' && m && !interieur(n.id))
          // Section repliée : ses cercles de sortie sur le bord bas.
          for (const c of m.sorties ?? []) {
            const x = range?.rangement.cercles.get(c.id) ?? b.largeur / 2;
            cercles.push({ id: c.id, noeud: n.id, x: b.x + x - 10, y: b.y + b.hauteur - 10, largeur: 20, hauteur: 20 });
          }
      }
      const routes = rangeRef.current?.rangement.routes;
      const liensVisables = modeleAffiche.liens
        .filter((l) => lienModifiable(l) && l.source !== exclu && l.cible !== exclu)
        .map((l) => ({ lien: l, route: routes?.get(l.id) ?? [] }))
        .filter((l) => l.route.length > 1);
      geometrie.current = { geo: { cartes, cercles, liens: liensVisables }, boites };
    },
    [rf, modeleAffiche, parId, interieur, lienModifiable, range],
  );
  const cibleSous = useCallback(
    (x: number, y: number): CibleDepot | null => {
      const g = geometrie.current;
      if (!g) return null;
      return chercherCible(rf.screenToFlowPosition({ x, y }), g.geo, rf.getViewport().zoom);
    },
    [rf],
  );
  const cleCible = (c: CibleDepot | null) =>
    c === null
      ? ''
      : c.type === 'carte'
        ? `c|${c.id}|${c.parallele ? 'p' : 'a'}`
        : c.type === 'cercle'
          ? `o|${c.noeud}|${c.id}`
          : `l|${c.lien.id}`;
  const memeCible = (a: CibleDepot | null, b: CibleDepot | null) => cleCible(a) === cleCible(b);
  const majCible = useCallback((c: CibleDepot | null) => setCible((avant) => (memeCible(avant, c) ? avant : c)), []);

  // Réorganisation : la rangée du nœud glissé est relevée au départ du geste.
  const rangee = useRef<Rangee | null>(null);
  const [reordre, setReordre] = useState<{ index: number } | null>(null);
  const releverRangee = (id: string): Rangee | null => {
    const n = rf.getInternalNode(id);
    if (!n) return null;
    const freres = noeudsRef.current
      .filter((x) => GLISSABLE.has(x.type ?? '') && (x.parentId ?? null) === (n.parentId ?? null))
      .map((x) => rf.getInternalNode(x.id))
      .filter((x): x is NonNullable<typeof x> => !!x && Math.abs(x.position.y - n.position.y) < 2)
      .sort((a, b) => a.internals.positionAbsolute.x - b.internals.positionAbsolute.x);
    if (freres.length < 2) return null;
    const haut = Math.min(...freres.map((x) => x.internals.positionAbsolute.y));
    const bas = Math.max(...freres.map((x) => x.internals.positionAbsolute.y + (x.measured.height ?? 0)));
    return {
      id,
      ids: freres.map((x) => x.id),
      boites: freres.map((x) => ({ x: x.internals.positionAbsolute.x, largeur: x.measured.width ?? 0 })),
      haut,
      bas,
    };
  };
  /** Place visée dans la rangée (index dans l'ordre sans le nœud glissé), ou null si le pointeur est hors de la rangée. */
  const placeDansRangee = (ecran: { x: number; y: number }): number | null => {
    const r = rangee.current;
    if (!r) return null;
    const p = rf.screenToFlowPosition(ecran);
    if (p.y < r.haut - GRILLE * 2 || p.y > r.bas + GRILLE * 2) return null;
    const autres = r.boites.filter((_, i) => r.ids[i] !== r.id);
    const index = autres.filter((b) => b.x + b.largeur / 2 < p.x).length;
    const actuel = r.ids.indexOf(r.id);
    return index === actuel ? null : index;
  };
  const idElement = (id: string) => (id.startsWith('s:') ? id.slice(2) : id);
  /**
   * Dépôt d'une carte (ou d'une section) glissée : cible sous le pointeur, ou place dans sa rangée. Dans sa propre rangée,
   * le côté d'une carte sœur change l'ordre (les liens ne bougent pas) au lieu de la placer en parallèle.
   */
  const depotCarte = (etape: boolean, p: { x: number; y: number }) => {
    const c = etape ? cibleSous(p.x, p.y) : null;
    // Côté d'une carte de sa propre rangée : ordre (ou rien, si c'est sa place actuelle), jamais « en parallèle ».
    if (c?.type === 'carte' && c.parallele && rangee.current?.ids.includes(c.id))
      return { c: null, index: placeDansRangee(p) };
    return { c, index: c ? null : placeDansRangee(p) };
  };

  /** Nœud du graphe qu'on peut lâcher sur le bac : une carte (hors section dépliée) ou une section. */
  const retirable = (n: NoeudRf) =>
    n.type === 'etape'
      ? !!actions.retirerEtape && parId.has(n.id) && !n.parentId
      : (n.type === 'section' || n.type === 'cadre') && !!actions.retirerSection && !!n.data.sectionId;
  /** Carte ou section lâchée sur le bac : elle revient d'abord à sa place, puis quitte le graphe avec le rangement reçu. */
  const retirer = (n: NoeudRf) => {
    setGlisse(null);
    setCible(null);
    setReordre(null);
    rangee.current = null;
    setNoeuds(noeudsDepuis(modeleAffiche, range?.rangement ?? null, parId, edition, new Map()));
    if (!retirable(n)) return;
    if (n.type === 'etape') actions.retirerEtape?.(n.id);
    else if (n.data.sectionId) actions.retirerSection?.(n.data.sectionId);
  };

  const finGlisser = useCallback(
    (id: string, c: CibleDepot | null, index: number | null) => {
      setGlisse(null);
      setCible(null);
      setReordre(null);
      const r = rangee.current;
      rangee.current = null;
      if (index !== null && r && actions.ordonner) {
        const avant = r.ids;
        const apres = avant.filter((x) => x !== id);
        apres.splice(index, 0, id);
        derniereAncre.current = id;
        actions.ordonner(avant.map(idElement), apres.map(idElement));
        return;
      }
      const d = c ? deplacementPour(id, c) : null;
      if (d && actions.deplacer) {
        derniereAncre.current = c?.type === 'carte' ? c.id : c?.type === 'lien' ? c.lien.cible : (c?.noeud ?? null);
        actions.deplacer(id, d);
        return;
      }
      if (c) onMessage?.('Déplacement sans effet.');
      // Rien de changé : la carte revient à sa place.
      setNoeuds(noeudsDepuis(modeleAffiche, range?.rangement ?? null, parId, edition, new Map()));
    },
    [actions, onMessage, modeleAffiche, range, parId, edition],
  );

  // Un seul calcul de cible par image, quel que soit le nombre d'événements du pointeur (glisser fluide).
  const image = useRef<{ id: number; x: number; y: number; suite: (x: number, y: number) => void } | null>(null);
  const parImage = useCallback((x: number, y: number, suite: (x: number, y: number) => void) => {
    const en = image.current;
    if (en) {
      en.x = x;
      en.y = y;
      en.suite = suite;
      return;
    }
    const nouvelle = {
      id: window.requestAnimationFrame(() => {
        image.current = null;
        nouvelle.suite(nouvelle.x, nouvelle.y);
      }),
      x,
      y,
      suite,
    };
    image.current = nouvelle;
  }, []);
  const annulerImage = useCallback(() => {
    if (image.current) window.cancelAnimationFrame(image.current.id);
    image.current = null;
  }, []);
  useEffect(() => annulerImage, [annulerImage]);

  // Copie flottante d'une étape modèle : glisser au pointeur, même cible qu'une carte. La copie suit le pointeur sans
  // rendu React (transformation posée directement) ; seul un changement de cible rend le graphe.
  const [clone, setClone] = useState(false);
  // Copie glissée au pointeur : étape modèle pas encore posée, ou étape du bac.
  const [copie, setCopie] = useState<Etape | null>(null);
  // Bac posé à droite du graphe (`data-bac-plan`) : sa place est relevée au début d'un glisser ; une carte ou une section
  // lâchée dessus quitte le graphe. Pointeur au-dessus : le graphe ne défile pas tout seul (`autoPanOnNodeDrag`).
  const rectBac = useRef<DOMRect | null>(null);
  const releverBac = () => {
    rectBac.current = cadre.current?.querySelector('[data-bac-plan]')?.getBoundingClientRect() ?? null;
  };
  const dansBac = (x: number, y: number) => {
    const r = rectBac.current;
    return !!r && x >= r.left && x <= r.right && y >= r.top && y <= r.bottom;
  };
  const [depotBac, setDepotBac] = useState<BacGraphe['depot']>('aucun');
  const cloneRef = useRef<HTMLDivElement>(null);
  const positionClone = useRef({ x: 0, y: 0 });
  const placerClone = useCallback(() => {
    const el = cloneRef.current;
    const { x, y } = positionClone.current;
    if (el) el.style.transform = `translate(${x - 40}px, ${y - 20}px) scale(${rf.getViewport().zoom})`;
  }, [rf]);
  useLayoutEffect(() => {
    if (clone) placerClone();
  }, [clone, placerClone]);
  /**
   * Glisser d'une copie au pointeur (étape modèle pas encore posée, étape du bac) : mêmes cibles qu'une carte. Lâchée sur une
   * cible : `deposer(place)` ; ailleurs dans le graphe : `deposer({})` (à part) ; hors du graphe ou sur le bac : rien.
   * `seuil` : le glisser ne part qu'après ce déplacement (un clic sur l'étape du bac reste un clic).
   */
  const glisserCopie = (
    depart: { x: number; y: number },
    etape: Etape,
    deposer: (place: PlaceEtape) => void,
    seuil = 0,
  ) => {
    let actif = false;
    const commencer = (x: number, y: number) => {
      actif = true;
      releverGeometrie(null);
      releverBac();
      setGlisse(etape.id);
      setCopie(etape);
      positionClone.current = { x, y };
      setClone(true);
    };
    const fin = (e: PointerEvent, lacher: boolean) => {
      window.removeEventListener('pointermove', bouger);
      window.removeEventListener('pointerup', relacher);
      window.removeEventListener('pointercancel', annuler);
      if (!actif) return;
      annulerImage();
      setClone(false);
      setCopie(null);
      setGlisse(null);
      const surLeBac = dansBac(e.clientX, e.clientY);
      const c = lacher && !surLeBac ? cibleSous(e.clientX, e.clientY) : null;
      geometrie.current = null;
      rectBac.current = null;
      setCible(null);
      if (!lacher || surLeBac) return;
      const pile = document.elementsFromPoint(e.clientX, e.clientY);
      // Lâchée sur le tiroir d'une étape (vue compacte) : pas dans le graphe, même s'il est dessous.
      const dansGraphe =
        !pile[0]?.closest('[data-tiroir-etape]') && pile.some((el) => el.closest('[data-graphe-libre]'));
      const place = c ? placePour(c) : null;
      if (place) deposer(place);
      else if (dansGraphe) deposer({});
    };
    const bouger = (e: PointerEvent) => {
      if (!actif) {
        if (Math.hypot(e.clientX - depart.x, e.clientY - depart.y) < seuil) return;
        commencer(e.clientX, e.clientY);
      }
      positionClone.current = { x: e.clientX, y: e.clientY };
      placerClone();
      parImage(e.clientX, e.clientY, (x, y) => majCible(dansBac(x, y) ? null : cibleSous(x, y)));
    };
    const relacher = (e: PointerEvent) => fin(e, true);
    const annuler = (e: PointerEvent) => fin(e, false);
    if (seuil <= 0) commencer(depart.x, depart.y);
    window.addEventListener('pointermove', bouger);
    window.addEventListener('pointerup', relacher);
    window.addEventListener('pointercancel', annuler);
  };
  const appuyerFlottante = (ev: PointerEventReact<HTMLDivElement>) => {
    if (!flottante) return;
    glisserCopie({ x: ev.clientX, y: ev.clientY }, flottante.etape, (place) => actions.ajouter?.(place));
  };
  const glisserEtapeDuBac = (ev: PointerEventReact<HTMLElement>, e: Etape) => {
    const placer = actions.placerEtape;
    if (!placer || ev.button !== 0) return;
    glisserCopie({ x: ev.clientX, y: ev.clientY }, e, (place) => placer(e.id, place), 4);
  };

  // Aperçu du dépôt : la carte à venir (en pointillé) et les liens qui seront créés.
  const fantome = useMemo(() => {
    const g = geometrie.current;
    if (!cible || !glisse || !g) return null;
    return fantomePour(cible, g.boites, g.geo.cercles, modeleAffiche.liens, range?.rangement.routes ?? new Map(), code);
    // La géométrie est relevée au début du geste ; l'aperçu suit la cible.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [cible, glisse]);

  // ---------- Tracer un lien.
  const onConnect = useCallback(
    (c: Connection) => {
      const a = interpreterConnexion(c, etapes, sections);
      if (!a) return;
      if (a.type === 'refus') onMessage?.(a.raison);
      else actions.lier?.(a.de, a.vers);
    },
    [etapes, sections, actions, onMessage],
  );
  const connexionValide = useCallback(
    (c: Connection | LienRf) => {
      const a = interpreterConnexion(
        { source: c.source, sourceHandle: c.sourceHandle, target: c.target, targetHandle: c.targetHandle },
        etapes,
        sections,
      );
      // Deux bouts à l'intérieur de sections dépliées : ce lien se trace dans la vue de la section.
      if (interieur(c.source) && interieur(c.target) && parentDe.get(c.source) === parentDe.get(c.target)) return false;
      return !!a && a.type !== 'refus';
    },
    [etapes, sections, interieur, parentDe],
  );

  // ---------- Navigation : zoom, ajuster, aller à une étape par son code.
  const [zoom, setZoom] = useState(1);
  const visees = useRef<string | null>(null);
  const aller = (texte: string): boolean => {
    const q = texte.trim().toLowerCase();
    if (!q) return false;
    const e = etapes.find((x) => x.code.toLowerCase() === q) ?? etapes.find((x) => x.code.toLowerCase().includes(q));
    if (!e) {
      onMessage?.(`Aucune étape « ${texte.trim()} » dans ce plan.`);
      return false;
    }
    if (e.aPlacer) {
      onMessage?.(`${e.code} est dans le bac « Étapes à placer » : glissez-la dans le graphe pour la placer.`);
      return false;
    }
    const sid = infoSection(e).sectionId;
    if (!sectionVue && sid && parSection.get(sid)?.placee === false) {
      onMessage?.(`${e.code} est dans la section « ${parSection.get(sid)?.titre} », encore dans le bac.`);
      return false;
    }
    if (!sectionVue && sid && !deplies.has(sid)) onBasculer(sid);
    visees.current = e.id;
    derniereAncre.current = e.id;
    onSelection(e.id);
    return true;
  };
  useEffect(() => {
    const id = visees.current;
    if (!id) return;
    if (!centrerSur(id, { zoom: Math.max(zoom, 0.8), duration: 300 })) return;
    visees.current = null;
    // La recherche suit les nœuds affichés ; `centrerSur` lit la vue au moment du centrage.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [noeuds, rf, zoom]);
  // Vue compacte : l'étape ouverte en tiroir (ou une autre choisie pendant qu'il est ouvert) est recentrée dans la partie
  // visible du graphe, qui ne se rétrécit pas.
  useEffect(() => {
    if (!tiroirOuvert || !selection || !pret) return;
    centrerSur(selection, { zoom: Math.max(rf.getViewport().zoom, 0.8), duration: 250 });
    // Une fois par ouverture ou changement d'étape.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tiroirOuvert, selection]);

  function clavier(ev: KeyboardEvent<HTMLDivElement>) {
    if (ev.target instanceof HTMLElement && /^(INPUT|TEXTAREA|SELECT)$/.test(ev.target.tagName)) return;
    // Lien atteint au clavier (Tab) : Entrée ouvre son menu, comme un clic.
    const g = ev.target instanceof Element ? ev.target.closest('.react-flow__edge') : null;
    const idFocus = g?.getAttribute('data-id');
    if ((ev.key === 'Enter' || ev.key === ' ') && g && idFocus && liensParId.has(idFocus)) {
      ev.preventDefault();
      const r = g.getBoundingClientRect();
      setLienSelectionne(idFocus);
      if (edition)
        setMenu({ type: 'lien', point: { x: r.left + r.width / 2, y: r.top + r.height / 2 }, ids: [idFocus] });
      return;
    }
    if ((ev.key === 'Delete' || ev.key === 'Backspace') && lienSelectionne && edition) {
      const l = liensParId.get(lienSelectionne);
      if (!l) return;
      ev.preventDefault();
      if (lienModifiable(l)) {
        actions.supprimerLien?.(l);
        setLienSelectionne(null);
      } else
        onMessage?.(
          lienInterne(l)
            ? 'Ce lien est à l’intérieur d’une section : supprimez-le dans la vue de la section.'
            : 'Ce lien représente une section repliée : dépliez-la pour agir sur un lien précis.',
        );
    } else if (ev.key === 'Escape' && lienSelectionne) {
      ev.preventDefault();
      setLienSelectionne(null);
    }
  }

  const valeur = useMemo<ValeurGraphe>(
    () => ({
      mep,
      mode,
      edition,
      descriptions,
      etapes: parId,
      sections: parSection,
      etapesParSection,
      selection,
      cible,
      glisse,
      placement,
      cercles: rangeVisible?.rangement.cercles ?? new Map(),
      sectionVue,
      enAvant,
      enMouvement: anime,
      sectionsEnCours,
      sectionsRepli,
      reordre: reordre ? glisse : null,
      estEstompee,
      onActiver: (id) => {
        setLienSelectionne(null);
        derniereAncre.current = id;
        onSelection(id);
      },
      onAjouter: actions.ajouter
        ? (place, ancre, el) =>
            demanderCreationRef.current(
              place,
              ancre,
              el,
              'apres' in place
                ? `Nouvelle étape après ${code(ancre ?? '')}`
                : `Nouvelle étape en parallèle de ${code(ancre ?? '')}`,
            )
        : undefined,
      onBasculer: (id) => {
        derniereAncre.current = idSection(id);
        if (!deplies.has(id)) aMontrer.current = { id: idSection(id), jusqua: performance.now() + 8000 };
        onBasculer(id);
      },
      onEditerSection: actions.editerSection,
      onRetirerSection: actions.retirerSection,
      onOuvrirCercle: actions.ouvrirCercle,
      ensemble: props.ensemble ?? null,
    }),
    [
      mep,
      mode,
      edition,
      descriptions,
      parId,
      parSection,
      etapesParSection,
      selection,
      cible,
      glisse,
      placement,
      rangeVisible,
      sectionVue,
      enAvant,
      anime,
      sectionsEnCours,
      sectionsRepli,
      reordre,
      estEstompee,
      onSelection,
      actions,
      props.ensemble,
      onBasculer,
      deplies,
      code,
    ],
  );

  const carteGlissee = copie && glisse === copie.id ? copie : null;
  const noeudsAffiches = useMemo(
    () =>
      (glisse ? noeuds.map((n) => (n.id === glisse ? { ...n, zIndex: Z.glisse } : n)) : noeuds).concat(noeudsMesure),
    [noeuds, glisse, noeudsMesure],
  );

  // Indicateur de réorganisation : barre verticale entre les deux voisins visés.
  const r = rangee.current;
  let barre: { x: number; haut: number; bas: number } | null = null;
  if (reordre && r) {
    const autres = r.boites.filter((_, i) => r.ids[i] !== r.id);
    const gauche = autres[reordre.index - 1];
    const droite = autres[reordre.index];
    const x =
      gauche && droite
        ? (gauche.x + gauche.largeur + droite.x) / 2
        : droite
          ? droite.x - GRILLE
          : gauche
            ? gauche.x + gauche.largeur + GRILLE
            : 0;
    barre = { x, haut: r.haut - GRILLE / 2, bas: r.bas + GRILLE / 2 };
  }
  // Ce que fera le dépôt, annoncé pendant le glisser (deux gestes : changer les liens, ou l'ordre dans la rangée).
  const geste0 =
    glisse && !carteGlissee
      ? reordre
        ? 'Lâcher : changer l’ordre dans la rangée (les liens ne changent pas)'
        : cible?.type === 'lien'
          ? `Lâcher : insérer sur le lien ${flecheLien(cible.lien)}`
          : cible?.type === 'carte'
            ? cible.parallele
              ? `Lâcher : placer en parallèle de ${code(cible.id)} (mêmes préalables, mêmes suivants)`
              : `Lâcher : placer après ${code(cible.id)} (ses liens changent)`
            : cible?.type === 'cercle'
              ? 'Lâcher : placer après ce cercle'
              : parId.has(glisse)
                ? 'Sur un lien : insérer · sur une carte : placer après · à gauche ou à droite dans sa rangée : changer l’ordre'
                : 'À gauche ou à droite dans sa rangée : changer l’ordre'
      : null;
  const bac = useMemo<BacGraphe>(
    () => ({ depot: depotBac, glisserEtape: actions.placerEtape ? glisserEtapeDuBac : undefined }),
    // Le glisser lit l'état du graphe au moment du geste.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [depotBac, actions.placerEtape],
  );
  const copieDuBac = !!carteGlissee && carteGlissee.id !== flottante?.etape.id;
  const geste =
    depotBac === 'vise'
      ? glisse && parId.has(glisse)
        ? 'Lâcher : retirer l’étape du plan (ses suivants attendront ses préalables), elle attendra dans le bac'
        : 'Lâcher : remettre la section dans le bac (ses cercles perdent leurs liens)'
      : copieDuBac
        ? cible?.type === 'lien'
          ? `Lâcher : placer sur le lien ${flecheLien(cible.lien)}`
          : cible?.type === 'carte'
            ? cible.parallele
              ? `Lâcher : placer en parallèle de ${code(cible.id)}`
              : `Lâcher : placer après ${code(cible.id)}`
            : cible?.type === 'cercle'
              ? 'Lâcher : placer après ce cercle'
              : 'Sur un lien, une carte ou un cercle : placer là · ailleurs dans le graphe : à part'
        : geste0;

  return (
    <ContexteGraphe.Provider value={valeur}>
      <div
        ref={cadre}
        className={cn(
          'graphe-libre flex min-w-0 flex-col overflow-hidden bg-surface shadow-sm',
          plein ? 'fixed inset-0 z-graphe rounded-none' : 'relative rounded-xl',
          plein && retraitDroit && 'min-[1100px]:right-[608px]',
          plan === 'RetourArriere' ? 'border-2 border-rollback-solid' : !plein && 'border border-line',
        )}
        style={plein ? undefined : { height: hauteur ?? 'clamp(520px, calc(100vh - 280px), 940px)' }}
        role={plein ? 'region' : undefined}
        aria-label={plein ? 'Plan en plein écran' : undefined}
      >
        <BarreOutilsGraphe
          plan={plan}
          etapes={etapes}
          enTete={enTete}
          outils={outils}
          boutonEtape={
            edition && actions.ajouter
              ? {
                  placement,
                  menu: !!creation,
                  onClick: (el) =>
                    placement
                      ? actions.ajouter?.({})
                      : demanderCreation(
                          {},
                          null,
                          el,
                          sectionVue ? 'Nouvelle étape dans la section' : 'Nouvelle étape à part',
                        ),
                }
              : undefined
          }
          plein={plein}
          onPlein={setPlein}
          onAller={aller}
          compact={compact}
          resume={compact ? resume : undefined}
          menu={menuPlus}
        />

        {bandeau && <div className="shrink-0 border-b border-line">{bandeau}</div>}

        <div className="relative flex min-h-0 flex-1">
          {/* Raccourcis délégués (Entrée, Suppr, Échap) : les touches viennent des liens focusables du graphe. Survol des
              liens calculé sur leurs tracés (un tronçon partagé désigne tous les liens qui y passent). */}
          {/* eslint-disable-next-line jsx-a11y/no-static-element-interactions */}
          <div
            ref={zone}
            data-graphe-libre
            data-survol-lien={survol.length > 0 || undefined}
            data-anime={anime || undefined}
            onKeyDown={clavier}
            onPointerMove={survoler}
            onPointerLeave={() => setSurvol([])}
            className="relative min-w-0 flex-1"
          >
            <MarqueursLiens />
            {!montre && <SqueletteGraphe />}
            <ReactFlow<NoeudRf, LienRf>
              // Vue quittée (plan ↔ section) : cachée aussitôt, jamais montrée sous l'en-tête de la vue suivante ; la
              // nouvelle paraît en fondu une fois rangée.
              className={cn(montre ? 'transition-opacity duration-200 ease-out' : 'opacity-0')}
              aria-busy={!montre || undefined}
              nodes={noeudsAffiches}
              edges={liens}
              zIndexMode="manual"
              nodeTypes={TYPES_NOEUDS}
              edgeTypes={TYPES_LIENS}
              onNodesChange={onNodesChange}
              onPaneClick={cliquerFond}
              // Sans gestionnaire de clic, React Flow rend les nœuds non glissables « transparents » au pointeur
              // (exécution, lecture seule) : la carte gère elle-même son clic. Le fond d'un cadre déplié vise les liens.
              onNodeClick={(ev, n) => {
                if (n.type === 'cadre') cliquerFond(ev);
              }}
              // Double-clic sur une section (repliée, dépliée ou son titre), ou sur une étape d'une section : sa vue d'édition.
              onNodeDoubleClick={(_, n) => {
                if (!actions.editerSection) return;
                const d = n.data as DonneesNoeud;
                if (d.sectionId && (n.type === 'section' || n.type === 'cadre' || n.type === 'titre'))
                  actions.editerSection(d.sectionId);
                else if (!sectionVue && d.etapeId) {
                  const sid = parId.get(d.etapeId)?.sectionId;
                  if (sid) actions.editerSection(sid, d.etapeId);
                }
              }}
              onNodeDragStart={(_, n) => {
                if (!GLISSABLE.has(n.type ?? '')) return;
                setLienSelectionne(null);
                setSurvol([]);
                setMenu(null);
                rangee.current = actions.ordonner ? releverRangee(n.id) : null;
                if (n.type === 'etape') releverGeometrie(n.id);
                // Le bac devient une zone de dépôt : une carte ou une section lâchée dessus quitte le graphe.
                rectBac.current = null;
                if (retirable(n)) {
                  releverBac();
                  if (rectBac.current) setDepotBac('possible');
                }
                setGlisse(n.id);
              }}
              onNodeDrag={(ev, n) => {
                if (!GLISSABLE.has(n.type ?? '')) return;
                const p = pointDe(ev);
                const etape = n.type === 'etape';
                parImage(p.x, p.y, (x, y) => {
                  if (dansBac(x, y)) {
                    setDepotBac('vise');
                    majCible(null);
                    setReordre(null);
                    return;
                  }
                  if (rectBac.current) setDepotBac('possible');
                  const { c, index } = depotCarte(etape, { x, y });
                  majCible(c);
                  setReordre((a) => (index === null ? null : a?.index === index ? a : { index }));
                });
              }}
              onNodeDragStop={(ev, n) => {
                if (!GLISSABLE.has(n.type ?? '')) return;
                const p = pointDe(ev);
                annulerImage();
                const surLeBac = dansBac(p.x, p.y);
                rectBac.current = null;
                setDepotBac('aucun');
                if (surLeBac) {
                  geometrie.current = null;
                  retirer(n);
                  return;
                }
                const { c, index } = depotCarte(n.type === 'etape', p);
                geometrie.current = null;
                finGlisser(n.id, c, index);
              }}
              onConnectStart={() => setTrace(true)}
              onConnectEnd={() => setTrace(false)}
              onConnect={onConnect}
              isValidConnection={connexionValide}
              onMove={(_, v) => setZoom(v.zoom)}
              // Pointeur au-dessus du bac : le graphe ne défile pas tout seul, le bac reçoit le dépôt.
              autoPanOnNodeDrag={depotBac !== 'vise'}
              nodesDraggable={edition}
              nodesConnectable={edition}
              nodesFocusable={false}
              edgesFocusable
              elementsSelectable={false}
              deleteKeyCode={null}
              selectionKeyCode={null}
              multiSelectionKeyCode={null}
              zoomOnScroll={false}
              panOnScroll
              zoomActivationKeyCode={['Control', 'Meta']}
              zoomOnDoubleClick={false}
              minZoom={ZOOM_MIN}
              maxZoom={ZOOM_MAX}
              snapToGrid={false}
              nodeDragThreshold={4}
              connectionRadius={28}
              proOptions={{ hideAttribution: true }}
              aria-label={sectionVue ? 'Graphe de la section' : 'Graphe des étapes'}
            >
              {fantome && <ApercuDepot fantome={fantome} />}
              {liensMouvement && (
                <ViewportPortal>
                  <LiensEnMouvement liens={liensMouvement} zIndex={Z.lien} />
                </ViewportPortal>
              )}
              {barre && (
                <ViewportPortal>
                  <div
                    aria-hidden
                    className="pointer-events-none absolute z-30 w-[3px] -translate-x-1/2 rounded-full bg-accent shadow-[0_0_0_3px_var(--accent-soft)]"
                    style={{ left: barre.x, top: barre.haut, height: barre.bas - barre.haut }}
                  />
                </ViewportPortal>
              )}
              {miniCarte && <MiniCarte />}
            </ReactFlow>

            <AvisRangement cause={indisponible} onReessayer={reessayer} />
            {(geste || enAttente) && (
              <span
                role="status"
                className={cn(
                  'pointer-events-none absolute bottom-3 left-1/2 z-20 max-w-[min(56%,560px)] -translate-x-1/2 rounded-2xl border px-2.5 py-1 text-center text-xs font-medium shadow-sm',
                  geste ? 'border-accent bg-accent-soft text-accent-text' : 'border-warn-bd bg-warn-bg text-warn-fg',
                )}
              >
                {geste ?? 'Modifications reçues : rangées dès la fin de votre action'}
              </span>
            )}

            <div
              role="toolbar"
              aria-label="Vue du graphe"
              className="absolute right-3 bottom-3 z-20 flex items-center gap-0.5 rounded-lg border border-line bg-surface/95 p-0.5 shadow-md backdrop-blur"
            >
              <BoutonIcone
                label="Dézoomer"
                taille="sm"
                disabled={zoom <= ZOOM_MIN + 0.001}
                onClick={() => void rf.zoomTo(Math.max(ZOOM_MIN, zoom / PAS_ZOOM), { duration: 150 })}
              >
                <ZoomOut className="size-4" aria-hidden />
              </BoutonIcone>
              <button
                type="button"
                onClick={() => void rf.zoomTo(1, { duration: 150 })}
                title="Revenir à 100 %"
                aria-label={`Zoom ${Math.round(zoom * 100)} % : revenir à 100 %`}
                className="h-8 min-w-12 rounded-md px-1.5 text-xs font-semibold text-fg-muted tnum hover:bg-surface-2 hover:text-fg"
              >
                {Math.round(zoom * 100)} %
              </button>
              <BoutonIcone
                label="Zoomer"
                taille="sm"
                disabled={zoom >= ZOOM_MAX - 0.001}
                onClick={() => void rf.zoomTo(Math.min(ZOOM_MAX, zoom * PAS_ZOOM), { duration: 150 })}
              >
                <ZoomIn className="size-4" aria-hidden />
              </BoutonIcone>
              <BoutonIcone label="Ajuster à l’écran" taille="sm" onClick={ajuster}>
                <Maximize className="size-4" aria-hidden />
              </BoutonIcone>
              <BoutonIcone
                label={miniCarte ? 'Masquer la mini-carte' : 'Afficher la mini-carte'}
                taille="sm"
                aria-pressed={miniCarte}
                onClick={basculerMiniCarte}
              >
                <IconeCarte className="size-4" aria-hidden />
              </BoutonIcone>
            </div>
            <p className="sr-only">
              Glissez le fond ou utilisez la molette pour vous déplacer ; Ctrl + molette pour zoomer. Tab pour aller de
              carte en carte, Entrée pour ouvrir.
            </p>
            {placement && flottante && (
              <EtapeFlottante
                mep={mep}
                etape={flottante.etape}
                descriptions={descriptions}
                glissee={glisse === flottante.etape.id}
                onAppuyer={appuyerFlottante}
                onAnnuler={flottante.onAnnuler}
              />
            )}
            {etapes.length === 0 && !sectionVue && (
              <p className="pointer-events-none absolute inset-x-0 top-1/2 text-center text-sm text-fg-subtle">
                {edition ? 'Aucune étape : « + Étape » pour commencer.' : 'Aucune étape dans ce plan.'}
              </p>
            )}
          </div>
          <ContexteBac.Provider value={bac}>{lateral}</ContexteBac.Provider>
          {/* Vue compacte : emplacement du tiroir de l'étape ouverte, par-dessus le graphe et le bac, sous le bandeau. */}
          {compact && tiroir && (
            <div
              ref={tiroir.emplacement}
              data-emplacement-tiroir
              className="pointer-events-none absolute inset-0 z-30 overflow-hidden"
            />
          )}
        </div>
      </div>
      {menu && (
        <MenuAuPoint
          point={menu.point}
          label={menu.type === 'lien' ? 'Actions du lien' : 'Créer une étape'}
          onFermer={() => {
            setMenu(null);
            setCandidat(null);
          }}
        >
          {contenuMenu(menu)}
        </MenuAuPoint>
      )}
      {carteGlissee &&
        clone &&
        createPortal(
          <div
            ref={cloneRef}
            aria-hidden
            data-copie-glissee
            className={cn(
              'pointer-events-none fixed top-0 left-0 z-panneau rounded-lg shadow-lg transition-opacity duration-100 will-change-transform',
              // Une cible visée : la copie s'efface pour laisser voir l'aperçu de sa place.
              cible ? 'opacity-35' : 'opacity-90',
            )}
            style={{ transformOrigin: '40px 20px' }}
          >
            <CarteEtape mep={mep} etape={carteGlissee} mode={mode} descriptions={descriptions} />
          </div>,
          document.body,
        )}
    </ContexteGraphe.Provider>
  );
}

/** Aperçu du dépôt, dans le repère du graphe : la carte à venir en pointillé et les liens qui seront créés. */
function ApercuDepot({ fantome }: { fantome: Fantome }) {
  const { boite: b, liens } = fantome;
  const xs = [b.x, b.x + b.largeur, ...liens.flatMap((l) => [l.de.x, l.vers.x])];
  const ys = [b.y, b.y + b.hauteur, ...liens.flatMap((l) => [l.de.y, l.vers.y])];
  const x0 = Math.min(...xs) - 8;
  const y0 = Math.min(...ys) - 8;
  return (
    <ViewportPortal>
      <svg
        aria-hidden
        data-apercu-depot
        className="pointer-events-none absolute z-30 overflow-visible"
        style={{ left: x0, top: y0, width: Math.max(...xs) - x0 + 8, height: Math.max(...ys) - y0 + 8 }}
      >
        {liens.map((l) => {
          const de = { x: l.de.x - x0, y: l.de.y - y0 };
          const vers = { x: l.vers.x - x0, y: l.vers.y - y0 };
          // Coude juste au-dessus de la carte visée (lien vers le fantôme) ou juste sous le fantôme (lien qui en part).
          const milieu = l.vers.y === b.y ? Math.max(de.y + 12, vers.y - 22) : Math.min(de.y + 22, vers.y - 12);
          return (
            <path
              key={`${l.de.x},${l.de.y}>${l.vers.x},${l.vers.y}`}
              d={`M${de.x},${de.y} V${milieu} H${vers.x} V${vers.y}`}
              fill="none"
              strokeDasharray="6 4"
              strokeWidth={2.5}
              style={{ stroke: 'var(--accent)' }}
              markerEnd="url(#fleche-accent)"
            />
          );
        })}
      </svg>
      <div
        aria-hidden
        className="pointer-events-none absolute z-30 grid place-content-center rounded-lg border-2 border-dashed border-accent bg-accent-soft/70 text-xs font-semibold text-accent-text"
        style={{ left: b.x, top: b.y, width: b.largeur, height: b.hauteur }}
      >
        {fantome.libelle}
      </div>
    </ViewportPortal>
  );
}
