import {
  forwardRef,
  useId,
  type InputHTMLAttributes,
  type ReactNode,
  type SelectHTMLAttributes,
  type TextareaHTMLAttributes,
} from 'react';
import { ChevronDown } from 'lucide-react';
import { cn } from './cn';

const BASE =
  'w-full rounded-lg border border-line-strong bg-surface px-3 text-sm text-fg shadow-sm transition-[border-color,box-shadow] duration-150 ' +
  'placeholder:text-fg-subtle hover:border-fg-subtle focus:border-accent focus:outline-none focus:ring-2 focus:ring-accent/30 ' +
  'disabled:cursor-not-allowed disabled:bg-surface-2 disabled:text-fg-muted disabled:opacity-80';

export const Input = forwardRef<HTMLInputElement, InputHTMLAttributes<HTMLInputElement>>(function Input(
  { className, ...rest },
  ref,
) {
  return <input ref={ref} className={cn(BASE, 'h-9', className)} {...rest} />;
});

export const Textarea = forwardRef<
  HTMLTextAreaElement,
  TextareaHTMLAttributes<HTMLTextAreaElement> & { mono?: boolean }
>(function Textarea({ className, mono, rows = 3, ...rest }, ref) {
  return (
    <textarea
      ref={ref}
      rows={rows}
      spellCheck={mono ? false : undefined}
      className={cn(BASE, 'py-2 leading-relaxed', mono && 'font-mono text-[13px] whitespace-pre', className)}
      wrap={mono ? 'off' : undefined}
      {...rest}
    />
  );
});

export const Select = forwardRef<HTMLSelectElement, SelectHTMLAttributes<HTMLSelectElement>>(function Select(
  { className, children, ...rest },
  ref,
) {
  return (
    <div className="relative">
      <select ref={ref} className={cn(BASE, 'h-9 cursor-pointer appearance-none pr-9', className)} {...rest}>
        {children}
      </select>
      <ChevronDown
        className="pointer-events-none absolute top-1/2 right-3 size-4 -translate-y-1/2 text-fg-subtle"
        aria-hidden
      />
    </div>
  );
});

interface FieldProps {
  label: ReactNode;
  hint?: ReactNode;
  erreur?: ReactNode;
  className?: string;
  children: (id: string) => ReactNode;
}

/** Libellé + contrôle + aide, avec association label/for. */
export function Field({ label, hint, erreur, className, children }: FieldProps) {
  const id = useId();
  return (
    <div className={cn('flex min-w-0 flex-col gap-1.5', className)}>
      <label htmlFor={id} className="text-[13px] font-medium text-fg">
        {label}
      </label>
      {children(id)}
      {hint && !erreur && <p className="text-xs text-fg-subtle">{hint}</p>}
      {erreur && (
        <p className="text-xs text-danger-fg" role="alert">
          {erreur}
        </p>
      )}
    </div>
  );
}
