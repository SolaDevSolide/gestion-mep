import type { ElkExtendedEdge, ElkNode, ElkPort } from 'elkjs/lib/elk-api';
import { calculerRangement } from './moteurElk';
import { POIGNEE_BAS, POIGNEE_HAUT, type ModeleGraphe, type NoeudModele } from './modeleGraphe';
import {
  CARTE,
  CERCLE,
  GRILLE,
  tailleSectionRepliee,
  type Place,
  type Point,
  type Rangement,
  type Taille,
} from './rangement';

/**
 * Rangement du graphe libre « cercles sur les bordures » (remplace `ranger` de rangement.ts ; mêmes entrées, même sortie).
 *
 * ELK layered de haut en bas, rangé de bas en haut (`SEPARATE_CHILDREN`) : l'intérieur de chaque section dépliée est rangé
 * à part, puis le plan, où la section est un bloc. Un ajout hors d'une section ne change donc rien dedans, et le bloc se
 * décale librement (placement NETWORK_SIMPLEX) pour laisser passer les liens qui le longent.
 *
 * - Cercles d'une section dépliée : ports du cadre, à cheval sur la bordure comme sur une section repliée. Le nœud cercle
 *   (132 × 32) a son point sur la bordure : 8 px au-dessus du bord haut (entrée), 8 px sous le bord bas (sortie) ; son
 *   étiquette est à l'intérieur. Les liens extérieurs touchent le point, les liens intérieurs partent sous l'étiquette.
 * - Titre d'une section dépliée : étiquette posée sur la bordure haute, à gauche des cercles d'entrée (port sans lien).
 * - Lien direct qui entre dans un cadre ou en sort sans cercle : port implicite sur le bord (un par carte et par sens).
 * - Fusion : une carte a un point d'arrivée (milieu du haut) et un point de départ (milieu du bas). Les liens qui arrivent
 *   au même point se fondent avant d'arriver ; ceux qui partent d'un même point font un tronc commun puis se séparent.
 *   Groupes complets : les liens qui arrivent sur un même port sont groupés par ensemble de sources ; chaque groupe part
 *   d'un port de départ à lui (même position). Un tronc commun relie donc toujours toutes ses sources à toutes ses cibles :
 *   suivre un trait ne fait jamais lire une relation qui n'existe pas.
 * - Ordre : celui du modèle (rang choisi, sinon barycentrique) est tenu dans chaque rangée (`forceNodeModelOrder`, sans
 *   l'échange glouton qui l'ignore) ; seuls les liens longs se placent librement entre les cartes.
 */
export const CERCLE_BORD = { largeur: 132, hauteur: 32, dehors: 8, dedans: 24 };
export const TITRE_BORD = { largeur: 296, hauteur: 36 };
const PORT_IMPLICITE = 8;

const OPTIONS_LIENS: Record<string, string> = {
  'elk.edgeRouting': 'ORTHOGONAL',
  'elk.spacing.edgeEdge': '14',
  'elk.spacing.edgeNode': '20',
  'elk.layered.spacing.edgeEdgeBetweenLayers': '14',
  'elk.layered.spacing.edgeNodeBetweenLayers': '20',
  'elk.spacing.portPort': '24',
};

/** Options de rangement, reprises par chaque cadre (rangé à part). */
const OPTIONS_RANGEMENT: Record<string, string> = {
  'elk.algorithm': 'layered',
  'elk.direction': 'DOWN',
  'elk.layered.considerModelOrder.strategy': 'NODES_AND_EDGES',
  'elk.layered.crossingMinimization.forceNodeModelOrder': 'true',
  'elk.layered.crossingMinimization.greedySwitch.type': 'OFF',
  'elk.layered.layering.strategy': 'LONGEST_PATH_SOURCE',
  'elk.layered.nodePlacement.strategy': 'NETWORK_SIMPLEX',
  'elk.layered.thoroughness': '30',
};

export const OPTIONS_RACINE: Record<string, string> = {
  ...OPTIONS_RANGEMENT,
  'elk.hierarchyHandling': 'SEPARATE_CHILDREN',
  'elk.layered.considerModelOrder.components': 'MODEL_ORDER',
  'elk.spacing.nodeNode': String(GRILLE * 2),
  'elk.layered.spacing.nodeNodeBetweenLayers': String(GRILLE * 3),
  'elk.spacing.componentComponent': String(GRILLE * 3),
  'elk.padding': `[top=${GRILLE},left=${GRILLE},bottom=${GRILLE},right=${GRILLE}]`,
  'elk.json.edgeCoords': 'ROOT',
  ...OPTIONS_LIENS,
};

