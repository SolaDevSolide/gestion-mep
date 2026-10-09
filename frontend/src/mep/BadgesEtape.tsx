import { DatabaseBackup, Flag } from 'lucide-react';
import type { Etape } from '../types';
import { Badge } from '../ui/StatusPill';

/** Étiquettes propres aux étapes de déploiement : sauvegarde et point de non-retour. */
export function BadgesEtape({ etape }: { etape: Pick<Etape, 'type' | 'pointDeNonRetour'> }) {
  if (etape.type !== 'Sauvegarde' && !etape.pointDeNonRetour) return null;
  return (
    <>
      {etape.type === 'Sauvegarde' && <Badge ton="run"><DatabaseBackup className="size-3" aria-hidden />Sauvegarde</Badge>}
      {etape.pointDeNonRetour && <Badge ton="warn"><Flag className="size-3" aria-hidden />Point de non-retour</Badge>}
    </>
  );
}
