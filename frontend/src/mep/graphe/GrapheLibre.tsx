import '@xyflow/react/dist/base.css';
import {
  MiniMap,
  ReactFlow,
  ReactFlowProvider,
  useReactFlow,
  type Edge,
  type Node,
  type NodeChange,
  type OnConnectStartParams,
  type Connection,
  type EdgeChange,
  applyNodeChanges,
} from '@xyflow/react';
import { Maximize, Search, ZoomIn, ZoomOut } from 'lucide-react';
import { createPortal } from 'react-dom';
import {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  type KeyboardEvent,
  type PointerEvent as PointerEventReact,
  type ReactNode,
} from 'react';
import type { Etape, Mep, TypePlan } from '../../types';
import { BoutonIcone } from '../../ui/Button';
import { cn } from '../../ui/cn';
import type { Deplacement, PlaceEtape } from './apiGraphe';
import { CarteEtape, type ModeGraphe } from './CarteEtape';
import { deplacementPour, interpreterConnexion, type CibleDepot } from './connexions';
import { ContexteGraphe, type ValeurGraphe } from './contexteGraphe';
import { EtapeFlottante } from './EtapesModeles';
import { MarqueursLiens, TYPES_LIENS, type DonneesLien } from './LienGraphe';
import { construireModele, idSection, type LienModele, type ModeleGraphe } from './modeleGraphe';
import { TYPES_NOEUDS, type DonneesNoeud } from './NoeudsGraphe';
import {
  CARTE,
  decalageAncrage,
  ENTETE_CADRE,
  GRILLE,
  noeudAuCentre,
  positionsAbsolues,
  type Rangement,
  type Taille,
} from './rangement';
import { infoSection, type Section } from './sections';
import { useRangement } from './useRangement';

export const ZOOM_MIN = 0.25;
export const ZOOM_MAX = 1.6;
const PAS_ZOOM = 1.2;
const DUREE_ANIMATION = 260;

/** Actions d'édition du graphe ; absentes en lecture ou en exécution. */
export interface ActionsGraphe {
  /** Création rapide (ou pose de la copie d'une étape modèle) : après une carte, sur un lien, en parallèle, isolée. */
  ajouter?: (place: PlaceEtape) => void;
  /** Carte lâchée sur un lien (insertion) ou sur une carte (après elle) ; le serveur en déduit la section. */
  deplacer?: (id: string, d: Deplacement) => void;
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
  /** Contrôles posés en haut à gauche du graphe (positionnés par eux-mêmes). */
  enTete?: ReactNode;
  /** Contrôles ajoutés en haut à droite (Tout replier…). */
  outils?: ReactNode;
}

type NoeudRf = Node<DonneesNoeud>;

/** Point du pointeur (souris ou doigt) d'un événement de glisser. */
function pointDe(ev: MouseEvent | TouchEvent): { x: number; y: number } {
  if ('touches' in ev) {
    const t = ev.touches[0] ?? ev.changedTouches[0];
    return { x: t?.clientX ?? 0, y: t?.clientY ?? 0 };
  }
  return { x: ev.clientX, y: ev.clientY };
}
type LienRf = Edge<DonneesLien>;

/** Position de repli tant que le rangement n'est pas calculé : grille par ligne et ordre. */
function placeProvisoire(e: Etape | undefined, i: number) {
  return e
    ? { x: e.ordre * (CARTE.largeur + GRILLE * 2), y: e.ligne * (CARTE.hauteur + GRILLE * 3) }
    : { x: i * GRILLE, y: i * GRILLE };
}

function noeudsDepuis(
  modele: ModeleGraphe,
  rangement: Rangement | null,
  etapes: ReadonlyMap<string, Etape>,
  edition: boolean,
  precedents: ReadonlyMap<string, NoeudRf>,
): NoeudRf[] {
  const noeuds = modele.noeuds.map((n, i) => {
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
      draggable: edition && n.nature === 'etape',
      selectable: false,
      connectable: edition,
      focusable: false,
      zIndex: n.nature === 'cadre' ? Z.cadre : Z.noeud,
      ...(style ? { style } : {}),
      ...(avant?.measured ? { measured: avant.measured } : {}),
    } satisfies NoeudRf;
  });
  // Titre de chaque section dépliée, au-dessus des liens : un lien qui entre par un cercle passe sous lui.
  const titres = noeuds
    .filter((n) => n.type === 'cadre')
    .map(
      (n) =>
        ({
          id: `${PREFIXE_TITRE}${n.id}`,
          type: 'titreCadre',
          parentId: n.id,
          position: { x: BORD_CADRE, y: BORD_CADRE },
          data: { sectionId: n.data.sectionId },
          draggable: false,
          selectable: false,
          connectable: false,
          focusable: false,
          zIndex: Z.titre,
          style: { width: Math.max(0, (n.style?.width ?? 480) - 2 * BORD_CADRE), height: ENTETE_CADRE },
        }) satisfies NoeudRf,
    );
  return [...noeuds, ...titres];
}

