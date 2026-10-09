import { useMemo } from 'react';
import { calculerGraphe, DIMENSIONS_PAR_DEFAUT, placerGraphe } from '../graphe';
import { executantEffectif, nomParticipant } from '../domaine';
import { metaEtape } from '../statuts';
import type { Etape, Mep } from '../types';
import { Avatar } from '../ui/Avatar';
import { cn } from '../ui/cn';
import { TON } from '../ui/tons';
import { CircleAlert } from 'lucide-react';

interface Props {
  mep: Mep;
  etapes: Etape[];
  selectionId: string | null;
  onSelect: (id: string) => void;
  /** Étapes écartées par les filtres : affichées estompées pour garder la structure. */
  estEstompee?: (e: Etape) => boolean;
}

const COULEUR_LIEN: Record<string, string> = {
  done: 'var(--done-solid)',
  ok: 'var(--ok-solid)',
  run: 'var(--run-solid)',
  danger: 'var(--danger-solid)',
};

/** Arbre d'exécution façon graphe GitHub Actions : colonnes par niveau, liens courbes, un nœud par étape. */
export function GrapheExecution({ mep, etapes, selectionId, onSelect, estEstompee }: Props) {
  const dim = DIMENSIONS_PAR_DEFAUT;
  const { graphe, dispo } = useMemo(() => {
    const g = calculerGraphe(etapes.map((e) => ({ id: e.id, dependances: e.dependances })));
    return { graphe: g, dispo: placerGraphe(g, dim) };
  }, [etapes, dim]);
  const parId = useMemo(() => new Map(etapes.map((e) => [e.id, e])), [etapes]);

  if (etapes.length === 0) return null;
  const moi = mep.droits.participantId;

  return (
    <div className="overflow-x-auto rounded-xl border border-line bg-surface p-2 shadow-sm" tabIndex={0} aria-label="Arbre d’exécution (défilement horizontal)">
      {graphe.aCycle && (
        <p className="mb-2 flex items-center gap-2 rounded-md bg-warn-bg px-3 py-1.5 text-xs text-warn-fg"><CircleAlert className="size-4" aria-hidden />Des dépendances circulaires ont été ignorées dans l’affichage.</p>
      )}
      <div className="relative" style={{ width: dispo.largeur, height: dispo.hauteur }}>
        <svg className="absolute inset-0" width={dispo.largeur} height={dispo.hauteur} aria-hidden>
          <defs>
            <marker id={`fleche-${mep.id}`} viewBox="0 0 8 8" refX="7" refY="4" markerWidth="7" markerHeight="7" orient="auto">
              <path d="M0,0 L8,4 L0,8 z" fill="context-stroke" />
            </marker>
          </defs>
          {dispo.liens.map((l) => {
            const source = parId.get(l.de);
            const actif = selectionId !== null && (l.de === selectionId || l.vers === selectionId);
            const ton = source ? metaEtape(source).ton : 'wait';
            return (
              <path
                key={`${l.de}-${l.vers}`}
                d={l.chemin}
                fill="none"
                strokeWidth={actif ? 2.5 : 1.75}
                strokeLinecap="round"
                markerEnd={`url(#fleche-${mep.id})`}
                style={{ stroke: actif ? 'var(--accent)' : source?.etat === 'Reussie' ? COULEUR_LIEN.done : COULEUR_LIEN[ton] ?? 'var(--line-strong)', transition: 'stroke 150ms' }}
                opacity={actif ? 1 : 0.8}
              />
            );
          })}
        </svg>
        {dispo.noeuds.map((n) => {
          const e = parId.get(n.id);
          if (!e) return null;
          const meta = metaEtape(e);
          const exec = executantEffectif(e);
          const mien = moi !== null && exec === moi;
          const nom = nomParticipant(mep, exec);
          const selectionnee = selectionId === e.id;
          const Icone = meta.Icone;
          return (
            <button
              key={e.id}
              type="button"
              onClick={() => onSelect(e.id)}
              aria-pressed={selectionnee}
              aria-label={`${e.code} ${e.action}, ${meta.libelle}${mien ? ', à vous' : ''}`}
              className={cn(
                'absolute flex overflow-hidden rounded-xl border bg-surface text-left shadow-sm transition-[box-shadow,opacity,border-color] duration-150 hover:shadow-md',
                selectionnee ? 'border-accent ring-2 ring-accent' : meta.ton === 'later' ? 'border-dashed border-line-strong' : 'border-line-strong',
                estEstompee?.(e) && !selectionnee && 'opacity-35',
              )}
              style={{ left: n.x, top: n.y, width: dim.largeurNoeud, height: dim.hauteurNoeud }}
            >
              <span className={cn('w-1.5 shrink-0', TON[meta.ton].bgSolide)} aria-hidden />
              <span className="flex min-w-0 flex-1 flex-col justify-between px-2.5 py-2">
                <span className="flex items-center gap-1.5">
                  <span className="font-mono text-xs font-bold">{e.code}</span>
                  {mien && <span className="rounded bg-accent px-1.5 py-px text-[10px] font-bold tracking-wide text-accent-fg uppercase">À vous</span>}
                </span>
                <span className="truncate text-[13px] leading-tight font-medium">{e.action || 'Sans libellé'}</span>
                <span className={cn('flex items-center gap-1 truncate text-[11px] font-semibold', TON[meta.ton].texte)}>
                  <Icone className={cn('size-3.5 shrink-0', meta.anime && 'animate-spin')} aria-hidden />
                  <span className="truncate">{meta.libelle}</span>
                </span>
              </span>
              <span className="flex shrink-0 items-start pt-2 pr-2.5">
                {nom ? <Avatar nom={nom} taille="sm" /> : <span className="size-6 rounded-full border border-dashed border-line-strong" title="Sans exécutant" />}
              </span>
            </button>
          );
        })}
      </div>
    </div>
  );
}
