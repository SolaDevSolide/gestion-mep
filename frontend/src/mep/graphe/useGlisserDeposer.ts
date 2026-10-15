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
import { ancrageFiche, repereLiens } from './useMesureCartes';
import type { DeplacementVue } from './useZoomVue';

/**
 * Cible d'un glisser de fiche (repère du graphe non zoomé pour le repère bleu) :
 * - dans une ligne : position d'insertion `index` parmi les autres fiches, repère vertical en `x` entre `haut` et `bas` ;
 * - entre deux lignes (`nouvelle`) : la fiche ouvrira une ligne à elle juste au-dessus de `ligne`, repère horizontal
 *   centré en `x`, à la hauteur `y`, large de `largeur`.
 */
export type Cible =
  | { nouvelle: false; ligne: number; index: number; x: number; haut: number; bas: number }
  | { nouvelle: true; ligne: number; index: 0; x: number; y: number; largeur: number };

/** Bande verticale d'une ligne affichée, à l'écran. */
export interface Rang {
  ligne: number;
  haut: number;
  bas: number;
}

/**
 * Ligne visée par le pointeur (ordonnée `y` à l'écran) parmi les lignes affichées, de haut en bas. Sur une ligne (ou à
 * `tolerance` px de ses bords) : cette ligne. Dans l'espace entre deux lignes, ou juste au-dessus de la première : une
 * nouvelle ligne insérée là. Sous la dernière, jusqu'à `marge` px : la dernière. Plus loin : rien.
 */
export function rangSous(
  y: number,
  rangs: Rang[],
  tolerance = 8,
  marge = 60,
): { ligne: number; nouvelle: boolean } | null {
  for (let i = 0; i < rangs.length; i++) {
    const r = rangs[i];
    if (y >= r.haut - tolerance && y <= r.bas + tolerance) return { ligne: r.ligne, nouvelle: false };
    // Au-dessus de cette ligne (et sous la précédente) : insertion d'une ligne juste au-dessus d'elle.
    if (y < r.haut) return i === 0 && y < r.haut - marge ? null : { ligne: r.ligne, nouvelle: true };
  }
  const dernier = rangs.at(-1);
  return dernier && y <= dernier.bas + marge ? { ligne: dernier.ligne, nouvelle: false } : null;
}

/** Bande sensible au bord de la zone visible, et vitesse maximale du défilement automatique (px par image). */
const BANDE_DEFILEMENT = 48;
const VITESSE_DEFILEMENT = 18;

/**
 * Défilement automatique pendant un glisser : vitesse (px par image) quand le pointeur approche d'un bord de la zone
 * visible `vue`, d'autant plus rapide qu'il en est près (ou au-delà) ; 0 ailleurs, ou si la zone est trop petite.
 */
export function vitesseDefilement(
  p: { x: number; y: number },
  vue: { left: number; top: number; right: number; bottom: number },
): { dx: number; dy: number } {
  const axe = (v: number, min: number, max: number) => {
    if (max - min < 3 * BANDE_DEFILEMENT) return 0;
    const avant = min + BANDE_DEFILEMENT - v;
    const apres = v - (max - BANDE_DEFILEMENT);
    if (avant > 0) return -Math.round(VITESSE_DEFILEMENT * Math.min(1, avant / BANDE_DEFILEMENT));
    if (apres > 0) return Math.round(VITESSE_DEFILEMENT * Math.min(1, apres / BANDE_DEFILEMENT));
    return 0;
  };
  return { dx: axe(p.x, vue.left, vue.right), dy: axe(p.y, vue.top, vue.bottom) };
}

/** Partie de la zone du graphe visible à l'écran (la zone peut dépasser de la fenêtre). */
function zoneVisible(zone: HTMLElement) {
  const r = zone.getBoundingClientRect();
  return {
    left: Math.max(r.left, 0),
    top: Math.max(r.top, 0),
    right: Math.min(r.left + zone.clientWidth, window.innerWidth),
    bottom: Math.min(r.top + zone.clientHeight, window.innerHeight),
  };
}

const memeCible = (a: Cible | null, b: Cible | null) =>
  a === b || (!!a && !!b && a.nouvelle === b.nouvelle && a.ligne === b.ligne && a.index === b.index);

export type Glisse =
  | { t: 'carte'; id: string; cible: Cible | null }
  | { t: 'lien'; de: string; cote: 'haut' | 'bas'; survol: string | null; possible: boolean };

/** Position de la fiche glissée : suit exactement le pointeur (transformation GPU, aucune aimantation). */
export const transformClone = (p: { x: number; y: number }, zoom: number) =>
  `translate3d(${p.x}px, ${p.y}px, 0) scale(${zoom}) rotate(1.5deg)`;

