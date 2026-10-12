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

/**
 * Géométrie à retenir après une mesure. Une mesure identique à l'avant-dernière est une oscillation : la mise en page
 * bascule entre deux états (une barre de défilement qui apparaît puis disparaît, et avec elle la largeur de la zone, que
 * le graphe reprend comme largeur minimale). On garde alors l'état affiché, sinon React relance le rendu sans fin
 * (« Maximum update depth exceeded ») et la page devient blanche.
 */
export function geometrieRetenue(affichee: Geometrie, precedente: Geometrie | null, mesuree: Geometrie): Geometrie {
  const cle = JSON.stringify(mesuree);
  if (cle === JSON.stringify(affichee)) return affichee;
  if (precedente && cle === JSON.stringify(precedente)) return affichee;
  return mesuree;
}

/**
 * Mesure des fiches après chaque rendu, redimensionnement ou chargement des polices : les liens sont dessinés
 * d'après leur position réelle, dans le repère non zoomé.
 */
export function useMesureCartes(
  zone: RefObject<HTMLDivElement | null>,
  graphe: RefObject<HTMLDivElement | null>,
  zoomRef: MutableRefObject<number>,
): Geometrie {
  const [geo, setGeo] = useState<Geometrie>({ ancrages: {}, largeur: 0, hauteur: 0, zone: 0 });
  // Géométrie affichée avant l'actuelle, pour reconnaître une oscillation (voir geometrieRetenue).
  const precedente = useRef<Geometrie | null>(null);

  const mesurer = useCallback(() => {
    const g = graphe.current;
    if (!g) return;
    const z = zoomRef.current;
    const base = g.getBoundingClientRect();
    const ancrages: Record<string, Ancrage> = {};
    g.querySelectorAll<HTMLElement>('[data-carte]').forEach((el) => {
      const r = el.getBoundingClientRect();
      ancrages[el.dataset.carte!] = {
        x: (r.left + r.width / 2 - base.left) / z,
        haut: (r.top - base.top) / z,
        bas: (r.bottom - base.top) / z,
      };
    });
    const suivante: Geometrie = {
      ancrages,
      largeur: g.offsetWidth,
      hauteur: g.offsetHeight,
      zone: zone.current?.clientWidth ?? 0,
    };
    setGeo((p) => {
      const retenue = geometrieRetenue(p, precedente.current, suivante);
      if (retenue !== p) precedente.current = p;
      return retenue;
    });
  }, [zone, graphe, zoomRef]);
  useLayoutEffect(() => {
    mesurer();
  });
  useEffect(() => {
    const g = graphe.current;
    window.addEventListener('resize', mesurer);
    const ro = typeof ResizeObserver !== 'undefined' && g ? new ResizeObserver(() => mesurer()) : null;
    if (ro && g) ro.observe(g);
    if (ro && zone.current) ro.observe(zone.current);
    void document.fonts?.ready?.then(() => mesurer());
    return () => {
      window.removeEventListener('resize', mesurer);
      ro?.disconnect();
    };
  }, [mesurer]);

  return geo;
}
