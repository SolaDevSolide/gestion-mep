import { useId, useMemo, type RefObject } from 'react';
import { tracerJonctions, type Ancrage, type GroupeLiens } from '../../graphe';
import type { Etape } from '../../types';
import type { ModeGraphe } from './CarteEtape';

const COULEUR = { neutre: 'var(--fg-subtle)', accent: 'var(--accent)', ok: 'var(--ok-solid)' } as const;

interface Props {
  groupes: GroupeLiens[];
  ancrages: Record<string, Ancrage>;
  parId: Map<string, Etape>;
  mode: ModeGraphe;
  /** Édition : les liens deviennent sélectionnables (souris et clavier). */
  edition: boolean;
  groupeSelection: string | null;
  onSelection: (id: string | null) => void;
  onGroupeSelection?: (cle: string | null) => void;
  /** Lien en cours de tirage : tracé temporaire, mis à jour directement par le glisser-déposer. */
  tirage: boolean;
  trace: RefObject<SVGPathElement | null>;
}

/** Couche SVG des liens et jonctions, derrière les fiches (repère du graphe non zoomé). */
export function CoucheLiens({
  groupes,
  ancrages,
  parId,
  mode,
  edition,
  groupeSelection,
  onSelection,
  onGroupeSelection,
  tirage,
  trace,
}: Props) {
  const idSvg = useId().replace(/:/g, '');
  const traces = useMemo(() => tracerJonctions(groupes, ancrages), [groupes, ancrages]);
  const groupeParCle = useMemo(() => new Map(groupes.map((g) => [g.cle, g])), [groupes]);
  const code = (id: string) => parId.get(id)?.code ?? id;
  const libelleGroupe = (g: GroupeLiens) =>
    `${g.sources.length * g.cibles.length > 1 ? 'Jonction' : 'Lien'} ${g.sources.map(code).join(', ')} vers ${g.cibles.map(code).join(', ')}`;

  return (
    <svg
      data-liens
      // Toujours à la taille du graphe (sans attendre une mesure) : origine du repère des liens, voir useMesureCartes.
      className="pointer-events-none absolute top-0 left-0 z-0 size-full overflow-visible"
    >
      <defs>
        {(Object.keys(COULEUR) as (keyof typeof COULEUR)[]).map((k) => (
          <marker
            key={k}
            id={`${idSvg}-${k}`}
            viewBox="0 0 10 10"
            refX="8"
            refY="5"
            markerWidth="6"
            markerHeight="6"
            orient="auto"
          >
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
        const epaisseur = sel ? 3 : 1.8;
        const opacite = sel || fait ? 1 : 0.75;
        return (
          <g key={t.cle}>
            <g aria-hidden>
              {t.traits && (
                <path
                  d={t.traits}
                  style={style}
                  strokeWidth={epaisseur}
                  fill="none"
                  opacity={opacite}
                  strokeLinecap="round"
                />
              )}
              {t.fleches.map((d, i) => (
                <path
                  key={i}
                  d={d}
                  style={style}
                  strokeWidth={epaisseur}
                  fill="none"
                  opacity={opacite}
                  markerEnd={`url(#${idSvg}-${ton})`}
                />
              ))}
              {t.points.map((p, i) => (
                <circle key={i} cx={p.x} cy={p.y} r={3} style={{ fill: COULEUR[ton] }} />
              ))}
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
                onClick={() => {
                  onSelection(null);
                  onGroupeSelection?.(t.cle);
                }}
                onKeyDown={(ev) => {
                  if (ev.key === 'Enter' || ev.key === ' ') {
                    ev.preventDefault();
                    onSelection(null);
                    onGroupeSelection?.(t.cle);
                  }
                }}
              >
                <title>{libelleGroupe(g)}</title>
              </path>
            )}
          </g>
        );
      })}
      {tirage && (
        <path
          ref={trace}
          style={{ stroke: COULEUR.accent }}
          strokeWidth={2.4}
          strokeDasharray="6 5"
          fill="none"
          markerEnd={`url(#${idSvg}-accent)`}
        />
      )}
    </svg>
  );
}
