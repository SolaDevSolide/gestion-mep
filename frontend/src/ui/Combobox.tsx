import { Check, ChevronsUpDown, X } from 'lucide-react';
import { Command } from 'cmdk';
import { useState, type ReactNode } from 'react';
import { cn } from './cn';
import { Popover, PopoverAnchor, PopoverContent, PopoverTrigger } from './Popover';

export interface OptionCombo {
  valeur: string;
  libelle: string;
  detail?: string;
}

const norm = (s: string) => s.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();
const filtre = (_v: string, recherche: string, mots?: string[]) =>
  norm((mots ?? []).join(' ')).includes(norm(recherche)) ? 1 : 0;

interface ListeProps {
  options: OptionCombo[];
  selection: string[];
  onToggle: (valeur: string) => void;
  placeholderRecherche: string;
  vide: string;
  pied?: ReactNode;
}

function Liste({ options, selection, onToggle, placeholderRecherche, vide, pied }: ListeProps) {
  return (
    <Command filter={filtre} className="w-[min(24rem,calc(100vw-3rem))]">
      <Command.Input
        placeholder={placeholderRecherche}
        className="h-10 w-full border-b border-line bg-transparent px-3 text-sm text-fg outline-none placeholder:text-fg-subtle"
      />
      <Command.List className="max-h-64 overflow-y-auto p-1">
        <Command.Empty className="px-3 py-6 text-center text-sm text-fg-subtle">{vide}</Command.Empty>
        {options.map((o) => {
          const coche = selection.includes(o.valeur);
          return (
            <Command.Item
              key={o.valeur}
              value={o.valeur}
              keywords={[o.libelle, o.detail ?? '']}
              onSelect={() => onToggle(o.valeur)}
              className="flex cursor-pointer items-start gap-2.5 rounded-lg px-2.5 py-2 text-sm text-fg data-[selected=true]:bg-surface-2"
            >
              <Check className={cn('mt-0.5 size-4 shrink-0 text-accent-text', !coche && 'invisible')} aria-hidden />
              <span className="min-w-0">
                <span className="block">{o.libelle}</span>
                {o.detail && <span className="block text-xs text-fg-subtle">{o.detail}</span>}
              </span>
            </Command.Item>
          );
        })}
      </Command.List>
      {pied && <div className="border-t border-line p-2">{pied}</div>}
    </Command>
  );
}

const DECLENCHEUR =
  'flex min-h-9 w-full items-center justify-between gap-2 rounded-lg border border-line-strong bg-surface px-3 py-1.5 text-left text-sm shadow-sm transition-colors duration-150 hover:border-fg-subtle disabled:cursor-not-allowed disabled:opacity-60';

interface MultiProps {
  id?: string;
  options: OptionCombo[];
  valeurs: string[];
  onChange: (v: string[]) => void;
  placeholder: string;
  placeholderRecherche?: string;
  vide?: string;
  disabled?: boolean;
  pied?: ReactNode;
  /** Libellé affiché dans les puces pour les valeurs absentes des options. */
  libelleInconnu?: string;
}

export function MultiSelect({
  id,
  options,
  valeurs,
  onChange,
  placeholder,
  placeholderRecherche = 'Rechercher…',
  vide = 'Aucun résultat.',
  disabled,
  pied,
  libelleInconnu = 'Inconnu',
}: MultiProps) {
  const [ouvert, setOuvert] = useState(false);
  const basculer = (v: string) => onChange(valeurs.includes(v) ? valeurs.filter((x) => x !== v) : [...valeurs, v]);
  // Les boutons « Retirer » sont frères du déclencheur, pas dedans : un bouton ne peut pas contenir d'éléments interactifs.
  return (
    <Popover open={ouvert} onOpenChange={setOuvert}>
      <PopoverAnchor asChild>
        <div className={cn(DECLENCHEUR, 'relative', disabled && 'cursor-not-allowed opacity-60')}>
          <span className="pointer-events-none relative z-[1] flex min-w-0 flex-1 flex-wrap gap-1">
            {valeurs.length === 0 && <span className="text-fg-subtle">{placeholder}</span>}
            {valeurs.map((v) => {
              const o = options.find((x) => x.valeur === v);
              return (
                <span
                  key={v}
                  className="inline-flex max-w-full items-center gap-1 rounded-md border border-line bg-surface-2 py-0.5 pr-1 pl-2 text-xs font-medium text-fg"
                >
                  <span className="truncate">{o?.libelle ?? libelleInconnu}</span>
                  {!disabled && (
                    <button
                      type="button"
                      aria-label={`Retirer ${o?.libelle ?? libelleInconnu}`}
                      className="pointer-events-auto rounded p-0.5 text-fg-subtle hover:bg-line hover:text-fg"
                      onClick={() => basculer(v)}
                    >
                      <X className="size-3" aria-hidden />
                    </button>
                  )}
                </span>
              );
            })}
          </span>
          <PopoverTrigger asChild>
            <button
              id={id}
              type="button"
              disabled={disabled}
              aria-haspopup="listbox"
              className="absolute inset-0 flex items-center justify-end rounded-lg pr-3 disabled:cursor-not-allowed"
            >
              <span className="sr-only">
                {valeurs.length === 0 ? placeholder : `${valeurs.length} sélectionné${valeurs.length > 1 ? 's' : ''}`}
              </span>
              <ChevronsUpDown className="size-4 shrink-0 text-fg-subtle" aria-hidden />
            </button>
          </PopoverTrigger>
        </div>
      </PopoverAnchor>
      <PopoverContent className="p-0">
        <Liste
          options={options}
          selection={valeurs}
          onToggle={basculer}
          placeholderRecherche={placeholderRecherche}
          vide={vide}
          pied={pied}
        />
      </PopoverContent>
    </Popover>
  );
}

interface SimpleProps {
  id?: string;
  options: OptionCombo[];
  valeur: string | null;
  onChange: (v: string | null) => void;
  placeholder: string;
  placeholderRecherche?: string;
  vide?: string;
  disabled?: boolean;
}

/** Sélection unique avec recherche. */
export function Combobox({
  id,
  options,
  valeur,
  onChange,
  placeholder,
  placeholderRecherche = 'Rechercher…',
  vide = 'Aucun résultat.',
  disabled,
}: SimpleProps) {
  const [ouvert, setOuvert] = useState(false);
  const courant = options.find((o) => o.valeur === valeur);
  return (
    <Popover open={ouvert} onOpenChange={setOuvert}>
      <PopoverTrigger asChild>
        <button id={id} type="button" disabled={disabled} className={cn(DECLENCHEUR, 'h-9')} aria-haspopup="listbox">
          <span className={cn('truncate', !courant && 'text-fg-subtle')}>{courant?.libelle ?? placeholder}</span>
          <ChevronsUpDown className="size-4 shrink-0 text-fg-subtle" aria-hidden />
        </button>
      </PopoverTrigger>
      <PopoverContent className="p-0">
        <Liste
          options={options}
          selection={valeur ? [valeur] : []}
          onToggle={(v) => {
            onChange(v === valeur ? null : v);
            setOuvert(false);
          }}
          placeholderRecherche={placeholderRecherche}
          vide={vide}
        />
      </PopoverContent>
    </Popover>
  );
}
