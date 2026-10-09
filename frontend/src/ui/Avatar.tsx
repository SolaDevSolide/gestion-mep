import { initiales } from '../format';
import { cn } from './cn';

const PALETTE = [
  'bg-indigo-600', 'bg-sky-700', 'bg-emerald-700', 'bg-rose-700', 'bg-amber-700', 'bg-violet-700', 'bg-teal-700', 'bg-fuchsia-700',
];

function teinte(nom: string): string {
  let h = 0;
  for (const c of nom) h = (h * 31 + c.charCodeAt(0)) >>> 0;
  return PALETTE[h % PALETTE.length];
}

export function Avatar({ nom, taille = 'md', className }: { nom: string | null | undefined; taille?: 'xs' | 'sm' | 'md' | 'lg'; className?: string }) {
  const n = nom ?? '';
  return (
    <span
      role="img"
      aria-label={n || 'Inconnu'}
      title={n || undefined}
      className={cn(
        'inline-flex shrink-0 items-center justify-center rounded-full font-semibold text-white select-none',
        taille === 'xs' && 'size-5 text-[9px]', taille === 'sm' && 'size-6 text-[10px]', taille === 'md' && 'size-8 text-xs', taille === 'lg' && 'size-10 text-sm',
        n ? teinte(n) : 'bg-fg-subtle',
        className,
      )}
    >
      {initiales(n)}
    </span>
  );
}