/**
 * Superposition (React Flow en mode manuel) : cadres des sections dépliées, puis liens, puis cartes et cercles (un lien
 * ne barre jamais une carte), puis titres des cadres, et la carte glissée au-dessus de tout.
 */
const Z = { cadre: 0, lien: 1, noeud: 2, titre: 5, glisse: 10 };

/** Hauteur des barres d'outils posées sur le haut du graphe : la vue d'arrivée et « Ajuster » laissent cette place. */
const BARRE_OUTILS = 56;

/** Titres des sections dépliées : nœuds d'affichage, hors rangement. */
const PREFIXE_TITRE = 'titre:';
const BORD_CADRE = 2;

/** Graphe libre (React Flow + rangement ELK) : cartes, sections, cercles ; création rapide, glisser par liens. */
export function GrapheLibre(props: Props) {
  return (
    <ReactFlowProvider>
      <Graphe {...props} />
    </ReactFlowProvider>
  );
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

  // ---------- Gestes en cours : gel du rangement (protection 1).
  const [glisse, setGlisse] = useState<string | null>(null);
  const [trace, setTrace] = useState(false);
  const [cible, setCible] = useState<CibleDepot | null>(null);
  const [tailles, setTailles] = useState<ReadonlyMap<string, Taille>>(() => new Map());
  const taillesRef = useRef(tailles);
  taillesRef.current = tailles;
  const gele = gel || glisse !== null || trace;
  const { range, enAttente } = useRangement(modele, tailles, gele, jeton);

  // ---------- Nœuds affichés : ceux du dernier rangement appliqué, animés vers leur place.
  const [noeuds, setNoeuds] = useState<NoeudRf[]>([]);
  const noeudsRef = useRef<NoeudRf[]>([]);
  noeudsRef.current = noeuds;
  const modeleAffiche = range?.modele ?? modele;
  const animation = useRef<number | undefined>(undefined);
  const premier = useRef(true);
  // Dernier nœud touché (« + », dépôt) : ancre de la vue quand il n'y a pas de sélection.
  const derniereAncre = useRef<string | null>(null);
  const zone = useRef<HTMLDivElement>(null);

  const ancreCourante = useCallback(
    (avant: ReadonlyMap<string, { x: number; y: number }>) => {
      const candidats = [selection, cible?.type === 'carte' ? cible.id : null, derniereAncre.current];
      for (const c of candidats) {
        if (!c) continue;
        if (avant.has(c)) return c;
        const sid = parId.get(c) && infoSection(parId.get(c) as Etape).sectionId;
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

  /** Vue d'arrivée : tout le graphe s'il tient lisiblement, sinon le haut du graphe, centré, à 80 % au moins. */
  const vueInitiale = () => {
    const el = zone.current;
    const noeudsRf = rf.getNodes();
    if (!el || noeudsRf.length === 0) return;
    const b = rf.getNodesBounds(noeudsRf);
    const marge = GRILLE * 3;
    const hauteur = el.clientHeight - BARRE_OUTILS;
    const ajuste = Math.min(1, el.clientWidth / (b.width + 2 * marge), hauteur / (b.height + 2 * marge));
    const z = Math.max(0.8, ajuste);
    void rf.setViewport({
      x: el.clientWidth / 2 - (b.x + b.width / 2) * z,
      y: BARRE_OUTILS + (ajuste >= 0.8 ? hauteur / 2 - (b.y + b.height / 2) * z : marge - b.y * z),
      zoom: z,
    });
  };

  useEffect(() => {
    const precedents = new Map(noeudsRef.current.filter((n) => !n.id.startsWith(PREFIXE_TITRE)).map((n) => [n.id, n]));
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

  // ---------- Liens affichés.
  const [lienSelectionne, setLienSelectionne] = useState<string | null>(null);
  const [lienActif, setLienActif] = useState<string | null>(null);
  const liensParId = useMemo(() => new Map(modeleAffiche.liens.map((l) => [l.id, l])), [modeleAffiche]);
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
  const inserer = useCallback(
    (l: LienModele) => {
      if (!l.logique) return;
      derniereAncre.current = l.cible;
      actions.ajouter?.({ entre: l.logique });
    },
    [actions],
  );
  const code = useCallback((id: string) => parId.get(id)?.code ?? '?', [parId]);
  const libelleLien = useCallback(
    (l: LienModele) => {
      const nom = (n: string) =>
        n.startsWith('s:')
          ? `section ${parSection.get(n.slice(2))?.titre ?? ''}`
          : n.startsWith('c:')
            ? `cercle ${sections.flatMap((s) => s.ports).find((c) => c.id === n.slice(2))?.titre ?? ''}`
            : code(n);
      return `Lien ${nom(l.source)} vers ${nom(l.cible)}`;
    },
    [code, parSection, sections],
  );
  const liens = useMemo<LienRf[]>(
    () =>
      modeleAffiche.liens.map((l) => ({
        id: l.id,
        type: 'lien',
        source: l.source,
        sourceHandle: l.sourcePoignee,
        target: l.cible,
        targetHandle: l.ciblePoignee,
        selectable: edition,
        focusable: edition,
        selected: lienSelectionne === l.id,
        ariaLabel: libelleLien(l),
        zIndex: Z.lien,
        data: {
          lien: l,
          fait: fait(l),
          actif: lienActif === l.id,
          onInserer: actions.ajouter ? inserer : undefined,
          onSupprimer: actions.supprimerLien,
        },
      })),
    [modeleAffiche, edition, lienSelectionne, lienActif, fait, actions, inserer, libelleLien],
  );
  const onEdgesChange = useCallback((changes: EdgeChange<LienRf>[]) => {
    for (const c of changes)
      if (c.type === 'select') setLienSelectionne((s) => (c.selected ? c.id : s === c.id ? null : s));
  }, []);

  // ---------- Glisser une carte : cible sous le pointeur (lien ou carte), indicateur, dépôt.
  const cibleSous = useCallback(
    (x: number, y: number, exclu: string | null): CibleDepot | null => {
      for (const el of document.elementsFromPoint(x, y)) {
        const n = el.closest('.react-flow__node');
        if (n) {
          const id = n.getAttribute('data-id');
          if (id && id !== exclu && parId.has(id)) return { type: 'carte', id };
          continue;
        }
        const lien = el.closest('.react-flow__edge');
        if (lien) {
          const l = liensParId.get(lien.getAttribute('data-id') ?? '');
          if (l?.logique) return { type: 'lien', lien: l };
        }
      }
      return null;
    },
    [parId, liensParId],
  );
  const memeCible = (a: CibleDepot | null, b: CibleDepot | null) =>
    a === b ||
    (!!a &&
      !!b &&
      a.type === b.type &&
      (a.type === 'carte' ? a.id === (b as typeof a).id : a.lien.id === (b as typeof a).lien.id));
  const majCible = useCallback((c: CibleDepot | null) => setCible((avant) => (memeCible(avant, c) ? avant : c)), []);

  const finGlisser = useCallback(
    (id: string, c: CibleDepot | null) => {
      setGlisse(null);
      setCible(null);
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
    [actions, etapes, onMessage, modeleAffiche, range, parId, edition],
  );

  // Copie flottante d'une étape modèle : glisser au pointeur, même cible qu'une carte.
  const [clone, setClone] = useState<{ x: number; y: number } | null>(null);
  const appuyerFlottante = (ev: PointerEventReact<HTMLDivElement>) => {
    if (!flottante) return;
    const fin = (e: PointerEvent, poser: boolean) => {
      window.removeEventListener('pointermove', bouger);
      window.removeEventListener('pointerup', lacher);
      window.removeEventListener('pointercancel', annuler);
      setClone(null);
      setGlisse(null);
      const c = poser ? cibleSous(e.clientX, e.clientY, null) : null;
      setCible(null);
      if (!poser) return;
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
  const debutTrace = useRef<OnConnectStartParams | null>(null);
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
      return !!a && a.type !== 'refus';
    },
    [etapes, sections],
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
    if ((ev.key === 'Delete' || ev.key === 'Backspace') && lienSelectionne && edition) {
      const l = liensParId.get(lienSelectionne);
      if (l) {
        ev.preventDefault();
        actions.supprimerLien?.(l);
        setLienSelectionne(null);
      }
    } else if (ev.key === 'Escape' && lienSelectionne) setLienSelectionne(null);
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
      estEstompee,
      onActiver: (id) => {
        setLienSelectionne(null);
        derniereAncre.current = id;
        onSelection(id);
      },
      onAjouter: actions.ajouter
        ? (place, ancre) => {
            derniereAncre.current = ancre;
            actions.ajouter?.(place);
          }
        : undefined,
      onBasculer: (id) => {
        derniereAncre.current = idSection(id);
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
      estEstompee,
      onSelection,
      actions,
      onBasculer,
    ],
  );

  const carteGlissee = flottante && glisse === flottante.etape.id ? flottante.etape : null;
  const noeudsAffiches = useMemo(
    () => (glisse ? noeuds.map((n) => (n.id === glisse ? { ...n, zIndex: Z.glisse } : n)) : noeuds),
    [noeuds, glisse],
  );

  return (
    <ContexteGraphe.Provider value={valeur}>
      {/* Raccourcis délégués (Suppr, Échap) : les touches viennent des liens focusables du graphe. */}
      {/* eslint-disable-next-line jsx-a11y/no-static-element-interactions */}
      <div
        ref={zone}
        data-graphe-libre
        onKeyDown={clavier}
        className={cn(
          'graphe-libre relative h-[clamp(520px,calc(100vh-280px),940px)] min-w-0 overflow-hidden rounded-xl bg-surface shadow-sm',
          plan === 'RetourArriere' ? 'border-2 border-rollback-solid' : 'border border-line',
        )}
      >
        <MarqueursLiens />
        <ReactFlow<NoeudRf, LienRf>
          nodes={noeudsAffiches}
          edges={liens}
          zIndexMode="manual"
          nodeTypes={TYPES_NOEUDS}
          edgeTypes={TYPES_LIENS}
          onNodesChange={onNodesChange}
          onEdgesChange={onEdgesChange}
          onEdgeMouseEnter={(_, e) => setLienActif(e.id)}
          onEdgeMouseLeave={(_, e) => setLienActif((a) => (a === e.id ? null : a))}
          onPaneClick={() => setLienSelectionne(null)}
          // Sans gestionnaire de clic, React Flow rend les nœuds non glissables « transparents » au pointeur
          // (exécution, lecture seule) : la carte gère elle-même son clic.
          onNodeClick={() => undefined}
          onNodeDragStart={(_, n) => {
            if (n.type !== 'etape') return;
            setLienSelectionne(null);
            setGlisse(n.id);
          }}
          onNodeDrag={(ev, n) => {
            if (n.type !== 'etape') return;
            const p = pointDe(ev);
            majCible(cibleSous(p.x, p.y, n.id));
          }}
          onNodeDragStop={(ev, n) => {
            if (n.type !== 'etape') return;
            const p = pointDe(ev);
            finGlisser(n.id, cibleSous(p.x, p.y, n.id));
          }}
          onConnectStart={(_, p) => {
            debutTrace.current = p;
            setTrace(true);
          }}
          onConnectEnd={() => {
            debutTrace.current = null;
            setTrace(false);
          }}
          onConnect={onConnect}
          isValidConnection={connexionValide}
          onMove={(_, v) => setZoom(v.zoom)}
          nodesDraggable={edition}
          nodesConnectable={edition}
          nodesFocusable={false}
          edgesFocusable={edition}
          elementsSelectable={edition}
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
          <MiniMap
            pannable
            zoomable
            position="bottom-left"
            ariaLabel="Mini-carte du graphe"
            className="!m-3 overflow-hidden rounded-lg border border-line !bg-surface-2 shadow-md"
            nodeColor={(n) =>
              n.type === 'cadre' || n.type === 'titreCadre'
                ? 'var(--accent-soft)'
                : n.type === 'cercle'
                  ? 'var(--accent)'
                  : 'var(--line-strong)'
            }
            maskColor="color-mix(in srgb, var(--bg) 60%, transparent)"
            nodeBorderRadius={6}
          />
        </ReactFlow>

        {enAttente && (
          <span
            role="status"
            className="absolute bottom-3 left-1/2 z-20 max-w-[60%] -translate-x-1/2 rounded-full border border-warn-bd bg-warn-bg px-2.5 py-1 text-center text-xs font-medium text-warn-fg shadow-sm"
          >
            Modifications reçues : rangées dès la fin de votre action
          </span>
        )}
        <div className="pointer-events-none absolute top-3 right-3 z-20 flex flex-wrap items-center justify-end gap-2">
          {outils}
          <form
            role="search"
            className="pointer-events-auto relative"
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
              placeholder="Aller à… (code)"
              list={`codes-${plan}`}
              value={recherche}
              onChange={(e) => setRecherche(e.target.value)}
              className="h-8 w-40 rounded-lg border border-line bg-surface/95 pr-2 pl-7 text-[13px] shadow-sm backdrop-blur focus:border-accent focus:ring-2 focus:ring-accent/30 focus:outline-none"
            />
            <datalist id={`codes-${plan}`}>
              {etapes.map((e) => (
                <option key={e.id} value={e.code}>
                  {e.action}
                </option>
              ))}
            </datalist>
          </form>
        </div>

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
          <BoutonIcone
            label="Ajuster à l’écran"
            taille="sm"
            onClick={() =>
              void rf.fitView({
                padding: { top: `${BARRE_OUTILS + GRILLE}px`, x: `${GRILLE}px`, bottom: `${GRILLE}px` },
                maxZoom: 1,
                duration: 250,
              })
            }
          >
            <Maximize className="size-4" aria-hidden />
          </BoutonIcone>
        </div>
        <p className="sr-only">
          Glissez le fond ou utilisez la molette pour vous déplacer ; Ctrl + molette pour zoomer. Tab pour aller de
          carte en carte, Entrée pour ouvrir.
        </p>
        {enTete}
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
