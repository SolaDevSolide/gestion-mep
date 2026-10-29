import type { ReactNode } from 'react';
import { formatDate } from '../../format';
import type { SecretEtat } from '../../types';
import { cn } from '../../ui/cn';
import { Field, Input } from '../../ui/Input';

/**
 * Champ d'un secret, en écriture seule : vide, « Enregistré — laisser vide pour le conserver », jamais réaffiché. Ce qu'on
 * tape remplace le secret à l'enregistrement. `ressaisir` : le domaine a changé, l'ancien secret sera effacé.
 */
export function ChampSecret({
  label,
  valeur,
  onChange,
  etat,
  ressaisir,
  obligatoire,
  className,
  hint,
}: {
  label: string;
  valeur: string;
  onChange: (v: string) => void;
  etat: SecretEtat | null | undefined;
  /** Message quand le secret enregistré ne vaut plus (domaine changé). */
  ressaisir?: string | null;
  obligatoire?: boolean;
  className?: string;
  hint?: ReactNode;
}) {
  const enregistre = Boolean(etat?.enregistre) && !ressaisir;
  const illisible = enregistre && etat?.lisible === false;
  const aide = ressaisir
    ? ressaisir
    : illisible
      ? 'Le secret enregistré est illisible (clés de l’application changées, par exemple après une restauration) : saisissez-le de nouveau.'
      : enregistre && etat?.remplaceLe
        ? `Remplacé le ${formatDate(etat.remplaceLe)}${etat.remplacePar ? ` par ${etat.remplacePar}` : ''}. Jamais réaffiché.`
        : !enregistre && obligatoire
          ? 'Obligatoire. Chiffré à l’enregistrement, jamais réaffiché.'
          : hint;
  return (
    <Field
      label={`${label}${obligatoire && !enregistre ? ' *' : ''}`}
      className={className}
      hint={ressaisir || illisible ? undefined : aide}
      erreur={ressaisir || illisible ? aide : undefined}
    >
      {(id) => (
        <Input
          id={id}
          type="password"
          value={valeur}
          onChange={(e) => onChange(e.target.value)}
          placeholder={enregistre && !illisible ? 'Enregistré — laisser vide pour le conserver' : ''}
          autoComplete="new-password"
          spellCheck={false}
          className="font-mono text-ui"
        />
      )}
    </Field>
  );
}

/** Choix multiple compact (filtres) : pastilles à cocher ; aucune cochée = tout. */
export function Pastilles<T extends string>({
  label,
  options,
  valeurs,
  onChange,
  hint,
}: {
  label: string;
  options: { valeur: T; libelle: string }[];
  valeurs: T[];
  onChange: (v: T[]) => void;
  hint?: ReactNode;
}) {
  return (
    <fieldset className="min-w-0">
      <legend className="text-ui font-medium text-fg">{label}</legend>
      <div className="mt-1.5 flex flex-wrap gap-1.5">
        {options.map((o) => {
          const coche = valeurs.includes(o.valeur);
          return (
            <button
              key={o.valeur}
              type="button"
              role="checkbox"
              aria-checked={coche}
              onClick={() => onChange(coche ? valeurs.filter((v) => v !== o.valeur) : [...valeurs, o.valeur])}
              className={cn(
                'rounded-full border px-2.5 py-1 text-xs font-medium transition-colors duration-150',
                coche
                  ? 'border-accent bg-accent-soft text-accent-text'
                  : 'border-line bg-surface text-fg-muted hover:border-line-strong hover:text-fg',
              )}
            >
              {o.libelle}
            </button>
          );
        })}
        {options.length === 0 && <span className="text-sm text-fg-subtle">Aucun choix possible.</span>}
      </div>
      {hint && <p className="mt-1.5 text-xs text-fg-subtle">{hint}</p>}
    </fieldset>
  );
}

/** Titre numéroté d'une carte de l'éditeur (« 2 Requête »). */
export function TitreCarte({ numero, titre, description }: { numero: number; titre: string; description?: string }) {
  return (
    <div className="flex items-start gap-3">
      <span
        aria-hidden
        className="flex size-7 shrink-0 items-center justify-center rounded-full bg-accent-soft text-sm font-semibold text-accent-text"
      >
        {numero}
      </span>
      <div className="min-w-0">
        <h2 className="text-md font-semibold tracking-tight text-fg">{titre}</h2>
        {description && <p className="mt-0.5 text-ui text-fg-subtle">{description}</p>}
      </div>
    </div>
  );
}
