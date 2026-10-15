import type { Etape } from '../../types';
import {
  cerclesTries,
  dependancesDirectes,
  infoSection,
  trouverCercle,
  type CercleSection,
  type Section,
  type SensCercle,
} from './sections';

/**
 * Modèle du graphe libre, sans React : ce qui est dessiné (cartes, sections repliées ou dépliées, cercles) et les liens
 * entre eux, dans un ordre stable (ligne, puis ordre, puis code des étapes) qui sert d'ordre d'entrée au rangement.
 */
export type NatureNoeud = 'etape' | 'section' | 'cadre' | 'titre' | 'cercle';

export interface NoeudModele {
  /** Étape : son identifiant ; section : `s:{id}` ; titre d'une section dépliée : `t:{id}` ; cercle déplié : `c:{id}`. */
  id: string;
  nature: NatureNoeud;
  /** Cadre d'une section dépliée qui contient ce nœud. */
  parent: string | null;
  etapeId?: string;
  sectionId?: string;
  cercleId?: string;
  sens?: SensCercle;
  /** Section repliée : ses cercles d'entrée (bord haut) puis de sortie (bord bas). */
  entrees?: CercleSection[];
  sorties?: CercleSection[];
}

export interface LienModele {
  id: string;
  source: string;
  /** Poignée de départ : `bas` (carte, cercle) ou l'identifiant d'un cercle de sortie d'une section repliée. */
  sourcePoignee: string;
  cible: string;
  ciblePoignee: string;
  /**
   * Lien tel que le serveur le connaît : `vers` attend `de` (étapes ou cercles). Sert à insérer une étape dessus, à y
   * lâcher une carte, à le retirer. Absent pour un lien ramené au cadre d'une section repliée (plusieurs liens possibles).
   */
  logique?: { de: string; vers: string };
}

export interface ModeleGraphe {
  noeuds: NoeudModele[];
  liens: LienModele[];
}

export const idSection = (id: string) => `s:${id}`;
export const idCercle = (id: string) => `c:${id}`;
export const idTitre = (id: string) => `t:${id}`;
export const POIGNEE_HAUT = 'haut';
export const POIGNEE_BAS = 'bas';

/** Rang gauche-droite choisi dans le graphe (0 : jamais réordonné ; lecture tolérante des données de test). */
export const rangDe = (x: { rang?: number }) => x.rang ?? 0;

/** Le plan a-t-il été réordonné à la main ? Alors l'ordre des rangs fait foi, sinon ligne, ordre, code. */
export const planOrdonne = (etapes: Etape[], sections: Pick<Section, 'rang'>[]) =>
  etapes.some((e) => rangDe(e) > 0) || sections.some((s) => rangDe(s) > 0);

/** Ordre d'entrée stable du rangement : ligne, ordre, puis code. */
export const ordreStable = (a: Etape, b: Etape) =>
  a.ligne - b.ligne || a.ordre - b.ordre || a.code.localeCompare(b.code);

/** Ordre des étapes : par rang si le plan a été réordonné, sinon {@link ordreStable}. */
const comparateur = (ordonne: boolean) => (a: Etape, b: Etape) =>
  ordonne ? rangDe(a) - rangDe(b) || a.code.localeCompare(b.code) : ordreStable(a, b);

/**
 * Ordre gauche-droite de tous les éléments du plan (identifiants d'étapes et de sections, y compris celles du bac), tel que le
 * rangement l'applique : c'est la liste qu'on renvoie au serveur après une réorganisation.
 */
