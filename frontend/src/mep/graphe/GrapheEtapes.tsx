import { Maximize, ZoomIn, ZoomOut } from 'lucide-react';
import { createPortal } from 'react-dom';
import { Fragment, useCallback, useEffect, useId, useLayoutEffect, useMemo, useRef, useState, type KeyboardEvent, type PointerEvent as PointerEventReact } from 'react';
import {
  construireLignes, courbeTemporaire, groupesDeLiens, indexLignes, lienPossible, liensDuPlan, tracerJonctions,
  type Ancrage, type GroupeLiens, type Lien, type Point,
} from '../../graphe';
import type { Etape, Mep, TypePlan } from '../../types';
import { BoutonIcone } from '../../ui/Button';
import { cn } from '../../ui/cn';
import { CarteEtape, libelleCarte, type ModeGraphe } from './CarteEtape';

interface Props {
  mep: Mep;
  plan: TypePlan;
  /** Étapes du plan. */
  etapes: Etape[];
  mode: ModeGraphe;
  /** Édition : glisser-déposer, ronds de liens, blocs fantômes, sélection des liens. */
  modifiable?: boolean;
  descriptions: Map<string, string>;
  selection: string | null;
  onSelection: (id: string | null) => void;
  groupeSelection?: string | null;
  onGroupeSelection?: (cle: string | null) => void;
  /** Déplacement d'une fiche (ligne affichée, position dans la ligne). */
  onPlacer?: (id: string, ligne: number, position: number, auClavier?: boolean) => void;
  /** Lien particulier : `vers` attendra `de` (validité déjà vérifiée). */
  onLier?: (de: string, vers: string) => void;
  /** Bloc fantôme : ligne existante, ou null pour une nouvelle ligne en bas. */
  onAjouter?: (ligne: number | null) => void;
  onMessage?: (message: string) => void;
  estEstompee?: (e: Etape) => boolean;
}

/** Cible d'un glisser de fiche : ligne, position d'insertion, et repère bleu (repère du graphe non zoomé). */
interface Cible { ligne: number; index: number; x: number; haut: number; bas: number }

type Glisse =
  | { t: 'carte'; id: string; cible: Cible | null }
  | { t: 'lien'; de: string; cote: 'haut' | 'bas'; survol: string | null; possible: boolean };

interface Geometrie { ancrages: Record<string, Ancrage>; largeur: number; hauteur: number; zone: number }

const ZOOM_MIN = 0.3;
const ZOOM_MAX = 1.5;
const PAS_ZOOM = 1.2;
/** Marge horizontale du graphe (place des blocs fantômes « + Étape »). */
const MARGE_X = 150;

/** Position de la fiche glissée : suit exactement le pointeur (transformation GPU, aucune aimantation). */
const transformClone = (p: { x: number; y: number }, zoom: number) => `translate3d(${p.x}px, ${p.y}px, 0) scale(${zoom}) rotate(1.5deg)`;

const COULEUR = { neutre: 'var(--fg-subtle)', accent: 'var(--accent)', ok: 'var(--ok-solid)' } as const;

/**
 * Graphe des étapes par lignes (maquette jalon 4) : lignes empilées et centrées, liens et jonctions en SVG derrière
 * les fiches, barrière du point de non-retour. En édition : glisser-déposer, ronds pour tirer un lien, blocs fantômes.
 */