const OPTIONS_CADRE: Record<string, string> = {
  ...OPTIONS_RANGEMENT,
  'elk.padding': `[top=${GRILLE + CERCLE_BORD.dedans},left=${GRILLE},bottom=${GRILLE + CERCLE_BORD.dedans},right=${GRILLE}]`,
  'elk.spacing.nodeNode': String(GRILLE * 2),
  'elk.layered.spacing.nodeNodeBetweenLayers': String(GRILLE * 2),
  // Ordre des cercles sur un bord : celui de la section (ordre choisi), titre en premier.
  'elk.portConstraints': 'FIXED_ORDER',
  ...OPTIONS_LIENS,
};

function tailleDe(n: NoeudModele, tailles: ReadonlyMap<string, Taille>): Taille {
  const m = tailles.get(n.id);
  if (n.nature === 'section') return tailleSectionRepliee(n, m);
  // Cercle de la vue d'une section (nœud du plan, pas un port de bordure).
  if (n.nature === 'cercle') return CERCLE;
  return m ?? CARTE;
}

/** Morceau de lien passé à ELK (un lien qui entre dans un cadre ou en sort est coupé au port de la bordure). */
interface Morceau {
  id: string;
  source: string;
  cible: string;
  /** Cadre qui contient le morceau (null : le plan). */
  conteneur: string | null;
}

export interface GrapheBordure {
  graphe: ElkNode;
  /** Cercles des cadres dépliés : cadre et sens. */
  cerclesCadre: Map<string, { cadre: string; sens: 'Entree' | 'Sortie' }>;
  /** Titres des cadres dépliés → cadre. */
  titres: Map<string, string>;
  /** Morceaux ELK de chaque lien du modèle, dans l'ordre. */
  morceaux: Map<string, string[]>;
}

