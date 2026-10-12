import {
  useCallback,
  useEffect,
  useLayoutEffect,
  useRef,
  useState,
  type MutableRefObject,
  type RefObject,
} from 'react';
import type { Ancrage } from '../../graphe';

/** Position des fiches et taille du graphe (repère non zoomé), ainsi que la largeur de la zone visible. */
export interface Geometrie {
  ancrages: Record<string, Ancrage>;
  largeur: number;
  hauteur: number;
  zone: number;
}

/** Rectangle à l'écran (sous-ensemble de DOMRect). */
export interface Rectangle {
  left: number;
  top: number;
  width: number;
  height: number;
}

/**
 * Ancrage d'une fiche dans le repère de la couche des liens : `repere` est le rectangle à l'écran du SVG des liens
 * (sans viewBox, une unité du SVG vaut un pixel du graphe non zoomé), `zoom` l'échelle appliquée au graphe.
 * Mesurer depuis le SVG lui-même rend le calcul indépendant des bordures, marges, barres de défilement et du
 * défilement de la zone : seuls comptent les écarts à l'écran entre la fiche et l'origine du SVG.
 */
export function ancrageFiche(fiche: Rectangle, repere: Rectangle, zoom: number): Ancrage {
  const z = zoom > 0 ? zoom : 1;
  return {
    x: (fiche.left + fiche.width / 2 - repere.left) / z,
    haut: (fiche.top - repere.top) / z,
    bas: (fiche.top + fiche.height - repere.top) / z,
  };
}

/** Rectangle à l'écran du SVG des liens (origine du repère des liens), à défaut celui du graphe. */
export const repereLiens = (graphe: HTMLElement): DOMRect =>
  (graphe.querySelector<SVGSVGElement>('[data-liens]') ?? graphe).getBoundingClientRect();

/** Ancrages de toutes les fiches mesurées, dans le repère de la couche des liens. */
export function calculerAncrages(
  fiches: { id: string; rect: Rectangle }[],
  repere: Rectangle,
  zoom: number,
): Record<string, Ancrage> {
  const ancrages: Record<string, Ancrage> = {};
  for (const f of fiches) ancrages[f.id] = ancrageFiche(f.rect, repere, zoom);
  return ancrages;
}

const tailles = (g: Geometrie) => `${g.largeur}|${g.hauteur}|${g.zone}`;

/** Vrai si les deux géométries ont les mêmes tailles (graphe et zone), quelles que soient les fiches. */
export const memesTailles = (a: Geometrie, b: Geometrie) => tailles(a) === tailles(b);

/**
 * Géométrie à retenir après une mesure.
 *
 * Les tailles (graphe, zone) agissent sur la mise en page : le graphe prend la largeur de la zone pour minimum et son
 * conteneur prend la taille du graphe. Des tailles identiques à celles d'avant le dernier changement signalent une
 * oscillation (une barre de défilement qui apparaît puis disparaît) : on garde alors les tailles affichées, sinon React
 * relance le rendu sans fin (« Maximum update depth exceeded ») et la page devient blanche.
 *
 * Les ancrages, eux, n'agissent pas sur la mise en page (la couche des liens est en position absolue) : ils sont
 * toujours repris de la dernière mesure. Les figer avec les tailles laissait les liens à la position d'une mise en page
 * révolue, décalés de la moitié de l'écart de largeur (5 px pour une barre de défilement de 10 px).
 */
export function geometrieRetenue(affichee: Geometrie, precedente: Geometrie | null, mesuree: Geometrie): Geometrie {
  const oscillation = !!precedente && memesTailles(mesuree, precedente) && !memesTailles(mesuree, affichee);
  const base = oscillation ? affichee : mesuree;
  const memesAncrages = JSON.stringify(mesuree.ancrages) === JSON.stringify(affichee.ancrages);
  if (memesTailles(base, affichee) && memesAncrages) return affichee;
  return { ...base, ancrages: mesuree.ancrages };
}

/**
 * Mesure des fiches après chaque rendu, redimensionnement (fenêtre, zone, graphe, fiches) ou chargement des polices :
 * les liens sont dessinés d'après la position réelle des fiches, dans le repère du SVG des liens (non zoomé).
 */
export function useMesureCartes(
  zone: RefObject<HTMLDivElement | null>,
  graphe: RefObject<HTMLDivElement | null>,
  zoomRef: MutableRefObject<number>,
): Geometrie {
  const [geo, setGeo] = useState<Geometrie>({ ancrages: {}, largeur: 0, hauteur: 0, zone: 0 });
  // Géométrie affichée avant le dernier changement de tailles, pour reconnaître une oscillation (voir geometrieRetenue).
  // Oubliée à chaque changement venu de l'extérieur (zoom, fenêtre) : revenir à des tailles déjà affichées après un
  // tel changement (100 % → Ajuster → 100 %) n'est pas une oscillation.
  const precedente = useRef<Geometrie | null>(null);
  const zoomMesure = useRef<number | null>(null);
  const observateur = useRef<ResizeObserver | null>(null);
  const observees = useRef(new WeakSet<Element>());

  const mesurer = useCallback(() => {
    const g = graphe.current;
    if (!g) return;
    if (zoomMesure.current !== zoomRef.current) precedente.current = null;
    zoomMesure.current = zoomRef.current;
    const repere = repereLiens(g);
    const fiches = [...g.querySelectorAll<HTMLElement>('[data-carte]')];
    // Une fiche qui change de taille sans changer celle du graphe (ligne plus haute ailleurs) relance aussi la mesure.
    const ro = observateur.current;
    if (ro)
      for (const el of fiches)
        if (!observees.current.has(el)) {
          observees.current.add(el);
          ro.observe(el);
        }
    const suivante: Geometrie = {
      ancrages: calculerAncrages(
        fiches.map((el) => ({ id: el.dataset.carte!, rect: el.getBoundingClientRect() })),
        repere,
        zoomRef.current,
      ),
      largeur: g.offsetWidth,
      hauteur: g.offsetHeight,
      zone: zone.current?.clientWidth ?? 0,
    };
    setGeo((p) => {
      const retenue = geometrieRetenue(p, precedente.current, suivante);
      if (!memesTailles(retenue, p)) precedente.current = p;
      return retenue;
    });
  }, [zone, graphe, zoomRef]);
  useLayoutEffect(() => {
    mesurer();
  });
  useEffect(() => {
    const g = graphe.current;
    const redimensionnement = () => {
      precedente.current = null;
      mesurer();
    };
    window.addEventListener('resize', redimensionnement);
    const ro = typeof ResizeObserver !== 'undefined' ? new ResizeObserver(() => mesurer()) : null;
    observateur.current = ro;
    observees.current = new WeakSet();
    if (ro && g) ro.observe(g);
    if (ro && zone.current) ro.observe(zone.current);
    mesurer();
    void document.fonts?.ready?.then(() => mesurer());
    return () => {
      window.removeEventListener('resize', redimensionnement);
      ro?.disconnect();
      observateur.current = null;
    };
  }, [zone, graphe, mesurer]);

  return geo;
}
