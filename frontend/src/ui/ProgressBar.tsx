import type { Ton } from '../format';
import { cn } from './cn';
import { TON } from './tons';

export interface Segment {
  valeur: number;
  ton: Ton;
  libelle: string;
}

const SINGULIER: Record<string, string> = { Réussies: 'réussie', Échecs: 'échec', Restantes: 'restante' };
export const nommerSegment = (s: Segment) =>
  s.valeur <= 1 ? (SINGULIER[s.libelle] ?? s.libelle.toLowerCase()) : s.libelle.toLowerCase();

/** Barre de progression segmentée (réussies / en cours / échecs / restantes). */
export function ProgressBar({
  segments,
  className,
  legende = false,
  hauteur = 'h-2',
}: {
  segments: Segment[];
  className?: string;
  legende?: boolean;
  hauteur?: string;
}) {
  const total = segments.reduce((s, x) => s + x.valeur, 0);
  const description =
    segments
      .filter((s) => s.valeur > 0)
      .map((s) => `${s.valeur} ${nommerSegment(s)}`)
      .join(', ') || 'Aucune étape';
  return (
    <div className={className}>
      <div
        role="img"
        aria-label={`Progression : ${description}`}
        className={cn('flex w-full gap-0.5 overflow-hidden rounded-full bg-sunken', hauteur)}
      >
        {total > 0 &&
          segments
            .filter((s) => s.valeur > 0)
            .map((s) => (
              <div
                key={s.libelle}
                className={cn(
                  'h-full first:rounded-l-full last:rounded-r-full transition-[flex-grow] duration-300',
                  TON[s.ton].bgSolide,
                  s.ton === 'wait' && 'opacity-40',
                )}
                style={{ flexGrow: s.valeur, flexBasis: 0 }}
              />
            ))}
      </div>
      {legende && (
        <ul className="mt-1.5 flex flex-wrap gap-x-3 gap-y-0.5 text-xs text-fg-subtle">
          {segments.map((s) => (
            <li key={s.libelle} className="inline-flex items-center gap-1.5 tnum">
              <span
                className={cn('size-2 rounded-full', TON[s.ton].bgSolide, s.ton === 'wait' && 'opacity-40')}
                aria-hidden
              />
              {s.valeur} {nommerSegment(s)}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
