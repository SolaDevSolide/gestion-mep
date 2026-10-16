import type { ReactNode } from 'react';
import { cn } from './cn';

/**
 * Chargements : un squelette tient la place du contenu à venir, aux mêmes dimensions (la page ne saute pas quand il arrive),
 * puis le contenu apparaît en fondu (`Apparition`). Animations coupées si le système demande moins de mouvement.
 */

/** Bloc gris qui respire doucement. */
export function Squelette({ className }: { className?: string }) {
  return <div aria-hidden className={cn('squelette rounded-md bg-sunken', className)} />;
}

/**
 * Zone en chargement : annoncée aux lecteurs d'écran (`libelle`), son squelette n'apparaît qu'après un court délai (un
 * chargement rapide ne clignote pas).
 */
export function EnChargement({
  libelle = 'Chargement…',
  className,
  children,
}: {
  libelle?: string;
  className?: string;
  children: ReactNode;
}) {
  return (
    <div role="status" aria-busy="true" aria-label={libelle} className={cn('squelette-zone', className)}>
      {children}
    </div>
  );
}

const LARGEURS = ['w-11/12', 'w-4/5', 'w-2/3', 'w-3/4', 'w-1/2', 'w-5/6'];

/** Lignes de texte. */
export function SqueletteLignes({ lignes = 3, className }: { lignes?: number; className?: string }) {
  return (
    <div className={cn('grid gap-2', className)}>
      {Array.from({ length: lignes }, (_, i) => (
        <Squelette key={i} className={cn('h-4', LARGEURS[i % LARGEURS.length])} />
      ))}
    </div>
  );
}

/** Tableau : en-tête et lignes (hauteur d'une ligne `hauteurLigne`, en classes Tailwind). */
export function SqueletteTableau({
  lignes = 6,
  colonnes = 4,
  hauteurLigne = 'h-11',
  className,
}: {
  lignes?: number;
  colonnes?: number;
  hauteurLigne?: string;
  className?: string;
}) {
  const ligne = (i: number, entete = false) => (
    <div
      key={i}
      className={cn('grid items-center gap-4 border-b border-line px-4 last:border-b-0', entete ? 'h-9' : hauteurLigne)}
      style={{ gridTemplateColumns: `2fr repeat(${colonnes - 1}, 1fr)` }}
    >
      {Array.from({ length: colonnes }, (_, j) => (
        <Squelette
          key={j}
          className={cn(
            entete ? 'h-3 w-16' : 'h-4',
            !entete && (j === 0 ? 'w-4/5' : LARGEURS[(i + j) % LARGEURS.length]),
          )}
        />
      ))}
    </div>
  );
  return (
    <div className={cn('overflow-hidden rounded-xl border border-line bg-surface', className)}>
      {ligne(-1, true)}
      {Array.from({ length: lignes }, (_, i) => ligne(i))}
    </div>
  );
}

/** Grille de cartes (`hauteur` : classe Tailwind d'une carte). */
export function SqueletteCartes({
  cartes = 6,
  hauteur = 'h-36',
  className,
}: {
  cartes?: number;
  hauteur?: string;
  className?: string;
}) {
  return (
    <div className={cn('grid gap-3 sm:grid-cols-2 xl:grid-cols-3', className)}>
      {Array.from({ length: cartes }, (_, i) => (
        <div key={i} className={cn('grid content-start gap-3 rounded-xl border border-line bg-surface p-4', hauteur)}>
          <Squelette className="h-4 w-2/5" />
          <Squelette className="h-5 w-4/5" />
          <SqueletteLignes lignes={2} />
        </div>
      ))}
    </div>
  );
}

/** Formulaire : libellés et champs. */
export function SqueletteFormulaire({ champs = 4, className }: { champs?: number; className?: string }) {
  return (
    <div className={cn('grid gap-4', className)}>
      {Array.from({ length: champs }, (_, i) => (
        <div key={i} className="grid gap-1.5">
          <Squelette className="h-3.5 w-28" />
          <Squelette className="h-9 w-full" />
        </div>
      ))}
    </div>
  );
}

/** Chronologie (journal) : un jour, puis des événements avec leur pastille. */
export function SqueletteChronologie({ evenements = 6, className }: { evenements?: number; className?: string }) {
  return (
    <div className={cn('grid gap-3', className)}>
      <Squelette className="h-3 w-40" />
      <div className="border-l border-line">
        {Array.from({ length: evenements }, (_, i) => (
          <div key={i} className="relative flex py-2.5 pl-8">
            <Squelette className="absolute top-3 -left-[15px] size-[30px] rounded-full" />
            <div className="grid flex-1 gap-1.5">
              <Squelette className={cn('h-4', LARGEURS[i % LARGEURS.length])} />
              <Squelette className="h-3 w-48" />
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

/** Lignes d'un vrai tableau en chargement (dans son `<tbody aria-busy>`), sous son en-tête déjà affiché. */
export function SqueletteRangees({ lignes = 6, colonnes }: { lignes?: number; colonnes: number }) {
  return Array.from({ length: lignes }, (_, i) => (
    <tr key={i} aria-hidden>
      {Array.from({ length: colonnes }, (_, j) => (
        <td key={j} className="px-4 py-3.5">
          <Squelette className={cn('h-4', LARGEURS[(i + j) % LARGEURS.length])} />
        </td>
      ))}
    </tr>
  ));
}
