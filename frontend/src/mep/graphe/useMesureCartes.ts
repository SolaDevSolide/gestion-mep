import { useCallback, useEffect, useLayoutEffect, useState, type MutableRefObject, type RefObject } from 'react';
import type { Ancrage } from '../../graphe';

/** Position des fiches et taille du graphe (repère non zoomé), ainsi que la largeur de la zone visible. */
export interface Geometrie { ancrages: Record<string, Ancrage>; largeur: number; hauteur: number; zone: number }

/**
 * Mesure des fiches après chaque rendu, redimensionnement ou chargement des polices : les liens sont dessinés
 * d'après leur position réelle, dans le repère non zoomé.
 */
export function useMesureCartes(zone: RefObject<HTMLDivElement>, graphe: RefObject<HTMLDivElement>, zoomRef: MutableRefObject<number>): Geometrie {
  const [geo, setGeo] = useState<Geometrie>({ ancrages: {}, largeur: 0, hauteur: 0, zone: 0 });

  const mesurer = useCallback(() => {
    const g = graphe.current;
    if (!g) return;
    const z = zoomRef.current;
    const base = g.getBoundingClientRect();
    const ancrages: Record<string, Ancrage> = {};
    g.querySelectorAll<HTMLElement>('[data-carte]').forEach((el) => {
      const r = el.getBoundingClientRect();
      ancrages[el.dataset.carte!] = { x: (r.left + r.width / 2 - base.left) / z, haut: (r.top - base.top) / z, bas: (r.bottom - base.top) / z };
    });
    const suivante: Geometrie = { ancrages, largeur: g.offsetWidth, hauteur: g.offsetHeight, zone: zone.current?.clientWidth ?? 0 };
    setGeo((p) => (JSON.stringify(p) === JSON.stringify(suivante) ? p : suivante));
  }, [zone, graphe, zoomRef]);
  useLayoutEffect(() => { mesurer(); });
  useEffect(() => {
    const g = graphe.current;
    window.addEventListener('resize', mesurer);
    const ro = typeof ResizeObserver !== 'undefined' && g ? new ResizeObserver(() => mesurer()) : null;
    if (ro && g) ro.observe(g);
    if (ro && zone.current) ro.observe(zone.current);
    void document.fonts?.ready?.then(() => mesurer());
    return () => { window.removeEventListener('resize', mesurer); ro?.disconnect(); };
  }, [mesurer]);

  return geo;
}
