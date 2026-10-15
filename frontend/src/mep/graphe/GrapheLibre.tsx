import '@xyflow/react/dist/base.css';
import {
  MiniMap,
  ReactFlow,
  ReactFlowProvider,
  ViewportPortal,
  useReactFlow,
  type Edge,
  type Node,
  type NodeChange,
  type Connection,
  applyNodeChanges,
} from '@xyflow/react';
import { Map as IconeCarte, Maximize, Maximize2, Minimize2, Plus, Search, ZoomIn, ZoomOut } from 'lucide-react';
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
  type RefObject,
} from 'react';
import { PresentsMep } from '../../presence';
import type { Etape, Mep, ModeleEtape, TypePlan } from '../../types';
import { BoutonIcone, Button } from '../../ui/Button';
import { cn } from '../../ui/cn';
import type { Deplacement, PlaceEtape } from './apiGraphe';
import { CarteEtape, type ModeGraphe } from './CarteEtape';
import { deplacementPour, interpreterConnexion, type CibleDepot } from './connexions';
import { ContexteGraphe, type ValeurGraphe } from './contexteGraphe';
import { EtapeFlottante } from './EtapesModeles';
import { MarqueursLiens, TYPES_LIENS, type DonneesLien } from './LienGraphe';
import { MenuLabel } from '../../ui/Menu';
import { ContenuMenuLien, EntreesCreation, MenuAuPoint, type ChoixCreation } from './MenusGraphe';
import { construireModele, idSection, type LienModele, type ModeleGraphe } from './modeleGraphe';
import { TYPES_NOEUDS, type DonneesNoeud } from './NoeudsGraphe';
import {
  CARTE,
  decalageAncrage,
  GRILLE,
  noeudAuCentre,
  positionsAbsolues,
  type Rangement,
  type Taille,
} from './rangement';
import { liensAuPoint, sautsDesLiens } from './routage';
import { infoSection, type Section } from './sections';
import { useRangement } from './useRangement';

export const ZOOM_MIN = 0.25;
export const ZOOM_MAX = 1.6;
const PAS_ZOOM = 1.2;
const DUREE_ANIMATION = 260;
/** Hauteur minimale du graphe quand la fenêtre est très basse. */
const HAUTEUR_MIN = 360;
const MINI_CARTE = { largeur: 168, hauteur: 112 };

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
  editerSection?: (sectionId: string) => void;
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
  /** Panneau latéral intérieur, à droite du graphe (bac des sections), avec son propre défilement. */
  lateral?: ReactNode;
  /** Espace d'une étape ouvert en panneau à droite : le plein écran lui laisse sa place. */
  retraitDroit?: boolean;
  /** Bandeau d'informations dans le cadre, sous la barre d'outils (résumé, messages, en-tête de la section éditée). */
  bandeau?: ReactNode;
  /** Plein écran tenu par l'écran (il reste actif quand on entre dans une section ou qu'on en sort). */
  plein?: boolean;
  onPlein?: (plein: boolean) => void;
  /** Création depuis une étape modèle, proposée avec « Étape vierge » dans chaque menu de création. */
  creation?: {
    /** Les plus utilisées pour les systèmes de la MEP. */
    modeles: ModeleEtape[];
    depuisModele: (place: PlaceEtape, m: ModeleEtape) => void;
    /** Ouvre la bibliothèque ; l'étape choisie sera posée à cette place. */
    parcourir: (place: PlaceEtape) => void;
  };
}

type NoeudRf = Node<DonneesNoeud>;

/** Menu ouvert sur le graphe : celui d'un lien (choisir parmi des liens fusionnés, insérer, supprimer) ou de création. */
type MenuOuvert =
  | { type: 'lien'; point: { x: number; y: number }; ids: string[] }
  | { type: 'creation'; point: { x: number; y: number }; place: PlaceEtape; ancre: string | null; titre: string };
type LienRf = Edge<DonneesLien>;

/** Point du pointeur (souris ou doigt) d'un événement de glisser. */
function pointDe(ev: MouseEvent | TouchEvent): { x: number; y: number } {
  if ('touches' in ev) {
    const t = ev.touches[0] ?? ev.changedTouches[0];
    return { x: t?.clientX ?? 0, y: t?.clientY ?? 0 };
  }
  return { x: ev.clientX, y: ev.clientY };
}

