import { X } from 'lucide-react';
import { Dialog as D } from 'radix-ui';
import type { ReactNode } from 'react';
import { BoutonIcone } from '../../ui/Button';
import { cn } from '../../ui/cn';

/**
 * Espace d'une étape (édition ou exécution) : surcouche large, barre du haut, trois colonnes visibles ensemble.
 * Seule la colonne centrale (instructions) est faite pour défiler ; sous 1100 px, les colonnes s'empilent.
 */
export function Surcouche({ etiquette, onDemandeFermeture, barre, gauche, centre, droite }: {
  etiquette: string;
  /** Échap, ✕ ou clic hors de l'espace : le parent décide (garde si saisie en cours). */
  onDemandeFermeture: () => void;
  barre: ReactNode;
  gauche: ReactNode;
  centre: ReactNode;
  droite: ReactNode;
}) {
  return (
    <D.Root open onOpenChange={(o) => { if (!o) onDemandeFermeture(); }}>
      <D.Portal>
        <D.Overlay className="fixed inset-0 z-40 bg-black/20 data-[state=open]:animate-fade-in" />
        <D.Content
          aria-describedby={undefined}
          className="fixed inset-y-2 right-2 z-50 flex w-[calc(100vw-1rem)] flex-col overflow-hidden rounded-2xl border border-line bg-surface shadow-lg outline-none data-[state=open]:animate-slide-in min-[1100px]:w-[92vw]"
        >
          <D.Title className="sr-only">{etiquette}</D.Title>
          <div className="flex shrink-0 items-start gap-3 border-b border-line px-4 py-3">
            <div className="min-w-0 flex-1">{barre}</div>
            <D.Close asChild>
              <BoutonIcone label="Fermer" taille="sm"><X className="size-4" aria-hidden /></BoutonIcone>
            </D.Close>
          </div>
          <div className="min-h-0 flex-1 overflow-y-auto min-[1100px]:grid min-[1100px]:grid-cols-[15.5rem_minmax(0,1fr)_21rem] min-[1100px]:overflow-hidden 2xl:grid-cols-[17rem_minmax(0,1fr)_25rem]">
            <Colonne className="min-[1100px]:border-r">{gauche}</Colonne>
            <Colonne principale>{centre}</Colonne>
            <Colonne className="min-[1100px]:border-l">{droite}</Colonne>
          </div>
        </D.Content>
      </D.Portal>
    </D.Root>
  );
}

function Colonne({ children, principale, className }: { children: ReactNode; principale?: boolean; className?: string }) {
  return (
    <div className={cn('grid min-h-0 min-w-0 grid-cols-[minmax(0,1fr)] content-start border-line p-4 min-[1100px]:overflow-y-auto', principale ? 'gap-5 bg-surface-2/30' : 'gap-4', className)}>
      {children}
    </div>
  );
}

/** Bloc titré d'une colonne. */
export function Bloc({ titre, children, action }: { titre: string; children: ReactNode; action?: ReactNode }) {
  return (
    <section className="grid min-w-0 grid-cols-[minmax(0,1fr)] gap-2">
      <div className="flex items-center justify-between gap-2">
        <h3 className="text-[11px] font-semibold tracking-[0.06em] text-fg-subtle uppercase">{titre}</h3>
        {action}
      </div>
      {children}
    </section>
  );
}
