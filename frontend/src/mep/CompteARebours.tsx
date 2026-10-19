import { Siren, TimerReset } from 'lucide-react';
import { calculerCompteARebours, formatDateHeureFuseau, fuseauValide } from '../format';
import { useNow } from '../hooks';
import { cn } from '../ui/cn';

/**
 * Compte à rebours jusqu'à l'échéance de retour arrière : orange sous 1 h, rouge une fois dépassée. L'heure est donnée dans le
 * fuseau de la MEP. En recette, l'échéance dépassée est seulement signalée : le pilote décide.
 */
export function CompteARebours({
  echeance,
  fuseau,
  recette = false,
  className,
}: {
  echeance: string | null;
  fuseau: string;
  recette?: boolean;
  className?: string;
}) {
  const now = useNow(1000);
  const c = calculerCompteARebours(echeance, now, recette);
  const heure = `${formatDateHeureFuseau(echeance, fuseau)} (${fuseauValide(fuseau)})`;

  if (c.niveau === 'aucun') {
    return (
      <div className={cn('rounded-lg border border-warn-bd bg-warn-bg px-3 py-1 text-warn-fg', className)}>
        <p className="text-[11px] font-semibold tracking-wide uppercase">Retour arrière</p>
        <p className="text-sm font-semibold">Échéance à renseigner</p>
      </div>
    );
  }
  if (c.niveau === 'depasse') {
    return (
      <div
        role="alert"
        className={cn(
          'flex items-center gap-2.5 rounded-lg bg-rollback-bg px-3 py-1.5 text-rollback-fg',
          !recette && 'pulse-ring',
          className,
        )}
      >
        <Siren className="size-6 shrink-0" aria-hidden />
        <div>
          <p className="text-sm leading-tight font-extrabold tracking-wide">{c.texte}</p>
          <p className="text-xs opacity-90">
            {recette ? 'Le pilote décide' : 'Échéance dépassée'} : {heure}
          </p>
        </div>
      </div>
    );
  }
  const attention = c.niveau === 'attention';
  return (
    <div
      className={cn(
        'rounded-lg border px-3 py-1',
        attention ? 'border-warn-bd bg-warn-bg text-warn-fg' : 'border-line bg-surface-2 text-fg',
        className,
      )}
      title={`Échéance de retour arrière : ${heure}`}
    >
      <p
        className={cn(
          'flex items-center gap-1.5 text-[10px] font-semibold tracking-wide uppercase',
          !attention && 'text-fg-subtle',
        )}
      >
        <TimerReset className="size-3.5" aria-hidden /> Retour arrière dans
      </p>
      <p className="font-mono text-lg leading-tight font-semibold tnum" aria-label={c.texte}>
        {c.texte}
      </p>
    </div>
  );
}