/** Déplacement demandé : au clavier (la fiche garde le focus), ou sur une ligne à elle insérée au-dessus de `ligne`. */
export interface OptionsPlacer {
  auClavier?: boolean;
  nouvelleLigne?: boolean;
}

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
  onPlacer?: (id: string, ligne: number, position: number, options?: OptionsPlacer) => void;
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
    const a = ancrageFiche(el.getBoundingClientRect(), repereLiens(g), zoomRef.current);
    return { x: a.x, y: a[cote] };
  };
  const noeudSous = (x: number, y: number, cible: EventTarget | null): string | null => {
    const el =
      (typeof document.elementFromPoint === 'function' ? document.elementFromPoint(x, y) : null) ??
      (cible instanceof Element ? cible : null);
    const n = el?.closest<HTMLElement>('[data-noeud]');
    return n && graphe.current?.contains(n) ? n.dataset.noeud! : null;
  };

  /**
   * Ligne visée (voir rangSous), position d'insertion parmi les autres fiches et place du repère (repère du graphe non
   * zoomé). Hors de la partie visible de la zone (panneau d'une étape, haut de page…) : aucune cible. Rien ne bouge
   * dans le graphe pendant le glisser : seul le repère indique la cible.
   */
  const cibleSous = (x: number, y: number, id: string): Cible | null => {
    const gr = graphe.current;
    const zo = options.zone.current;
    if (!gr || !zo) return null;
    const vue = zoneVisible(zo);
    if (x < vue.left || x > vue.right || y < vue.top || y > vue.bottom) return null;
    const z = zoomRef.current;
    const bandes = [...gr.querySelectorAll<HTMLElement>('[data-ligne]')].map((el) => {
      const r = (el.firstElementChild ?? el).getBoundingClientRect();
      return { el, r, ligne: Number(el.dataset.ligne), haut: r.top, bas: r.bottom };
    });
    const vise = rangSous(y, bandes, 8 * z);
    if (!vise) return null;
    const base = gr.getBoundingClientRect();
    const i = bandes.findIndex((b) => b.ligne === vise.ligne);
    const rang = bandes[i];
    if (vise.nouvelle) {
      const dessus = bandes[i - 1];
      const yr = dessus ? (dessus.bas + rang.haut) / 2 : rang.haut - 14 * z;
      const gauche = Math.min(rang.r.left, dessus?.r.left ?? Infinity);
      const droite = Math.max(rang.r.right, dessus?.r.right ?? -Infinity);
      return {
        nouvelle: true,
        ligne: vise.ligne,
        index: 0,
        x: ((gauche + droite) / 2 - base.left) / z,
        y: (yr - base.top) / z,
        largeur: Math.max(212 * z, droite - gauche) / z,
      };
    }
    const autres = [...rang.el.querySelectorAll<HTMLElement>('[data-noeud]')].filter((n) => n.dataset.noeud !== id);
    let index = autres.findIndex((n) => {
      const r = n.getBoundingClientRect();
      return x < r.left + r.width / 2;
    });
    if (index < 0) index = autres.length;
    const demiEcart = 11 * z;
    const repere = autres[index]?.getBoundingClientRect().left;
    const dernier = autres[autres.length - 1]?.getBoundingClientRect().right;
    const fantome = rang.el.querySelector('button')?.getBoundingClientRect().left;
    const xr =
      repere !== undefined
        ? repere - demiEcart
        : dernier !== undefined
          ? dernier + demiEcart
          : (fantome ?? rang.r.left + rang.r.width / 2) - demiEcart;
    return {
      nouvelle: false,
      ligne: vise.ligne,
      index,
      x: (xr - base.left) / z,
      haut: (rang.haut - base.top) / z,
      bas: (rang.bas - base.top) / z,
    };
  };

  // Défilement automatique de la zone (puis de la page) quand une fiche glissée approche d'un bord.
  const pointeur = useRef<{ x: number; y: number } | null>(null);
  const defilement = useRef<number | null>(null);
  const majCible = (x: number, y: number) => {
    const g = glisseRef.current;
    if (g?.t !== 'carte') return;
    const cible = cibleSous(x, y, g.id);
    if (!memeCible(cible, g.cible)) majGlisse({ ...g, cible });
  };
  const defiler = () => {
    defilement.current = null;
    const z = options.zone.current;
    const p = pointeur.current;
    if (!z || !p || glisseRef.current?.t !== 'carte') return;
    const { dx, dy } = vitesseDefilement(p, zoneVisible(z));
    if (dx === 0 && dy === 0) return;
    const [gauche, haut] = [z.scrollLeft, z.scrollTop];
    z.scrollLeft += dx;
    z.scrollTop += dy;
    // La zone est au bout (ou ne défile pas) : c'est la page qui défile.
    const resteY = dy - (z.scrollTop - haut);
    const avantPage = window.scrollY;
    if (Math.abs(resteY) >= 1) window.scrollBy(0, resteY);
    if (z.scrollLeft !== gauche || z.scrollTop !== haut || window.scrollY !== avantPage) majCible(p.x, p.y);
    defilement.current = requestAnimationFrame(defiler);
  };
  const arreterDefilement = () => {
    pointeur.current = null;
    if (defilement.current !== null) cancelAnimationFrame(defilement.current);
    defilement.current = null;
  };
  // Versions courantes pour les écouteurs du document (posés une seule fois).
  const fonctions = useRef({ cibleSous, majCible, defiler, arreterDefilement });
  fonctions.current = { cibleSous, majCible, defiler, arreterDefilement };

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
        const b = repereLiens(gr);
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
      fonctions.current.majCible(ev.clientX, ev.clientY);
      pointeur.current = { x: ev.clientX, y: ev.clientY };
      if (defilement.current === null) defilement.current = requestAnimationFrame(() => fonctions.current.defiler());
    }

    function lacher(ev: PointerEvent) {
      if (etat.current.vue.lacher()) return;
      const { lignes: ls, ligneDe: lignesDe, liens: liensCourants, ...p } = etat.current;
      const g = glisseRef.current;
      const a = appui.current;
      appui.current = null;
      fonctions.current.arreterDefilement();
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
        // Cible au point de lâcher (la page a pu défiler depuis le dernier mouvement) ; hors de la zone : rien.
        const cible = fonctions.current.cibleSous(ev.clientX, ev.clientY, g.id);
        if (!cible || deplacementSansEffet(g.id, cible, ls, lignesDe)) return;
        p.onPlacer?.(g.id, cible.ligne, cible.index, cible.nouvelle ? { nouvelleLigne: true } : undefined);
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
      fonctions.current.arreterDefilement();
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
      fonctions.current.arreterDefilement();
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
    // Équivalent clavier du glisser-déposer : Alt + flèches ; avec Maj, ↑ / ↓ mettent la fiche sur une ligne à elle.
    if (!edition || !ev.altKey || !options.onPlacer || !ev.key.startsWith('Arrow')) return;
    ev.preventDefault();
    const cible = cibleClavier(id, ev.key, ev.shiftKey, lignes, ligneDe);
    if (!cible) return;
    focusApres.current = id;
    options.onPlacer(id, cible.ligne, cible.index, { auClavier: true, nouvelleLigne: cible.nouvelle });
  }

  return { glisse, clone, trace, positionClone, appuyer, clavierCarte };
}