/** Graphe ELK du modèle (exporté pour les tests). */
export function grapheBordure(modele: ModeleGraphe, tailles: ReadonlyMap<string, Taille>): GrapheBordure {
  const noeudsElk = new Map<string, ElkNode>();
  const racine: ElkNode[] = [];
  const cerclesCadre = new Map<string, { cadre: string; sens: 'Entree' | 'Sortie' }>();
  const titres = new Map<string, string>();
  const parent = new Map(modele.noeuds.map((n) => [n.id, n.parent]));
  const ajouterPort = (cadre: string, p: ElkPort) => {
    const c = noeudsElk.get(cadre);
    if (c) (c.ports ??= []).push(p);
  };

  for (const n of modele.noeuds) {
    if (n.parent && (n.nature === 'cercle' || n.nature === 'titre')) {
      const titre = n.nature === 'titre';
      const entree = titre || n.sens === 'Entree';
      if (titre) titres.set(n.id, n.parent);
      else cerclesCadre.set(n.id, { cadre: n.parent, sens: entree ? 'Entree' : 'Sortie' });
      const largeur = titre ? (tailles.get(n.id)?.largeur ?? TITRE_BORD.largeur) : CERCLE_BORD.largeur;
      const hauteur = titre ? TITRE_BORD.hauteur : CERCLE_BORD.hauteur;
      ajouterPort(n.parent, {
        id: titre ? `t:${n.id}` : `p:${n.cercleId}`,
        width: largeur,
        height: hauteur,
        layoutOptions: {
          'elk.port.side': entree ? 'NORTH' : 'SOUTH',
          'elk.port.borderOffset': String(titre ? -hauteur / 2 : -CERCLE_BORD.dedans),
        },
      });
      continue;
    }
    const noeud: ElkNode = { id: n.id };
    noeudsElk.set(n.id, noeud);
    if (n.nature === 'cadre') {
      noeud.layoutOptions = OPTIONS_CADRE;
      noeud.children = [];
    } else {
      const t = tailleDe(n, tailles);
      noeud.width = t.largeur;
      noeud.height = t.hauteur;
      noeud.layoutOptions = { 'elk.alignment': 'TOP' };
      if (n.nature === 'etape' || n.nature === 'cercle') {
        noeud.ports = [
          {
            id: `${n.id}|${POIGNEE_BAS}`,
            width: 0,
            height: 0,
            x: t.largeur / 2,
            y: t.hauteur,
            layoutOptions: { 'elk.port.side': 'SOUTH' },
          },
          {
            id: `${n.id}|${POIGNEE_HAUT}`,
            width: 0,
            height: 0,
            x: t.largeur / 2,
            y: 0,
            layoutOptions: { 'elk.port.side': 'NORTH' },
          },
        ];
        noeud.layoutOptions['elk.portConstraints'] = 'FIXED_POS';
      }
      if (n.nature === 'section') {
        const sorties = n.sorties ?? [];
        noeud.ports = [
          ...(n.entrees ?? []).map((c, i) => ({
            id: `p:${c.id}`,
            width: 1,
            height: 1,
            layoutOptions: { 'elk.port.side': 'NORTH', 'elk.port.index': String(i) },
          })),
          ...sorties.map((c, i) => ({
            id: `p:${c.id}`,
            width: 1,
            height: 1,
            layoutOptions: { 'elk.port.side': 'SOUTH', 'elk.port.index': String(1000 + sorties.length - i) },
          })),
        ];
        noeud.layoutOptions['elk.portConstraints'] = 'FIXED_ORDER';
      }
    }
    const liste = n.parent ? noeudsElk.get(n.parent)?.children : racine;
    (liste ?? racine).push(noeud);
  }

  // Bout ELK d'une extrémité de lien, et lieu (cadre ou plan) où elle se trouve vue depuis l'autre extrémité.
  const portDe = (noeud: string, poignee: string): string => {
    if (cerclesCadre.has(noeud)) return `p:${noeud.slice(2)}`;
    if (noeud.startsWith('s:') && poignee !== POIGNEE_BAS && poignee !== POIGNEE_HAUT) return `p:${poignee}`;
    return noeudsElk.get(noeud)?.ports ? `${noeud}|${poignee}` : noeud;
  };
  const lieuPropre = (noeud: string): string | null => {
    const c = cerclesCadre.get(noeud);
    return c ? (parent.get(c.cadre) ?? null) : (parent.get(noeud) ?? null);
  };
  const lieu = (noeud: string, autre: string): string | null => {
    const c = cerclesCadre.get(noeud);
    if (!c) return lieuPropre(noeud);
    const a = cerclesCadre.get(autre);
    const dedans = a ? a.cadre === c.cadre : lieuPropre(autre) === c.cadre;
    return dedans ? c.cadre : (parent.get(c.cadre) ?? null);
  };
  const implicites = new Set<string>();
  const portImplicite = (cadre: string, carte: string, entree: boolean) => {
    const id = `x:${entree ? 'n' : 's'}:${carte}`;
    if (!implicites.has(id)) {
      implicites.add(id);
      ajouterPort(cadre, {
        id,
        width: PORT_IMPLICITE,
        height: PORT_IMPLICITE,
        layoutOptions: {
          'elk.port.side': entree ? 'NORTH' : 'SOUTH',
          'elk.port.borderOffset': String(-PORT_IMPLICITE / 2),
        },
      });
    }
    return id;
  };
  const tous: Morceau[] = [];
  const morceaux = new Map<string, string[]>();
  for (const l of modele.liens) {
    const ls = lieu(l.source, l.cible);
    const lc = lieu(l.cible, l.source);
    const m: Omit<Morceau, 'id'>[] = [];
    let courant = portDe(l.source, l.sourcePoignee);
    const cible = portDe(l.cible, l.ciblePoignee);
    if (ls === lc) m.push({ source: courant, cible, conteneur: ls });
    else {
      // Une seule profondeur de cadres : sortie du cadre source, trajet dans le plan, entrée dans le cadre cible.
      if (ls) {
        const x = portImplicite(ls, l.source, false);
        m.push({ source: courant, cible: x, conteneur: ls });
        courant = x;
      }
      if (lc) {
        const x = portImplicite(lc, l.cible, true);
        m.push({ source: courant, cible: x, conteneur: parent.get(lc) ?? null });
        m.push({ source: x, cible, conteneur: lc });
      } else m.push({ source: courant, cible, conteneur: null });
    }
    const ids = m.map((_, i) => (m.length === 1 ? l.id : `${l.id}#${i}`));
    m.forEach((x, i) => tous.push({ id: ids[i], ...x }));
    morceaux.set(l.id, ids);
  }
  grouper(tous, noeudsElk);

  // Ordre des ports d'un cadre, dans le sens horaire depuis le coin haut gauche : titre, entrées, ports implicites en haut ;
  // sorties de gauche à droite en bas.
  for (const n of noeudsElk.values()) {
    if (!n.children || !n.ports) continue;
    const cote = (p: ElkPort) => p.layoutOptions?.['elk.port.side'];
    const nord = n.ports.filter((p) => cote(p) === 'NORTH');
    const sud = n.ports.filter((p) => cote(p) === 'SOUTH');
    nord.forEach((p, i) => ((p.layoutOptions ??= {})['elk.port.index'] = String(i)));
    sud.forEach((p, i) => ((p.layoutOptions ??= {})['elk.port.index'] = String(1000 + sud.length - i)));
    // Un cadre vide (ou presque) garde la place de son titre et de ses cercles sur ses bordures, sans chevauchement.
    const largeur = (ps: ElkPort[]) => ps.reduce((t, p) => t + (p.width ?? 0) + GRILLE, GRILLE);
    n.layoutOptions = {
      ...n.layoutOptions,
      'elk.nodeSize.constraints': 'MINIMUM_SIZE',
      'elk.nodeSize.minimum': `(${Math.max(largeur(nord), largeur(sud))},${2 * (GRILLE + CERCLE_BORD.dedans) + GRILLE * 2})`,
    };
  }
  const edges = (conteneur: string | null): ElkExtendedEdge[] =>
    tous.filter((x) => x.conteneur === conteneur).map((x) => ({ id: x.id, sources: [x.source], targets: [x.cible] }));
  for (const [id, n] of noeudsElk) if (n.children) n.edges = edges(id);
  return {
    graphe: { id: 'racine', layoutOptions: OPTIONS_RACINE, children: racine, edges: edges(null) },
    cerclesCadre,
    titres,
    morceaux,
  };
}

