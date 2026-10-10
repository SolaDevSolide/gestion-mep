import { Popover as P } from 'radix-ui';
import type { ComponentProps } from 'react';
import { cn } from './cn';

export const Popover = P.Root;
export const PopoverTrigger = P.Trigger;
export const PopoverAnchor = P.Anchor;

export function PopoverContent({
  className,
  align = 'start',
  sideOffset = 8,
  ...rest
}: ComponentProps<typeof P.Content>) {
  return (
    <P.Portal>
      <P.Content
        align={align}
        sideOffset={sideOffset}
        className={cn(
          'z-[60] rounded-xl border border-line bg-surface p-1 shadow-lg outline-none data-[state=open]:animate-fade-in',
          className,
        )}
        {...rest}
      />
    </P.Portal>
  );
}