/**
 * Déplacement qui ne changerait rien : même place dans la même ligne, seule sur la dernière ligne et lâchée sur
 * « Nouvelle ligne », ou seule sur sa ligne et lâchée sur une nouvelle ligne juste au-dessus ou juste en dessous.
 */
export function deplacementSansEffet(
  id: string,
  cible: { ligne: number; index: number; nouvelle: boolean },
  lignes: { id: string }[][],
  ligneDe: Map<string, number>,
): boolean {
  const avant = ligneDe.get(id);
  if (avant === undefined) return false;
  const seule = lignes[avant].length === 1;
  if (cible.nouvelle) return seule && (cible.ligne === avant || cible.ligne === avant + 1);
  if (cible.ligne === avant) return cible.index === lignes[avant].findIndex((e) => e.id === id);
  return seule && avant === lignes.length - 1 && cible.ligne === lignes.length;
}

/**
 * Cible d'un déplacement au clavier. Alt + ← / → : dans la ligne ; Alt + ↑ / ↓ : sur la ligne au-dessus / en dessous
 * (sous la dernière : nouvelle ligne en bas). Avec Maj, ↑ / ↓ : sur une ligne à elle, juste au-dessus / en dessous de
 * sa ligne (si elle y est déjà seule : de l'autre côté de la ligne voisine). Null si le déplacement est impossible.
 */
export function cibleClavier(
  id: string,
  touche: string,
  maj: boolean,
  lignes: { id: string }[][],
  ligneDe: Map<string, number>,
): { ligne: number; index: number; nouvelle: boolean } | null {
  const ligne = ligneDe.get(id);
  if (ligne === undefined) return null;
  const index = lignes[ligne].findIndex((e) => e.id === id);
  const seule = lignes[ligne].length === 1;
  const derniere = ligne === lignes.length - 1;
  if (maj && touche === 'ArrowUp') {
    if (!seule) return { ligne, index: 0, nouvelle: true };
    return ligne > 0 ? { ligne: ligne - 1, index: 0, nouvelle: true } : null;
  }
  if (maj && touche === 'ArrowDown') {
    if (!seule) return { ligne: ligne + 1, index: 0, nouvelle: true };
    return derniere ? null : { ligne: ligne + 2, index: 0, nouvelle: true };
  }
  if (touche === 'ArrowLeft') return index > 0 ? { ligne, index: index - 1, nouvelle: false } : null;
  if (touche === 'ArrowRight')
    return index < lignes[ligne].length - 1 ? { ligne, index: index + 1, nouvelle: false } : null;
  if (touche === 'ArrowUp')
    return ligne > 0 ? { ligne: ligne - 1, index: Math.min(index, lignes[ligne - 1].length), nouvelle: false } : null;
  if (touche === 'ArrowDown') {
    if (seule && derniere) return null;
    return { ligne: ligne + 1, index: derniere ? 0 : Math.min(index, lignes[ligne + 1].length), nouvelle: false };
  }
  return null;
}