/** Position de repli tant que le rangement n'est pas calculé : grille par ligne et ordre. */
function placeProvisoire(e: Etape | undefined, i: number) {
  return e
    ? { x: e.ordre * (CARTE.largeur + GRILLE * 2), y: e.ligne * (CARTE.hauteur + GRILLE * 3) }
    : { x: i * GRILLE, y: i * GRILLE };
}

/**
 * Superposition (React Flow en mode manuel) : cadres des sections dépliées, puis liens (ceux mis en avant au-dessus des autres,
 * tronçons partagés compris), puis cartes, titres et cercles (un lien ne passe jamais sur une carte), et la carte glissée
 * au-dessus de tout.
 */
const Z = { cadre: 0, lien: 1, lienEnAvant: 2, noeud: 3, glisse: 10 };

/** Nœuds qu'on peut glisser : cartes (liens ou ordre) et sections (ordre seulement). */
const GLISSABLE = new Set(['etape', 'section', 'cadre']);

function noeudsDepuis(
  modele: ModeleGraphe,
  rangement: Rangement | null,
  etapes: ReadonlyMap<string, Etape>,
  edition: boolean,
  precedents: ReadonlyMap<string, NoeudRf>,
): NoeudRf[] {
  return modele.noeuds.map((n, i) => {
    const place = rangement?.places.get(n.id);
    const avant = precedents.get(n.id);
    const position = place ? { x: place.x, y: place.y } : (avant?.position ?? placeProvisoire(etapes.get(n.id), i));
    const style =
      n.nature === 'cadre'
        ? { width: place?.largeur ?? 480, height: place?.hauteur ?? 240 }
        : n.nature === 'section'
          ? { width: place?.largeur ?? 288 }
          : undefined;
    return {
      id: n.id,
      type: n.nature,
      position,
      data: { etapeId: n.etapeId, sectionId: n.sectionId, cercleId: n.cercleId },
      ...(n.parent ? { parentId: n.parent } : {}),
      // Dans le plan, l'intérieur d'une section dépliée est en lecture seule : ses cartes ne se glissent pas.
      draggable: edition && GLISSABLE.has(n.nature) && !n.parent,
      selectable: false,
      connectable: edition,
      focusable: false,
      zIndex: n.nature === 'cadre' ? Z.cadre : Z.noeud,
      ...(style ? { style } : {}),
      // Mesure reprise seulement pour le même genre de nœud (une section repliée n'a pas la taille de son cadre déplié).
      ...(avant?.measured && avant.type === n.nature ? { measured: avant.measured } : {}),
    } satisfies NoeudRf;
  });
}

/**
 * Hauteur du graphe pour que la page tienne dans la fenêtre sans défiler : de son bord haut au bas de la fenêtre, moins ce
 * qui reste dessous (marges du cadre de page). Recalculée quand la fenêtre ou la page change de taille.
 */
function useHauteurDisponible(ref: RefObject<HTMLElement | null>, actif: boolean): number | null {
  const [hauteur, setHauteur] = useState<number | null>(null);
  useLayoutEffect(() => {
    if (!actif) return undefined;
    let image = 0;
    const calculer = () => {
      const el = ref.current;
      if (!el) return;
      let dessous = 0;
      for (let p: HTMLElement | null = el.parentElement; p && p !== document.body; p = p.parentElement) {
        const s = getComputedStyle(p);
        dessous += parseFloat(s.paddingBottom) + parseFloat(s.borderBottomWidth);
      }
      const haut = el.getBoundingClientRect().top + window.scrollY;
      const h = Math.max(HAUTEUR_MIN, Math.floor(window.innerHeight - haut - dessous - 1));
      setHauteur(h);
      // Correction : ce qui dépasse encore (contenu sous le graphe) est retiré au tour suivant.
      window.cancelAnimationFrame(image);
      image = window.requestAnimationFrame(() => {
        const exces = document.documentElement.scrollHeight - window.innerHeight;
        if (exces > 0 && exces < 400) setHauteur((x) => (x === null ? x : Math.max(HAUTEUR_MIN, x - exces)));
      });
    };
    calculer();
    window.addEventListener('resize', calculer);
    const ro = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(calculer);
    const el = ref.current;
    if (ro && el?.parentElement) ro.observe(el.parentElement);
    return () => {
      window.removeEventListener('resize', calculer);
      window.cancelAnimationFrame(image);
      ro?.disconnect();
    };
  }, [ref, actif]);
  return hauteur;
}