export function GrapheEtapes(props: Props) {
  const { mep, plan, etapes, mode, modifiable = false, descriptions, selection, onSelection, groupeSelection = null, onGroupeSelection, onAjouter, estEstompee } = props;
  const edition = mode === 'edition' && modifiable;
  const idSvg = useId().replace(/:/g, '');

  const lignes = useMemo(() => construireLignes(etapes), [etapes]);
  const ligneDe = useMemo(() => indexLignes(lignes), [lignes]);
  const liens = useMemo(() => liensDuPlan(etapes), [etapes]);
  const groupes = useMemo(() => groupesDeLiens(liens), [liens]);
  const parId = useMemo(() => new Map(etapes.map((e) => [e.id, e])), [etapes]);

  const zone = useRef<HTMLDivElement>(null);
  const graphe = useRef<HTMLDivElement>(null);
  const clone = useRef<HTMLDivElement>(null);
  const trace = useRef<SVGPathElement>(null);
  const [geo, setGeo] = useState<Geometrie>({ ancrages: {}, largeur: 0, hauteur: 0, zone: 0 });
  const [glisse, setGlisse] = useState<Glisse | null>(null);
  // Copie synchrone de `glisse` : les événements du pointeur peuvent se suivre avant le rendu suivant.
  const glisseRef = useRef<Glisse | null>(null);
  const majGlisse = (g: Glisse | null) => { glisseRef.current = g; setGlisse(g); };

  // Valeurs courantes pour les écouteurs du document.
  const etat = useRef({ props, lignes, ligneDe, liens, edition });
  etat.current = { props, lignes, ligneDe, liens, edition };
  const appui = useRef<{ id: string; x: number; y: number; dx: number; dy: number } | null>(null);
  const positionClone = useRef({ x: 0, y: 0 });

  // ---------- Zoom et déplacement de la vue ----------
  const [zoom, setZoom] = useState(1);
  const zoomRef = useRef(1);
  zoomRef.current = zoom;
  /** Point du graphe (repère non zoomé) à garder sous le pointeur après un changement de zoom. */
  const ancre = useRef<{ px: number; py: number; cx: number; cy: number } | null>(null);
  const deplacementVue = useRef<{ x: number; y: number; sl: number; st: number } | null>(null);
  const [vueSaisie, setVueSaisie] = useState(false);

  const zoomer = useCallback((cible: number, cx?: number, cy?: number) => {
    const z = zone.current;
    const z2 = Math.round(Math.min(ZOOM_MAX, Math.max(ZOOM_MIN, cible)) * 100) / 100;
    if (!z || z2 === zoomRef.current) return;
    const r = z.getBoundingClientRect();
    const x = cx === undefined ? r.width / 2 : cx - r.left;
    const y = cy === undefined ? Math.min(r.height, z.clientHeight) / 2 : cy - r.top;
    ancre.current = { px: (z.scrollLeft + x) / zoomRef.current, py: (z.scrollTop + y) / zoomRef.current, cx: x, cy: y };
    setZoom(z2);
  }, []);

  // ---------- Mesure des fiches (les liens sont dessinés d'après leur position réelle, dans le repère non zoomé) ----------
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
  }, []);
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
    const t = window.setTimeout(() => { ancre.current = null; }, 0);
    return () => window.clearTimeout(t);
  }, [zoom, geo.largeur, geo.hauteur]);
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
    requestAnimationFrame(() => { z.scrollLeft = Math.max(0, (z.scrollWidth - z.clientWidth) / 2); z.scrollTop = 0; });
  }

  // Étape choisie depuis le panneau (ou le clavier) hors du champ : on la recentre.
  useEffect(() => {
    const z = zone.current;
    const el = selection ? graphe.current?.querySelector<HTMLElement>(`[data-carte="${selection}"]`) : null;
    if (!z || !el || typeof z.scrollTo !== 'function') return;
    const zr = z.getBoundingClientRect();
    const r = el.getBoundingClientRect();
    if (r.left >= zr.left && r.right <= zr.right && r.top >= zr.top && r.bottom <= zr.bottom) return;
    z.scrollTo({ left: z.scrollLeft + (r.left + r.width / 2) - (zr.left + zr.width / 2), top: z.scrollTop + (r.top + r.height / 2) - (zr.top + Math.min(zr.height, z.clientHeight) / 2), behavior: 'smooth' });
  }, [selection]);

  // ---------- Glisser-déposer (fiches et liens) ----------
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
    const el = (typeof document.elementFromPoint === 'function' ? document.elementFromPoint(x, y) : null) ?? (cible instanceof Element ? cible : null);
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
        if (d < dist) { dist = d; meilleur = l; }
      }
      rang = dist < 60 ? meilleur : null;
    }
    if (!rang) return null;
    const autres = [...rang.querySelectorAll<HTMLElement>('[data-noeud]')].filter((n) => n.dataset.noeud !== id);
    let index = autres.findIndex((n) => { const r = n.getBoundingClientRect(); return x < r.left + r.width / 2; });
    if (index < 0) index = autres.length;
    const z = zoomRef.current;
    const base = gr.getBoundingClientRect();
    const groupe = (rang.firstElementChild as HTMLElement | null)?.getBoundingClientRect() ?? rang.getBoundingClientRect();
    const demiEcart = 11 * z;
    const repere = autres[index]?.getBoundingClientRect().left;
    const dernier = autres[autres.length - 1]?.getBoundingClientRect().right;
    const fantome = rang.querySelector('button')?.getBoundingClientRect().left;
    const xr = repere !== undefined ? repere - demiEcart : dernier !== undefined ? dernier + demiEcart : (fantome ?? groupe.left + groupe.width / 2) - demiEcart;
    return { ligne: Number(rang.dataset.ligne), index, x: (xr - base.left) / z, haut: (groupe.top - base.top) / z, bas: (groupe.bottom - base.top) / z };
  };

  useEffect(() => {
    function deplacer(ev: PointerEvent) {
      const v = deplacementVue.current;
      if (v && zone.current) {
        zone.current.scrollLeft = v.sl - (ev.clientX - v.x);
        zone.current.scrollTop = v.st - (ev.clientY - v.y);
        return;
      }
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
        if (A && trace.current) trace.current.setAttribute('d', g.cote === 'bas' ? courbeTemporaire(A, B) : courbeTemporaire(B, A));
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
      if (deplacementVue.current) { deplacementVue.current = null; setVueSaisie(false); return; }
      const { props: p, lignes: ls, ligneDe: lignesDe, liens: liensCourants } = etat.current;
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
        if (verdict === 'remonte') p.onMessage?.('Un lien descend toujours : placez d’abord l’étape qui attend sur une ligne plus basse.');
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
    function annuler() { appui.current = null; deplacementVue.current = null; setVueSaisie(false); majGlisse(null); }

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
      if (cible.closest('button, [role="button"], input, select, textarea, a, [data-controles]') || !zone.current) return;
      ev.preventDefault();
      deplacementVue.current = { x: ev.clientX, y: ev.clientY, sl: zone.current.scrollLeft, st: zone.current.scrollTop };
      setVueSaisie(true);
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
    appui.current = { id, x: ev.clientX, y: ev.clientY, dx: carte ? ev.clientX - carte.left : 0, dy: carte ? ev.clientY - carte.top : 0 };
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
      onGroupeSelection?.(null);
      onSelection(id);
      return;
    }
    // Équivalent clavier du glisser-déposer : Alt + flèches.
    if (!edition || !ev.altKey || !props.onPlacer) return;
    const ligne = ligneDe.get(id);
    if (ligne === undefined) return;
    const index = lignes[ligne].findIndex((e) => e.id === id);
    const seule = lignes[ligne].length === 1;
    let cible: [number, number] | null = null;
    if (ev.key === 'ArrowLeft' && index > 0) cible = [ligne, index - 1];
    else if (ev.key === 'ArrowRight' && index < lignes[ligne].length - 1) cible = [ligne, index + 1];
    else if (ev.key === 'ArrowUp' && ligne > 0) cible = [ligne - 1, Math.min(index, lignes[ligne - 1].length)];
    else if (ev.key === 'ArrowDown' && !(seule && ligne === lignes.length - 1)) cible = [ligne + 1, ligne + 1 < lignes.length ? Math.min(index, lignes[ligne + 1].length) : 0];
    if (!ev.key.startsWith('Arrow')) return;
    ev.preventDefault();
    if (!cible) return;
    focusApres.current = id;
    props.onPlacer(id, cible[0], cible[1], true);
  }

  // ---------- Liens ----------
  const traces = useMemo(() => tracerJonctions(groupes, geo.ancrages), [groupes, geo.ancrages]);
  const groupeParCle = useMemo(() => new Map(groupes.map((g) => [g.cle, g])), [groupes]);
  const code = (id: string) => parId.get(id)?.code ?? id;
  const libelleGroupe = (g: GroupeLiens) => `${g.sources.length * g.cibles.length > 1 ? 'Jonction' : 'Lien'} ${g.sources.map(code).join(', ')} vers ${g.cibles.map(code).join(', ')}`;

  const glisseCarte = glisse?.t === 'carte' ? glisse : null;
  const glisseLien = glisse?.t === 'lien' ? glisse : null;

  const rendreLigne = (ids: Etape[]) => {
    const cartes = ids.map((e) => {
      const source = glisseCarte?.id === e.id || glisseLien?.de === e.id;
      const cible = glisseLien && glisseLien.survol === e.id ? (glisseLien.possible ? 'ok' : 'ko') : null;
      return (
        <div key={e.id} data-noeud={e.id} className={cn('group pointer-events-auto relative', edition && 'touch-none', source && glisseCarte && 'opacity-30')}>
          {edition && <span data-port="haut" title="Tirer vers une étape au-dessus" className="absolute -top-[7px] left-1/2 z-[2] -ml-[7px] size-3.5 cursor-crosshair rounded-full border-2 border-accent bg-surface opacity-55 transition-[opacity,transform] duration-150 group-hover:scale-110 group-hover:opacity-100 hover:bg-accent" />}
          <div
            data-carte={e.id}
            role="button"
            tabIndex={0}
            aria-pressed={selection === e.id}
            aria-label={libelleCarte(mep, e, mode)}
            aria-keyshortcuts={edition ? 'Alt+ArrowUp Alt+ArrowDown Alt+ArrowLeft Alt+ArrowRight' : undefined}
            onKeyDown={(ev) => clavierCarte(ev, e.id)}
            // La souris passe par le pointeur (appuyer / relâcher) ; un clic sans pointeur (detail 0) vient d'un lecteur d'écran.
            onClick={(ev) => { if (ev.detail === 0) { onGroupeSelection?.(null); onSelection(e.id); } }}
            className={cn('rounded-lg', edition ? 'cursor-grab' : 'cursor-pointer')}
          >
            <CarteEtape mep={mep} etape={e} mode={mode} descriptions={descriptions} selectionnee={selection === e.id} estompee={estEstompee?.(e)} cible={cible} />
          </div>
          {edition && <span data-port="bas" title="Tirer vers une étape en dessous" className="absolute -bottom-[7px] left-1/2 z-[2] -ml-[7px] size-3.5 cursor-crosshair rounded-full border-2 border-accent bg-surface opacity-55 transition-[opacity,transform] duration-150 group-hover:scale-110 group-hover:opacity-100 hover:bg-accent" />}
        </div>
      );
    });
    return cartes;
  };

  const sansFin = lignes.length;

  return (
    <div className="relative min-w-0">
    <div
      ref={zone}
      onPointerDown={appuyer}
      className={cn(
        'max-h-[940px] overflow-auto rounded-xl bg-surface shadow-sm',
        plan === 'RetourArriere' ? 'border-2 border-rollback-solid' : 'border border-line',
        vueSaisie ? 'cursor-grabbing select-none' : 'cursor-grab',
      )}
    >
      <div className="relative" style={{ width: geo.largeur ? geo.largeur * zoom : undefined, height: geo.hauteur ? geo.hauteur * zoom : undefined }}>
      <div
        ref={graphe}
        className="absolute top-0 left-0 flex flex-col gap-[46px] px-[150px] pt-7 pb-14 select-none"
        style={{ transform: zoom !== 1 ? `scale(${zoom})` : undefined, transformOrigin: '0 0', minWidth: geo.zone ? geo.zone / zoom : undefined }}
      >
        <svg className="pointer-events-none absolute top-0 left-0 z-0 overflow-visible" width={geo.largeur} height={geo.hauteur}>
          <defs>
            {(Object.keys(COULEUR) as (keyof typeof COULEUR)[]).map((k) => (
              <marker key={k} id={`${idSvg}-${k}`} viewBox="0 0 10 10" refX="8" refY="5" markerWidth="6" markerHeight="6" orient="auto">
                <path d="M0,0 L10,5 L0,10 z" style={{ fill: COULEUR[k] }} />
              </marker>
            ))}
          </defs>
          {traces.map((t) => {
            const g = groupeParCle.get(t.cle)!;
            const sel = groupeSelection === t.cle;
            const fait = mode === 'execution' && g.sources.every((s) => parId.get(s)?.etat === 'Reussie');
            const ton = sel ? 'accent' : fait ? 'ok' : 'neutre';
            const style = { stroke: COULEUR[ton] };
            const largeur = sel ? 3 : 1.8;
            const opacite = sel || fait ? 1 : 0.75;
            return (
              <g key={t.cle}>
                <g aria-hidden>
                  {t.traits && <path d={t.traits} style={style} strokeWidth={largeur} fill="none" opacity={opacite} strokeLinecap="round" />}
                  {t.fleches.map((d, i) => <path key={i} d={d} style={style} strokeWidth={largeur} fill="none" opacity={opacite} markerEnd={`url(#${idSvg}-${ton})`} />)}
                  {t.points.map((p, i) => <circle key={i} cx={p.x} cy={p.y} r={3} style={{ fill: COULEUR[ton] }} />)}
                </g>
                {edition && (
                  <path
                    d={`${t.traits ?? ''} ${t.fleches.join(' ')}`}
                    role="button"
                    tabIndex={0}
                    aria-label={libelleGroupe(g)}
                    aria-pressed={sel}
                    stroke="transparent"
                    strokeWidth={16}
                    fill="none"
                    className="cursor-pointer outline-none [pointer-events:stroke] focus-visible:stroke-accent/25"
                    onClick={() => { onSelection(null); onGroupeSelection?.(t.cle); }}
                    onKeyDown={(ev) => { if (ev.key === 'Enter' || ev.key === ' ') { ev.preventDefault(); onSelection(null); onGroupeSelection?.(t.cle); } }}
                  >
                    <title>{libelleGroupe(g)}</title>
                  </path>
                )}
              </g>
            );
          })}
          {glisseLien && <path ref={trace} style={{ stroke: COULEUR.accent }} strokeWidth={2.4} strokeDasharray="6 5" fill="none" markerEnd={`url(#${idSvg}-accent)`} />}
        </svg>
        {glisseCarte?.cible && (
          <div
            aria-hidden
            className="pointer-events-none absolute z-[3] w-2 rounded bg-accent shadow-[0_0_0_3px_var(--accent-soft)]"
            style={{ left: glisseCarte.cible.x - 4, top: glisseCarte.cible.haut, height: Math.max(40, glisseCarte.cible.bas - glisseCarte.cible.haut) }}
          />
        )}

        {lignes.map((ids, r) => {
          const pnr = ids.filter((e) => e.pointDeNonRetour);
          return (
            <Fragment key={r}>
              <div data-ligne={r} className="pointer-events-none relative z-[1] flex justify-center">
                <div className="relative flex min-h-[60px] items-stretch gap-[22px] rounded-[10px]">
                  {rendreLigne(ids)}
                  {edition && onAjouter && (
                    <button
                      type="button"
                      onClick={() => onAjouter(r)}
                      aria-label={`Ajouter une étape sur la ligne ${r + 1}`}
                      className="pointer-events-auto absolute top-0 bottom-0 left-[calc(100%+22px)] grid w-28 place-content-center gap-0.5 rounded-lg border-[1.5px] border-dashed border-line-strong text-[13px] text-fg-subtle transition-colors hover:border-accent hover:bg-accent-soft hover:text-accent-text"
                    >
                      <span className="text-xl leading-none text-accent-text" aria-hidden>+</span>Étape
                    </button>
                  )}
                </div>
              </div>
              {pnr.length > 0 && r < lignes.length - 1 && (
                <div role="note" className="pointer-events-none relative z-[1] -mx-[150px] -my-6 grid items-center">
                  <div className="col-start-1 row-start-1 border-t-[3px] border-dashed border-rollback-solid" />
                  <div className="col-start-1 row-start-1 justify-self-center rounded-full border-[1.5px] border-rollback-solid bg-surface px-3 py-0.5 text-xs font-bold whitespace-nowrap text-rollback-solid">
                    ⛔ {pnr.map((e) => e.code).join(', ')} : après ce point, plus de retour arrière
                  </div>
                </div>
              )}
            </Fragment>
          );
        })}

        {edition && onAjouter && (
          <div data-ligne={sansFin} className="pointer-events-none relative z-[1] flex justify-center">
            <div className="relative flex items-stretch gap-[22px] rounded-[10px]">
              <button
                type="button"
                onClick={() => onAjouter(null)}
                className="pointer-events-auto grid h-[58px] w-[212px] place-content-center gap-0.5 rounded-lg border-[1.5px] border-dashed border-line-strong text-[13px] text-fg-subtle transition-colors hover:border-accent hover:bg-accent-soft hover:text-accent-text"
              >
                <span className="text-xl leading-none text-accent-text" aria-hidden>+</span>Nouvelle ligne
              </button>
            </div>
          </div>
        )}
        {lignes.length === 0 && !edition && <p className="py-10 text-center text-sm text-fg-subtle">Aucune étape dans ce plan.</p>}
      </div>
      </div>
    </div>

      <div data-controles role="toolbar" aria-label="Vue du graphe" className="absolute right-3 bottom-3 z-20 flex items-center gap-0.5 rounded-lg border border-line bg-surface/95 p-0.5 shadow-md backdrop-blur">
        <BoutonIcone label="Dézoomer" taille="sm" disabled={zoom <= ZOOM_MIN} onClick={() => zoomer(zoom / PAS_ZOOM)}><ZoomOut className="size-4" aria-hidden /></BoutonIcone>
        <button type="button" onClick={() => zoomer(1)} title="Revenir à 100 %" aria-label={`Zoom ${Math.round(zoom * 100)} % : revenir à 100 %`} className="h-8 min-w-12 rounded-md px-1.5 text-xs font-semibold text-fg-muted tnum hover:bg-surface-2 hover:text-fg">
          {Math.round(zoom * 100)} %
        </button>
        <BoutonIcone label="Zoomer" taille="sm" disabled={zoom >= ZOOM_MAX} onClick={() => zoomer(zoom * PAS_ZOOM)}><ZoomIn className="size-4" aria-hidden /></BoutonIcone>
        <BoutonIcone label="Ajuster : tout voir" taille="sm" onClick={ajuster}><Maximize className="size-4" aria-hidden /></BoutonIcone>
      </div>
      <p className="sr-only">Glissez le fond du graphe pour vous déplacer ; Ctrl + molette pour zoomer.</p>

      {glisseCarte && parId.get(glisseCarte.id) && createPortal(
        <div
          ref={clone}
          aria-hidden
          className="pointer-events-none fixed z-50 rounded-lg opacity-90 shadow-lg"
          style={{ left: 0, top: 0, transform: transformClone(positionClone.current, zoom), transformOrigin: '0 0', willChange: 'transform' }}
        >
          <CarteEtape mep={mep} etape={parId.get(glisseCarte.id)!} mode={mode} descriptions={descriptions} />
        </div>,
        document.body,
      )}
    </div>
  );
}

/** Liens d'un groupe sélectionné, à partir de sa clé (null si le groupe n'existe plus). */
export function groupeDepuisCle(etapes: Etape[], cle: string | null): GroupeLiens | null {
  if (!cle) return null;
  return groupesDeLiens(liensDuPlan(etapes)).find((g) => g.cle === cle) ?? null;
}

export type { GroupeLiens, Lien };
