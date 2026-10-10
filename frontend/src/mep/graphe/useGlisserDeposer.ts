import {
  useEffect,
  useRef,
  useState,
  type KeyboardEvent,
  type MutableRefObject,
  type PointerEvent as PointerEventReact,
  type RefObject,
} from 'react';
import { courbeTemporaire, lienPossible, type Lien, type Point } from '../../graphe';
import type { Etape } from '../../types';
import type { DeplacementVue } from './useZoomVue';

/** Cible d'un glisser de fiche : ligne, position d'insertion, et repère bleu (repère du graphe non zoomé). */
export interface Cible {
  ligne: number;
  index: number;
  x: number;
  haut: number;
  bas: number;
}

export type Glisse =
  | { t: 'carte'; id: string; cible: Cible | null }
  | { t: 'lien'; de: string; cote: 'haut' | 'bas'; survol: string | null; possible: boolean };

/** Position de la fiche glissée : suit exactement le pointeur (transformation GPU, aucune aimantation). */
export const transformClone = (p: { x: number; y: number }, zoom: number) =>
  `translate3d(${p.x}px, ${p.y}px, 0) scale(${zoom}) rotate(1.5deg)`;

interface Options {
  zone: RefObject<HTMLDivElement | null>;
  graphe: RefObject<HTMLDivElement | null>;
  zoomRef: MutableRefObject<number>;
  /** Déplacement de la vue (fond du graphe), qui partage les écouteurs du pointeur. */
  vue: DeplacementVue;
  /** Édition : glisser-déposer des fiches, tirage des liens et Alt + flèches. */
  edition: boolean;
  etapes: Etape[];
  lignes: Etape[][];
  ligneDe: Map<string, number>;
  liens: Lien[];
  onSelection: (id: string | null) => void;
  onGroupeSelection?: (cle: string | null) => void;
  onPlacer?: (id: string, ligne: number, position: number, auClavier?: boolean) => void;
  onLier?: (de: string, vers: string) => void;
  onMessage?: (message: string) => void;
}

/**
 * Glisser-déposer du graphe : déplacement d'une fiche (clone qui suit le pointeur, repère d'insertion), tirage d'un
 * lien depuis un rond, sélection au relâcher, et équivalent clavier (Alt + flèches) avec refocalisation de la fiche.
 */
