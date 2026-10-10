import { Check } from 'lucide-react';
import { Checkbox as C } from 'radix-ui';
import { useId, type ReactNode } from 'react';
import { cn } from './cn';

interface Props {
  checked: boolean;
  onChange: (v: boolean) => void;
  label: ReactNode;
  description?: ReactNode;
  disabled?: boolean;
  className?: string;
}

export function Checkbox({ checked, onChange, label, description, disabled, className }: Props) {
  const id = useId();
  return (
    <div className={cn('flex items-start gap-3', className)}>
      <C.Root
        id={id}
        checked={checked}
        disabled={disabled}
        onCheckedChange={(v) => onChange(v === true)}
        className="mt-0.5 flex size-[18px] shrink-0 items-center justify-center rounded-[5px] border border-line-strong bg-surface shadow-sm transition-colors duration-150 data-[state=checked]:border-accent data-[state=checked]:bg-accent disabled:cursor-not-allowed disabled:opacity-60"
      >
        <C.Indicator>
          <Check className="size-3.5 text-accent-fg" strokeWidth={3} aria-hidden />
        </C.Indicator>
      </C.Root>
      <label htmlFor={id} className={cn('text-sm leading-5 text-fg', disabled && 'text-fg-muted')}>
        {label}
        {description && <span className="block text-xs text-fg-subtle">{description}</span>}
      </label>
    </div>
  );
}
