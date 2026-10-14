import { abregerEnvironnement } from '../environnements';
import { cn } from './cn';
import { TON } from './tons';

/**
 * Pastille d'un environnement : production en orange. `abrege` : nom court (Préprod, Prod) pour les cartes et l'en-tête.
 * `retire` : environnement qu'une étape garde alors qu'il n'est plus choisi pour le système (barré, en rouge).
 */
export function PastilleEnvironnement({
  nom,
  production,
  abrege = false,
  retire = false,
  className,
}: {
  nom: string;
  production: boolean;
  abrege?: boolean;
  retire?: boolean;
  className?: string;
}) {
  return (
    <span
      title={retire ? `${nom} : n’est plus choisi pour ce système (Préparation › Systèmes)` : nom}
      className={cn(
        'inline-flex items-center rounded-md border px-1.5 text-[11px] leading-4 font-medium whitespace-nowrap',
        retire
          ? cn(TON.danger.pastille, 'line-through')
          : production
            ? TON.warn.pastille
            : 'border-line bg-surface-2 text-fg-muted',
        className,
      )}
    >
      {abrege ? abregerEnvironnement(nom) : nom}
      {retire && <span className="sr-only"> (n’est plus choisi pour ce système)</span>}
    </span>
  );
}
