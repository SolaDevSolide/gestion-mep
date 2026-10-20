import { useReactFlow } from '@xyflow/react';
import {
  useCallback,
  useEffect,
  useLayoutEffect,
  useRef,
  useState,
  type Dispatch,
  type RefObject,
  type SetStateAction,
} from 'react';
import type { Etape } from '../../types';
import { mouvementReduit } from '../../ui/mouvement';
import { vueMontrant } from './mesuresGraphe';
import type { LienModele, ModeleGraphe } from './modeleGraphe';
import {
  adoucir,
  ancrageHaut,
  cheminArrondi,
  dansCadre,
  decoupe,
  DUREE_MOUVEMENT_MS,
  etatA,
  placeAncree,
  rampeApparition,
  rampeDisparition,
  revelation,
  routeSuivant,
  type Ancrage,
  type EtatNoeud,
  type LienMouvement,
  type PlaceDansCadre,
  type SensCadre,
} from './mouvementGraphe';
import { noeudsDepuis, ZOOM_MIN, type LienRf, type NoeudRf } from './noeudsRf';
import { decalageAncrage, GRILLE, positionsAbsolues, type Point, type Taille } from './rangement';
import { RAYON_COUDE } from './routage';
import type { Range } from './useRangement';

/** Glissement du contenu d'un cadre qui s'ouvre (il descend à sa place) ou se ferme (il remonte en s'effaçant). */
const GLISSE_CONTENU = 10;
/** Secours seulement (cas anormal) : un nœud monté que React Flow ne mesure jamais n'empêche pas le mouvement. */
export const SECOURS_MONTAGE_MS = 3000;

type Mesure = { width: number; height: number };

export interface ParamsMontage {
  /** Dernier rangement calculé (`useRangement`). */
  range: Range | null;
  /** Modèle de la vue en cours (pas encore rangé, le cas échéant). */
  modele: ModeleGraphe;
  edition: boolean;
  parId: ReadonlyMap<string, Etape>;
  sectionVue: string | null;
  /** Vue affichée (plan ou section) : en changer repart d'une première pose, sans mouvement. */
  contexte: string;
  noeudsRef: RefObject<NoeudRf[]>;
  setNoeuds: Dispatch<SetStateAction<NoeudRf[]>>;
  taillesRef: RefObject<ReadonlyMap<string, Taille>>;
  zone: RefObject<HTMLDivElement | null>;
  /** Section qu'on vient de déplier : la vue la montre en entier dans le même mouvement. */
  aMontrer: RefObject<{ id: string; jusqua: number } | null>;
  /** Nœud qui reste à sa place à l'écran pendant le mouvement (protection 2). */
  ancreCourante: (avant: ReadonlyMap<string, { x: number; y: number }>) => string | null;
  /** Lien « fait » (exécution) : tracé en vert pendant le mouvement aussi. */
  fait: (l: LienModele) => boolean;
}

/**
 * Pose et mouvement des nœuds du graphe libre à chaque rangement reçu.
 *
 * - Premier rangement d'une vue (plan ou section), tailles mesurées : nœuds posés sans animation, puis `arrivee` passe à
 *   vrai pour que le graphe pose sa vue d'arrivée (après le rendu de ces nœuds).
 * - Rangement suivant (repli, dépli, étapes reçues ou parties) : les nœuds qui vont paraître sont d'abord montés invisibles
 *   et mesurés par React Flow (`noterMesures`), puis un seul mouvement, entièrement calculé avant sa première image : chaque
 *   nœud va de sa place affichée à sa nouvelle place, la vue suit et les liens restent attachés à leurs nœuds.
 */
