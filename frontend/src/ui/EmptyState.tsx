import type { LucideIcon } from 'lucide-react';
import type { ReactNode } from 'react';
import { cn } from './cn';

export function EmptyState({
  icone: Icone,
  titre,
  description,
  action,
  className,
}: {
  icone: LucideIcon;
  titre: string;
  description?: ReactNode;
  action?: ReactNode;
  className?: string;
}) {
  return (
    <div className={cn('flex flex-col items-center justify-center px-6 py-14 text-center', className)}>
      <div className="relative mb-5 flex size-24 items-center justify-center" aria-hidden>
        <svg viewBox="0 0 96 96" className="absolute inset-0 size-full text-line-strong">
          <circle cx="48" cy="48" r="46" fill="none" stroke="currentColor" strokeDasharray="3 5" strokeWidth="1.5" />
          <circle cx="48" cy="48" r="34" className="fill-accent-soft" />
          <circle cx="82" cy="30" r="4" className="fill-accent" opacity="0.5" />
          <circle cx="14" cy="64" r="3" className="fill-accent" opacity="0.35" />
        </svg>
        <Icone className="relative size-9 text-accent-text" strokeWidth={1.6} />
      </div>
      <h3 className="text-base font-semibold text-fg">{titre}</h3>
      {description && <p className="mt-1 max-w-sm text-sm text-fg-subtle">{description}</p>}
      {action && <div className="mt-5">{action}</div>}
    </div>
  );
}