/**
 * Groupes complets : pour chaque port d'arrivée (dans un même conteneur), l'ensemble de ses ports sources ; les cibles qui
 * ont exactement le même ensemble forment un groupe où chaque source est reliée à chaque cible. Le port de départ d'une
 * carte est dédoublé par groupe (même position) : ELK route chaque groupe comme une hyperarête distincte.
 */
function grouper(morceaux: Morceau[], noeuds: Map<string, ElkNode>) {
  const sources = new Map<string, Set<string>>();
  const cleCible = (m: Morceau) => `${m.conteneur ?? ''}|${m.cible}`;
  for (const m of morceaux) sources.set(cleCible(m), (sources.get(cleCible(m)) ?? new Set()).add(m.source));
  const groupe = (m: Morceau) => [...(sources.get(cleCible(m)) ?? [])].sort().join(',');
  const parSource = new Map<string, string[]>();
  for (const m of morceaux) {
    const g = parSource.get(m.source) ?? [];
    if (!g.includes(groupe(m))) g.push(groupe(m));
    parSource.set(m.source, g);
  }
  for (const m of morceaux) {
    const g = parSource.get(m.source) ?? [];
    const i = g.indexOf(groupe(m));
    if (i <= 0) continue;
    // Seuls les ports de départ des cartes (position fixe) sont dédoublés ; un cercle reste un seul point.
    const [noeud, poignee] = m.source.split('|');
    const n = noeuds.get(noeud);
    const base = n?.ports?.find((p) => p.id === m.source);
    if (!n?.ports || !base || poignee !== POIGNEE_BAS) continue;
    const id = `${m.source}#g${i}`;
    if (!n.ports.some((p) => p.id === id)) n.ports.push({ ...base, id, layoutOptions: { ...base.layoutOptions } });
    m.source = id;
  }
}

/**
 * Range le modèle : places relatives au parent (React Flow), cercles (sections repliées et cadres), tracés absolus. Calcul
 * dans le fil de calcul d'ELK (`moteurElk`) ; échec : `RangementIndisponible`.
 */
export async function rangerBordure(modele: ModeleGraphe, tailles: ReadonlyMap<string, Taille>): Promise<Rangement> {
  const g = grapheBordure(modele, tailles);
  return relire(modele, await calculerRangement(g.graphe), g);
}

const arrondi = (v: number | undefined) => Math.round((v ?? 0) * 2) / 2;

