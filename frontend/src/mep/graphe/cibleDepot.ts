import type { CibleDepot } from './connexions';
import type { LienModele } from './modeleGraphe';
import type { Point } from './rangement';
import { milieuLien } from './routage';

/**
 * Cible d'un dépôt (copie d'une étape modèle ou carte déplacée), calculée sur la géométrie du graphe relevée au début du
 * geste : aucun accès au DOM pendant le mouvement. Par ordre de priorité :
 * 1. sur une carte : après elle ;
 * 2. sur un cercle (ou la poignée d'un cercle d'une section repliée) : après lui ;
 * 3. près d'un lien (zone d'accroche large) : insertion dessus ; de deux liens proches, le plus proche l'emporte ;
 * 4. sous une carte : après elle (elle reçoit la connexion de base) ; à sa gauche ou à sa droite : en parallèle.
 */
export interface Boite {
  x: number;
  y: number;
  largeur: number;
  hauteur: number;
}

export interface CarteDepot extends Boite {
  id: string;
}

export interface CercleDepot extends Boite {
  /** Identifiant du cercle (tel que le serveur le connaît). */
  id: string;
  /** Nœud du graphe qui le porte (cercle déplié, ou section repliée pour une poignée de bord). */
  noeud: string;
}

export interface Geometrie {
  cartes: CarteDepot[];
  cercles: CercleDepot[];
  /** Tracés des liens visables (coordonnées du graphe). */
  liens: { lien: LienModele; route: Point[] }[];
}

/** Zone d'accroche d'un lien, en pixels d'écran. */
export const TOLERANCE_LIEN_PX = 16;
/** Zone sous une carte (après) et sur ses côtés (en parallèle), en pixels d'écran. */
export const MARGE_CARTE_PX = 72;
/** Deux liens à moins de cet écart du plus proche partagent le même tronçon. */
const MEME_TRONCON = 1.5;

const dans = (p: Point, b: Boite, m = 0) =>
  p.x >= b.x - m && p.x <= b.x + b.largeur + m && p.y >= b.y - m && p.y <= b.y + b.hauteur + m;

function distanceSegment(p: Point, a: Point, b: Point): number {
  const dx = b.x - a.x;
  const dy = b.y - a.y;
  const l2 = dx * dx + dy * dy;
  const t = l2 === 0 ? 0 : Math.max(0, Math.min(1, ((p.x - a.x) * dx + (p.y - a.y) * dy) / l2));
  return Math.hypot(p.x - (a.x + t * dx), p.y - (a.y + t * dy));
}

export function distanceRoute(p: Point, route: Point[]): number {
  let d = Infinity;
  for (let i = 1; i < route.length; i++) d = Math.min(d, distanceSegment(p, route[i - 1], route[i]));
  return d;
}

/** Cible au point `p` (coordonnées du graphe) ; `zoom` convertit les zones d'accroche, données en pixels d'écran. */
export function chercherCible(p: Point, g: Geometrie, zoom: number): CibleDepot | null {
  const z = Math.max(zoom, 0.1);
  const carte = g.cartes.find((c) => dans(p, c));
  if (carte) return { type: 'carte', id: carte.id };
  const cercle = g.cercles.find((c) => dans(p, c, 8 / z));
  if (cercle) return { type: 'cercle', id: cercle.id, noeud: cercle.noeud };

  const tolerance = TOLERANCE_LIEN_PX / z;
  const proches = g.liens
    .map((l) => ({ ...l, d: distanceRoute(p, l.route) }))
    .filter((l) => l.d <= tolerance)
    .sort((a, b) => a.d - b.d);
  if (proches.length > 0) {
    // Tronçon partagé (liens fusionnés) : le lien dont la flèche est la plus proche du pointeur.
    const meme = proches.filter((l) => l.d - proches[0].d <= MEME_TRONCON / z);
    const fin = (r: Point[]) => {
      const pt = r.at(-1);
      return pt ? Math.hypot(pt.x - p.x, pt.y - p.y) : Infinity;
    };
    const lien = meme.sort((a, b) => fin(a.route) - fin(b.route))[0].lien;
    return { type: 'lien', lien };
  }

  const marge = MARGE_CARTE_PX / z;
  let meilleure: { c: CarteDepot; parallele: boolean; d: number } | null = null;
  for (const c of g.cartes) {
    const dessous = p.x >= c.x && p.x <= c.x + c.largeur && p.y > c.y + c.hauteur && p.y <= c.y + c.hauteur + marge;
    const cote =
      p.y >= c.y &&
      p.y <= c.y + c.hauteur &&
      ((p.x > c.x + c.largeur && p.x <= c.x + c.largeur + marge) || (p.x < c.x && p.x >= c.x - marge));
    if (!dessous && !cote) continue;
    const d = dessous ? p.y - (c.y + c.hauteur) : p.x > c.x ? p.x - (c.x + c.largeur) : c.x - p.x;
    if (!meilleure || d < meilleure.d) meilleure = { c, parallele: cote && !dessous, d };
  }
  if (meilleure) return { type: 'carte', id: meilleure.c.id, parallele: meilleure.parallele || undefined };
  return null;
}

