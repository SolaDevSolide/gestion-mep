import { forwardRef, useRef, useState, type ButtonHTMLAttributes, type ReactNode } from 'react';
import { Loader2 } from 'lucide-react';
import { cn } from './cn';

export type VarianteBouton = 'primaire' | 'secondaire' | 'fantome' | 'danger' | 'danger-doux' | 'succes';
export type TailleBouton = 'sm' | 'md' | 'lg';
type TailleInterne = TailleBouton | 'ic-sm' | 'ic-md' | 'ic-lg';

const VARIANTES: Record<VarianteBouton, string> = {
  primaire: 'bg-accent text-accent-fg hover:bg-accent-hover shadow-sm border border-transparent',
  secondaire: 'bg-surface text-fg border border-line-strong hover:bg-surface-2 shadow-sm',
  fantome: 'bg-transparent text-fg-muted hover:bg-surface-2 hover:text-fg border border-transparent',
  danger: 'bg-rollback-bg text-rollback-fg hover:brightness-110 shadow-sm border border-transparent',
  'danger-doux': 'bg-surface text-danger-fg border border-danger-bd hover:bg-danger-bg',
  succes: 'bg-ok-solid text-ok-solid-fg hover:brightness-110 shadow-sm border border-transparent',
};
const TAILLES: Record<TailleInterne, string> = {
  'ic-sm': 'size-8 rounded-md',
  'ic-md': 'size-9 rounded-lg',
  'ic-lg': 'size-12 rounded-xl',
  sm: 'h-8 px-2.5 text-ui gap-1.5 rounded-md',
  md: 'h-9 px-3.5 text-sm gap-2 rounded-lg',
  lg: 'h-12 px-5 text-base gap-2.5 rounded-xl font-semibold',
};

interface Props extends ButtonHTMLAttributes<HTMLButtonElement> {
  variante?: VarianteBouton;
  taille?: TailleInterne;
  chargement?: boolean;
  icone?: ReactNode;
  /**
   * Action lancée au clic (après `onClick`). Si elle renvoie une promesse, le bouton est occupé jusqu'à sa fin : roue à
   * la place de l'icône, désactivé (un second clic ne relance rien), `aria-busy`.
   */
  action?: () => unknown;
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
    action,
    onClick,
    ...rest
  },
  ref,
) {
  const [occupe, setOccupe] = useState(false);
  const enCours = useRef(false);
  const charge = chargement || occupe;
  const icones = taille.startsWith('ic-');
  return (
    <button
      ref={ref}
      type={type}
      disabled={disabled || charge}
      aria-busy={charge || undefined}
      onClick={(ev) => {
        onClick?.(ev);
        if (!action || enCours.current || ev.defaultPrevented) return;
        const r = action();
        if (!(r instanceof Promise)) return;
        enCours.current = true;
        setOccupe(true);
        void r
          .catch(() => undefined)
          .finally(() => {
            enCours.current = false;
            setOccupe(false);
          });
      }}
      className={cn(
        'inline-flex shrink-0 items-center justify-center font-medium whitespace-nowrap transition-[background-color,box-shadow,filter,color] duration-150 select-none',
        'disabled:cursor-not-allowed disabled:opacity-50 active:translate-y-px',
        VARIANTES[variante],
        TAILLES[taille],
        className,
      )}
      {...rest}
    >
      {charge ? <Loader2 className={cn('animate-spin', taille === 'lg' ? 'size-5' : 'size-4')} aria-hidden /> : icone}
      {/* Bouton icône occupé : la roue remplace l'icône. */}
      {charge && icones && !icone ? null : children}
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
