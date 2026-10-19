import { DropdownMenu as M } from 'radix-ui';
import type { ComponentProps } from 'react';
import { cn } from './cn';

export const Menu = M.Root;
export const MenuTrigger = M.Trigger;
export const MenuSeparator = ({ className }: { className?: string }) => (
  <M.Separator className={cn('my-1 h-px bg-line', className)} />
);
export const MenuLabel = ({ children }: { children: React.ReactNode }) => (
  <M.Label className="px-2.5 py-1.5 text-xs font-medium text-fg-subtle">{children}</M.Label>
);

export function MenuContent({ className, align = 'end', sideOffset = 8, ...rest }: ComponentProps<typeof M.Content>) {
  return (
    <M.Portal>
      <M.Content
        align={align}
        sideOffset={sideOffset}
        className={cn(
          'z-menu min-w-56 rounded-xl border border-line bg-surface p-1 shadow-lg data-[state=open]:animate-fade-in',
          className,
        )}
        {...rest}
      />
    </M.Portal>
  );
}

export function MenuItem({ className, ...rest }: ComponentProps<typeof M.Item>) {
  return (
    <M.Item
      className={cn(
        'flex cursor-pointer items-center gap-2.5 rounded-lg px-2.5 py-2 text-sm text-fg outline-none select-none data-[disabled]:opacity-50 data-[highlighted]:bg-surface-2',
        className,
      )}
      {...rest}
    />
  );
}