export function useGlisserDeposer(options: Options) {
  const { graphe, zoomRef, edition, etapes, lignes, ligneDe } = options;
  const clone = useRef<HTMLDivElement>(null);
  const trace = useRef<SVGPathElement>(null);
  const [glisse, setGlisse] = useState<Glisse | null>(null);
  // Copie synchrone de `glisse` : les événements du pointeur peuvent se suivre avant le rendu suivant.
  const glisseRef = useRef<Glisse | null>(null);
  const majGlisse = (g: Glisse | null) => {
    glisseRef.current = g;
    setGlisse(g);
  };

  // Valeurs courantes pour les écouteurs du document.
  const etat = useRef(options);
  etat.current = options;
  const appui = useRef<{ id: string; x: number; y: number; dx: number; dy: number } | null>(null);
  const positionClone = useRef({ x: 0, y: 0 });

  const point = (id: string, cote: 'haut' | 'bas'): Point | null => {
    const g = graphe.current;
    const el = g?.querySelector<HTMLElement>(`[data-carte="${id}"]`);
    if (!g || !el) return null;
    const z = zoomRef.current;
    const b = g.getBoundingClientRect();
    const r = el.getBoundingClientRect();
    return { x: (r.left + r.width / 2 - b.left) / z, y: ((cote === 'bas' ? r.bottom : r.top) - b.top) / z };
  };
  const noeudSous = (x: number, y: number, cible: EventTarget | null): string | null => {
    const el =
      (typeof document.elementFromPoint === 'function' ? document.elementFromPoint(x, y) : null) ??
      (cible instanceof Element ? cible : null);
    const n = el?.closest<HTMLElement>('[data-noeud]');
    return n && graphe.current?.contains(n) ? n.dataset.noeud! : null;
  };

  /**
   * Ligne survolée (ou la plus proche à moins de 60 px), position d'insertion parmi les autres fiches et place du repère
   * (repère du graphe non zoomé). Rien ne bouge dans le graphe pendant le glisser : seul le repère indique la cible.
   */
  const cibleSous = (x: number, y: number, id: string): Cible | null => {
    const gr = graphe.current;
    if (!gr) return null;
    const sous = typeof document.elementFromPoint === 'function' ? document.elementFromPoint(x, y) : null;
    let rang = sous?.closest<HTMLElement>('[data-ligne]') ?? null;
    if (!rang || !gr.contains(rang)) {
      let meilleur: HTMLElement | null = null;
      let dist = Infinity;
      for (const l of gr.querySelectorAll<HTMLElement>('[data-ligne]')) {
        const r = l.getBoundingClientRect();
        const d = Math.abs((r.top + r.bottom) / 2 - y);
        if (d < dist) {
          dist = d;
          meilleur = l;
        }
      }
      rang = dist < 60 ? meilleur : null;
    }
    if (!rang) return null;
    const autres = [...rang.querySelectorAll<HTMLElement>('[data-noeud]')].filter((n) => n.dataset.noeud !== id);
    let index = autres.findIndex((n) => {
      const r = n.getBoundingClientRect();
      return x < r.left + r.width / 2;
    });
    if (index < 0) index = autres.length;
    const z = zoomRef.current;
    const base = gr.getBoundingClientRect();
    const groupe =
      (rang.firstElementChild as HTMLElement | null)?.getBoundingClientRect() ?? rang.getBoundingClientRect();
    const demiEcart = 11 * z;
    const repere = autres[index]?.getBoundingClientRect().left;
    const dernier = autres[autres.length - 1]?.getBoundingClientRect().right;
    const fantome = rang.querySelector('button')?.getBoundingClientRect().left;
    const xr =
      repere !== undefined
        ? repere - demiEcart
        : dernier !== undefined
          ? dernier + demiEcart
          : (fantome ?? groupe.left + groupe.width / 2) - demiEcart;
    return {
      ligne: Number(rang.dataset.ligne),
      index,
      x: (xr - base.left) / z,
      haut: (groupe.top - base.top) / z,
      bas: (groupe.bottom - base.top) / z,
    };
  };

  useEffect(() => {
    function deplacer(ev: PointerEvent) {
      if (etat.current.vue.deplacer(ev.clientX, ev.clientY)) return;
      const { edition: ed, ligneDe: lignesDe, liens: ls } = etat.current;
      let g = glisseRef.current;
      const a = appui.current;
      if (!g && a && ed && Math.hypot(ev.clientX - a.x, ev.clientY - a.y) > 6) {
        positionClone.current = { x: ev.clientX - a.dx, y: ev.clientY - a.dy };
        g = { t: 'carte', id: a.id, cible: null };
        majGlisse(g);
      }
      if (!g) return;
      const gr = graphe.current;
      if (!gr) return;
      if (g.t === 'lien') {
        const b = gr.getBoundingClientRect();
        const A = point(g.de, g.cote);
        const B = { x: (ev.clientX - b.left) / zoomRef.current, y: (ev.clientY - b.top) / zoomRef.current };
        if (A && trace.current)
          trace.current.setAttribute('d', g.cote === 'bas' ? courbeTemporaire(A, B) : courbeTemporaire(B, A));
        const n = noeudSous(ev.clientX, ev.clientY, null);
        const survol = n && n !== g.de ? n : null;
        const [de, vers] = g.cote === 'bas' ? [g.de, survol] : [survol, g.de];
        const possible = !!survol && lienPossible(de!, vers!, lignesDe, ls) === 'ok';
        if (survol !== g.survol || possible !== g.possible) majGlisse({ ...g, survol, possible });
        return;
      }
      // Fiche : le clone suit la souris ; on cherche la ligne survolée et la position d'insertion.
      positionClone.current = { x: ev.clientX - (a?.dx ?? 0), y: ev.clientY - (a?.dy ?? 0) };
      if (clone.current) {
        clone.current.style.transform = transformClone(positionClone.current, zoomRef.current);
      }
      const cible = cibleSous(ev.clientX, ev.clientY, g.id);
      if (cible?.ligne !== g.cible?.ligne || cible?.index !== g.cible?.index) majGlisse({ ...g, cible });
    }

    function lacher(ev: PointerEvent) {
      if (etat.current.vue.lacher()) return;
      const { lignes: ls, ligneDe: lignesDe, liens: liensCourants, ...p } = etat.current;
      const g = glisseRef.current;
      const a = appui.current;
      appui.current = null;
      if (g?.t === 'lien') {
        majGlisse(null);
        const n = noeudSous(ev.clientX, ev.clientY, ev.target) ?? g.survol;
        if (!n || n === g.de) return;
        const [de, vers] = g.cote === 'bas' ? [g.de, n] : [n, g.de];
        const verdict = lienPossible(de, vers, lignesDe, liensCourants);
        const code = (id: string) => ls.flat().find((e) => e.id === id)?.code ?? id;
        if (verdict === 'remonte')
          p.onMessage?.('Un lien descend toujours : placez d’abord l’étape qui attend sur une ligne plus basse.');
        else if (verdict === 'existe') p.onMessage?.(`Le lien ${code(de)} → ${code(vers)} existe déjà.`);
        else p.onLier?.(de, vers);
        return;
      }
      if (g?.t === 'carte') {
        majGlisse(null);
        const cible = cibleSous(ev.clientX, ev.clientY, g.id) ?? g.cible;
        if (!cible) return;
        const ligneAvant = lignesDe.get(g.id);
        const indexAvant = ligneAvant === undefined ? -1 : ls[ligneAvant].findIndex((e) => e.id === g.id);
        const seuleEnBas = ligneAvant === ls.length - 1 && ls[ligneAvant].length === 1 && cible.ligne === ls.length;
        if ((cible.ligne === ligneAvant && cible.index === indexAvant) || seuleEnBas) return;
        p.onPlacer?.(g.id, cible.ligne, cible.index);
        return;
      }
      if (a) {
        const n = noeudSous(ev.clientX, ev.clientY, ev.target);
        if (n === a.id) {
          p.onGroupeSelection?.(null);
          p.onSelection(a.id);
        }
      }
    }
    function annuler() {
      appui.current = null;
      etat.current.vue.annuler();
      majGlisse(null);
    }

    document.addEventListener('pointermove', deplacer);
    document.addEventListener('pointerup', lacher);
    document.addEventListener('pointercancel', annuler);
    return () => {
      document.removeEventListener('pointermove', deplacer);
      document.removeEventListener('pointerup', lacher);
      document.removeEventListener('pointercancel', annuler);
    };
  }, []);

  function appuyer(ev: PointerEventReact<HTMLDivElement>) {
    if (ev.button !== 0) return;
    const cible = ev.target as Element;
    const n = cible.closest<HTMLElement>('[data-noeud]');
    if (!n) {
      // Fond du graphe (ni fiche, ni rond, ni lien, ni bouton) : on fait glisser la vue.
      if (
        cible.closest('button, [role="button"], input, select, textarea, a, [data-controles]') ||
        !options.zone.current
      )
        return;
      ev.preventDefault();
      options.vue.saisir(ev.clientX, ev.clientY);
      return;
    }
    const id = n.dataset.noeud!;
    const port = cible.closest<HTMLElement>('[data-port]');
    if (port && edition) {
      ev.preventDefault();
      const cote = port.dataset.port as 'haut' | 'bas';
      majGlisse({ t: 'lien', de: id, cote, survol: null, possible: false });
      return;
    }
    const carte = n.querySelector<HTMLElement>('[data-carte]')?.getBoundingClientRect();
    appui.current = {
      id,
      x: ev.clientX,
      y: ev.clientY,
      dx: carte ? ev.clientX - carte.left : 0,
      dy: carte ? ev.clientY - carte.top : 0,
    };
  }

  // Fiche à refocaliser après un déplacement au clavier (elle peut changer de ligne, donc de parent).
  const focusApres = useRef<string | null>(null);
  useEffect(() => {
    const id = focusApres.current;
    if (!id) return;
    focusApres.current = null;
    graphe.current?.querySelector<HTMLElement>(`[data-carte="${id}"]`)?.focus();
  }, [etapes]);

  function clavierCarte(ev: KeyboardEvent<HTMLDivElement>, id: string) {
    if (ev.key === 'Enter' || ev.key === ' ') {
      ev.preventDefault();
      options.onGroupeSelection?.(null);
      options.onSelection(id);
      return;
    }
    // Équivalent clavier du glisser-déposer : Alt + flèches.
    if (!edition || !ev.altKey || !options.onPlacer) return;
    const ligne = ligneDe.get(id);
    if (ligne === undefined) return;
    const index = lignes[ligne].findIndex((e) => e.id === id);
    const seule = lignes[ligne].length === 1;
    let cible: [number, number] | null = null;
    if (ev.key === 'ArrowLeft' && index > 0) cible = [ligne, index - 1];
    else if (ev.key === 'ArrowRight' && index < lignes[ligne].length - 1) cible = [ligne, index + 1];
    else if (ev.key === 'ArrowUp' && ligne > 0) cible = [ligne - 1, Math.min(index, lignes[ligne - 1].length)];
    else if (ev.key === 'ArrowDown' && !(seule && ligne === lignes.length - 1))
      cible = [ligne + 1, ligne + 1 < lignes.length ? Math.min(index, lignes[ligne + 1].length) : 0];
    if (!ev.key.startsWith('Arrow')) return;
    ev.preventDefault();
    if (!cible) return;
    focusApres.current = id;
    options.onPlacer(id, cible[0], cible[1], true);
  }

  return { glisse, clone, trace, positionClone, appuyer, clavierCarte };
}
