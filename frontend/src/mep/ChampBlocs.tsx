import { EditeurBlocs } from '../ui/EditeurBlocs';
import { Instructions } from '../ui/Instructions';

/**
 * Champ d'instructions en blocs Texte / Code (spec §7) : libellé visible + éditeur de blocs.
 * Utilisé pour le contenu des sauvegardes et les étapes de test de la recette.
 */
export function ChampBlocs({ label, hint, valeur, onChange, disabled }: {
  label: string; hint?: string; valeur: string; onChange: (v: string) => void; disabled?: boolean;
}) {
  return (
    <div className="flex min-w-0 flex-col gap-1.5">
      <span className="text-[13px] font-medium text-fg" aria-hidden>{label}</span>
      <EditeurBlocs label={label} valeur={valeur} onChange={onChange} disabled={disabled} />
      {hint && <p className="text-xs text-fg-subtle">{hint}</p>}
    </div>
  );
}

/** Rendu en lecture d'un contenu markdown : texte rendu, blocs de code avec bouton Copier. */
export function RenduBlocs({ source, vide }: { source: string; vide?: string }) {
  return <Instructions source={source} vide={vide ?? null} />;
}