/** Relit le résultat d'ELK (exporté pour les tests). */
export function relire(modele: ModeleGraphe, res: ElkNode, g: GrapheBordure): Rangement {
  const places = new Map<string, Place>();
  const cercles = new Map<string, number>();
  const brutes = new Map<string, Point[]>();
  const absolu = new Map<string, Point>();
  const portsCadre = new Map<string, { x: number; l: number }>();
  const parcourir = (n: ElkNode, ox: number, oy: number) => {
    for (const e of n.edges ?? []) {
      const s = e.sections?.[0];
      if (s)
        brutes.set(
          e.id,
          [s.startPoint, ...(s.bendPoints ?? []), s.endPoint].map((p) => ({ x: arrondi(p.x), y: arrondi(p.y) })),
        );
    }
    for (const c of n.children ?? []) {
      places.set(c.id, { x: arrondi(c.x), y: arrondi(c.y), largeur: arrondi(c.width), hauteur: arrondi(c.height) });
      const ax = ox + (c.x ?? 0);
      const ay = oy + (c.y ?? 0);
      absolu.set(c.id, { x: ax, y: ay });
      for (const p of c.ports ?? []) {
        if (c.children) portsCadre.set(p.id, { x: p.x ?? 0, l: p.width ?? 0 });
        else if (p.id.startsWith('p:')) cercles.set(p.id.slice(2), (p.x ?? 0) + (p.width ?? 0) / 2);
      }
      parcourir(c, ax, ay);
    }
  };
  parcourir(res, 0, 0);
  for (const [id, info] of g.cerclesCadre) {
    const p = portsCadre.get(`p:${id.slice(2)}`);
    const cadre = places.get(info.cadre);
    if (!p || !cadre) continue;
    const y = info.sens === 'Entree' ? -CERCLE_BORD.dehors : cadre.hauteur - CERCLE_BORD.dedans;
    places.set(id, { x: arrondi(p.x), y, largeur: CERCLE_BORD.largeur, hauteur: CERCLE_BORD.hauteur });
    cercles.set(id.slice(2), arrondi(p.x + p.l / 2));
  }
  for (const [id] of g.titres) {
    const p = portsCadre.get(`t:${id}`);
    if (p)
      places.set(id, {
        x: arrondi(p.x),
        y: -TITRE_BORD.hauteur / 2,
        largeur: arrondi(p.l),
        hauteur: TITRE_BORD.hauteur,
      });
  }
  const parent = new Map(modele.noeuds.map((n) => [n.id, n.parent]));
  const routes = new Map<string, Point[]>();
  for (const l of modele.liens) {
    const pts: Point[] = [];
    let complet = true;
    for (const id of g.morceaux.get(l.id) ?? [l.id]) {
      const b = brutes.get(id);
      if (!b) complet = false;
      else pts.push(...(pts.length ? b.slice(1) : b));
    }
    if (!complet || pts.length < 2) continue;
    // Bouts sur un cercle de cadre : sur le point de la bordure (dehors) ou sous l'étiquette (dedans), à la verticale.
    const recaler = (cercle: string, fin: boolean) => {
      const info = g.cerclesCadre.get(cercle);
      const place = places.get(cercle);
      const cadre = info && places.get(info.cadre);
      const a = info && absolu.get(info.cadre);
      if (!info || !place || !cadre || !a) return;
      const autre = fin ? l.source : l.cible;
      const dedans = parent.get(autre) === info.cadre || g.cerclesCadre.get(autre)?.cadre === info.cadre;
      const bord = info.sens === 'Entree' ? a.y : a.y + cadre.hauteur;
      const decalage = dedans ? CERCLE_BORD.dedans : 0;
      const y = info.sens === 'Entree' ? bord + decalage : bord - decalage;
      const x = a.x + place.x + place.largeur / 2;
      const i = fin ? pts.length - 1 : 0;
      const v = fin ? pts.length - 2 : 1;
      pts[i] = { x, y };
      if (Math.abs(pts[v].x - x) < 1) pts[v] = { x, y: pts[v].y };
      else pts.splice(fin ? pts.length - 1 : 1, 0, { x, y: pts[v].y });
    };
    if (g.cerclesCadre.has(l.cible)) recaler(l.cible, true);
    if (g.cerclesCadre.has(l.source)) recaler(l.source, false);
    routes.set(l.id, pts);
  }
  return { places, cercles, routes };
}
