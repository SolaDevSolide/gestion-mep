import { BaseEdge, EdgeLabelRenderer, getSmoothStepPath, type EdgeProps } from '@xyflow/react';
import { ArrowDown } from 'lucide-react';
import { useId } from 'react';
import { useGraphe } from './contexteGraphe';
import type { LienModele } from './modeleGraphe';
import type { LienMouvement } from './mouvementGraphe';
import type { Point } from './rangement';
import { cheminLien, milieuLien } from './routage';

export interface DonneesLien extends Record<string, unknown> {
  lien: LienModele;
  /** Tracé d'ELK (coordonnées du graphe) ; absent : chemin par défaut entre les poignées. */
  route?: Point[];
  /** Sauts aux croisements, par segment horizontal du tracé. */
  sauts?: ReadonlyMap<number, number[]>;
  /** Exécution : tout ce qui part de la source est réussi (lien vert). */
  fait?: boolean;
  /** Une copie d'étape modèle attend d'être posée et ce lien peut la recevoir. */
  onPoser?: (l: LienModele) => void;
  /** « Optimiser les liens » : ce lien inutile sera retiré si la personne confirme (rouge, pointillé). */
  retire?: boolean;
}

type Ton = 'neutre' | 'ok' | 'accent' | 'danger';

const COULEUR: Record<Ton, string> = {
  neutre: 'var(--fg-subtle)',
  ok: 'var(--ok-solid)',
  accent: 'var(--accent)',
  danger: 'var(--danger-solid)',
};

/**
 * Lien du graphe libre : tracé orthogonal d'ELK (il contourne cartes, cadres et cercles), coudes arrondis, sauts aux
 * croisements, liseré clair qui le détache des liens qu'il croise. Le tracé ne capte pas le pointeur : survol et clic sont
 * calculés par le graphe sur les tracés, pour viser un lien précis même là où plusieurs liens partagent un tronçon.
 */
export function LienGraphe(props: EdgeProps) {
  const { id, sourceX, sourceY, targetX, targetY, sourcePosition, targetPosition, selected } = props;
  const d = props.data as DonneesLien;
  const g = useGraphe();
  // Tracé d'ELK tant que ses deux bouts sont à leur place ; pendant une animation ou le glisser d'un de ses bouts (ou d'une
  // section, qui emporte ses étapes), un tracé provisoire qui suit les poignées.
  const bouge =
    g.enMouvement ||
    (!!g.glisse && (g.glisse.startsWith('s:') || props.source === g.glisse || props.target === g.glisse));
  const route = d.route && !bouge ? d.route : null;
  let chemin: string;
  let x: number;
  let y: number;
  if (route) {
    chemin = cheminLien(route, d.sauts);
    ({ x, y } = milieuLien(route));
  } else {
    [chemin, x, y] = getSmoothStepPath({
      sourceX,
      sourceY,
      targetX,
      targetY,
      sourcePosition,
      targetPosition,
      borderRadius: 8,
      offset: 16,
    });
  }
  const vise = g.cible?.type === 'lien' && g.cible.lien.id === id;
  const enAvant = g.enAvant?.liens.has(id) ?? false;
  const estompe = !!g.enAvant && !enAvant && !vise && !selected && !d.retire;
  const ton: Ton = d.retire ? 'danger' : vise || selected || enAvant ? 'accent' : d.fait ? 'ok' : 'neutre';
  const epaisseur = d.retire ? 3 : vise ? 3.5 : selected ? 3 : enAvant ? 2.6 : 1.75;
  return (
    <>
      {/* Liseré de la couleur du fond : un lien qui en croise un autre s'en détache nettement. */}
      <path
        d={chemin}
        fill="none"
        aria-hidden
        className="pointer-events-none"
        style={{ stroke: 'var(--surface)', strokeWidth: epaisseur + 4, opacity: estompe ? 0 : 1 }}
      />
      <BaseEdge
        id={id}
        path={chemin}
        markerEnd={`url(#fleche-${ton})`}
        interactionWidth={0}
        className={d.retire ? 'lien-retire' : undefined}
        style={{
          pointerEvents: 'none',
          stroke: COULEUR[ton],
          strokeWidth: epaisseur,
          strokeDasharray: vise ? '7 5' : d.retire ? '6 4' : undefined,
          strokeLinejoin: 'round',
          opacity: estompe ? 0.18 : 1,
          transition: 'opacity 120ms, stroke-width 120ms',
        }}
      />
      {vise && (
        <EdgeLabelRenderer>
          <span
            className="pointer-events-none absolute z-20 rounded-full bg-accent px-2 py-0.5 text-[11px] font-semibold whitespace-nowrap text-accent-fg shadow"
            style={{ transform: `translate(${x}px, ${y}px) translate(-50%, -50%)`, left: 0, top: 0 }}
          >
            Insérer ici
          </span>
        </EdgeLabelRenderer>
      )}
      {d.onPoser && !vise && (
        <EdgeLabelRenderer>
          <button
            type="button"
            onClick={() => d.onPoser?.(d.lien)}
            aria-label="Poser la copie sur ce lien"
            title="Poser la copie sur ce lien"
            className="nodrag nopan pointer-events-auto absolute z-20 grid size-6 place-content-center rounded-full border-[1.5px] border-accent bg-surface text-accent-text shadow-sm hover:bg-accent hover:text-accent-fg"
            style={{ transform: `translate(${x}px, ${y}px) translate(-50%, -50%)`, left: 0, top: 0 }}
          >
            <ArrowDown className="size-3.5" aria-hidden />
          </button>
        </EdgeLabelRenderer>
      )}
    </>
  );
}

