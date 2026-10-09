import type { LucideIcon } from 'lucide-react';
import type { ReactNode } from 'react';
import type { Ton } from '../format';
import { cn } from './cn';
import { TON } from './tons';

/** Encadré d'information / d'avertissement avec icône. */
export function Alerte({ ton, icone: Icone, titre, children, action, className, role }: { ton: Ton; icone: LucideIcon; titre?: ReactNode; children?: ReactNode; action?: ReactNode; className?: string; role?: 'alert' | 'status' }) {
  return (
    <div role={role} className={cn('flex items-start gap-3 rounded-lg border px-3.5 py-3 text-sm', TON[ton].pastille, ton === 'later' && 'border-solid', className)}>
      <Icone className="mt-0.5 size-4.5 shrink-0" aria-hidden />
      <div className="min-w-0 flex-1">
        {titre && <p className="font-semibold">{titre}</p>}
        {children && <div className={cn(titre && 'mt-0.5', 'whitespace-pre-line')}>{children}</div>}
      </div>
      {action}
    </div>
  );
}
