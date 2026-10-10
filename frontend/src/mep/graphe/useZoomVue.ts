import {
  useCallback,
  useEffect,
  useLayoutEffect,
  useRef,
  useState,
  type MutableRefObject,
  type RefObject,
} from 'react';

export const ZOOM_MIN = 0.3;
export const ZOOM_MAX = 1.5;
export const PAS_ZOOM = 1.2;
/** Marge horizontale du graphe (place des blocs fantômes « + Étape »). */
const MARGE_X = 150;

/** Déplacement de la vue à la souris, piloté par les écouteurs du glisser-déposer. */
export interface DeplacementVue {
  /** Commence à faire glisser la vue depuis ce point. */
  saisir: (x: number, y: number) => void;
  /** Fait suivre la vue au pointeur ; faux si la vue n'est pas saisie. */
  deplacer: (x: number, y: number) => boolean;
  /** Relâche la vue ; faux si la vue n'était pas saisie. */
  lacher: () => boolean;
  /** Abandon du pointeur : la vue est relâchée. */
  annuler: () => void;
}

/**
 * Zoom de la vue du graphe (boutons, Ctrl + molette autour du pointeur, « Ajuster »), centrage à l'ouverture et
 * déplacement de la vue en faisant glisser le fond. `zoomRef` est tenu à jour à chaque rendu pour les écouteurs.
 */
export function useZoomVue(
  zone: RefObject<HTMLDivElement | null>,
  graphe: RefObject<HTMLDivElement | null>,
  zoomRef: MutableRefObject<number>,
  taille: { largeur: number; hauteur: number },
) {
  const [zoom, setZoom] = useState(1);
  zoomRef.current = zoom;
  /** Point du graphe (repère non zoomé) à garder sous le pointeur après un changement de zoom. */
  const ancre = useRef<{ px: number; py: number; cx: number; cy: number } | null>(null);
  const deplacementVue = useRef<{ x: number; y: number; sl: number; st: number } | null>(null);
  const [vueSaisie, setVueSaisie] = useState(false);

  const zoomer = useCallback(
    (cible: number, cx?: number, cy?: number) => {
      const z = zone.current;
      const z2 = Math.round(Math.min(ZOOM_MAX, Math.max(ZOOM_MIN, cible)) * 100) / 100;
      if (!z || z2 === zoomRef.current) return;
      const r = z.getBoundingClientRect();
      const x = cx === undefined ? r.width / 2 : cx - r.left;
      const y = cy === undefined ? Math.min(r.height, z.clientHeight) / 2 : cy - r.top;
      ancre.current = {
        px: (z.scrollLeft + x) / zoomRef.current,
        py: (z.scrollTop + y) / zoomRef.current,
        cx: x,
        cy: y,
      };
      setZoom(z2);
    },
    [zone, zoomRef],
  );

  // Graphe centré horizontalement à l'ouverture.
  useLayoutEffect(() => {
    const z = zone.current;
    if (z) z.scrollLeft = Math.max(0, (z.scrollWidth - z.clientWidth) / 2);
  }, []);
  // Après un zoom : le point visé reste sous le pointeur (ou au centre).
  useLayoutEffect(() => {
    const a = ancre.current;
    const z = zone.current;
    if (!a || !z) return;
    z.scrollLeft = a.px * zoom - a.cx;
    z.scrollTop = a.py * zoom - a.cy;
    const t = window.setTimeout(() => {
      ancre.current = null;
    }, 0);
    return () => window.clearTimeout(t);
  }, [zoom, taille.largeur, taille.hauteur]);
  // Ctrl + molette : zoom autour du pointeur (écouteur non passif pour empêcher le zoom de la page).
  useEffect(() => {
    const z = zone.current;
    if (!z) return;
    const molette = (ev: WheelEvent) => {
      if (!ev.ctrlKey && !ev.metaKey) return;
      ev.preventDefault();
      zoomer(zoomRef.current * (ev.deltaY < 0 ? PAS_ZOOM : 1 / PAS_ZOOM), ev.clientX, ev.clientY);
    };
    z.addEventListener('wheel', molette, { passive: false });
    return () => z.removeEventListener('wheel', molette);
  }, [zoomer]);

  /** « Ajuster » : tout le graphe visible (sans dépasser 100 %). */
  function ajuster() {
    const z = zone.current;
    const g = graphe.current;
    if (!z || !g) return;
    const groupes = [...g.querySelectorAll<HTMLElement>('[data-ligne] > div')];
    const largeurContenu = Math.max(212, ...groupes.map((x) => x.offsetWidth)) + 2 * MARGE_X;
    const hauteurDispo = Math.max(320, Math.min(940, window.innerHeight - z.getBoundingClientRect().top - 16));
    const z2 = Math.min(1, z.clientWidth / largeurContenu, hauteurDispo / Math.max(1, g.offsetHeight));
    ancre.current = null;
    setZoom(Math.round(Math.max(ZOOM_MIN, z2) * 100) / 100);
    requestAnimationFrame(() => {
      z.scrollLeft = Math.max(0, (z.scrollWidth - z.clientWidth) / 2);
      z.scrollTop = 0;
    });
  }

  // Fonctions stables (seulement des refs) : les écouteurs du document les gardent d'un rendu à l'autre.
  const [vue] = useState<DeplacementVue>(() => ({
    saisir: (x, y) => {
      const z = zone.current;
      if (!z) return;
      deplacementVue.current = { x, y, sl: z.scrollLeft, st: z.scrollTop };
      setVueSaisie(true);
    },
    deplacer: (x, y) => {
      const v = deplacementVue.current;
      if (!v || !zone.current) return false;
      zone.current.scrollLeft = v.sl - (x - v.x);
      zone.current.scrollTop = v.st - (y - v.y);
      return true;
    },
    lacher: () => {
      if (!deplacementVue.current) return false;
      deplacementVue.current = null;
      setVueSaisie(false);
      return true;
    },
    annuler: () => {
      deplacementVue.current = null;
      setVueSaisie(false);
    },
  }));

  return { zoom, zoomer, ajuster, vueSaisie, vue };
}

/** Étape choisie depuis le panneau (ou le clavier) hors du champ : on la recentre dans la zone. */
export function useRecentrerSelection(
  zone: RefObject<HTMLDivElement | null>,
  graphe: RefObject<HTMLDivElement | null>,
  selection: string | null,
) {
  useEffect(() => {
    const z = zone.current;
    const el = selection ? graphe.current?.querySelector<HTMLElement>(`[data-carte="${selection}"]`) : null;
    if (!z || !el || typeof z.scrollTo !== 'function') return;
    const zr = z.getBoundingClientRect();
    const r = el.getBoundingClientRect();
    if (r.left >= zr.left && r.right <= zr.right && r.top >= zr.top && r.bottom <= zr.bottom) return;
    z.scrollTo({
      left: z.scrollLeft + (r.left + r.width / 2) - (zr.left + zr.width / 2),
      top: z.scrollTop + (r.top + r.height / 2) - (zr.top + Math.min(zr.height, z.clientHeight) / 2),
      behavior: 'smooth',
    });
  }, [selection]);
}
