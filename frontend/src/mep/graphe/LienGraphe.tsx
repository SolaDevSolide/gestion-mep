import { BaseEdge, EdgeLabelRenderer, getSmoothStepPath, type EdgeProps } from '@xyflow/react';
import { ArrowDown, Plus, Trash2 } from 'lucide-react';
import { useState } from 'react';
import { useGraphe } from './contexteGraphe';
import type { LienModele } from './modeleGraphe';
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
  /** Survol du pointeur ou focus clavier (affiche le « + » d'insertion). */
  actif?: boolean;
  onInserer?: (l: LienModele) => void;
  onSupprimer?: (l: LienModele) => void;
}

type Ton = 'neutre' | 'ok' | 'accent';

const COULEUR: Record<Ton, string> = {
  neutre: 'var(--fg-subtle)',
  ok: 'var(--ok-solid)',
  accent: 'var(--accent)',
};

/**
 * Lien du graphe libre : tracé orthogonal d'ELK (il contourne cartes, cadres et cercles), coudes arrondis, sauts aux
 * croisements, liseré clair qui le détache des liens qu'il croise ; « + » au survol pour insérer une étape, cible d'un dépôt.
 */
export function LienGraphe(props: EdgeProps) {
  const { id, sourceX, sourceY, targetX, targetY, sourcePosition, targetPosition, selected } = props;
  const d = props.data as DonneesLien;
  const g = useGraphe();
  const [survol, setSurvol] = useState(false);
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
  const estompe = !!g.enAvant && !enAvant && !vise && !selected;
  const ton: Ton = vise || selected || enAvant ? 'accent' : d.fait ? 'ok' : 'neutre';
  const montre = g.edition && (d.actif || survol || selected || (g.placement && !!d.lien.logique));
  const insertion = g.edition && !!d.lien.logique && !!d.onInserer;
  const epaisseur = vise ? 3.5 : selected || enAvant ? 2.6 : 1.75;
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
        interactionWidth={18}
        style={{
          stroke: COULEUR[ton],
          strokeWidth: epaisseur,
          strokeDasharray: vise ? '7 5' : undefined,
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
      {montre && !vise && (insertion || (selected && d.onSupprimer)) && (
        <EdgeLabelRenderer>
          <div
            className="nodrag nopan pointer-events-auto absolute z-20 flex items-center gap-1"
            style={{ transform: `translate(${x}px, ${y}px) translate(-50%, -50%)`, left: 0, top: 0 }}
            onMouseEnter={() => setSurvol(true)}
            onMouseLeave={() => setSurvol(false)}
          >
            {insertion && (
              <button
                type="button"
                onClick={() => d.onInserer?.(d.lien)}
                aria-label={g.placement ? 'Poser la copie sur ce lien' : 'Insérer une étape sur ce lien'}
                title={g.placement ? 'Poser la copie sur ce lien' : 'Insérer une étape entre ces deux cartes'}
                className="grid size-6 place-content-center rounded-full border-[1.5px] border-accent bg-surface text-accent-text shadow-sm hover:bg-accent hover:text-accent-fg"
              >
                {g.placement ? (
                  <ArrowDown className="size-3.5" aria-hidden />
                ) : (
                  <Plus className="size-3.5" aria-hidden />
                )}
              </button>
            )}
            {selected && d.onSupprimer && (
              <button
                type="button"
                onClick={() => d.onSupprimer?.(d.lien)}
                aria-label="Supprimer ce lien"
                title="Supprimer ce lien (Suppr)"
                className="grid size-6 place-content-center rounded-full border-[1.5px] border-danger-bd bg-surface text-danger-fg shadow-sm hover:bg-danger-bg"
              >
                <Trash2 className="size-3.5" aria-hidden />
              </button>
            )}
          </div>
        </EdgeLabelRenderer>
      )}
    </>
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