/** Fantôme de la nouvelle place : boîte de la carte à venir et liens qui seront créés (coordonnées du graphe). */
export interface Fantome {
  boite: Boite;
  liens: { de: Point; vers: Point }[];
  libelle: string;
}

const LARGEUR_FANTOME = 212;
const HAUTEUR_FANTOME = 64;
const ECART = 44;

const basDe = (b: Boite): Point => ({ x: b.x + b.largeur / 2, y: b.y + b.hauteur });
const hautDe = (b: Boite): Point => ({ x: b.x + b.largeur / 2, y: b.y });

/**
 * Place du fantôme pour une cible : sous la carte ou le cercle (après), à côté de la carte (en parallèle, avec les mêmes
 * liens qu'elle), au milieu du lien (insertion). `boites` : boîtes des nœuds par identifiant du graphe.
 */
export function fantomePour(
  c: CibleDepot,
  boites: ReadonlyMap<string, Boite>,
  cercles: readonly CercleDepot[],
  liens: readonly LienModele[],
  routes: ReadonlyMap<string, Point[]>,
  code: (id: string) => string,
): Fantome | null {
  // Bouts d'un lien : ceux de son tracé (poignée exacte, cercle d'une section repliée compris), sinon le centre des nœuds.
  const depart = (l: LienModele): Point | null => {
    const b = boites.get(l.source);
    return routes.get(l.id)?.[0] ?? (b ? basDe(b) : null);
  };
  const arrivee = (l: LienModele): Point | null => {
    const b = boites.get(l.cible);
    return routes.get(l.id)?.at(-1) ?? (b ? hautDe(b) : null);
  };
  const sous = (b: Boite): Boite => ({
    x: b.x + b.largeur / 2 - LARGEUR_FANTOME / 2,
    y: b.y + b.hauteur + ECART,
    largeur: LARGEUR_FANTOME,
    hauteur: HAUTEUR_FANTOME,
  });
  if (c.type === 'cercle') {
    const b = cercles.find((x) => x.id === c.id && x.noeud === c.noeud);
    if (!b) return null;
    const boite = sous(b);
    return { boite, liens: [{ de: basDe(b), vers: hautDe(boite) }], libelle: 'Après ce cercle' };
  }
  if (c.type === 'carte') {
    const b = boites.get(c.id);
    if (!b) return null;
    if (!c.parallele) {
      const boite = sous(b);
      return { boite, liens: [{ de: basDe(b), vers: hautDe(boite) }], libelle: `Après ${code(c.id)}` };
    }
    // À droite de la carte ; mêmes préalables et mêmes suivants qu'elle.
    const boite: Boite = {
      x: b.x + b.largeur + ECART / 2,
      y: b.y,
      largeur: b.largeur,
      hauteur: b.hauteur,
    };
    const traits: Fantome['liens'] = [];
    for (const l of liens) {
      if (l.cible === c.id) {
        const de = depart(l);
        if (de) traits.push({ de, vers: hautDe(boite) });
      } else if (l.source === c.id) {
        const vers = arrivee(l);
        if (vers) traits.push({ de: basDe(boite), vers });
      }
    }
    return { boite, liens: traits, libelle: `En parallèle de ${code(c.id)}` };
  }
  const de = depart(c.lien);
  const vers = arrivee(c.lien);
  if (!de || !vers) return null;
  const route = routes.get(c.lien.id);
  const m = route && route.length > 1 ? milieuLien(route) : { x: (de.x + vers.x) / 2, y: (de.y + vers.y) / 2 };
  const boite: Boite = {
    x: m.x - LARGEUR_FANTOME / 2,
    y: m.y - HAUTEUR_FANTOME / 2,
    largeur: LARGEUR_FANTOME,
    hauteur: HAUTEUR_FANTOME,
  };
  return {
    boite,
    liens: [
      { de, vers: hautDe(boite) },
      { de: basDe(boite), vers },
    ],
    libelle: 'Insérer sur ce lien',
  };
}
