import { Maximize, ZoomIn, ZoomOut } from 'lucide-react';
import { createPortal } from 'react-dom';
import { Fragment, useMemo, useRef } from 'react';
import { construireLignes, groupesDeLiens, indexLignes, liensDuPlan, type GroupeLiens, type Lien } from '../../graphe';
import type { Etape, Mep, TypePlan } from '../../types';
import { BoutonIcone } from '../../ui/Button';
import { cn } from '../../ui/cn';
import { CarteEtape, type ModeGraphe } from './CarteEtape';
import { CoucheLiens } from './CoucheLiens';
import { NoeudEtape } from './NoeudEtape';
import { transformClone, useGlisserDeposer } from './useGlisserDeposer';
import { useMesureCartes } from './useMesureCartes';
import { PAS_ZOOM, ZOOM_MAX, ZOOM_MIN, useRecentrerSelection, useZoomVue } from './useZoomVue';

interface Props {
  mep: Mep;
  plan: TypePlan;
  /** Étapes du plan. */
  etapes: Etape[];
  mode: ModeGraphe;
  /** Édition : glisser-déposer, ronds de liens, blocs fantômes, sélection des liens. */
  modifiable?: boolean;
  descriptions: Map<string, string>;
  selection: string | null;
  onSelection: (id: string | null) => void;
  groupeSelection?: string | null;
  onGroupeSelection?: (cle: string | null) => void;
  /** Déplacement d'une fiche (ligne affichée, position dans la ligne). */
  onPlacer?: (id: string, ligne: number, position: number, auClavier?: boolean) => void;
  /** Lien particulier : `vers` attendra `de` (validité déjà vérifiée). */
  onLier?: (de: string, vers: string) => void;
  /** Bloc fantôme : ligne existante, ou null pour une nouvelle ligne en bas. */
  onAjouter?: (ligne: number | null) => void;
  onMessage?: (message: string) => void;
  estEstompee?: (e: Etape) => boolean;
}

/**
 * Graphe des étapes par lignes (maquette jalon 4) : lignes empilées et centrées, liens et jonctions en SVG derrière
 * les fiches, barrière du point de non-retour. En édition : glisser-déposer, ronds pour tirer un lien, blocs fantômes.
 */
