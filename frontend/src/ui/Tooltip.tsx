import { Tooltip as T } from 'radix-ui';
import type { ReactNode } from 'react';

export const TooltipProvider = ({ children }: { children: ReactNode }) => (
  <T.Provider delayDuration={250}>{children}</T.Provider>
);

export function Tooltip({
  content,
  children,
  cote = 'top',
}: {
  content: ReactNode;
  children: ReactNode;
  cote?: 'top' | 'bottom' | 'left' | 'right';
}) {
  if (!content) return <>{children}</>;
  return (
    <T.Root>
      <T.Trigger asChild>{children}</T.Trigger>
      <T.Portal>
        <T.Content
          side={cote}
          sideOffset={6}
          className="z-menu max-w-xs rounded-md bg-fg px-2.5 py-1.5 text-xs font-medium text-bg shadow-md data-[state=delayed-open]:animate-fade-in"
        >
          {content}
          <T.Arrow className="fill-fg" />
        </T.Content>
      </T.Portal>
    </T.Root>
  );
}
