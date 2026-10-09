import { Siren, TimerReset } from 'lucide-react';
import { calculerCompteARebours, formatDateHeure } from '../format';
import { useNow } from '../hooks';
import { cn } from '../ui/cn';

/** Compte à rebours jusqu'à l'échéance de retour arrière : orange sous 1 h, rouge une fois dépassée. */
export function CompteARebours({ echeance, className }: { echeance: string | null; className?: string }) {
  const now = useNow(1000);
  const c = calculerCompteARebours(echeance, now);

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
      <div role="alert" className={cn('pulse-ring flex items-center gap-2.5 rounded-lg bg-rollback-bg px-3 py-1.5 text-white', className)}>
        <Siren className="size-6 shrink-0" aria-hidden />
        <div>
          <p className="text-sm leading-tight font-extrabold tracking-wide">RETOUR ARRIÈRE OBLIGATOIRE</p>
          <p className="text-xs opacity-90">Échéance dépassée : {formatDateHeure(echeance)}</p>
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
      title={`Échéance de retour arrière : ${formatDateHeure(echeance)}`}
    >
      <p className={cn('flex items-center gap-1.5 text-[10px] font-semibold tracking-wide uppercase', !attention && 'text-fg-subtle')}>
        <TimerReset className="size-3.5" aria-hidden /> Retour arrière dans
      </p>
      <p className="font-mono text-lg leading-tight font-semibold tnum" aria-label={c.texte}>{c.texte}</p>
    </div>
  );
}
