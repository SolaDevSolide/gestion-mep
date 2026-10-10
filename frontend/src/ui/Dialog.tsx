import { X } from 'lucide-react';
import { Dialog as D } from 'radix-ui';
import { useState, type ReactNode } from 'react';
import { Button, BoutonIcone, type VarianteBouton } from './Button';
import { cn } from './cn';

const LARGEURS = { sm: 'max-w-md', md: 'max-w-xl', lg: 'max-w-3xl', xl: 'max-w-5xl' } as const;

interface DialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  titre: ReactNode;
  description?: ReactNode;
  taille?: keyof typeof LARGEURS;
  pied?: ReactNode;
  children?: ReactNode;
  className?: string;
}

export function Dialog({
  open,
  onOpenChange,
  titre,
  description,
  taille = 'md',
  pied,
  children,
  className,
}: DialogProps) {
  return (
    <D.Root open={open} onOpenChange={onOpenChange}>
      <D.Portal>
        <D.Overlay className="fixed inset-0 z-50 grid place-items-center overflow-y-auto bg-black/50 p-4 backdrop-blur-[2px] data-[state=open]:animate-fade-in">
          <D.Content
            className={cn(
              'relative my-auto w-full rounded-2xl border border-line bg-surface shadow-lg outline-none data-[state=open]:animate-pop-in',
              LARGEURS[taille],
              className,
            )}
            aria-describedby={description ? undefined : undefined}
          >
            <div className="flex items-start justify-between gap-4 px-6 pt-5">
              <div className="min-w-0">
                <D.Title className="text-lg font-semibold tracking-tight text-fg">{titre}</D.Title>
                {description ? (
                  <D.Description className="mt-1 text-sm text-fg-muted">{description}</D.Description>
                ) : (
                  <D.Description className="sr-only">
                    {typeof titre === 'string' ? titre : 'Boîte de dialogue'}
                  </D.Description>
                )}
              </div>
              <D.Close asChild>
                <BoutonIcone label="Fermer" taille="sm" className="-mt-1 -mr-2">
                  <X className="size-4" aria-hidden />
                </BoutonIcone>
              </D.Close>
            </div>
            <div className="px-6 py-5">{children}</div>
            {pied && (
              <div className="flex flex-wrap items-center justify-end gap-2 rounded-b-2xl border-t border-line bg-surface-2/60 px-6 py-3.5">
                {pied}
              </div>
            )}
          </D.Content>
        </D.Overlay>
      </D.Portal>
    </D.Root>
  );
}

interface ConfirmProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  titre: ReactNode;
  description?: ReactNode;
  children?: ReactNode;
  libelleConfirmer: string;
  variante?: VarianteBouton;
  desactive?: boolean;
  /** Retourne true si l'action a abouti (le dialogue se ferme alors). */
  onConfirmer: () => Promise<boolean | void> | boolean | void;
  taille?: keyof typeof LARGEURS;
}

/** Confirmation en dialogue (jamais window.confirm). */
export function ConfirmDialog({
  open,
  onOpenChange,
  titre,
  description,
  children,
  libelleConfirmer,
  variante = 'primaire',
  desactive,
  onConfirmer,
  taille = 'sm',
}: ConfirmProps) {
  const [occupe, setOccupe] = useState(false);
  async function confirmer() {
    setOccupe(true);
    try {
      const r = await onConfirmer();
      if (r !== false) onOpenChange(false);
    } finally {
      setOccupe(false);
    }
  }
  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      titre={titre}
      description={description}
      taille={taille}
      pied={
        <>
          <Button variante="fantome" onClick={() => onOpenChange(false)}>
            Annuler
          </Button>
          <Button variante={variante} chargement={occupe} disabled={desactive} onClick={() => void confirmer()}>
            {libelleConfirmer}
          </Button>
        </>
      }
    >
      {children}
    </Dialog>
  );
}
