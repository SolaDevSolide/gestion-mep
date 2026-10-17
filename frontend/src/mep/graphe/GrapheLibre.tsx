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
} from 'react';
import { PresentsMep } from '../../presence';
import type { Etape, Mep, ModeleEtape, TypePlan } from '../../types';
import { BoutonIcone, Button } from '../../ui/Button';
import { cn } from '../../ui/cn';
import { mouvementReduit } from '../../ui/mouvement';
import type { Deplacement, PlaceEtape } from './apiGraphe';
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
import { LiensEnMouvement, MarqueursLiens, TYPES_LIENS, type DonneesLien } from './LienGraphe';
import { MenuLabel } from '../../ui/Menu';
import { ContenuMenuLien, EntreesCreation, MenuAuPoint, type ChoixCreation } from './MenusGraphe';
import { construireModele, idSection, type LienModele, type ModeleGraphe } from './modeleGraphe';
import { TYPES_NOEUDS, type DonneesNoeud } from './NoeudsGraphe';
import {
  CARTE,
  decalageAncrage,
  tailleSectionRepliee,
  GRILLE,
  noeudAuCentre,
  positionsAbsolues,
  type Point,
  type Rangement,
  type Taille,
} from './rangement';
import { liensAuPoint, RAYON_COUDE } from './routage';
import {
  adoucir,
  ancrageHaut,
  cheminArrondi,
  dansCadre,
  decoupe,
  DUREE_MOUVEMENT_MS,
  etatA,
  placeAncree,
  rampeApparition,
  rampeDisparition,
  revelation,
  routeSuivant,
  type Ancrage,
  type EtatNoeud,
  type LienMouvement,
  type PlaceDansCadre,
  type SensCadre,
} from './mouvementGraphe';
import { sautsSansJonctions } from './routageBordure';
import { SqueletteGraphe } from './SqueletteGraphe';
import { infoSection, type Section } from './sections';
import { useHauteurDisponible } from './hauteurDisponible';
import { hoteMesure, mesuresManquantes, PREFIXE_MESURE, tailleRetenue, vueMontrant } from './mesuresGraphe';
import { useRangement } from './useRangement';

export const ZOOM_MIN = 0.25;
export const ZOOM_MAX = 1.6;
const PAS_ZOOM = 1.2;
/** Glissement du contenu d'un cadre qui s'ouvre (il descend à sa place) ou se ferme (il remonte en s'effaçant). */
const GLISSE_CONTENU = 10;
/** Secours seulement (cas anormal) : un nœud monté que React Flow ne mesure jamais n'empêche pas le mouvement. */
const SECOURS_MONTAGE_MS = 3000;
/** Hauteur minimale du graphe quand la fenêtre est très basse. */
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
  /** Panneau latéral intérieur, à droite du graphe (bac des sections), avec son propre défilement. */
  /** Panneau latéral (bac) : il lit le dépôt et le glisser de ses étapes par `useBacGraphe`. */
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

/** Préférence de mini-carte : affichée par défaut, sauf dans la vue d'ensemble (#61), qui a sa propre préférence. */
const cleMiniCarte = (ensemble: boolean) => (ensemble ? 'graphe.miniCarte.ensemble' : 'graphe.miniCarte');

