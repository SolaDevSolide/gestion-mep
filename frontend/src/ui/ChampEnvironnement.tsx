import { ENVIRONNEMENTS } from '../format';
import { cn } from './cn';
import { Input } from './Input';

/** Environnement en texte libre, avec des suggestions cliquables (Recette / Préproduction / Production). */
export function ChampEnvironnement({ id, valeur, onChange, disabled }: { id?: string; valeur: string; onChange: (v: string) => void; disabled?: boolean }) {
  return (
    <div className="space-y-2">
      <Input id={id} value={valeur} disabled={disabled} onChange={(e) => onChange(e.target.value)} placeholder="ex. Production" />
      <div className="flex flex-wrap gap-1.5" role="group" aria-label="Suggestions d’environnement">
        {ENVIRONNEMENTS.map((e) => {
          const actif = valeur.trim().toLowerCase() === e.toLowerCase();
          return (
            <button
              key={e}
              type="button"
              disabled={disabled}
              aria-pressed={actif}
              onClick={() => onChange(e)}
              className={cn(
                'rounded-full border px-2.5 py-0.5 text-xs font-medium transition-colors duration-150 disabled:cursor-not-allowed disabled:opacity-60',
                actif ? 'border-accent bg-accent-soft text-accent-text' : 'border-line bg-surface-2 text-fg-muted hover:border-line-strong hover:text-fg',
              )}
            >
              {e}
            </button>
          );
        })}
      </div>
    </div>
  );
}
