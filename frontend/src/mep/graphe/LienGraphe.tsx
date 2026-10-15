import { BaseEdge, EdgeLabelRenderer, getSmoothStepPath, type EdgeProps } from '@xyflow/react';
import { ArrowDown, Plus, Trash2 } from 'lucide-react';
import { useState } from 'react';
import { cn } from '../../ui/cn';
import { useGraphe } from './contexteGraphe';
import type { LienModele } from './modeleGraphe';

export interface DonneesLien extends Record<string, unknown> {
  lien: LienModele;
  /** Exécution : tout ce qui part de la source est réussi (lien vert). */
  fait?: boolean;
  /** Survol du pointeur ou focus clavier (affiche le « + » d'insertion). */
  actif?: boolean;
  onInserer?: (l: LienModele) => void;
  onSupprimer?: (l: LienModele) => void;
}

const COULEUR = {
  neutre: 'var(--fg-subtle)',
  ok: 'var(--ok-solid)',
  accent: 'var(--accent)',
};

/** Lien du graphe libre : coudes arrondis, « + » au survol pour insérer une étape, cible d'un dépôt. */
export function LienGraphe(props: EdgeProps) {
  const { id, sourceX, sourceY, targetX, targetY, sourcePosition, targetPosition, selected } = props;
  const d = props.data as DonneesLien;
  const g = useGraphe();
  const [survol, setSurvol] = useState(false);
  const [chemin, x, y] = getSmoothStepPath({
    sourceX,
    sourceY,
    targetX,
    targetY,
    sourcePosition,
    targetPosition,
    borderRadius: 10,
    offset: 16,
  });
  const vise = g.cible?.type === 'lien' && g.cible.lien.id === id;
  const ton = vise || selected ? 'accent' : d.fait ? 'ok' : 'neutre';
  const montre = g.edition && (d.actif || survol || selected || (g.placement && !!d.lien.logique));
  const insertion = g.edition && !!d.lien.logique && !!d.onInserer;
  return (
    <>
      <BaseEdge
        id={id}
        path={chemin}
        markerEnd={`url(#fleche-${ton})`}
        interactionWidth={22}
        style={{
          stroke: COULEUR[ton],
          strokeWidth: vise ? 3.5 : selected ? 2.6 : 1.8,
          strokeDasharray: vise ? '7 5' : undefined,
          opacity: ton === 'neutre' ? 0.8 : 1,
        }}
      />
      {vise && (
        <EdgeLabelRenderer>
          <span
            className="pointer-events-none absolute z-20 -translate-x-1/2 -translate-y-1/2 rounded-full bg-accent px-2 py-0.5 text-[11px] font-semibold whitespace-nowrap text-accent-fg shadow"
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
                className={cn(
                  'grid size-6 place-content-center rounded-full border-[1.5px] border-accent bg-surface text-accent-text shadow-sm hover:bg-accent hover:text-accent-fg',
                )}
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

/** Pointes de flèche des liens (une par couleur), à poser une fois dans le graphe. */
export function MarqueursLiens() {
  return (
    <svg aria-hidden className="absolute size-0">
      <defs>
        {(Object.keys(COULEUR) as (keyof typeof COULEUR)[]).map((k) => (
          <marker
            key={k}
            id={`fleche-${k}`}
            viewBox="0 0 10 10"
            refX="9"
            refY="5"
            markerWidth="7"
            markerHeight="7"
            orient="auto-start-reverse"
          >
            <path d="M0,0 L10,5 L0,10 z" style={{ fill: COULEUR[k] }} />
          </marker>
        ))}
      </defs>
    </svg>
  );
}

export const TYPES_LIENS = { lien: LienGraphe };
