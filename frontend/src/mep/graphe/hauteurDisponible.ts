import { type RefObject, useLayoutEffect, useState } from 'react';

/** Hauteur minimale du graphe hors plein écran. */
export const HAUTEUR_MIN = 360;

/**
 * Hauteur en flux de ce qui suit l'élément dans la page (lignes sous le graphe, comme « Lecture seule… » de la vue
 * d'ensemble) : à chaque niveau, les éléments suivants posés dessous (pas à côté), hors éléments positionnés.
 */
function hauteurDessous(el: HTMLElement): number {
  let total = 0;
  for (let a: HTMLElement | null = el; a && a !== document.body; a = a.parentElement) {
    const bas = a.getBoundingClientRect().bottom;
    for (let s = a.nextElementSibling; s; s = s.nextElementSibling) {
      if (!(s instanceof HTMLElement)) continue;
      const style = getComputedStyle(s);
      if (style.position === 'absolute' || style.position === 'fixed' || style.display === 'none') continue;
      const r = s.getBoundingClientRect();
      if (r.height <= 0 || r.top < bas - 1) continue;
      total += r.height + (parseFloat(style.marginTop) || 0) + (parseFloat(style.marginBottom) || 0);
    }
  }
  return total;
}

/**
 * Hauteur du graphe pour que la page tienne dans la fenêtre sans défiler : de son bord haut au bas de la fenêtre, moins ce
 * qui reste dessous (contenu qui suit le graphe, marges du cadre de page). Recalculée quand la fenêtre ou la page change de
 * taille.
 *
 * Ce qui dépasse encore après coup est retiré au tour suivant et cette correction est gardée jusqu'au prochain
 * redimensionnement de la fenêtre : sinon, avec une barre de défilement qui prend de la place (Windows), la page basculait
 * sans fin entre deux hauteurs (la barre apparaît, le conteneur change de largeur, le calcul repart de zéro, la barre
 * disparaît…) et le bas du graphe paraissait dédoublé.
 */
export function useHauteurDisponible(ref: RefObject<HTMLElement | null>, actif: boolean): number | null {
  const [hauteur, setHauteur] = useState<number | null>(null);
  useLayoutEffect(() => {
    if (!actif) return undefined;
    let image = 0;
    let correction = 0;
    const calculer = () => {
      const el = ref.current;
      if (!el) return;
      let dessous = hauteurDessous(el);
      for (let p: HTMLElement | null = el.parentElement; p && p !== document.body; p = p.parentElement) {
        const s = getComputedStyle(p);
        dessous += (parseFloat(s.paddingBottom) || 0) + (parseFloat(s.borderBottomWidth) || 0);
      }
      const haut = el.getBoundingClientRect().top + window.scrollY;
      const h = Math.max(HAUTEUR_MIN, Math.floor(window.innerHeight - haut - dessous - 1 - correction));
      setHauteur(h);
      window.cancelAnimationFrame(image);
      image = window.requestAnimationFrame(() => {
        const exces = document.documentElement.scrollHeight - window.innerHeight;
        if (exces <= 0 || exces >= 400 || h <= HAUTEUR_MIN) return;
        const corrigee = Math.max(HAUTEUR_MIN, h - exces);
        correction += h - corrigee;
        setHauteur(corrigee);
      });
    };
    const redimensionner = () => {
      correction = 0;
      calculer();
    };
    calculer();
    window.addEventListener('resize', redimensionner);
    const ro = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(calculer);
    const el = ref.current;
    if (ro && el?.parentElement) ro.observe(el.parentElement);
    return () => {
      window.removeEventListener('resize', redimensionner);
      window.cancelAnimationFrame(image);
      ro?.disconnect();
    };
  }, [ref, actif]);
  return hauteur;
}
