import { Tabs as T } from 'radix-ui';
import type { ComponentProps } from 'react';
import { cn } from './cn';

export const Tabs = T.Root;

export function TabsList({ className, ...rest }: ComponentProps<typeof T.List>) {
  return <T.List className={cn('flex gap-1 overflow-x-auto', className)} {...rest} />;
}

export function TabsTrigger({ className, ...rest }: ComponentProps<typeof T.Trigger>) {
  return (
    <T.Trigger
      className={cn(
        'relative inline-flex h-10 shrink-0 items-center gap-2 px-3 text-sm font-medium text-fg-subtle transition-colors duration-150 hover:text-fg',
        'data-[state=active]:text-fg after:absolute after:inset-x-2 after:-bottom-px after:h-0.5 after:rounded-full after:bg-transparent data-[state=active]:after:bg-accent',
        className,
      )}
      {...rest}
    />
  );
}

export const TabsContent = T.Content;
