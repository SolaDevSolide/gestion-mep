import type { KeyboardEvent } from 'react';
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
  /** Fiche d'origine d'un glisser de fiche (estompée pendant le déplacement). */
  glissee: boolean;
  /** Pendant le tirage d'un lien : fiche survolée, cible valide ou non. */
  cible: 'ok' | 'ko' | null;
  onClavier: (ev: KeyboardEvent<HTMLDivElement>) => void;
  onActiver: () => void;
}

/** Fiche du graphe activable (souris, clavier, lecteur d'écran) avec, en édition, ses ronds pour tirer un lien. */
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
  onClavier,
  onActiver,
}: Props) {
  const editeurs = useEditeurs(`etape:${e.id}`);
  return (
    <div
      data-noeud={e.id}
      // Calé en haut de la ligne, à la hauteur de sa fiche : les ronds restent collés à ses bords même si une fiche
      // voisine est plus haute (sinon le rond du bas suivrait la hauteur de la ligne).
      className={cn('group pointer-events-auto relative self-start', edition && 'touch-none', glissee && 'opacity-30')}
    >
      {edition && (
        <span
          data-port="haut"
          title="Tirer vers une étape au-dessus"
          className="absolute -top-[7px] left-1/2 z-[2] -ml-[7px] size-3.5 cursor-crosshair rounded-full border-2 border-accent bg-surface opacity-55 transition-[opacity,transform] duration-150 group-hover:scale-110 group-hover:opacity-100 hover:bg-accent"
        />
      )}
      <div
        data-carte={e.id}
        role="button"
        tabIndex={0}
        aria-pressed={selectionnee}
        aria-label={libelleCarte(mep, e, mode)}
        aria-keyshortcuts={
          edition
            ? 'Alt+ArrowUp Alt+ArrowDown Alt+ArrowLeft Alt+ArrowRight Alt+Shift+ArrowUp Alt+Shift+ArrowDown'
            : undefined
        }
        onKeyDown={onClavier}
        // La souris passe par le pointeur (appuyer / relâcher) ; un clic sans pointeur (detail 0) vient d'un lecteur d'écran.
        onClick={(ev) => {
          if (ev.detail === 0) onActiver();
        }}
        className={cn('relative rounded-lg', edition ? 'cursor-grab' : 'cursor-pointer')}
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
      {edition && (
        <span
          data-port="bas"
          title="Tirer vers une étape en dessous"
          className="absolute -bottom-[7px] left-1/2 z-[2] -ml-[7px] size-3.5 cursor-crosshair rounded-full border-2 border-accent bg-surface opacity-55 transition-[opacity,transform] duration-150 group-hover:scale-110 group-hover:opacity-100 hover:bg-accent"
        />
      )}
    </div>
  );
}