export function ordreGlobal(etapes: Etape[], sections: Section[]): string[] {
  const ordonne = planOrdonne(etapes, sections);
  const triees = [...etapes].sort(comparateur(ordonne));
  const rangDans = new Map(triees.map((e, i) => [e.id, i]));
  const premier = new Map<string, number>();
  for (const e of triees) {
    const sid = infoSection(e).sectionId;
    if (sid && !premier.has(sid)) premier.set(sid, rangDans.get(e.id) ?? 0);
  }
  const elements = [
    ...triees.map((e) => ({ id: e.id, cle: ordonne ? rangDe(e) : (rangDans.get(e.id) ?? 0), code: e.code })),
    ...sections.map((s) => ({
      id: s.id,
      cle: ordonne ? rangDe(s) : (premier.get(s.id) ?? Number.MAX_SAFE_INTEGER) - 0.5,
      code: `${s.creeLe}|${s.id}`,
    })),
  ];
  return elements.sort((a, b) => a.cle - b.cle || a.code.localeCompare(b.code)).map((x) => x.id);
}

/**
 * Nouvel ordre global après une réorganisation d'un niveau : les places occupées par `avant` (éléments d'un même niveau, dans
 * leur ordre actuel) reçoivent `apres` (les mêmes, dans le nouvel ordre) ; le reste ne bouge pas.
 */
export function reordonner(global: string[], avant: string[], apres: string[]): string[] {
  const concernes = new Set(avant);
  const file = [...apres];
  return global.map((id) => (concernes.has(id) ? (file.shift() ?? id) : id));
}

interface Entree {
  etapes: Etape[];
  sections: Section[];
  /** Sections dépliées (plan) ; ignoré dans la vue d'une section. */
  deplies: ReadonlySet<string>;
  /** Vue d'édition d'une section : seules ses étapes et ses cercles. */
  sectionVue?: string | null;
}

/** Extrémité visible d'une étape dans le plan : sa carte, la section repliée qui la contient, ou rien (section au bac). */
type Extremite = { noeud: string; carte: boolean; section: string | null } | null;