function lireMiniCarte(ensemble: boolean): boolean {
  try {
    const v = window.localStorage.getItem(cleMiniCarte(ensemble));
    return ensemble ? v === 'oui' : v !== 'non';
  } catch {
    return !ensemble;
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
    retraits = null,
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
  const [miniCarte, setMiniCarte] = useState(() => lireMiniCarte(!!props.ensemble));
  const basculerMiniCarte = () =>
    setMiniCarte((v) => {
      try {
        window.localStorage.setItem(cleMiniCarte(!!props.ensemble), v ? 'non' : 'oui');
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
  const { range, enAttente } = useRangement(modele, tailles, gele, jeton, pret);
  const rangeRef = useRef(range);
  rangeRef.current = range;

  // ---------- Nœuds affichés : ceux du dernier rangement appliqué, animés vers leur place.
  const [noeuds, setNoeuds] = useState<NoeudRf[]>([]);
  const noeudsRef = useRef<NoeudRf[]>([]);
  noeudsRef.current = noeuds;
  const modeleAffiche = range?.modele ?? modele;
  const animation = useRef<number | undefined>(undefined);
  // Phase d'un repli ou d'un dépli avant le mouvement (effacement des nœuds qui partent ; secours du montage).
  const phase = useRef<number | undefined>(undefined);
  // Montage invisible des nœuds qui vont paraître : ceux que React Flow n'a pas encore mesurés, et la suite.
  const montage = useRef<{
    ids: Set<string>;
    mesures: Map<string, { width: number; height: number }>;
    suite: (mesures: ReadonlyMap<string, { width: number; height: number }>) => void;
  } | null>(null);
  const terminerMontage = useCallback(() => {
    const m = montage.current;
    if (!m) return;
    montage.current = null;
    window.clearTimeout(phase.current);
    m.suite(m.mesures);
  }, []);
  // Rangement des nœuds affichés (cercles sur le bord des sections repliées) : celui du mouvement en cours, pas un rangement
  // arrivé pendant qu'on mesure encore les nœuds apparus.
  const [rangeVisible, setRangeVisible] = useState(range);
  const [anime, setAnime] = useState(false);
  // Liens dessinés pendant un mouvement (ceux de React Flow sont alors masqués) : attachés aux nœuds qui bougent.
  const [liensMouvement, setLiensMouvement] = useState<LienMouvement[] | null>(null);
  const premier = useRef(true);
  useLayoutEffect(() => {
    premier.current = true;
  }, [contexte]);
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

  useEffect(() => {
    const precedents = new Map(noeudsRef.current.map((n) => [n.id, n]));
    const cibles = noeudsDepuis(modeleAffiche, range?.rangement ?? null, parId, edition, precedents);
    window.cancelAnimationFrame(animation.current ?? 0);
    window.clearTimeout(phase.current);
    montage.current = null;
    // Premier rangement fait avec les tailles mesurées : posé sans animation, puis la vue d'arrivée.
    const mesure =
      !!range &&
      range.modele.noeuds.every(
        (n) => (n.nature !== 'etape' && n.nature !== 'section') || taillesRef.current.has(n.id),
      );
    if (!range || premier.current || precedents.size === 0) {
      setRangeVisible(range);
      setNoeuds(cibles);
      setAnime(false);
      setLiensMouvement(null);
      // Rangement de la vue en cours (pas celui de la vue quittée), tailles mesurées : vue d'arrivée, puis le graphe paraît.
      if (range && premier.current && mesure && range.modele === modele) {
        premier.current = false;
        // Vue d'arrivée posée après le rendu de ces nœuds (React Flow connaît alors leurs places), pas à l'image suivante.
        const arriver = () => setArrivee(true);
        // Tailles connues par les copies de mesure, mais nœuds pas encore mesurés par React Flow : la vue d'arrivée
        // attend leurs mesures (sinon l'emprise du graphe serait fausse).
        const nonMesures = cibles.filter((n) => !n.measured).map((n) => n.id);
        if (nonMesures.length > 0) {
          montage.current = { ids: new Set(nonMesures), mesures: new Map(), suite: arriver };
          phase.current = window.setTimeout(() => terminerMontage(), SECOURS_MONTAGE_MS);
        } else arriver();
      }
      return;
    }
    const reduit = mouvementReduit();
    // Repli ou dépli d'une section, étapes reçues ou parties. Ce rangement est définitif : les nouveaux nœuds ont été
    // mesurés avant, en invisible (`useRangement` attend leurs mesures), et rien n'a bougé d'ici là. Puis un seul
    // mouvement, entièrement calculé avant sa première image (voir mouvementGraphe) : chaque nœud va de sa place affichée
    // à sa nouvelle place, le cadre qui s'ouvre ou se ferme dévoile ou cache son intérieur (découpé par lui), la vue suit,
    // et les liens restent attachés à leurs nœuds du début à la fin.
    const idsCibles = new Set(cibles.map((n) => n.id));
    const suite = (mesures: ReadonlyMap<string, { width: number; height: number }> = new Map()) => {
      // Mesures prises par React Flow pendant le montage invisible : reprises, sinon il cacherait ces nœuds le temps de
      // les mesurer à nouveau.
      for (const n of cibles) {
        const d = mesures.get(n.id);
        if (!n.measured && d) n.measured = d;
      }
      animer();
    };
    // Nœuds qui vont paraître : d'abord montés invisibles (et mesurés par React Flow) sans que rien d'autre ne change à
    // l'écran ; le mouvement part quand React Flow les a tous mesurés (pas après un délai fixe).
    const nouveaux = cibles.filter((n) => !precedents.has(n.id));
    if (nouveaux.length > 0) {
      montage.current = { ids: new Set(nouveaux.map((n) => n.id)), mesures: new Map(), suite };
      setNoeuds([...precedents.values(), ...nouveaux.map((n) => Object.assign({}, n, { className: 'noeud-cache' }))]);
      // Secours (cas anormal) : un nœud que React Flow ne mesure pas n'empêche pas le mouvement.
      phase.current = window.setTimeout(() => terminerMontage(), SECOURS_MONTAGE_MS);
      return;
    }
    suite();

    function animer() {
      if (!range) return;
      const ancien = rangeVisible;
      setRangeVisible(range);
      const parentsAvant = new Map([...precedents.values()].map((n) => [n.id, n.parentId ?? null]));
      const absAvant = positionsAbsolues(
        {
          places: new Map(
            [...precedents.values()].map((n) => [n.id, { x: n.position.x, y: n.position.y, largeur: 0, hauteur: 0 }]),
          ),
          cercles: new Map(),
          routes: new Map(),
        },
        parentsAvant,
      );
      const absApres = positionsAbsolues(range.rangement, new Map(cibles.map((n) => [n.id, n.parentId ?? null])));
      // Ancrage (protection 2) : le nœud regardé reste à sa place à l'écran ; le reste glisse vers sa nouvelle place.
      const ancre = ancreCourante(absAvant);
      const vp = rf.getViewport();
      const { dx, dy } = decalageAncrage(absAvant, absApres, ancre, vp.zoom);
      let vueFin = { x: vp.x + dx, y: vp.y + dy, zoom: vp.zoom };
      // Section qu'on vient de déplier : montrée en entier dans le même mouvement (pas de second déplacement après).
      const m = aMontrer.current;
      if (m && performance.now() > m.jusqua) aMontrer.current = null;
      const placeM = m ? range.rangement.places.get(m.id) : undefined;
      const absM = m ? absApres.get(m.id) : undefined;
      const el = zone.current;
      if (m && placeM && absM && el && cibles.some((n) => n.id === m.id && n.type === 'cadre')) {
        vueFin = vueMontrant(
          vueFin,
          { x: absM.x, y: absM.y, largeur: placeM.largeur, hauteur: placeM.hauteur },
          { largeur: el.clientWidth, hauteur: el.clientHeight },
          GRILLE,
          ZOOM_MIN,
        );
        aMontrer.current = null;
      }

      // États de départ (affichés) et d'arrivée (nouveau rangement) de chaque nœud ; boîtes à leur taille visible.
      const depart = new Map<string, EtatNoeud>();
      const arrivee = new Map<string, EtatNoeud>();
      for (const n of precedents.values()) {
        const a = absAvant.get(n.id);
        if (!a) continue;
        const echelle = Number(n.style ? (Object.entries(n.style).find(([c]) => c === '--echelle')?.[1] ?? 1) : 1);
        depart.set(n.id, {
          boite: {
            x: a.x,
            y: a.y,
            largeur: Number(n.style?.width ?? n.measured?.width ?? 0) * echelle,
            hauteur: Number(n.style?.height ?? n.measured?.height ?? 0) * echelle,
          },
          opacite: n.className?.includes('noeud-cache') ? 0 : Number(n.style?.opacity ?? 1),
          echelle,
        });
      }
      for (const n of cibles) {
        const a = absApres.get(n.id);
        if (!a) continue;
        const p = range.rangement.places.get(n.id);
        arrivee.set(n.id, {
          boite: {
            x: a.x,
            y: a.y,
            largeur: p?.largeur ?? n.measured?.width ?? 0,
            hauteur: p?.hauteur ?? n.measured?.height ?? 0,
          },
          opacite: 1,
          echelle: 1,
        });
      }
      // Ce qui paraît dans un cadre qui s'ouvre part de sa place, à l'échelle du cadre de départ ; ce qui part d'un cadre
      // qui se ferme va à l'échelle du cadre d'arrivée. Le titre accompagne le cadre, à sa taille, visible du début à la
      // fin ; les cercles de la bordure suivent la bordure, en fondu (sur le petit cadre de départ ou d'arrivée, ils
      // couvriraient le titre).
      const placeDans = (n: NoeudRf): PlaceDansCadre =>
        n.type === 'titre' ? 'titre' : n.type === 'cercle' && !sectionVue ? 'bord' : 'contenu';
      // Contenu (étapes) d'un cadre qui s'ouvre ou se ferme : à sa taille réelle, ancré au milieu du bord haut du cadre et
      // découpé par lui, révélé rangée par rangée (voir `revelation`) ; il glisse un peu en paraissant ou en partant.
      const reveles = new Map<
        string,
        { parent: string; ancrage: Ancrage; sens: SensCadre; largeur: number; hauteur: number }
      >();
      for (const n of cibles) {
        const fin = arrivee.get(n.id);
        if (depart.has(n.id) || !fin) continue;
        const pd = n.parentId ? depart.get(n.parentId) : undefined;
        const pa = n.parentId ? arrivee.get(n.parentId) : undefined;
        const place = placeDans(n);
        if (place === 'contenu' && pd && pa && n.parentId) {
          const ancrage = ancrageHaut(fin.boite, pa.boite);
          const { largeur, hauteur: haut } = fin.boite;
          reveles.set(n.id, { parent: n.parentId, ancrage, sens: 'ouvre', largeur, hauteur: haut });
          depart.set(n.id, { boite: placeAncree(ancrage, pd.boite, largeur, haut), opacite: 0, echelle: 1 });
          continue;
        }
        const r = pd && pa ? dansCadre(fin.boite, pa.boite, pd.boite, place) : null;
        depart.set(n.id, {
          boite: r?.boite ?? fin.boite,
          echelle: r?.echelle ?? 1,
          // Le cadre ne paraît jamais vide : son intérieur, en réduction, est déjà visible au départ.
          opacite: !r ? 0 : place === 'titre' ? 1 : place === 'contenu' ? 0.3 : 0,
        });
      }
      const partants = [...precedents.values()].filter((n) => !idsCibles.has(n.id));
      for (const n of partants) {
        const deb = depart.get(n.id);
        if (!deb) continue;
        const pd = n.parentId ? depart.get(n.parentId) : undefined;
        const pa = n.parentId ? arrivee.get(n.parentId) : undefined;
        const place = placeDans(n);
        if (place === 'contenu' && pd && pa && n.parentId) {
          const ancrage = ancrageHaut(deb.boite, pd.boite);
          const { largeur, hauteur: haut } = deb.boite;
          reveles.set(n.id, { parent: n.parentId, ancrage, sens: 'ferme', largeur, hauteur: haut });
          arrivee.set(n.id, {
            boite: placeAncree(ancrage, pa.boite, largeur, haut),
            opacite: 0,
            echelle: deb.echelle,
          });
          continue;
        }
        const r = pd && pa ? dansCadre(deb.boite, pd.boite, pa.boite, place) : null;
        arrivee.set(n.id, {
          boite: r?.boite ?? deb.boite,
          echelle: deb.echelle * (r?.echelle ?? 1),
          // Symétrique du dépli : l'intérieur reste visible, en réduction, jusqu'à ce que la section reprenne sa place.
          opacite: !r ? 0 : place === 'titre' ? deb.opacite : place === 'contenu' ? Math.min(deb.opacite, 0.3) : 0,
        });
      }
      // Pendant le mouvement, une section qui se replie reste un cadre (qui se réduit) ; elle redevient une section à
      // l'arrivée, à la même taille. Une section qui se déplie devient tout de suite un cadre, à sa taille de section.
      const enMouvement = [
        ...cibles.map((n) => {
          const a = precedents.get(n.id);
          return a && a.type === 'cadre' && n.type === 'section' ? a : n;
        }),
        ...partants,
      ];
      const idsNouveauxLiens = new Set(range.modele.liens.map((l) => l.id));
      const idsAnciensLiens = new Set(ancien?.modele.liens.map((l) => l.id) ?? []);
      const duree = reduit ? 1 : DUREE_MOUVEMENT_MS;
      const debut = performance.now();
      setAnime(true);
      const pas = () => {
        // Heure lue à chaque image (pas l'horodatage passé par requestAnimationFrame, qui peut précéder `debut`).
        const t = Math.min(1, Math.max(0, (performance.now() - debut) / duree));
        const k = adoucir(t);
        void rf.setViewport({
          x: vp.x + (vueFin.x - vp.x) * k,
          y: vp.y + (vueFin.y - vp.y) * k,
          zoom: vp.zoom + (vueFin.zoom - vp.zoom) * k,
        });
        if (t >= 1) {
          setNoeuds(cibles);
          setLiensMouvement(null);
          setAnime(false);
          return;
        }
        const etats = new Map<string, EtatNoeud>();
        for (const [id, a] of depart) {
          const b = arrivee.get(id);
          if (b) etats.set(id, etatA(a, b, k));
        }
        const decoupes = new Map<string, string | null>();
        for (const [id, r] of reveles) {
          const p = etats.get(r.parent);
          if (!p) continue;
          const opacite = revelation(r.ancrage.ry, k, r.sens);
          const b = placeAncree(r.ancrage, p.boite, r.largeur, r.hauteur);
          b.y -= (1 - opacite) * GLISSE_CONTENU;
          etats.set(id, { boite: b, opacite, echelle: 1 });
          decoupes.set(id, decoupe(b, p.boite));
        }
        setNoeuds(
          enMouvement.map((n) => {
            const e = etats.get(n.id);
            if (!e) return n;
            const parent = n.parentId ? etats.get(n.parentId) : undefined;
            const style: Record<string, unknown> = { ...n.style, opacity: e.opacite };
            if (e.echelle !== 1) style['--echelle'] = e.echelle;
            else delete style['--echelle'];
            const coupe = decoupes.get(n.id);
            if (coupe) style.clipPath = coupe;
            else delete style.clipPath;
            if (n.type === 'cadre' || n.type === 'section') {
              style.width = e.boite.largeur / e.echelle;
              style.height = e.boite.hauteur / e.echelle;
            }
            // Nœud qui change de genre (section repliée ↔ cadre déplié) : pas encore mesuré, React Flow le cacherait une
            // image ; sa taille animée lui sert de mesure.
            const w = style.width;
            const h = style.height;
            const mesureAnimee =
              !n.measured && typeof w === 'number' && typeof h === 'number'
                ? { measured: { width: w, height: h } }
                : {};
            return Object.assign({}, n, mesureAnimee, {
              position: parent
                ? { x: e.boite.x - parent.boite.x, y: e.boite.y - parent.boite.y }
                : { x: e.boite.x, y: e.boite.y },
              style,
              className: e.echelle !== 1 ? 'noeud-echelle' : undefined,
            });
          }),
        );
        // Liens attachés à leurs nœuds : ceux qui restent, tels quels ; ceux qui changent de bout (section repliée ↔
        // cercles de son cadre) passent en fondu de l'ancien tracé au nouveau.
        const traces: LienMouvement[] = [];
        const tracer = (l: LienModele, route: Point[] | undefined, ref: Map<string, EtatNoeud>, fondu: number) => {
          const rs = ref.get(l.source)?.boite;
          const rc = ref.get(l.cible)?.boite;
          const s = etats.get(l.source);
          const c = etats.get(l.cible);
          if (!route || !rs || !rc || !s || !c) return;
          const opacite = Math.min(s.opacite, c.opacite) * fondu;
          if (opacite <= 0.01) return;
          const r = reveles.get(l.source) ?? reveles.get(l.cible);
          const dans = r ? etats.get(r.parent)?.boite : undefined;
          traces.push({
            id: l.id,
            d: cheminArrondi(routeSuivant(route, rs, s.boite, rc, c.boite), RAYON_COUDE),
            opacite,
            ton: fait(l) ? 'ok' : 'neutre',
            ...(dans ? { dans } : {}),
          });
        };
        for (const l of range.modele.liens)
          tracer(l, range.rangement.routes.get(l.id), arrivee, idsAnciensLiens.has(l.id) ? 1 : rampeApparition(k));
        if (ancien)
          for (const l of ancien.modele.liens)
            if (!idsNouveauxLiens.has(l.id))
              tracer(l, ancien.rangement.routes.get(l.id), depart, 1 - rampeDisparition(k));
        setLiensMouvement(traces);
        animation.current = window.requestAnimationFrame(pas);
      };
      // Première image tout de suite, avec le rangement affiché : jamais une image où ils ne vont pas ensemble.
      pas();
    }
    // Rangement seulement : la sélection, la cible et les droits sont lus au moment de l'animation.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [range, edition]);
  useEffect(
    () => () => {
      window.cancelAnimationFrame(animation.current ?? 0);
      window.clearTimeout(phase.current);
    },
    [],
  );

  // Vue d'arrivée : après le rendu qui a donné à React Flow les nœuds rangés et mesurés.
  const [arrivee, setArrivee] = useState(false);
  useEffect(() => {
    if (!arrivee) return;
    setArrivee(false);
    const sel = selection && rf.getInternalNode(selection);
    if (sel) {
      const abs = sel.internals.positionAbsolute;
      void rf.setCenter(abs.x + CARTE.largeur / 2, abs.y + CARTE.hauteur / 2, { zoom: 1 });
    } else vueInitiale();
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
      const m = montage.current;
      if (m) {
        for (const c of dims) {
          m.ids.delete(c.id);
          if (c.dimensions) m.mesures.set(c.id, c.dimensions);
        }
        if (m.ids.size === 0) terminerMontage();
      }
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
    [terminerMontage],
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
      const dansGraphe = document
        .elementsFromPoint(e.clientX, e.clientY)
        .some((el) => el.closest('[data-graphe-libre]'));
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
    if (e.aPlacer) {
      onMessage?.(`${e.code} est dans le bac « Étapes à placer » : glissez-la dans le graphe pour la placer.`);
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
            data-anime={anime || undefined}
            onKeyDown={clavier}
            onPointerMove={survoler}
            onPointerLeave={() => setSurvol([])}
            className="relative min-w-0 flex-1"
          >
            <MarqueursLiens />
            {!pret && <SqueletteGraphe />}
            <ReactFlow<NoeudRf, LienRf>
              // Vue quittée (plan ↔ section) : cachée aussitôt, jamais montrée sous l'en-tête de la vue suivante ; la
              // nouvelle paraît en fondu une fois rangée.
              className={cn(pret ? 'transition-opacity duration-200 ease-out' : 'opacity-0')}
              aria-busy={!pret || undefined}
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
              {miniCarte && (
                <MiniMap
                  pannable
                  zoomable
                  position="bottom-left"
                  ariaLabel="Mini-carte du graphe"
                  style={{ width: MINI_CARTE.largeur, height: MINI_CARTE.hauteur }}
                  className="!m-3 overflow-hidden rounded-lg border border-line !bg-surface-2 shadow-md"
                  nodeColor={(n) =>
                    n.id.startsWith(PREFIXE_MESURE)
                      ? 'transparent'
                      : n.type === 'cadre'
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
          <ContexteBac.Provider value={bac}>{lateral}</ContexteBac.Provider>
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
              'pointer-events-none fixed top-0 left-0 z-50 rounded-lg shadow-lg transition-opacity duration-100 will-change-transform',
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
        className="pointer-events-none absolute z-30 grid place-content-center rounded-lg border-2 border-dashed border-accent bg-accent-soft/70 text-[12px] font-semibold text-accent-text"
        style={{ left: b.x, top: b.y, width: b.largeur, height: b.hauteur }}
      >
        {fantome.libelle}
      </div>
    </ViewportPortal>
  );
}