function lireMiniCarte(): boolean {
  try {
    return window.localStorage.getItem('graphe.miniCarte') !== 'non';
  } catch {
    return true;
  }
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

  // ---------- Cadre : hauteur de la fenêtre, plein écran.
  const cadre = useRef<HTMLDivElement>(null);
  const [pleinLocal, setPleinLocal] = useState(false);
  const plein = props.plein ?? pleinLocal;
  const onPlein = props.onPlein;
  const setPlein = useCallback((v: boolean) => (onPlein ? onPlein(v) : setPleinLocal(v)), [onPlein]);
  const hauteur = useHauteurDisponible(cadre, !plein);
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
  const [miniCarte, setMiniCarte] = useState(lireMiniCarte);
  const basculerMiniCarte = () =>
    setMiniCarte((v) => {
      try {
        window.localStorage.setItem('graphe.miniCarte', v ? 'non' : 'oui');
      } catch {
        /* préférence non mémorisée */
      }
      return !v;
    });

  // ---------- Gestes en cours : gel du rangement (protection 1).
  const [glisse, setGlisse] = useState<string | null>(null);
  const [trace, setTrace] = useState(false);
  const [cible, setCible] = useState<CibleDepot | null>(null);
  const [tailles, setTailles] = useState<ReadonlyMap<string, Taille>>(() => new Map());
  const taillesRef = useRef(tailles);
  taillesRef.current = tailles;
  const gele = gel || glisse !== null || trace;
  const { range, enAttente } = useRangement(modele, tailles, gele, jeton);
  const rangeRef = useRef(range);
  rangeRef.current = range;

  // ---------- Nœuds affichés : ceux du dernier rangement appliqué, animés vers leur place.
  const [noeuds, setNoeuds] = useState<NoeudRf[]>([]);
  const noeudsRef = useRef<NoeudRf[]>([]);
  noeudsRef.current = noeuds;
  const modeleAffiche = range?.modele ?? modele;
  const animation = useRef<number | undefined>(undefined);
  const [anime, setAnime] = useState(false);
  const premier = useRef(true);
  // Dernier nœud touché (« + », dépôt) : ancre de la vue quand il n'y a pas de sélection.
  const derniereAncre = useRef<string | null>(null);
  // Section qu'on vient de déplier : la vue la montre en entier une fois rangée (et après les rangements qui suivent de
  // près, quand ses cartes sont mesurées).
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
    const noeudsRf = rf.getNodes();
    if (!el || noeudsRf.length === 0) return;
    const b = rf.getNodesBounds(noeudsRf);
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
  const ajuster = () => {
    const m = marges();
    void rf.fitView({
      padding: { top: `${m.haut}px`, x: `${GRILLE}px`, bottom: `${m.bas}px` },
      maxZoom: 1,
      duration: 250,
    });
  };

  /** Montre un nœud en entier (section dépliée) : déplacement minimal, dézoom seulement s'il ne tient pas. */
  const montrer = useCallback(
    (id: string) => {
      const el = zone.current;
      const r = rangeRef.current;
      // Place et taille d'après le rangement (la mesure du cadre agrandi peut arriver un peu plus tard).
      const place = r?.rangement.places.get(id);
      const p = r
        ? positionsAbsolues(r.rangement, new Map(r.modele.noeuds.map((n) => [n.id, n.parent]))).get(id)
        : undefined;
      if (!el || !place || !p) return;
      const w = place.largeur;
      const h = place.hauteur;
      const vp = rf.getViewport();
      const marge = GRILLE;
      const largeurVue = el.clientWidth;
      const hauteurVue = el.clientHeight;
      if (w * vp.zoom > largeurVue - 2 * marge || h * vp.zoom > hauteurVue - 2 * marge) {
        void rf.fitBounds({ x: p.x, y: p.y, width: w, height: h }, { padding: 0.08, duration: 250 });
        return;
      }
      const gauche = p.x * vp.zoom + vp.x;
      const haut = p.y * vp.zoom + vp.y;
      let dx = 0;
      let dy = 0;
      if (gauche < marge) dx = marge - gauche;
      else if (gauche + w * vp.zoom > largeurVue - marge) dx = largeurVue - marge - (gauche + w * vp.zoom);
      if (haut < marge) dy = marge - haut;
      else if (haut + h * vp.zoom > hauteurVue - marge) dy = hauteurVue - marge - (haut + h * vp.zoom);
      if (dx || dy) void rf.setViewport({ x: vp.x + dx, y: vp.y + dy, zoom: vp.zoom }, { duration: 200 });
    },
    [rf],
  );

  useEffect(() => {
    const precedents = new Map(noeudsRef.current.map((n) => [n.id, n]));
    const cibles = noeudsDepuis(modeleAffiche, range?.rangement ?? null, parId, edition, precedents);
    window.cancelAnimationFrame(animation.current ?? 0);
    // Premier rangement fait avec les tailles mesurées : posé sans animation, puis la vue d'arrivée.
    const mesure =
      !!range &&
      range.modele.noeuds.every(
        (n) => (n.nature !== 'etape' && n.nature !== 'section') || taillesRef.current.has(n.id),
      );
    if (!range || premier.current || precedents.size === 0) {
      setNoeuds(cibles);
      setAnime(false);
      if (range && premier.current && mesure) {
        premier.current = false;
        requestAnimationFrame(() => {
          const sel = selection && rf.getNode(selection);
          if (sel) {
            const abs = rf.getInternalNode(sel.id)?.internals.positionAbsolute ?? sel.position;
            void rf.setCenter(abs.x + CARTE.largeur / 2, abs.y + CARTE.hauteur / 2, { zoom: 1 });
          } else vueInitiale();
        });
      }
      return;
    }
    // Ancrage (protection 2) : le nœud regardé reste à sa place à l'écran ; le reste glisse vers sa nouvelle place.
    const parents = new Map(modeleAffiche.noeuds.map((n) => [n.id, n.parent]));
    const absAvant = positionsAbsolues(
      {
        places: new Map(
          [...precedents.values()].map((n) => [n.id, { x: n.position.x, y: n.position.y, largeur: 0, hauteur: 0 }]),
        ),
        cercles: new Map(),
        routes: new Map(),
      },
      new Map([...precedents.values()].map((n) => [n.id, n.parentId ?? null])),
    );
    const absApres = positionsAbsolues(range.rangement, parents);
    const ancre = ancreCourante(absAvant);
    const vp = rf.getViewport();
    const { dx, dy } = decalageAncrage(absAvant, absApres, ancre, vp.zoom);
    const depart = new Map([...precedents.values()].map((n) => [n.id, n.position]));
    const debut = performance.now();
    const ease = (t: number) => (t < 0.5 ? 4 * t * t * t : 1 - (-2 * t + 2) ** 3 / 2);
    setAnime(true);
    const pas = (maintenant: number) => {
      const t = Math.min(1, (maintenant - debut) / DUREE_ANIMATION);
      const k = ease(t);
      setNoeuds(
        cibles.map((n) => {
          const a = depart.get(n.id);
          // Nœud apparu : directement à sa place.
          if (!a || t >= 1) return n;
          return { ...n, position: { x: a.x + (n.position.x - a.x) * k, y: a.y + (n.position.y - a.y) * k } };
        }),
      );
      void rf.setViewport({ x: vp.x + dx * k, y: vp.y + dy * k, zoom: vp.zoom });
      if (t < 1) animation.current = window.requestAnimationFrame(pas);
      else {
        setAnime(false);
        const m = aMontrer.current;
        if (m && performance.now() > m.jusqua) aMontrer.current = null;
        else if (m) window.requestAnimationFrame(() => montrer(m.id));
      }
    };
    animation.current = window.requestAnimationFrame(pas);
    // Rangement seulement : la sélection, la cible et les droits sont lus au moment de l'animation.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [range, edition]);
  useEffect(() => () => window.cancelAnimationFrame(animation.current ?? 0), []);

  const onNodesChange = useCallback((changes: NodeChange<NoeudRf>[]) => {
    setNoeuds((n) => applyNodeChanges(changes, n));
    const dims = changes.filter(
      (c): c is Extract<NodeChange<NoeudRf>, { type: 'dimensions' }> => c.type === 'dimensions' && !!c.dimensions,
    );
    if (dims.length === 0) return;
    setTailles((avant) => {
      let change = false;
      const apres = new Map(avant);
      for (const c of dims) {
        const n = noeudsRef.current.find((x) => x.id === c.id);
        if (!n || (n.type !== 'etape' && n.type !== 'section') || !c.dimensions) continue;
        const t = { largeur: c.dimensions.width, hauteur: c.dimensions.height };
        const a = avant.get(c.id);
        if (a && Math.abs(a.largeur - t.largeur) < 4 && Math.abs(a.hauteur - t.hauteur) < 4) continue;
        apres.set(c.id, t);
        change = true;
      }
      return change ? apres : avant;
    });
  }, []);

  // ---------- Liens affichés : tracé d'ELK, sauts aux croisements, mise en avant.
  const [lienSelectionne, setLienSelectionne] = useState<string | null>(null);
  // Liens sous le pointeur : plusieurs quand le pointeur est sur un tronçon partagé (liens fusionnés).
  const [survol, setSurvol] = useState<readonly string[]>([]);
  const [menu, setMenu] = useState<MenuOuvert | null>(null);
  // Lien sous le pointeur ou le focus dans le menu de choix (liens fusionnés) : mis en avant seul.
  const [candidat, setCandidat] = useState<string | null>(null);
  const liensParId = useMemo(() => new Map(modeleAffiche.liens.map((l) => [l.id, l])), [modeleAffiche]);
  const sauts = useMemo(() => sautsDesLiens(range?.rangement.routes ?? new Map()), [range]);
  // Dans le plan, l'intérieur d'une section dépliée (ses étapes, ses cercles) se lit seulement : on le modifie dans la vue
  // de la section. Les cercles restent reliables depuis le dehors.
  const parentDe = useMemo(() => new Map(modeleAffiche.noeuds.map((n) => [n.id, n.parent ?? null])), [modeleAffiche]);
  const interieur = useCallback((id: string) => !!parentDe.get(id), [parentDe]);
  const lienInterne = useCallback((l: LienModele) => interieur(l.source) && interieur(l.cible), [interieur]);
  const lienModifiable = useCallback(
    (l: LienModele) => edition && !!l.logique && !lienInterne(l),
    [edition, lienInterne],
  );
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
  const liens = useMemo<LienRf[]>(
    () =>
      modeleAffiche.liens.map((l) => ({
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
        zIndex: enAvant?.liens.has(l.id) ? Z.lienEnAvant : Z.lien,
        data: {
          lien: l,
          route: range?.rangement.routes.get(l.id),
          sauts: sauts.get(l.id),
          fait: fait(l),
          onPoser: placement && lienModifiable(l) ? poser : undefined,
        },
      })),
    [modeleAffiche, range, sauts, lienSelectionne, enAvant, fait, placement, lienModifiable, poser, libelleLien],
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

  // ---------- Glisser une carte : cible sous le pointeur (lien, carte, ou place dans sa rangée), indicateur, dépôt.
  const cibleSous = useCallback(
    (x: number, y: number, exclu: string | null): CibleDepot | null => {
      for (const el of document.elementsFromPoint(x, y)) {
        const n = el.closest('.react-flow__node');
        if (!n) continue;
        if (n.classList.contains('react-flow__node-cadre')) break;
        const id = n.getAttribute('data-id');
        // L'intérieur d'une section dépliée ne se modifie que dans sa vue.
        if (id && id !== exclu && parId.has(id) && !interieur(id)) return { type: 'carte', id };
        if (id !== exclu) return null;
      }
      const routes = rangeRef.current?.rangement.routes;
      const p = rf.screenToFlowPosition({ x, y });
      const candidats = liensSous(x, y)
        .map((id) => liensParId.get(id))
        .filter((l): l is LienModele => !!l && lienModifiable(l) && l.source !== exclu && l.cible !== exclu);
      if (candidats.length === 0) return null;
      // Sur un tronçon partagé, le lien dont la flèche est la plus proche du pointeur (la pastille dit lequel).
      const fin = (l: LienModele) => {
        const pt = routes?.get(l.id)?.at(-1);
        return pt ? Math.hypot(pt.x - p.x, pt.y - p.y) : Infinity;
      };
      const lien = [...candidats].sort((a, b) => fin(a) - fin(b))[0];
      return { type: 'lien', lien };
    },
    [parId, interieur, rf, liensSous, liensParId, lienModifiable],
  );
  const cleCible = (c: CibleDepot | null) => (c === null ? '' : c.type === 'carte' ? `c|${c.id}` : `l|${c.lien.id}`);
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
        derniereAncre.current = c?.type === 'carte' ? c.id : (c?.lien.cible ?? null);
        actions.deplacer(id, d);
        return;
      }
      if (c) onMessage?.('Déplacement sans effet.');
      // Rien de changé : la carte revient à sa place.
      setNoeuds(noeudsDepuis(modeleAffiche, range?.rangement ?? null, parId, edition, new Map()));
    },
    [actions, onMessage, modeleAffiche, range, parId, edition],
  );

  // Copie flottante d'une étape modèle : glisser au pointeur, même cible qu'une carte.
  const [clone, setClone] = useState<{ x: number; y: number } | null>(null);
  const appuyerFlottante = (ev: PointerEventReact<HTMLDivElement>) => {
    if (!flottante) return;
    const fin = (e: PointerEvent, deposer: boolean) => {
      window.removeEventListener('pointermove', bouger);
      window.removeEventListener('pointerup', lacher);
      window.removeEventListener('pointercancel', annuler);
      setClone(null);
      setGlisse(null);
      const c = deposer ? cibleSous(e.clientX, e.clientY, null) : null;
      setCible(null);
      if (!deposer) return;
      const dansGraphe = document
        .elementsFromPoint(e.clientX, e.clientY)
        .some((el) => el.closest('[data-graphe-libre]'));
      if (c?.type === 'carte') actions.ajouter?.({ apres: c.id });
      else if (c?.type === 'lien' && c.lien.logique) actions.ajouter?.({ entre: c.lien.logique });
      else if (dansGraphe) actions.ajouter?.({});
    };
    const bouger = (e: PointerEvent) => {
      setClone({ x: e.clientX, y: e.clientY });
      majCible(cibleSous(e.clientX, e.clientY, null));
    };
    const lacher = (e: PointerEvent) => fin(e, true);
    const annuler = (e: PointerEvent) => fin(e, false);
    setGlisse(flottante.etape.id);
    setClone({ x: ev.clientX, y: ev.clientY });
    window.addEventListener('pointermove', bouger);
    window.addEventListener('pointerup', lacher);
    window.addEventListener('pointercancel', annuler);
  };

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
  const [recherche, setRecherche] = useState('');
  const visees = useRef<string | null>(null);
  const aller = (texte: string) => {
    const q = texte.trim().toLowerCase();
    if (!q) return;
    const e = etapes.find((x) => x.code.toLowerCase() === q) ?? etapes.find((x) => x.code.toLowerCase().includes(q));
    if (!e) {
      onMessage?.(`Aucune étape « ${texte.trim()} » dans ce plan.`);
      return;
    }
    const sid = infoSection(e).sectionId;
    if (!sectionVue && sid && parSection.get(sid)?.placee === false) {
      onMessage?.(`${e.code} est dans la section « ${parSection.get(sid)?.titre} », encore dans le bac.`);
      return;
    }
    if (!sectionVue && sid && !deplies.has(sid)) onBasculer(sid);
    visees.current = e.id;
    derniereAncre.current = e.id;
    onSelection(e.id);
    setRecherche('');
  };
  useEffect(() => {
    const id = visees.current;
    if (!id) return;
    const n = rf.getInternalNode(id);
    if (!n) return;
    visees.current = null;
    const p = n.internals.positionAbsolute;
    void rf.setCenter(p.x + CARTE.largeur / 2, p.y + CARTE.hauteur / 2, { zoom: Math.max(zoom, 0.8), duration: 300 });
  }, [noeuds, rf, zoom]);

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
      cercles: range?.rangement.cercles ?? new Map(),
      sectionVue,
      enAvant,
      enMouvement: anime,
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
        if (!deplies.has(id)) aMontrer.current = { id: idSection(id), jusqua: performance.now() + 2000 };
        onBasculer(id);
      },
      onEditerSection: actions.editerSection,
      onOuvrirCercle: actions.ouvrirCercle,
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
      range,
      sectionVue,
      enAvant,
      anime,
      reordre,
      estEstompee,
      onSelection,
      actions,
      onBasculer,
      deplies,
      code,
    ],
  );

  const carteGlissee = flottante && glisse === flottante.etape.id ? flottante.etape : null;
  const noeudsAffiches = useMemo(
    () => (glisse ? noeuds.map((n) => (n.id === glisse ? { ...n, zIndex: Z.glisse } : n)) : noeuds),
    [noeuds, glisse],
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
  const geste =
    glisse && !carteGlissee
      ? reordre
        ? 'Lâcher : changer l’ordre dans la rangée (les liens ne changent pas)'
        : cible?.type === 'lien'
          ? `Lâcher : insérer sur le ${libelleLien(cible.lien).toLowerCase()}`
          : cible?.type === 'carte'
            ? `Lâcher : placer après ${code(cible.id)} (ses liens changent)`
            : parId.has(glisse)
              ? 'Sur un lien : insérer · sur une carte : placer après · à gauche ou à droite dans sa rangée : changer l’ordre'
              : 'À gauche ou à droite dans sa rangée : changer l’ordre'
      : null;

  return (
    <ContexteGraphe.Provider value={valeur}>
      <div
        ref={cadre}
        className={cn(
          'graphe-libre flex min-w-0 flex-col overflow-hidden bg-surface shadow-sm',
          plein ? 'fixed inset-0 z-40 rounded-none' : 'relative rounded-xl',
          plein && retraitDroit && 'min-[1100px]:right-[608px]',
          plan === 'RetourArriere' ? 'border-2 border-rollback-solid' : !plein && 'border border-line',
        )}
        style={plein ? undefined : { height: hauteur ?? 'clamp(520px, calc(100vh - 280px), 940px)' }}
        role={plein ? 'region' : undefined}
        aria-label={plein ? 'Plan en plein écran' : undefined}
      >
        <div
          role="toolbar"
          aria-label="Outils du graphe"
          className="@container flex min-h-12 shrink-0 items-center gap-2 overflow-x-auto border-b border-line bg-surface-2/60 px-2 py-1.5"
        >
          {/* Boutons des étapes modèles : posés dans la barre (ils se placent d'eux-mêmes en absolu ailleurs). */}
          {enTete && (
            <div className="shrink-0 [&>div]:!static [&>div]:!shadow-none [&_button]:whitespace-nowrap">{enTete}</div>
          )}
          <div className="ml-auto flex shrink-0 items-center gap-2">
            {/* Plein écran : qui est sur la MEP reste visible (la page en dessous est masquée). */}
            {plein && <PresentsMep />}
            {outils}
            {edition && actions.ajouter && (
              <Button
                taille="sm"
                icone={<Plus className="size-4" aria-hidden />}
                aria-haspopup={placement || !creation ? undefined : 'menu'}
                onClick={(e) =>
                  placement
                    ? actions.ajouter?.({})
                    : demanderCreation(
                        {},
                        null,
                        e.currentTarget,
                        sectionVue ? 'Nouvelle étape dans la section' : 'Nouvelle étape à part',
                      )
                }
                title={placement ? 'Poser la copie à part, sans lien' : 'Créer une étape à part, sans lien'}
              >
                {placement ? 'Poser à part' : 'Étape'}
              </Button>
            )}
            <form
              role="search"
              className="relative"
              onSubmit={(e) => {
                e.preventDefault();
                aller(recherche);
              }}
            >
              <Search
                className="pointer-events-none absolute top-1/2 left-2 size-3.5 -translate-y-1/2 text-fg-subtle"
                aria-hidden
              />
              <input
                type="search"
                aria-label="Aller à une étape (code)"
                placeholder="Aller à…"
                list={`codes-${plan}`}
                value={recherche}
                onChange={(e) => setRecherche(e.target.value)}
                className="h-8 w-36 rounded-lg border border-line bg-surface pr-2 pl-7 text-[13px] focus:border-accent focus:ring-2 focus:ring-accent/30 focus:outline-none @max-2xl:w-28"
              />
              <datalist id={`codes-${plan}`}>
                {etapes.map((e) => (
                  <option key={e.id} value={e.code}>
                    {e.action}
                  </option>
                ))}
              </datalist>
            </form>
            <BoutonIcone
              label={plein ? 'Quitter le plein écran (Échap)' : 'Plan en plein écran'}
              taille="sm"
              aria-pressed={plein}
              onClick={() => setPlein(!plein)}
            >
              {plein ? <Minimize2 className="size-4" aria-hidden /> : <Maximize2 className="size-4" aria-hidden />}
            </BoutonIcone>
          </div>
        </div>

        {bandeau && <div className="shrink-0 border-b border-line">{bandeau}</div>}

        <div className="flex min-h-0 flex-1">
          {/* Raccourcis délégués (Entrée, Suppr, Échap) : les touches viennent des liens focusables du graphe. Survol des
              liens calculé sur leurs tracés (un tronçon partagé désigne tous les liens qui y passent). */}
          {/* eslint-disable-next-line jsx-a11y/no-static-element-interactions */}
          <div
            ref={zone}
            data-graphe-libre
            data-survol-lien={survol.length > 0 || undefined}
            onKeyDown={clavier}
            onPointerMove={survoler}
            onPointerLeave={() => setSurvol([])}
            className="relative min-w-0 flex-1"
          >
            <MarqueursLiens />
            <ReactFlow<NoeudRf, LienRf>
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
              onNodeDragStart={(_, n) => {
                if (!GLISSABLE.has(n.type ?? '')) return;
                setLienSelectionne(null);
                setSurvol([]);
                setMenu(null);
                rangee.current = actions.ordonner ? releverRangee(n.id) : null;
                setGlisse(n.id);
              }}
              onNodeDrag={(ev, n) => {
                if (!GLISSABLE.has(n.type ?? '')) return;
                const p = pointDe(ev);
                const c = n.type === 'etape' ? cibleSous(p.x, p.y, n.id) : null;
                majCible(c);
                const index = c ? null : placeDansRangee(p);
                setReordre((a) => (index === null ? null : a?.index === index ? a : { index }));
              }}
              onNodeDragStop={(ev, n) => {
                if (!GLISSABLE.has(n.type ?? '')) return;
                const p = pointDe(ev);
                const c = n.type === 'etape' ? cibleSous(p.x, p.y, n.id) : null;
                finGlisser(n.id, c, c ? null : placeDansRangee(p));
              }}
              onConnectStart={() => setTrace(true)}
              onConnectEnd={() => setTrace(false)}
              onConnect={onConnect}
              isValidConnection={connexionValide}
              onMove={(_, v) => setZoom(v.zoom)}
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
              {barre && (
                <ViewportPortal>
                  <div
                    aria-hidden
                    className="pointer-events-none absolute z-30 w-[3px] -translate-x-1/2 rounded-full bg-accent shadow-[0_0_0_3px_var(--accent-soft)]"
                    style={{ left: barre.x, top: barre.haut, height: barre.bas - barre.haut }}
                  />
                </ViewportPortal>
              )}
              {miniCarte && (
                <MiniMap
                  pannable
                  zoomable
                  position="bottom-left"
                  ariaLabel="Mini-carte du graphe"
                  style={{ width: MINI_CARTE.largeur, height: MINI_CARTE.hauteur }}
                  className="!m-3 overflow-hidden rounded-lg border border-line !bg-surface-2 shadow-md"
                  nodeColor={(n) =>
                    n.type === 'cadre'
                      ? 'var(--accent-soft)'
                      : n.type === 'cercle'
                        ? 'var(--accent)'
                        : 'var(--line-strong)'
                  }
                  maskColor="color-mix(in srgb, var(--bg) 60%, transparent)"
                  nodeBorderRadius={6}
                />
              )}
            </ReactFlow>

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
          {lateral}
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
            aria-hidden
            className="pointer-events-none fixed z-50 rounded-lg opacity-90 shadow-lg"
            style={{ left: clone.x - 40, top: clone.y - 20, transform: `scale(${zoom})`, transformOrigin: '40px 20px' }}
          >
            <CarteEtape mep={mep} etape={carteGlissee} mode={mode} descriptions={descriptions} />
          </div>,
          document.body,
        )}
    </ContexteGraphe.Provider>
  );
}
