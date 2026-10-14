import { initiales } from '../format';
import { cn } from './cn';

/** Teintes des personnes (contraste suffisant avec un texte blanc, en clair comme en sombre). */
const PALETTE = [
  '#4f46e5', // indigo
  '#0369a1', // bleu ciel
  '#047857', // émeraude
  '#be123c', // rose
  '#b45309', // ambre
  '#6d28d9', // violet
  '#0f766e', // sarcelle
  '#a21caf', // fuchsia
];

/** Couleur stable d'une personne, d'après son nom : la même pour son avatar et pour ses encadrés de présence. */
export function couleurPersonne(nom: string): string {
  let h = 0;
  for (const c of nom) h = (h * 31 + c.charCodeAt(0)) >>> 0;
  return PALETTE[h % PALETTE.length];
}

export function Avatar({
  nom,
  taille = 'md',
  className,
  titre,
}: {
  nom: string | null | undefined;
  taille?: 'xs' | 'sm' | 'md' | 'lg';
  className?: string;
  /** Infobulle et nom lu (par défaut, le nom). */
  titre?: string;
}) {
  const n = nom ?? '';
  return (
    <span
      role="img"
      aria-label={titre ?? (n || 'Inconnu')}
      title={titre ?? (n || undefined)}
      className={cn(
        'inline-flex shrink-0 items-center justify-center rounded-full font-semibold text-white select-none',
        taille === 'xs' && 'size-5 text-[9px]',
        taille === 'sm' && 'size-6 text-[10px]',
        taille === 'md' && 'size-8 text-xs',
        taille === 'lg' && 'size-10 text-sm',
        !n && 'bg-fg-subtle',
        className,
      )}
      style={n ? { backgroundColor: couleurPersonne(n) } : undefined}
    >
      {initiales(n)}
    </span>
  );
}
