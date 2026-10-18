import { cn } from '../../ui/cn';
import { Squelette, SqueletteChronologie, SqueletteLignes } from '../../ui/Squelette';

/**
 * Squelettes des écrans de suivi (#61), aux dimensions du contenu final : la page ne saute pas quand il arrive (voir
 * `ui/Squelette.tsx`).
 */

/** Rangée de chiffres (tuiles). */
function Tuiles({ nombre, className }: { nombre: number; className?: string }) {
  return (
    <div className={cn('grid grid-cols-2 gap-3', className)}>
      {Array.from({ length: nombre }, (_, i) => (
        <div key={i} className="grid h-[86px] content-start gap-2 rounded-xl border border-line bg-surface px-4 py-3">
          <Squelette className="h-3 w-24" />
          <Squelette className="h-6 w-16" />
          <Squelette className="h-3.5 w-28" />
        </div>
      ))}
    </div>
  );
}

/** Vue d'ensemble d'une MEP : chiffres clés, puis le cadre du graphe et son panneau de droite. */
export function SqueletteEnsemble() {
  return (
    <div className="mx-auto grid w-full max-w-[1700px] gap-4 px-4 py-5 sm:px-6">
      <Tuiles nombre={6} className="lg:grid-cols-6" />
      <div className="flex h-[clamp(520px,calc(100vh-380px),940px)] overflow-hidden rounded-xl border border-line bg-surface">
        <div className="flex-1">
          <div className="flex h-12 items-center gap-3 border-b border-line bg-surface-2/60 px-3">
            <Squelette className="h-5 w-24 rounded-full" />
            <Squelette className="h-4 w-96 max-w-full" />
          </div>
        </div>
        <div className="grid w-[340px] shrink-0 content-start gap-5 border-l border-line p-4">
          <Squelette className="h-3 w-32" />
          <SqueletteLignes lignes={3} />
          <Squelette className="h-3 w-28" />
          <SqueletteLignes lignes={4} />
        </div>
      </div>
    </div>
  );
}

/** Événements marquants, avant la première lecture du journal. */
export function SqueletteEvenements() {
  return <SqueletteChronologie evenements={4} />;
}

/** Tableau de bord Suivi en direct : quatre chiffres, des cartes de MEP, les deux listes. */
export function SqueletteSuivi() {
  return (
    <div>
      <Tuiles nombre={4} className="mt-6 lg:grid-cols-4" />
      <div className="mt-6 space-y-3">
        {[0, 1].map((i) => (
          <div key={i} className="grid h-[148px] content-start gap-3 rounded-xl border border-line bg-surface p-4">
            <div className="flex items-center gap-3">
              <Squelette className="h-5 w-56" />
              <Squelette className="h-5 w-24 rounded-full" />
              <Squelette className="ml-auto h-8 w-32 rounded-lg" />
            </div>
            <Squelette className="h-2.5 w-full rounded-full" />
            <SqueletteLignes lignes={2} />
          </div>
        ))}
      </div>
      <div className="mt-6 grid gap-3 lg:grid-cols-2">
        {[0, 1].map((i) => (
          <div key={i} className="grid h-40 content-start gap-3 rounded-xl border border-line bg-surface p-4">
            <Squelette className="h-4 w-48" />
            <SqueletteLignes lignes={3} />
          </div>
        ))}
      </div>
    </div>
  );
}

/** Rapport de fin : la page du rapport, titre, chiffres, plan et premières parties. */
export function SqueletteRapport() {
  return (
    <div className="mx-auto mt-4 grid max-w-[1040px] gap-6 rounded-xl border border-line bg-surface p-8">
      <div className="grid gap-2">
        <Squelette className="h-3 w-32" />
        <Squelette className="h-8 w-96 max-w-full" />
        <Squelette className="h-4 w-[28rem] max-w-full" />
      </div>
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
        {[0, 1, 2, 3].map((i) => (
          <Squelette key={i} className="h-16" />
        ))}
      </div>
      <Squelette className="mx-auto h-[420px] w-full max-w-[700px] rounded-lg" />
      <div className="grid gap-3">
        <Squelette className="h-5 w-48" />
        <SqueletteLignes lignes={5} />
      </div>
    </div>
  );
}