export function construireModele({ etapes, sections, deplies, sectionVue = null }: Entree): ModeleGraphe {
  const ordonne = planOrdonne(etapes, sections);
  const triees = [...etapes].sort(comparateur(ordonne));
  const parSection = new Map(sections.map((s) => [s.id, s]));
  const noeuds: NoeudModele[] = [];
  const liens: LienModele[] = [];
  const vus = new Set<string>();
  const ajouterLien = (l: Omit<LienModele, 'id'>) => {
    const id = `${l.source}:${l.sourcePoignee}>${l.cible}:${l.ciblePoignee}`;
    if (vus.has(id) || l.source === l.cible) return;
    vus.add(id);
    liens.push({ id, ...l });
  };

  if (sectionVue) {
    const s = parSection.get(sectionVue);
    const dedans = triees.filter((e) => infoSection(e).sectionId === sectionVue);
    const ids = new Set(dedans.map((e) => e.id));
    for (const c of s ? cerclesTries(s, 'Entree') : [])
      noeuds.push({
        id: idCercle(c.id),
        nature: 'cercle',
        parent: null,
        cercleId: c.id,
        sens: 'Entree',
        sectionId: sectionVue,
      });
    for (const e of dedans) noeuds.push({ id: e.id, nature: 'etape', parent: null, etapeId: e.id });
    for (const c of s ? cerclesTries(s, 'Sortie') : [])
      noeuds.push({
        id: idCercle(c.id),
        nature: 'cercle',
        parent: null,
        cercleId: c.id,
        sens: 'Sortie',
        sectionId: sectionVue,
      });
    for (const e of dedans)
      for (const d of dependancesDirectes(e))
        if (ids.has(d))
          ajouterLien({
            source: d,
            sourcePoignee: POIGNEE_BAS,
            cible: e.id,
            ciblePoignee: POIGNEE_HAUT,
            logique: { de: d, vers: e.id },
          });
    if (s) liensInternes(s, ids, ajouterLien);
    return { noeuds, liens };
  }

  const placees = sections.filter((s) => s.placee);
  const placeesIds = new Set(placees.map((s) => s.id));
  const extremite = (id: string): Extremite => {
    const e = triees.find((x) => x.id === id);
    if (!e) return null;
    const sid = infoSection(e).sectionId;
    if (sid && parSection.has(sid)) {
      if (!placeesIds.has(sid)) return null;
      if (!deplies.has(sid)) return { noeud: idSection(sid), carte: false, section: sid };
      return { noeud: e.id, carte: true, section: sid };
    }
    return { noeud: e.id, carte: true, section: null };
  };

  // Ordre des nœuds : étapes hors section et sections, à la place de leur première étape (les sections vides en fin).
  const rangDans = new Map(triees.map((e, i) => [e.id, i]));
  const premier = new Map<string, number>();
  for (const e of triees) {
    const sid = infoSection(e).sectionId;
    if (sid && placeesIds.has(sid) && !premier.has(sid)) premier.set(sid, rangDans.get(e.id) ?? 0);
  }
  const racine: { rang: number; cle: string; ajouter: () => void }[] = [];
  for (const e of triees) {
    const sid = infoSection(e).sectionId;
    if (sid && parSection.has(sid)) continue;
    racine.push({
      rang: ordonne ? rangDe(e) : (rangDans.get(e.id) ?? 0),
      cle: e.code,
      ajouter: () => noeuds.push({ id: e.id, nature: 'etape', parent: null, etapeId: e.id }),
    });
  }
  for (const s of placees) {
    const id = idSection(s.id);
    racine.push({
      rang: ordonne ? rangDe(s) : (premier.get(s.id) ?? Number.MAX_SAFE_INTEGER) - 0.5,
      cle: `${s.creeLe}|${s.id}`,
      ajouter: () => {
        if (!deplies.has(s.id)) {
          noeuds.push({
            id,
            nature: 'section',
            parent: null,
            sectionId: s.id,
            entrees: cerclesTries(s, 'Entree'),
            sorties: cerclesTries(s, 'Sortie'),
          });
          return;
        }
        noeuds.push({ id, nature: 'cadre', parent: null, sectionId: s.id });
        // Titre du cadre : un nœud rangé comme les autres (à gauche des cercles d'entrée), que les liens contournent.
        noeuds.push({ id: idTitre(s.id), nature: 'titre', parent: id, sectionId: s.id });
        for (const c of cerclesTries(s, 'Entree'))
          noeuds.push({
            id: idCercle(c.id),
            nature: 'cercle',
            parent: id,
            cercleId: c.id,
            sens: 'Entree',
            sectionId: s.id,
          });
        for (const e of triees)
          if (infoSection(e).sectionId === s.id) noeuds.push({ id: e.id, nature: 'etape', parent: id, etapeId: e.id });
        for (const c of cerclesTries(s, 'Sortie'))
          noeuds.push({
            id: idCercle(c.id),
            nature: 'cercle',
            parent: id,
            cercleId: c.id,
            sens: 'Sortie',
            sectionId: s.id,
          });
      },
    });
  }
  racine.sort((a, b) => a.rang - b.rang || a.cle.localeCompare(b.cle));
  for (const r of racine) r.ajouter();

  // Liens directs, ramenés au cadre d'une section repliée.
  for (const e of triees) {
    const b = extremite(e.id);
    if (!b) continue;
    for (const d of dependancesDirectes(e)) {
      const a = extremite(d);
      if (!a || (a.section && a.section === b.section && !a.carte)) continue;
      ajouterLien({
        source: a.noeud,
        sourcePoignee: POIGNEE_BAS,
        cible: b.noeud,
        ciblePoignee: POIGNEE_HAUT,
        ...(a.carte && b.carte ? { logique: { de: d, vers: e.id } } : {}),
      });
    }
  }

  // Cercles : sources des entrées, étapes qui attendent une sortie, liens internes d'une section dépliée.
  const boutCercle = (cercleId: string, cote: 'source' | 'cible'): { noeud: string; poignee: string } | null => {
    const t = trouverCercle(placees, cercleId);
    if (!t) return null;
    if (!deplies.has(t.section.id)) return { noeud: idSection(t.section.id), poignee: cercleId };
    return { noeud: idCercle(cercleId), poignee: cote === 'source' ? POIGNEE_BAS : POIGNEE_HAUT };
  };
  for (const s of placees) {
    for (const c of cerclesTries(s, 'Entree')) {
      const cible = boutCercle(c.id, 'cible');
      if (!cible) continue;
      for (const src of c.sources) {
        const sortie = trouverCercle(placees, src);
        const a = sortie ? boutCercle(src, 'source') : extremiteVersBout(extremite(src));
        if (!a) continue;
        ajouterLien({
          source: a.noeud,
          sourcePoignee: a.poignee,
          cible: cible.noeud,
          ciblePoignee: cible.poignee,
          logique: { de: src, vers: c.id },
        });
      }
    }
    if (deplies.has(s.id))
      liensInternes(s, new Set(triees.filter((e) => infoSection(e).sectionId === s.id).map((e) => e.id)), ajouterLien);
  }
  for (const e of triees) {
    const b = extremite(e.id);
    if (!b) continue;
    for (const p of infoSection(e).portsAttendus) {
      const a = boutCercle(p, 'source');
      if (!a) continue;
      ajouterLien({
        source: a.noeud,
        sourcePoignee: a.poignee,
        cible: b.noeud,
        ciblePoignee: POIGNEE_HAUT,
        logique: { de: p, vers: e.id },
      });
    }
  }
  return { noeuds, liens };
}

