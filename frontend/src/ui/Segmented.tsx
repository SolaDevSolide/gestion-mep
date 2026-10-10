import { ToggleGroup } from 'radix-ui';
import type { ReactNode } from 'react';
import { cn } from './cn';

interface Option<T extends string> {
  valeur: T;
  libelle: ReactNode;
  titre?: string;
}

/** Choix exclusif compact (Graphe / Liste, filtres de phase). */
export function Segmented<T extends string>({
  valeur,
  onChange,
  options,
  label,
  className,
  disabled,
}: {
  valeur: T;
  onChange: (v: T) => void;
  options: Option<T>[];
  label: string;
  className?: string;
  disabled?: boolean;
}) {
  return (
    <ToggleGroup.Root
      type="single"
      value={valeur}
      onValueChange={(v) => {
        if (v) onChange(v as T);
      }}
      aria-label={label}
      disabled={disabled}
      className={cn('inline-flex rounded-lg border border-line bg-surface-2 p-0.5', className)}
    >
      {options.map((o) => (
        <ToggleGroup.Item
          key={o.valeur}
          value={o.valeur}
          title={o.titre}
          className="inline-flex h-7 items-center gap-1.5 rounded-md px-2.5 text-[13px] font-medium text-fg-subtle transition-colors duration-150 hover:text-fg disabled:cursor-not-allowed disabled:opacity-60 data-[state=on]:bg-surface data-[state=on]:text-fg data-[state=on]:shadow-sm"
        >
          {o.libelle}
        </ToggleGroup.Item>
      ))}
    </ToggleGroup.Root>
  );
}
