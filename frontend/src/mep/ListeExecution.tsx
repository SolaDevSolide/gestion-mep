import { executantEffectif, nomParticipant } from '../domaine';
import { LIBELLE_CRITICITE } from '../format';
import { metaEtape } from '../statuts';
import type { Etape, Mep } from '../types';
import { Avatar } from '../ui/Avatar';
import { cn } from '../ui/cn';
import { Badge, StatusPill } from '../ui/StatusPill';

/** Vue liste des étapes d'exécution (alternative au graphe). */
export function ListeExecution({ mep, etapes, selectionId, onSelect }: { mep: Mep; etapes: Etape[]; selectionId: string | null; onSelect: (id: string) => void }) {
  const moi = mep.droits.participantId;
  return (
    <ol className="divide-y divide-line overflow-hidden rounded-xl border border-line bg-surface shadow-sm">
      {etapes.map((e) => {
        const meta = metaEtape(e);
        const exec = executantEffectif(e);
        const nom = nomParticipant(mep, exec);
        const mien = moi !== null && exec === moi;
        return (
          <li key={e.id}>
            <button
              type="button"
              onClick={() => onSelect(e.id)}
              aria-pressed={selectionId === e.id}
              className={cn('flex w-full flex-wrap items-center gap-x-4 gap-y-1.5 px-4 py-3 text-left transition-colors duration-150 hover:bg-surface-2', selectionId === e.id && 'bg-accent-soft hover:bg-accent-soft')}
            >
              <span className="w-14 shrink-0 font-mono text-xs font-bold">{e.code}</span>
              <span className="min-w-0 flex-1 basis-48">
                <span className="block truncate text-sm font-medium">{e.action || 'Sans libellé'}</span>
                <span className="block truncate text-xs text-fg-subtle">{mep.systemes.find((s) => s.id === e.systemeId)?.nom ?? '—'}{e.criticite && ` · ${LIBELLE_CRITICITE[e.criticite]}`}</span>
              </span>
              {mien && <Badge ton="run">À vous</Badge>}
              <span className="flex items-center gap-2 text-xs text-fg-muted">{nom && <Avatar nom={nom} taille="sm" />}{nom}</span>
              <StatusPill ton={meta.ton} icone={meta.Icone} anime={meta.anime} taille="sm">{meta.libelle}</StatusPill>
            </button>
          </li>
        );
      })}
    </ol>
  );
}