function extremiteVersBout(x: Extremite): { noeud: string; poignee: string } | null {
  return x ? { noeud: x.noeud, poignee: POIGNEE_BAS } : null;
}

/** Cercle d'entrée → ses étapes ; étapes → cercle de sortie (cadre déplié ou vue de la section). */
function liensInternes(s: Section, dedans: Set<string>, ajouter: (l: Omit<LienModele, 'id'>) => void) {
  for (const c of cerclesTries(s, 'Entree'))
    for (const e of c.etapes)
      if (dedans.has(e))
        ajouter({
          source: idCercle(c.id),
          sourcePoignee: POIGNEE_BAS,
          cible: e,
          ciblePoignee: POIGNEE_HAUT,
          logique: { de: c.id, vers: e },
        });
  for (const c of cerclesTries(s, 'Sortie'))
    for (const e of c.etapes)
      if (dedans.has(e))
        ajouter({
          source: e,
          sourcePoignee: POIGNEE_BAS,
          cible: idCercle(c.id),
          ciblePoignee: POIGNEE_HAUT,
          logique: { de: e, vers: c.id },
        });
}

/** Étapes qui dépendent (directement ou non) de `id`, d'après les préalables effectifs. */
export function descendants(etapes: Pick<Etape, 'id' | 'dependances'>[], id: string): Set<string> {
  const suivants = new Map<string, string[]>();
  for (const e of etapes) for (const d of e.dependances) suivants.set(d, [...(suivants.get(d) ?? []), e.id]);
  const vus = new Set<string>();
  const pile = [id];
  while (pile.length) {
    const x = pile.pop() as string;
    for (const s of suivants.get(x) ?? [])
      if (!vus.has(s)) {
        vus.add(s);
        pile.push(s);
      }
  }
  return vus;
}

/** Étapes dont `id` dépend (directement ou non). */
export function ascendants(etapes: Pick<Etape, 'id' | 'dependances'>[], id: string): Set<string> {
  const parId = new Map(etapes.map((e) => [e.id, e]));
  const vus = new Set<string>();
  const pile = [id];
  while (pile.length) {
    const x = pile.pop() as string;
    for (const d of parId.get(x)?.dependances ?? [])
      if (!vus.has(d)) {
        vus.add(d);
        pile.push(d);
      }
  }
  return vus;
}

/** `vers` peut-il attendre `de` ? Refusé : auto-lien, lien existant, ou cycle (de dépend déjà de vers). */
export function lienAutorise(
  etapes: Pick<Etape, 'id' | 'dependances'>[],
  de: string,
  vers: string,
): 'ok' | 'soi' | 'existe' | 'cycle' {
  if (de === vers) return 'soi';
  if (etapes.find((e) => e.id === vers)?.dependances.includes(de)) return 'existe';
  if (descendants(etapes, vers).has(de)) return 'cycle';
  return 'ok';
}
