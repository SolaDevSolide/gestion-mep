import { createContext, useContext, useSyncExternalStore } from 'react';

/**
 * Petits écrans (portable 1366 × 768 : fenêtre utile d'environ 1366 × 641). Sous `HAUTEUR_COMPACTE` px de haut, le plan
 * passe en vue compacte : cadre du graphe à la hauteur de la fenêtre, barre d'outils et bandeau de section d'une ligne,
 * étape ouverte en tiroir par-dessus le graphe. Sous `LARGEUR_ETROITE` px de large, en plus, le bac est replié en rail et
 * le menu latéral se replie tant qu'une étape est ouverte.
 */
export const HAUTEUR_COMPACTE = 800;
export const LARGEUR_ETROITE = 1440;
/** Marge laissée au-dessus et au-dessous du cadre du graphe en vue compacte (la page défile jusqu'à lui). */
export const MARGE_CADRE = 12;
/** Largeur du tiroir d'une étape, par-dessus le graphe. */
export const LARGEUR_TIROIR = 'clamp(420px, 36vw, 560px)';

function useMedia(requete: string): boolean {
  return useSyncExternalStore(
    (f) => {
      if (typeof window.matchMedia !== 'function') return () => undefined;
      const mq = window.matchMedia(requete);
      mq.addEventListener('change', f);
      return () => mq.removeEventListener('change', f);
    },
    () => typeof window.matchMedia === 'function' && window.matchMedia(requete).matches,
    () => false,
  );
}

/** Fenêtre de moins de 800 px de haut : vue compacte du plan. */
export const useFenetreBasse = () => useMedia(`(max-height: ${HAUTEUR_COMPACTE - 0.02}px)`);
/** Fenêtre de moins de 1440 px de large. */
export const useFenetreEtroite = () => useMedia(`(max-width: ${LARGEUR_ETROITE - 0.02}px)`);

/** Largeur du tiroir en pixels (même calcul que `LARGEUR_TIROIR`). */
export function largeurTiroir(): number {
  return Math.min(560, Math.max(420, window.innerWidth * 0.36));
}

/**
 * Hauteur de ce qui reste collé en haut de la fenêtre quand la page défile : bandeau de démonstration, et en-tête de la
 * MEP quand il est collant (fenêtre assez haute).
 */
export function hautCollant(): number {
  const racine = getComputedStyle(document.documentElement);
  let haut = parseFloat(racine.getPropertyValue('--bandeau-demo')) || 0;
  const entete = document.querySelector<HTMLElement>('[data-entete-mep]');
  if (entete && getComputedStyle(entete).position === 'sticky') haut += entete.offsetHeight;
  return haut;
}

/**
 * Emplacement du tiroir d'une étape, dans le cadre du graphe sous son bandeau (vue compacte) : l'espace de l'étape
 * (`Surcouche`) s'y affiche en tiroir au lieu du panneau collé au bord de la fenêtre. null : panneau habituel.
 */
export const EmplacementTiroir = createContext<HTMLElement | null>(null);
export const useEmplacementTiroir = () => useContext(EmplacementTiroir);