export function GrapheEtapes(props: Props) {
  const {
    mep,
    plan,
    etapes,
    mode,
    modifiable = false,
    descriptions,
    selection,
    onSelection,
    groupeSelection = null,
    onGroupeSelection,
    onAjouter,
    estEstompee,
  } = props;
  const edition = mode === 'edition' && modifiable;

  const lignes = useMemo(() => construireLignes(etapes), [etapes]);
  const ligneDe = useMemo(() => indexLignes(lignes), [lignes]);
  const liens = useMemo(() => liensDuPlan(etapes), [etapes]);
  const groupes = useMemo(() => groupesDeLiens(liens), [liens]);
  const parId = useMemo(() => new Map(etapes.map((e) => [e.id, e])), [etapes]);

  const zone = useRef<HTMLDivElement>(null);
  const graphe = useRef<HTMLDivElement>(null);
  const zoomRef = useRef(1);
  const geo = useMesureCartes(zone, graphe, zoomRef);
  const { zoom, zoomer, ajuster, vueSaisie, vue } = useZoomVue(zone, graphe, zoomRef, geo);
  useRecentrerSelection(zone, graphe, selection);
  const { glisse, clone, trace, positionClone, appuyer, clavierCarte } = useGlisserDeposer({
    zone,
    graphe,
    zoomRef,
    vue,
    edition,
    etapes,
    lignes,
    ligneDe,
    liens,
    onSelection,
    onGroupeSelection,
    onPlacer: props.onPlacer,
    onLier: props.onLier,
    onMessage: props.onMessage,
  });

  const glisseCarte = glisse?.t === 'carte' ? glisse : null;
  const glisseLien = glisse?.t === 'lien' ? glisse : null;

  const rendreLigne = (ids: Etape[]) =>
    ids.map((e) => (
      <NoeudEtape
        key={e.id}
        mep={mep}
        etape={e}
        mode={mode}
        edition={edition}
        descriptions={descriptions}
        selectionnee={selection === e.id}
        estompee={estEstompee?.(e)}
        glissee={glisseCarte?.id === e.id}
        cible={glisseLien && glisseLien.survol === e.id ? (glisseLien.possible ? 'ok' : 'ko') : null}
        onClavier={(ev) => clavierCarte(ev, e.id)}
        onActiver={() => {
          onGroupeSelection?.(null);
          onSelection(e.id);
        }}
      />
    ));

  const sansFin = lignes.length;

  return (
    <div className="relative min-w-0">
      <div
        ref={zone}
        onPointerDown={appuyer}
        className={cn(
          // Gouttière réservée : la largeur utile ne dépend pas de la barre verticale (le graphe la prend pour minimum).
          // Largeur confinée : la zone prend la largeur de la page, jamais celle du graphe (zoomé ou mesuré avant un
          // changement), sinon la page s'élargit, une barre de défilement apparaît et la mise en page boucle.
          'max-h-[940px] overflow-auto rounded-xl bg-surface shadow-sm [contain:inline-size] [scrollbar-gutter:stable]',
          plan === 'RetourArriere' ? 'border-2 border-rollback-solid' : 'border border-line',
          vueSaisie ? 'cursor-grabbing select-none' : 'cursor-grab',
        )}
      >
        <div
          className="relative"
          style={{
            width: geo.largeur ? geo.largeur * zoom : undefined,
            height: geo.hauteur ? geo.hauteur * zoom : undefined,
          }}
        >
          <div
            ref={graphe}
            className="absolute top-0 left-0 flex flex-col gap-[46px] px-[150px] pt-7 pb-14 select-none"
            style={{
              transform: zoom !== 1 ? `scale(${zoom})` : undefined,
              transformOrigin: '0 0',
              minWidth: geo.zone ? geo.zone / zoom : undefined,
            }}
          >
            <CoucheLiens
              groupes={groupes}
              ancrages={geo.ancrages}
              parId={parId}
              mode={mode}
              edition={edition}
              groupeSelection={groupeSelection}
              onSelection={onSelection}
              onGroupeSelection={onGroupeSelection}
              tirage={!!glisseLien}
              trace={trace}
            />
            {glisseCarte?.cible && (
              <div
                aria-hidden
                className="pointer-events-none absolute z-[3] w-2 rounded bg-accent shadow-[0_0_0_3px_var(--accent-soft)]"
                style={{
                  left: glisseCarte.cible.x - 4,
                  top: glisseCarte.cible.haut,
                  height: Math.max(40, glisseCarte.cible.bas - glisseCarte.cible.haut),
                }}
              />
            )}

            {lignes.map((ids, r) => {
              const pnr = ids.filter((e) => e.pointDeNonRetour);
              return (
                <Fragment key={r}>
                  <div data-ligne={r} className="pointer-events-none relative z-[1] flex justify-center">
                    <div className="relative flex min-h-[60px] items-stretch gap-[22px] rounded-[10px]">
                      {rendreLigne(ids)}
                      {edition && onAjouter && (
                        <button
                          type="button"
                          onClick={() => onAjouter(r)}
                          aria-label={`Ajouter une étape sur la ligne ${r + 1}`}
                          className="pointer-events-auto absolute top-0 bottom-0 left-[calc(100%+22px)] grid w-28 place-content-center gap-0.5 rounded-lg border-[1.5px] border-dashed border-line-strong text-[13px] text-fg-subtle transition-colors hover:border-accent hover:bg-accent-soft hover:text-accent-text"
                        >
                          <span className="text-xl leading-none text-accent-text" aria-hidden>
                            +
                          </span>
                          Étape
                        </button>
                      )}
                    </div>
                  </div>
                  {pnr.length > 0 && r < lignes.length - 1 && (
                    <div role="note" className="pointer-events-none relative z-[1] -mx-[150px] -my-6 grid items-center">
                      <div className="col-start-1 row-start-1 border-t-[3px] border-dashed border-rollback-solid" />
                      <div className="col-start-1 row-start-1 justify-self-center rounded-full border-[1.5px] border-rollback-solid bg-surface px-3 py-0.5 text-xs font-bold whitespace-nowrap text-rollback-solid">
                        ⛔ {pnr.map((e) => e.code).join(', ')} : après ce point, plus de retour arrière
                      </div>
                    </div>
                  )}
                </Fragment>
              );
            })}

            {edition && onAjouter && (
              <div data-ligne={sansFin} className="pointer-events-none relative z-[1] flex justify-center">
                <div className="relative flex items-stretch gap-[22px] rounded-[10px]">
                  <button
                    type="button"
                    onClick={() => onAjouter(null)}
                    className="pointer-events-auto grid h-[58px] w-[212px] place-content-center gap-0.5 rounded-lg border-[1.5px] border-dashed border-line-strong text-[13px] text-fg-subtle transition-colors hover:border-accent hover:bg-accent-soft hover:text-accent-text"
                  >
                    <span className="text-xl leading-none text-accent-text" aria-hidden>
                      +
                    </span>
                    Nouvelle ligne
                  </button>
                </div>
              </div>
            )}
            {lignes.length === 0 && !edition && (
              <p className="py-10 text-center text-sm text-fg-subtle">Aucune étape dans ce plan.</p>
            )}
          </div>
        </div>
      </div>

      <div
        data-controles
        role="toolbar"
        aria-label="Vue du graphe"
        className="absolute right-3 bottom-3 z-20 flex items-center gap-0.5 rounded-lg border border-line bg-surface/95 p-0.5 shadow-md backdrop-blur"
      >
        <BoutonIcone label="Dézoomer" taille="sm" disabled={zoom <= ZOOM_MIN} onClick={() => zoomer(zoom / PAS_ZOOM)}>
          <ZoomOut className="size-4" aria-hidden />
        </BoutonIcone>
        <button
          type="button"
          onClick={() => zoomer(1)}
          title="Revenir à 100 %"
          aria-label={`Zoom ${Math.round(zoom * 100)} % : revenir à 100 %`}
          className="h-8 min-w-12 rounded-md px-1.5 text-xs font-semibold text-fg-muted tnum hover:bg-surface-2 hover:text-fg"
        >
          {Math.round(zoom * 100)} %
        </button>
        <BoutonIcone label="Zoomer" taille="sm" disabled={zoom >= ZOOM_MAX} onClick={() => zoomer(zoom * PAS_ZOOM)}>
          <ZoomIn className="size-4" aria-hidden />
        </BoutonIcone>
        <BoutonIcone label="Ajuster : tout voir" taille="sm" onClick={ajuster}>
          <Maximize className="size-4" aria-hidden />
        </BoutonIcone>
      </div>
      <p className="sr-only">Glissez le fond du graphe pour vous déplacer ; Ctrl + molette pour zoomer.</p>

      {glisseCarte &&
        parId.get(glisseCarte.id) &&
        createPortal(
          <div
            ref={clone}
            aria-hidden
            className="pointer-events-none fixed z-50 rounded-lg opacity-90 shadow-lg"
            style={{
              left: 0,
              top: 0,
              transform: transformClone(positionClone.current, zoom),
              transformOrigin: '0 0',
              willChange: 'transform',
            }}
          >
            <CarteEtape mep={mep} etape={parId.get(glisseCarte.id)!} mode={mode} descriptions={descriptions} />
          </div>,
          document.body,
        )}
    </div>
  );
}

/** Liens d'un groupe sélectionné, à partir de sa clé (null si le groupe n'existe plus). */
export function groupeDepuisCle(etapes: Etape[], cle: string | null): GroupeLiens | null {
  if (!cle) return null;
  return groupesDeLiens(liensDuPlan(etapes)).find((g) => g.cle === cle) ?? null;
}

export type { GroupeLiens, Lien };
