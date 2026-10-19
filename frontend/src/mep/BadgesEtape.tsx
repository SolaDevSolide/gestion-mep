import { DatabaseBackup, Flag, Wrench } from 'lucide-react';
import type { Etape } from '../types';
import { Badge } from '../ui/StatusPill';
import { cn } from '../ui/cn';

/** Étiquettes propres aux étapes de déploiement : sauvegarde, point de non-retour, étape corrective. */
export function BadgesEtape({
  etape,
}: {
  etape: Pick<Etape, 'type' | 'pointDeNonRetour'> & { ajouteeApresGo?: boolean };
}) {
  if (etape.type !== 'Sauvegarde' && !etape.pointDeNonRetour && !etape.ajouteeApresGo) return null;
  return (
    <>
      {etape.type === 'Sauvegarde' && (
        <Badge ton="run">
          <DatabaseBackup className="size-3" aria-hidden />
          Sauvegarde
        </Badge>
      )}
      {etape.pointDeNonRetour && (
        <Badge ton="warn">
          <Flag className="size-3" aria-hidden />
          Point de non-retour
        </Badge>
      )}
      {etape.ajouteeApresGo && (
        <Badge ton="run">
          <Wrench className="size-3" aria-hidden />
          Ajoutée après le Go
        </Badge>
      )}
    </>
  );
}

/** Mention « Ajoutée après le Go » d'une étape corrective, pour les cartes compactes. */
export function BadgeAjouteeApresGo({ className }: { className?: string }) {
  return (
    <span
      className={cn(
        'inline-flex items-center rounded border border-accent/40 bg-accent-soft px-1.5 py-px text-2xs font-medium text-accent-text',
        className,
      )}
    >
      Ajoutée après le Go
    </span>
  );
}
