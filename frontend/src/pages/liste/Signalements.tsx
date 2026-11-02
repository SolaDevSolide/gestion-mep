import { Snowflake, TriangleAlert } from 'lucide-react';
import { FUSEAU_INSTANCE, texteConflit, texteGelMep } from '../../calendrier';
import type { Phase, SignalementCalendrier } from '../../types';
import { cn } from '../../ui/cn';
import { PILULE } from './CalendrierMeps';
import { StatusPill } from '../../ui/StatusPill';

/**
 * Gels et conflits d'une MEP de la liste (#129) : un conflit est un simple avertissement ; un gel bloque le Go d'une MEP en
 * préparation tant qu'aucune dérogation n'est accordée. Les heures se lisent dans le fuseau de l'instance.
 */
export function Signalements({
  s,
  phase,
  className,
}: {
  s: SignalementCalendrier | undefined;
  phase: Phase;
  className?: string;
}) {
  if (!s || (s.gels.length === 0 && s.conflits.length === 0)) return null;
  return (
    <div className={cn('flex flex-wrap gap-1.5', className)} aria-label="Signalements du calendrier">
      {s.gels.map((g) => (
        <StatusPill
          key={g.periodeGelId + g.debut}
          ton={phase === 'Preparation' && g.derogation !== 'Accordee' ? 'run' : 'wait'}
          icone={Snowflake}
          taille="sm"
          className={PILULE}
        >
          Gel : {texteGelMep(g, phase)}
        </StatusPill>
      ))}
      {s.conflits.length === 1 && (
        <StatusPill ton="danger" icone={TriangleAlert} taille="sm" className={PILULE}>
          {texteConflit(s.conflits[0], FUSEAU_INSTANCE)}
        </StatusPill>
      )}
      {s.conflits.length > 1 && (
        <span title={s.conflits.map((c) => texteConflit(c, FUSEAU_INSTANCE)).join('\n')} className="max-w-full">
          <StatusPill ton="danger" icone={TriangleAlert} taille="sm" className={PILULE}>
            Conflit avec {s.conflits.length} MEP sur {[...new Set(s.conflits.flatMap((c) => c.systemes))].join(', ')}
          </StatusPill>
          <span className="sr-only"> : {s.conflits.map((c) => texteConflit(c, FUSEAU_INSTANCE)).join(' ; ')}</span>
        </span>
      )}
    </div>
  );
}
