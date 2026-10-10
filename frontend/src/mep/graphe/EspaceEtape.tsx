import { Maximize2, PanelRight, X } from 'lucide-react';
import { Dialog as D } from 'radix-ui';
import { useSyncExternalStore, type ReactNode } from 'react';
import { BoutonIcone } from '../../ui/Button';
import { cn } from '../../ui/cn';

// ---------- Mode d'affichage de l'espace d'une étape : plein écran ou panneau latéral (mémorisé) ----------

export type ModeEspace = 'plein' | 'panneau';
const CLE = 'mep.espace.mode';
const abonnes = new Set<() => void>();

function lireMode(): ModeEspace {
  try {
    return window.localStorage.getItem(CLE) === 'panneau' ? 'panneau' : 'plein';
  } catch {
    return 'plein';
  }
}
let modeCourant: ModeEspace | null = null;

export function changerModeEspace(m: ModeEspace) {
  modeCourant = m;
  try { window.localStorage.setItem(CLE, m); } catch { /* stockage indisponible */ }
  abonnes.forEach((f) => f());
}

/** Mode courant, partagé entre l'espace et la vue du graphe (qui laisse la place au panneau). */
export function useModeEspace(): ModeEspace {
  return useSyncExternalStore(
    (f) => { abonnes.add(f); return () => { abonnes.delete(f); }; },
    () => (modeCourant ??= lireMode()),
    () => 'plein',
  );
}

/** Largeur du panneau latéral (le graphe garde le reste, à gauche). */
export const LARGEUR_PANNEAU = 'min(600px, 100vw - 1rem)';

/**
 * Espace d'une étape (édition ou exécution).
 * - Plein écran : surcouche large, barre du haut, trois colonnes visibles ensemble ; seule la colonne centrale défile.
 * - Panneau latéral : le graphe reste utilisable à gauche ; les colonnes s'enchaînent dans un défilement unique.
 * Sous 1100 px de large, le plein écran empile aussi les colonnes.
 */
export function Surcouche({ etiquette, onDemandeFermeture, barre, gauche, centre, droite }: {
  etiquette: string;
  /** Échap, ✕ ou clic hors de l'espace (plein écran) : le parent décide (garde si saisie en cours). */
  onDemandeFermeture: () => void;
  barre: ReactNode;
  gauche: ReactNode;
  centre: ReactNode;
  droite: ReactNode;
}) {
  const mode = useModeEspace();
  const plein = mode === 'plein';
  return (
    <D.Root open modal={plein} onOpenChange={(o) => { if (!o) onDemandeFermeture(); }}>
      <D.Portal>
        {plein && <D.Overlay className="fixed inset-0 z-40 bg-black/20 data-[state=open]:animate-fade-in" />}
        <D.Content
          aria-describedby={undefined}
          // Panneau : le graphe reste utilisable, un clic dessus ne ferme pas l'espace.
          onInteractOutside={plein ? undefined : (e) => e.preventDefault()}
          style={plein ? undefined : { width: LARGEUR_PANNEAU }}
          className={cn(
            'fixed inset-y-2 right-2 z-50 flex flex-col overflow-hidden rounded-2xl border border-line bg-surface shadow-lg outline-none data-[state=open]:animate-slide-in',
            plein && 'w-[calc(100vw-1rem)] min-[1100px]:w-[92vw]',
          )}
        >
          <D.Title className="sr-only">{etiquette}</D.Title>
          <div className="flex shrink-0 items-start gap-2 border-b border-line px-4 py-3">
            <div className="min-w-0 flex-1">{barre}</div>
            <BoutonIcone
              label={plein ? 'Afficher en panneau latéral' : 'Afficher en plein écran'}
              taille="sm"
              onClick={() => changerModeEspace(plein ? 'panneau' : 'plein')}
            >
              {plein ? <PanelRight className="size-4" aria-hidden /> : <Maximize2 className="size-4" aria-hidden />}
            </BoutonIcone>
            <D.Close asChild>
              <BoutonIcone label="Fermer" taille="sm"><X className="size-4" aria-hidden /></BoutonIcone>
            </D.Close>
          </div>
          <div className={cn('min-h-0 flex-1 overflow-y-auto', plein && 'min-[1100px]:grid min-[1100px]:grid-cols-[17rem_minmax(0,1fr)_23rem] min-[1100px]:overflow-hidden 2xl:grid-cols-[19rem_minmax(0,1fr)_27rem]')}>
            <Colonne plein={plein} className={plein ? 'min-[1100px]:border-r' : 'border-b'}>{gauche}</Colonne>
            <Colonne plein={plein} principale className={plein ? undefined : 'border-b'}>{centre}</Colonne>
            <Colonne plein={plein} className={plein ? 'min-[1100px]:border-l' : undefined}>{droite}</Colonne>
          </div>
        </D.Content>
      </D.Portal>
    </D.Root>
  );
}

function Colonne({ children, principale, plein, className }: { children: ReactNode; principale?: boolean; plein: boolean; className?: string }) {
  return (
    <div
      className={cn(
        'grid min-h-0 min-w-0 grid-cols-[minmax(0,1fr)] content-start border-line p-4',
        plein && 'min-[1100px]:overflow-y-auto',
        principale ? 'gap-5 bg-surface-2/30' : 'gap-4',
        className,
      )}
    >
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