/**
 * Liens pendant un repli ou un dépli (ceux de React Flow sont masqués) : mêmes traits, liseré et flèches, attachés aux
 * nœuds qui bougent. Posés dans la vue du graphe, sous les cartes et au-dessus des cadres, comme les liens.
 */
export function LiensEnMouvement({ liens, zIndex }: { liens: readonly LienMouvement[]; zIndex: number }) {
  const prefixe = useId().replace(/:/g, '');
  return (
    <svg aria-hidden className="pointer-events-none absolute top-0 left-0 size-px overflow-visible" style={{ zIndex }}>
      {/* Liens d'un cadre qui s'ouvre ou se ferme : découpés par le cadre, comme son contenu. */}
      <defs>
        {liens.map(
          (l, i) =>
            l.dans && (
              <clipPath key={l.id} id={`${prefixe}-${i}`} clipPathUnits="userSpaceOnUse">
                <rect x={l.dans.x} y={l.dans.y} width={l.dans.largeur} height={l.dans.hauteur} />
              </clipPath>
            ),
        )}
      </defs>
      {liens.map((l, i) => (
        <g key={l.id} style={{ opacity: l.opacite }} clipPath={l.dans ? `url(#${prefixe}-${i})` : undefined}>
          <path d={l.d} fill="none" style={{ stroke: 'var(--surface)', strokeWidth: 1.75 + 4 }} />
          <path
            d={l.d}
            fill="none"
            markerEnd={`url(#fleche-${l.ton})`}
            style={{ stroke: COULEUR[l.ton], strokeWidth: 1.75, strokeLinejoin: 'round' }}
          />
        </g>
      ))}
    </svg>
  );
}

/** Pointes de flèche des liens (une par couleur), nettes à tout zoom : taille en unités du graphe. */
export function MarqueursLiens() {
  return (
    <svg aria-hidden className="absolute size-0">
      <defs>
        {(Object.keys(COULEUR) as Ton[]).map((k) => (
          <marker
            key={k}
            id={`fleche-${k}`}
            viewBox="0 0 12 12"
            refX="11"
            refY="6"
            markerWidth="11"
            markerHeight="11"
            markerUnits="userSpaceOnUse"
            orient="auto-start-reverse"
          >
            <path d="M1,1.5 L11,6 L1,10.5 L3.5,6 z" style={{ fill: COULEUR[k] }} />
          </marker>
        ))}
      </defs>
    </svg>
  );
}

export const TYPES_LIENS = { lien: LienGraphe };
