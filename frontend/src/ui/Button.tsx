import { forwardRef, type ButtonHTMLAttributes, type ReactNode } from 'react';
import { Loader2 } from 'lucide-react';
import { cn } from './cn';

export type VarianteBouton = 'primaire' | 'secondaire' | 'fantome' | 'danger' | 'danger-doux' | 'succes';
export type TailleBouton = 'sm' | 'md' | 'lg';
type TailleInterne = TailleBouton | 'ic-sm' | 'ic-md' | 'ic-lg';

const VARIANTES: Record<VarianteBouton, string> = {
  primaire: 'bg-accent text-accent-fg hover:bg-accent-hover shadow-sm border border-transparent',
  secondaire: 'bg-surface text-fg border border-line-strong hover:bg-surface-2 shadow-sm',
  fantome: 'bg-transparent text-fg-muted hover:bg-surface-2 hover:text-fg border border-transparent',
  danger: 'bg-rollback-bg text-white hover:brightness-110 shadow-sm border border-transparent',
  'danger-doux': 'bg-surface text-danger-fg border border-danger-bd hover:bg-danger-bg',
  succes: 'bg-ok-solid text-white hover:brightness-110 shadow-sm border border-transparent',
};
const TAILLES: Record<TailleInterne, string> = {
  'ic-sm': 'size-8 rounded-md',
  'ic-md': 'size-9 rounded-lg',
  'ic-lg': 'size-12 rounded-xl',
  sm: 'h-8 px-2.5 text-[13px] gap-1.5 rounded-md',
  md: 'h-9 px-3.5 text-sm gap-2 rounded-lg',
  lg: 'h-12 px-5 text-base gap-2.5 rounded-xl font-semibold',
};

interface Props extends ButtonHTMLAttributes<HTMLButtonElement> {
  variante?: VarianteBouton;
  taille?: TailleInterne;
  chargement?: boolean;
  icone?: ReactNode;
}

export const Button = forwardRef<HTMLButtonElement, Props>(function Button(
  {
    variante = 'secondaire',
    taille = 'md',
    chargement = false,
    icone,
    className,
    children,
    disabled,
    type = 'button',
    ...rest
  },
  ref,
) {
  return (
    <button
      ref={ref}
      type={type}
      disabled={disabled || chargement}
      className={cn(
        'inline-flex shrink-0 items-center justify-center font-medium whitespace-nowrap transition-[background-color,box-shadow,filter,color] duration-150 select-none',
        'disabled:cursor-not-allowed disabled:opacity-50 active:translate-y-px',
        VARIANTES[variante],
        TAILLES[taille],
        className,
      )}
      {...rest}
    >
      {chargement ? <Loader2 className="size-4 animate-spin" aria-hidden /> : icone}
      {children}
    </button>
  );
});

export const BoutonIcone = forwardRef<HTMLButtonElement, Props & { label: string }>(function BoutonIcone(
  { label, variante = 'fantome', taille = 'md', className, children, ...rest },
  ref,
) {
  return (
    <Button
      ref={ref}
      aria-label={label}
      title={label}
      variante={variante}
      taille={taille === 'sm' ? 'ic-sm' : taille === 'lg' ? 'ic-lg' : 'ic-md'}
      className={className}
      {...rest}
    >
      {children}
    </Button>
  );
});