export function useMontageAnime({
  range,
  modele,
  edition,
  parId,
  sectionVue,
  contexte,
  noeudsRef,
  setNoeuds,
  taillesRef,
  zone,
  aMontrer,
  ancreCourante,
  fait,
}: ParamsMontage) {
  const rf = useReactFlow<NoeudRf, LienRf>();
  const modeleAffiche = range?.modele ?? modele;
  const animation = useRef<number | undefined>(undefined);
  // Phase d'un repli ou d'un dépli avant le mouvement (effacement des nœuds qui partent ; secours du montage).
  const phase = useRef<number | undefined>(undefined);
  // Montage invisible des nœuds qui vont paraître : ceux que React Flow n'a pas encore mesurés, et la suite.
  const montage = useRef<{
    ids: Set<string>;
    mesures: Map<string, Mesure>;
    suite: (mesures: ReadonlyMap<string, Mesure>) => void;
  } | null>(null);
  const terminerMontage = useCallback(() => {
    const m = montage.current;
    if (!m) return;
    montage.current = null;
    window.clearTimeout(phase.current);
    m.suite(m.mesures);
  }, []);
  // Rangement des nœuds affichés (cercles sur le bord des sections repliées) : celui du mouvement en cours, pas un rangement
  // arrivé pendant qu'on mesure encore les nœuds apparus.
  const [rangeVisible, setRangeVisible] = useState(range);
  const [anime, setAnime] = useState(false);
  // Liens dessinés pendant un mouvement (ceux de React Flow sont alors masqués) : attachés aux nœuds qui bougent.
  const [liensMouvement, setLiensMouvement] = useState<LienMouvement[] | null>(null);
  // Vue d'arrivée à poser : après le rendu qui a donné à React Flow les nœuds rangés et mesurés.
  const [arriveeAPoser, setArrivee] = useState(false);
  const premier = useRef(true);
  useLayoutEffect(() => {
    premier.current = true;
  }, [contexte]);

  useEffect(() => {
    const precedents = new Map(noeudsRef.current.map((n) => [n.id, n]));
    const cibles = noeudsDepuis(modeleAffiche, range?.rangement ?? null, parId, edition, precedents);
    window.cancelAnimationFrame(animation.current ?? 0);
    window.clearTimeout(phase.current);
    montage.current = null;
    // Premier rangement fait avec les tailles mesurées : posé sans animation, puis la vue d'arrivée.
    const mesure =
      !!range &&
      range.modele.noeuds.every(
        (n) => (n.nature !== 'etape' && n.nature !== 'section') || taillesRef.current.has(n.id),
      );
    if (!range || premier.current || precedents.size === 0) {
      setRangeVisible(range);
      setNoeuds(cibles);
      setAnime(false);
      setLiensMouvement(null);
      // Rangement de la vue en cours (pas celui de la vue quittée), tailles mesurées : vue d'arrivée, puis le graphe paraît.
      if (range && premier.current && mesure && range.modele === modele) {
        premier.current = false;
        // Vue d'arrivée posée après le rendu de ces nœuds (React Flow connaît alors leurs places), pas à l'image suivante.
        const arriver = () => setArrivee(true);
        // Tailles connues par les copies de mesure, mais nœuds pas encore mesurés par React Flow : la vue d'arrivée
        // attend leurs mesures (sinon l'emprise du graphe serait fausse).
        const nonMesures = cibles.filter((n) => !n.measured).map((n) => n.id);
        if (nonMesures.length > 0) {
          montage.current = { ids: new Set(nonMesures), mesures: new Map(), suite: arriver };
          phase.current = window.setTimeout(() => terminerMontage(), SECOURS_MONTAGE_MS);
        } else arriver();
      }
      return;
    }
    const reduit = mouvementReduit();
    // Repli ou dépli d'une section, étapes reçues ou parties. Ce rangement est définitif : les nouveaux nœuds ont été
    // mesurés avant, en invisible (`useRangement` attend leurs mesures), et rien n'a bougé d'ici là. Puis un seul
    // mouvement, entièrement calculé avant sa première image (voir mouvementGraphe) : chaque nœud va de sa place affichée
    // à sa nouvelle place, le cadre qui s'ouvre ou se ferme dévoile ou cache son intérieur (découpé par lui), la vue suit,
    // et les liens restent attachés à leurs nœuds du début à la fin.
    const idsCibles = new Set(cibles.map((n) => n.id));
    const suite = (mesures: ReadonlyMap<string, { width: number; height: number }> = new Map()) => {
      // Mesures prises par React Flow pendant le montage invisible : reprises, sinon il cacherait ces nœuds le temps de
      // les mesurer à nouveau.
      for (const n of cibles) {
        const d = mesures.get(n.id);
        if (!n.measured && d) n.measured = d;
      }
      animer();
    };
    // Nœuds qui vont paraître : d'abord montés invisibles (et mesurés par React Flow) sans que rien d'autre ne change à
    // l'écran ; le mouvement part quand React Flow les a tous mesurés (pas après un délai fixe).
    const nouveaux = cibles.filter((n) => !precedents.has(n.id));
    if (nouveaux.length > 0) {
      montage.current = { ids: new Set(nouveaux.map((n) => n.id)), mesures: new Map(), suite };
      setNoeuds([...precedents.values(), ...nouveaux.map((n) => Object.assign({}, n, { className: 'noeud-cache' }))]);
      // Secours (cas anormal) : un nœud que React Flow ne mesure pas n'empêche pas le mouvement.
      phase.current = window.setTimeout(() => terminerMontage(), SECOURS_MONTAGE_MS);
      return;
    }
    suite();

    function animer() {
      if (!range) return;
      const ancien = rangeVisible;
      setRangeVisible(range);
      const parentsAvant = new Map([...precedents.values()].map((n) => [n.id, n.parentId ?? null]));
      const absAvant = positionsAbsolues(
        {
          places: new Map(
            [...precedents.values()].map((n) => [n.id, { x: n.position.x, y: n.position.y, largeur: 0, hauteur: 0 }]),
          ),
          cercles: new Map(),
          routes: new Map(),
        },
        parentsAvant,
      );
      const absApres = positionsAbsolues(range.rangement, new Map(cibles.map((n) => [n.id, n.parentId ?? null])));
      // Ancrage (protection 2) : le nœud regardé reste à sa place à l'écran ; le reste glisse vers sa nouvelle place.
      const ancre = ancreCourante(absAvant);
      const vp = rf.getViewport();
      const { dx, dy } = decalageAncrage(absAvant, absApres, ancre, vp.zoom);
      let vueFin = { x: vp.x + dx, y: vp.y + dy, zoom: vp.zoom };
      // Section qu'on vient de déplier : montrée en entier dans le même mouvement (pas de second déplacement après).
      const m = aMontrer.current;
      if (m && performance.now() > m.jusqua) aMontrer.current = null;
      const placeM = m ? range.rangement.places.get(m.id) : undefined;
      const absM = m ? absApres.get(m.id) : undefined;
      const el = zone.current;
      if (m && placeM && absM && el && cibles.some((n) => n.id === m.id && n.type === 'cadre')) {
        vueFin = vueMontrant(
          vueFin,
          { x: absM.x, y: absM.y, largeur: placeM.largeur, hauteur: placeM.hauteur },
          { largeur: el.clientWidth, hauteur: el.clientHeight },
          GRILLE,
          ZOOM_MIN,
        );
        aMontrer.current = null;
      }

      // États de départ (affichés) et d'arrivée (nouveau rangement) de chaque nœud ; boîtes à leur taille visible.
      const depart = new Map<string, EtatNoeud>();
      const arrivee = new Map<string, EtatNoeud>();
      for (const n of precedents.values()) {
        const a = absAvant.get(n.id);
        if (!a) continue;
        const echelle = Number(n.style ? (Object.entries(n.style).find(([c]) => c === '--echelle')?.[1] ?? 1) : 1);
        depart.set(n.id, {
          boite: {
            x: a.x,
            y: a.y,
            largeur: Number(n.style?.width ?? n.measured?.width ?? 0) * echelle,
            hauteur: Number(n.style?.height ?? n.measured?.height ?? 0) * echelle,
          },
          opacite: n.className?.includes('noeud-cache') ? 0 : Number(n.style?.opacity ?? 1),
          echelle,
        });
      }
      for (const n of cibles) {
        const a = absApres.get(n.id);
        if (!a) continue;
        const p = range.rangement.places.get(n.id);
        arrivee.set(n.id, {
          boite: {
            x: a.x,
            y: a.y,
            largeur: p?.largeur ?? n.measured?.width ?? 0,
            hauteur: p?.hauteur ?? n.measured?.height ?? 0,
          },
          opacite: 1,
          echelle: 1,
        });
      }
      // Ce qui paraît dans un cadre qui s'ouvre part de sa place, à l'échelle du cadre de départ ; ce qui part d'un cadre
      // qui se ferme va à l'échelle du cadre d'arrivée. Le titre accompagne le cadre, à sa taille, visible du début à la
      // fin ; les cercles de la bordure suivent la bordure, en fondu (sur le petit cadre de départ ou d'arrivée, ils
      // couvriraient le titre).
      const placeDans = (n: NoeudRf): PlaceDansCadre =>
        n.type === 'titre' ? 'titre' : n.type === 'cercle' && !sectionVue ? 'bord' : 'contenu';
      // Contenu (étapes) d'un cadre qui s'ouvre ou se ferme : à sa taille réelle, ancré au milieu du bord haut du cadre et
      // découpé par lui, révélé rangée par rangée (voir `revelation`) ; il glisse un peu en paraissant ou en partant.
      const reveles = new Map<
        string,
        { parent: string; ancrage: Ancrage; sens: SensCadre; largeur: number; hauteur: number }
      >();
      for (const n of cibles) {
        const fin = arrivee.get(n.id);
        if (depart.has(n.id) || !fin) continue;
        const pd = n.parentId ? depart.get(n.parentId) : undefined;
        const pa = n.parentId ? arrivee.get(n.parentId) : undefined;
        const place = placeDans(n);
        if (place === 'contenu' && pd && pa && n.parentId) {
          const ancrage = ancrageHaut(fin.boite, pa.boite);
          const { largeur, hauteur: haut } = fin.boite;
          reveles.set(n.id, { parent: n.parentId, ancrage, sens: 'ouvre', largeur, hauteur: haut });
          depart.set(n.id, { boite: placeAncree(ancrage, pd.boite, largeur, haut), opacite: 0, echelle: 1 });
          continue;
        }
        const r = pd && pa ? dansCadre(fin.boite, pa.boite, pd.boite, place) : null;
        depart.set(n.id, {
          boite: r?.boite ?? fin.boite,
          echelle: r?.echelle ?? 1,
          // Le cadre ne paraît jamais vide : son intérieur, en réduction, est déjà visible au départ.
          opacite: !r ? 0 : place === 'titre' ? 1 : place === 'contenu' ? 0.3 : 0,
        });
      }
      const partants = [...precedents.values()].filter((n) => !idsCibles.has(n.id));
      for (const n of partants) {
        const deb = depart.get(n.id);
        if (!deb) continue;
        const pd = n.parentId ? depart.get(n.parentId) : undefined;
        const pa = n.parentId ? arrivee.get(n.parentId) : undefined;
        const place = placeDans(n);
        if (place === 'contenu' && pd && pa && n.parentId) {
          const ancrage = ancrageHaut(deb.boite, pd.boite);
          const { largeur, hauteur: haut } = deb.boite;
          reveles.set(n.id, { parent: n.parentId, ancrage, sens: 'ferme', largeur, hauteur: haut });
          arrivee.set(n.id, {
            boite: placeAncree(ancrage, pa.boite, largeur, haut),
            opacite: 0,
            echelle: deb.echelle,
          });
          continue;
        }
        const r = pd && pa ? dansCadre(deb.boite, pd.boite, pa.boite, place) : null;
        arrivee.set(n.id, {
          boite: r?.boite ?? deb.boite,
          echelle: deb.echelle * (r?.echelle ?? 1),
          // Symétrique du dépli : l'intérieur reste visible, en réduction, jusqu'à ce que la section reprenne sa place.
          opacite: !r ? 0 : place === 'titre' ? deb.opacite : place === 'contenu' ? Math.min(deb.opacite, 0.3) : 0,
        });
      }
      // Pendant le mouvement, une section qui se replie reste un cadre (qui se réduit) ; elle redevient une section à
      // l'arrivée, à la même taille. Une section qui se déplie devient tout de suite un cadre, à sa taille de section.
      const enMouvement = [
        ...cibles.map((n) => {
          const a = precedents.get(n.id);
          return a && a.type === 'cadre' && n.type === 'section' ? a : n;
        }),
        ...partants,
      ];
      const idsNouveauxLiens = new Set(range.modele.liens.map((l) => l.id));
      const idsAnciensLiens = new Set(ancien?.modele.liens.map((l) => l.id) ?? []);
      const duree = reduit ? 1 : DUREE_MOUVEMENT_MS;
      const debut = performance.now();
      setAnime(true);
      const pas = () => {
        // Heure lue à chaque image (pas l'horodatage passé par requestAnimationFrame, qui peut précéder `debut`).
        const t = Math.min(1, Math.max(0, (performance.now() - debut) / duree));
        const k = adoucir(t);
        void rf.setViewport({
          x: vp.x + (vueFin.x - vp.x) * k,
          y: vp.y + (vueFin.y - vp.y) * k,
          zoom: vp.zoom + (vueFin.zoom - vp.zoom) * k,
        });
        if (t >= 1) {
          setNoeuds(cibles);
          setLiensMouvement(null);
          setAnime(false);
          return;
        }
        const etats = new Map<string, EtatNoeud>();
        for (const [id, a] of depart) {
          const b = arrivee.get(id);
          if (b) etats.set(id, etatA(a, b, k));
        }
        const decoupes = new Map<string, string | null>();
        for (const [id, r] of reveles) {
          const p = etats.get(r.parent);
          if (!p) continue;
          const opacite = revelation(r.ancrage.ry, k, r.sens);
          const b = placeAncree(r.ancrage, p.boite, r.largeur, r.hauteur);
          b.y -= (1 - opacite) * GLISSE_CONTENU;
          etats.set(id, { boite: b, opacite, echelle: 1 });
          decoupes.set(id, decoupe(b, p.boite));
        }
        setNoeuds(
          enMouvement.map((n) => {
            const e = etats.get(n.id);
            if (!e) return n;
            const parent = n.parentId ? etats.get(n.parentId) : undefined;
            const style: Record<string, unknown> = { ...n.style, opacity: e.opacite };
            if (e.echelle !== 1) style['--echelle'] = e.echelle;
            else delete style['--echelle'];
            const coupe = decoupes.get(n.id);
            if (coupe) style.clipPath = coupe;
            else delete style.clipPath;
            if (n.type === 'cadre' || n.type === 'section') {
              style.width = e.boite.largeur / e.echelle;
              style.height = e.boite.hauteur / e.echelle;
            }
            // Nœud qui change de genre (section repliée ↔ cadre déplié) : pas encore mesuré, React Flow le cacherait une
            // image ; sa taille animée lui sert de mesure.
            const w = style.width;
            const h = style.height;
            const mesureAnimee =
              !n.measured && typeof w === 'number' && typeof h === 'number'
                ? { measured: { width: w, height: h } }
                : {};
            return Object.assign({}, n, mesureAnimee, {
              position: parent
                ? { x: e.boite.x - parent.boite.x, y: e.boite.y - parent.boite.y }
                : { x: e.boite.x, y: e.boite.y },
              style,
              className: e.echelle !== 1 ? 'noeud-echelle' : undefined,
            });
          }),
        );
        // Liens attachés à leurs nœuds : ceux qui restent, tels quels ; ceux qui changent de bout (section repliée ↔
        // cercles de son cadre) passent en fondu de l'ancien tracé au nouveau.
        const traces: LienMouvement[] = [];
        const tracer = (l: LienModele, route: Point[] | undefined, ref: Map<string, EtatNoeud>, fondu: number) => {
          const rs = ref.get(l.source)?.boite;
          const rc = ref.get(l.cible)?.boite;
          const s = etats.get(l.source);
          const c = etats.get(l.cible);
          if (!route || !rs || !rc || !s || !c) return;
          const opacite = Math.min(s.opacite, c.opacite) * fondu;
          if (opacite <= 0.01) return;
          const r = reveles.get(l.source) ?? reveles.get(l.cible);
          const dans = r ? etats.get(r.parent)?.boite : undefined;
          traces.push({
            id: l.id,
            d: cheminArrondi(routeSuivant(route, rs, s.boite, rc, c.boite), RAYON_COUDE),
            opacite,
            ton: fait(l) ? 'ok' : 'neutre',
            ...(dans ? { dans } : {}),
          });
        };
        for (const l of range.modele.liens)
          tracer(l, range.rangement.routes.get(l.id), arrivee, idsAnciensLiens.has(l.id) ? 1 : rampeApparition(k));
        if (ancien)
          for (const l of ancien.modele.liens)
            if (!idsNouveauxLiens.has(l.id))
              tracer(l, ancien.rangement.routes.get(l.id), depart, 1 - rampeDisparition(k));
        setLiensMouvement(traces);
        animation.current = window.requestAnimationFrame(pas);
      };
      // Première image tout de suite, avec le rangement affiché : jamais une image où ils ne vont pas ensemble.
      pas();
    }
    // Rangement seulement : la sélection, la cible et les droits sont lus au moment de l'animation.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [range, edition]);
  useEffect(
    () => () => {
      window.cancelAnimationFrame(animation.current ?? 0);
      window.clearTimeout(phase.current);
    },
    [],
  );

  /** Mesures reçues de React Flow : quand tous les nœuds montés invisibles sont mesurés, le mouvement part. */
  const noterMesures = useCallback(
    (dims: { id: string; dimensions?: Mesure }[]) => {
      const m = montage.current;
      if (!m) return;
      for (const c of dims) {
        m.ids.delete(c.id);
        if (c.dimensions) m.mesures.set(c.id, c.dimensions);
      }
      if (m.ids.size === 0) terminerMontage();
    },
    [terminerMontage],
  );

  return {
    rangeVisible,
    anime,
    liensMouvement,
    arrivee: arriveeAPoser,
    finArrivee: () => setArrivee(false),
    noterMesures,
  };
}
