import type { LucideIcon } from 'lucide-react';
import type { ReactNode } from 'react';
import type { Ton } from '../format';
import { cn } from './cn';
import { TON } from './tons';

interface Props {
  ton: Ton;
  icone?: LucideIcon;
  anime?: boolean;
  children: ReactNode;
  taille?: 'sm' | 'md' | 'lg';
  className?: string;
}

/** Pastille d'état : toujours une icône et un libellé, jamais la couleur seule. */
export function StatusPill({ ton, icone: Icone, anime, children, taille = 'md', className }: Props) {
  return (
    <span
      className={cn(
        'inline-flex max-w-full items-center gap-1.5 rounded-full border font-medium leading-none whitespace-nowrap',
        taille === 'sm' ? 'px-2 py-1 text-2xs' : taille === 'lg' ? 'px-3.5 py-2 text-sm' : 'px-2.5 py-1 text-xs',
        TON[ton].pastille,
        className,
      )}
    >
      {Icone && (
        <Icone
          className={cn('shrink-0', taille === 'lg' ? 'size-4' : 'size-3.5', anime && 'animate-spin')}
          aria-hidden
        />
      )}
      <span className="truncate">{children}</span>
    </span>
  );
}

/** Étiquette neutre (criticité, système, etc.). */
export function Badge({ children, className, ton }: { children: ReactNode; className?: string; ton?: Ton }) {
  return (
    <span
      className={cn(
        'inline-flex items-center gap-1 rounded-md border px-1.5 py-0.5 text-2xs font-medium leading-4',
        ton ? TON[ton].pastille : 'border-line bg-surface-2 text-fg-muted',
        className,
      )}
    >
      {children}
    </span>
  );
}
