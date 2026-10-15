import type { ReactNode } from 'react';
import { MarquePresence, useEditeurs } from '../../presence';
import type { Etape, Mep } from '../../types';
import { cn } from '../../ui/cn';
import { CarteEtape, libelleCarte, type ModeGraphe } from './CarteEtape';

interface Props {
  mep: Mep;
  etape: Etape;
  mode: ModeGraphe;
  edition: boolean;
  descriptions: Map<string, string>;
  selectionnee: boolean;
  estompee?: boolean;
  /** Carte en cours de glisser (estompée pendant le déplacement). */
  glissee: boolean;
  /** Pendant un glisser : carte visée, cible valide ou non. */
  cible: 'ok' | 'ko' | null;
  /** Mise en avant (lien survolé qui l'atteint). */
  enAvant?: boolean;
  onActiver: () => void;
  /** Poignées de liens et boutons de création rapide, posés autour de la carte. */
  children?: ReactNode;
}

/** Carte du graphe activable (souris, clavier, lecteur d'écran), avec qui d'autre la modifie. */
export function NoeudEtape({
  mep,
  etape: e,
  mode,
  edition,
  descriptions,
  selectionnee,
  estompee,
  glissee,
  cible,
  enAvant = false,
  onActiver,
  children,
}: Props) {
  const editeurs = useEditeurs(`etape:${e.id}`);
  return (
    <div data-noeud={e.id} className={cn('group relative', glissee && 'opacity-60')}>
      <div
        data-carte={e.id}
        role="button"
        tabIndex={0}
        aria-pressed={selectionnee}
        aria-label={libelleCarte(mep, e, mode)}
        onKeyDown={(ev) => {
          if (ev.key === 'Enter' || ev.key === ' ') {
            ev.preventDefault();
            onActiver();
          }
        }}
        onClick={onActiver}
        className={cn(
          'relative rounded-lg outline-none focus-visible:ring-2 focus-visible:ring-accent focus-visible:ring-offset-2',
          edition ? 'cursor-grab' : 'cursor-pointer',
          enAvant && !selectionnee && 'ring-2 ring-accent ring-offset-2 ring-offset-surface',
        )}
      >
        <CarteEtape
          mep={mep}
          etape={e}
          mode={mode}
          descriptions={descriptions}
          selectionnee={selectionnee}
          estompee={estompee}
          cible={cible}
        />
        <MarquePresence editeurs={editeurs} />
      </div>
      {children}
    </div>
  );
}
