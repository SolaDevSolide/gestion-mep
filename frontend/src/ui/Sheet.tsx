import { X } from 'lucide-react';
import { Dialog as D } from 'radix-ui';
import type { ReactNode } from 'react';
import { BoutonIcone } from './Button';
import { cn } from './cn';

interface SheetProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  titre: ReactNode;
  description?: ReactNode;
  pied?: ReactNode;
  children?: ReactNode;
  className?: string;
}

/** Panneau latéral droit (édition d'une étape, etc.). */
export function Sheet({ open, onOpenChange, titre, description, pied, children, className }: SheetProps) {
  return (
    <D.Root open={open} onOpenChange={onOpenChange}>
      <D.Portal>
        <D.Overlay className="fixed inset-0 z-50 bg-black/45 data-[state=open]:animate-fade-in" />
        <D.Content
          className={cn(
            'fixed inset-y-0 right-0 z-50 flex w-full max-w-[560px] flex-col border-l border-line bg-surface shadow-lg outline-none data-[state=open]:animate-slide-in',
            className,
          )}
        >
          <div className="flex items-start justify-between gap-4 border-b border-line px-6 py-4">
            <div className="min-w-0">
              <D.Title className="text-lg font-semibold tracking-tight text-fg">{titre}</D.Title>
              {description ? (
                <D.Description className="mt-0.5 text-sm text-fg-muted">{description}</D.Description>
              ) : (
                <D.Description className="sr-only">Panneau d’édition</D.Description>
              )}
            </div>
            <D.Close asChild>
              <BoutonIcone label="Fermer" taille="sm"><X className="size-4" aria-hidden /></BoutonIcone>
            </D.Close>
          </div>
          <div className="min-h-0 flex-1 overflow-y-auto px-6 py-5">{children}</div>
          {pied && <div className="flex flex-wrap items-center justify-end gap-2 border-t border-line bg-surface-2/60 px-6 py-3.5">{pied}</div>}
        </D.Content>
      </D.Portal>
    </D.Root>
  );
}
