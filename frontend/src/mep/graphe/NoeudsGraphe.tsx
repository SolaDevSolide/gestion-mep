import { Handle, Position, type NodeProps } from '@xyflow/react';
import { ChevronDown, ChevronRight, Pencil, Plus } from 'lucide-react';
import type { CSSProperties } from 'react';
import { cn } from '../../ui/cn';
import { useGraphe } from './contexteGraphe';
import { POIGNEE_BAS, POIGNEE_HAUT } from './modeleGraphe';
import { NoeudEtape } from './NoeudEtape';
import { CARTE, CERCLE, TITRE_CADRE } from './rangement';
import { avancementSection, cerclesTries, libelleAvancement, type CercleSection, type Section } from './sections';
import { PuceSysteme, systemesDe } from './systemes';

export interface DonneesNoeud extends Record<string, unknown> {
  etapeId?: string;
  sectionId?: string;
  cercleId?: string;
}

/** Poignée de lien : rond accentué en édition, invisible sinon (les liens s'y accrochent toujours). */
function poignee(edition: boolean, extra?: string) {
  return cn(
    '!size-3.5 !rounded-full !border-2 !border-accent !bg-surface transition-[opacity,transform] duration-150',
    edition ? 'opacity-60 group-hover:opacity-100 hover:!bg-accent' : '!pointer-events-none opacity-0',
    extra,
  );
}

const bouton =
  'nodrag nopan absolute grid size-7 place-content-center rounded-full border-[1.5px] border-dashed bg-surface text-accent-text shadow-sm transition-opacity duration-150 hover:border-solid hover:bg-accent hover:text-accent-fg focus-visible:opacity-100';

/** Carte d'une étape : poignées en haut (attend) et en bas (débloque), « + » dessous (après) et à droite (en parallèle). */
export function NoeudCarte({ data }: NodeProps) {
  const g = useGraphe();
  const id = (data as DonneesNoeud).etapeId ?? '';
  const e = g.etapes.get(id);
  if (!e) return <div style={{ width: CARTE.largeur, height: CARTE.hauteur }} aria-hidden />;
  const selectionnee = g.selection === id;
  const visee = g.cible?.type === 'carte' && g.cible.id === id;
  const visible = selectionnee || g.placement ? 'opacity-100' : 'opacity-0 group-hover:opacity-100';
  const ajout = g.placement ? 'Poser la copie' : 'Ajouter une étape';
  return (
    <NoeudEtape
      mep={g.mep}
      etape={e}
      mode={g.mode}
      edition={g.edition}
      descriptions={g.descriptions}
      selectionnee={selectionnee}
      estompee={g.estEstompee?.(e)}
      glissee={g.glisse === id}
      cible={visee ? 'ok' : null}
      enAvant={g.enAvant?.noeuds.has(id) ?? false}
      onActiver={() => g.onActiver(id)}
    >
      <Handle
        type="target"
        id={POIGNEE_HAUT}
        position={Position.Top}
        isConnectable={g.edition}
        className={poignee(g.edition)}
      />
      <Handle
        type="source"
        id={POIGNEE_BAS}
        position={Position.Bottom}
        isConnectable={g.edition}
        title="Tirer vers l’étape qui doit attendre celle-ci"
        className={poignee(g.edition)}
      />
      {g.edition && g.onAjouter && (
        <>
          <button
            type="button"
            aria-label={`${ajout} après ${e.code}`}
            title={`${ajout} après ${e.code} (elle l’attendra)`}
            onClick={() => g.onAjouter?.({ apres: id }, id)}
            className={cn(bouton, 'top-[calc(100%+16px)] left-1/2 -ml-3.5', visible)}
          >
            <Plus className="size-4" aria-hidden />
          </button>
          <button
            type="button"
            aria-label={`${ajout} en parallèle de ${e.code}`}
            title={`${ajout} en parallèle de ${e.code} (mêmes préalables, mêmes suivants)`}
            onClick={() => g.onAjouter?.({ paralleleA: id }, id)}
            className={cn(bouton, 'top-1/2 left-[calc(100%+10px)] -mt-3.5', visible)}
          >
            <Plus className="size-4" aria-hidden />
          </button>
        </>
      )}
    </NoeudEtape>
  );
}

/** En-tête d'une section : titre, systèmes, nombre d'étapes (ou avancement en exécution), auteur, boutons. */
function EnTeteSection({ section, deplie }: { section: Section; deplie: boolean }) {
  const g = useGraphe();
  const etapes = g.etapesParSection.get(section.id) ?? [];
  const systemes = systemesDe(g.mep, section.systemeIds);
  const av = avancementSection(etapes);
  const Chevron = deplie ? ChevronDown : ChevronRight;
  return (
    <div className="flex min-w-0 items-start gap-2">
      <button
        type="button"
        onClick={() => g.onBasculer(section.id)}
        aria-expanded={deplie}
        aria-label={`${deplie ? 'Replier' : 'Déplier'} la section ${section.titre}`}
        className="nodrag nopan -ml-1 grid size-7 shrink-0 place-content-center rounded-md text-fg-muted hover:bg-surface-2 hover:text-fg"
      >
        <Chevron className="size-4" aria-hidden />
      </button>
      <div className="grid min-w-0 flex-1 gap-1">
        <p className="truncate text-[14px] leading-tight font-semibold" title={section.titre}>
          {section.titre || 'Section sans titre'}
        </p>
        <div className="flex flex-wrap items-center gap-1">
          {systemes.map((s) => (
            <PuceSysteme key={s.id} nom={s.nom} className="px-1.5 py-0 text-[11px]" />
          ))}
        </div>
        <p className="text-xs text-fg-muted tnum">
          {g.mode === 'execution' ? (
            <span
              className={cn(
                'font-semibold',
                av.echecs > 0 ? 'text-danger-fg' : av.reussies === av.total && av.total > 0 ? 'text-ok-fg' : '',
              )}
            >
              {libelleAvancement(av)}
            </span>
          ) : (
            <>
              {etapes.length} étape{etapes.length > 1 ? 's' : ''}
            </>
          )}
          {section.auteur && <> · par {section.auteur}</>}
        </p>
      </div>
      {g.onEditerSection && (
        <button
          type="button"
          onClick={() => g.onEditerSection?.(section.id)}
          aria-label={`Ouvrir la section ${section.titre}`}
          title="Ouvrir la vue de la section"
          className="nodrag nopan grid size-7 shrink-0 place-content-center rounded-md text-fg-muted hover:bg-surface-2 hover:text-fg"
        >
          <Pencil className="size-3.5" aria-hidden />
        </button>
      )}
    </div>
  );
}

/** Cercle nommé posé sur le bord d'une section repliée. */
function CercleBord({ cercle, x, edition }: { cercle: CercleSection; x: number | undefined; edition: boolean }) {
  const entree = cercle.sens === 'Entree';
  const style: CSSProperties = { left: x ?? '50%' };
  return (
    <>
      <Handle
        type={entree ? 'target' : 'source'}
        id={cercle.id}
        position={entree ? Position.Top : Position.Bottom}
        isConnectable={edition}
        style={style}
        title={cercle.description || cercle.titre}
        aria-label={`${entree ? 'Entrée' : 'Sortie'} ${cercle.titre}`}
        className="!z-10 !size-4 !rounded-full !border-2 !border-accent !bg-surface hover:!bg-accent"
      />
      <span
        className={cn(
          'pointer-events-none absolute max-w-[128px] -translate-x-1/2 truncate rounded-full bg-accent-soft px-1.5 text-[11px] font-semibold text-accent-text',
          entree ? 'top-2.5' : 'bottom-2.5',
        )}
        style={style}
      >
        {cercle.titre}
      </span>
    </>
  );
}

/** Section repliée : un cadre (titre, systèmes, nombre d'étapes, auteur) et ses cercles sur les bords. */
export function NoeudSection({ data }: NodeProps) {
  const g = useGraphe();
  const id = (data as DonneesNoeud).sectionId ?? '';
  const s = g.sections.get(id);
  if (!s) return null;
  const selectionnee = g.selection === `s:${id}` || (g.enAvant?.noeuds.has(`s:${id}`) ?? false);
  return (
    <div
      data-section={id}
      role="group"
      aria-label={`Section ${s.titre}, repliée`}
      className={cn(
        'group relative size-full rounded-xl border-2 bg-surface px-3 pt-9 pb-9 shadow-sm',
        selectionnee ? 'border-accent shadow-[0_0_0_3px_var(--accent-soft)]' : 'border-accent/60',
      )}
    >
      <Handle type="target" id={POIGNEE_HAUT} position={Position.Top} isConnectable={false} className="!opacity-0" />
      <Handle type="source" id={POIGNEE_BAS} position={Position.Bottom} isConnectable={false} className="!opacity-0" />
      {cerclesTries(s, 'Entree').map((c) => (
        <CercleBord key={c.id} cercle={c} x={g.cercles.get(c.id)} edition={g.edition} />
      ))}
      <button
        type="button"
        onClick={() => g.onBasculer(id)}
        className={cn('absolute inset-0 rounded-xl', g.edition ? 'cursor-grab' : 'cursor-pointer')}
        title={
          g.edition
            ? 'Cliquer pour déplier ; glisser à gauche ou à droite pour changer l’ordre'
            : 'Cliquer pour déplier'
        }
        aria-hidden
        tabIndex={-1}
      />
      <div className="pointer-events-none relative [&_button]:pointer-events-auto">
        <EnTeteSection section={s} deplie={false} />
      </div>
      {cerclesTries(s, 'Sortie').map((c) => (
        <CercleBord key={c.id} cercle={c} x={g.cercles.get(c.id)} edition={g.edition} />
      ))}
    </div>
  );
}

/** Section dépliée : cadre qui contient ses étapes et ses cercles (rendus par React Flow comme nœuds enfants). */
export function NoeudCadre({ data }: NodeProps) {
  const g = useGraphe();
  const id = (data as DonneesNoeud).sectionId ?? '';
  const s = g.sections.get(id);
  if (!s) return null;
  return (
    <div
      data-section={id}
      role="group"
      aria-label={`Section ${s.titre}, dépliée`}
      className="size-full rounded-2xl border-2 border-dashed border-accent/60 bg-accent-soft/40"
    />
  );
}

/** Titre d'une section dépliée : un nœud rangé dans le cadre, à gauche des cercles d'entrée ; les liens le contournent. */
export function NoeudTitreCadre({ data }: NodeProps) {
  const g = useGraphe();
  const s = g.sections.get((data as DonneesNoeud).sectionId ?? '');
  if (!s) return null;
  return (
    <div className="pt-0.5" style={{ width: TITRE_CADRE.largeur, height: TITRE_CADRE.hauteur }}>
      <EnTeteSection section={s} deplie />
    </div>
  );
}

/** Cercle d'une section dépliée ou de la vue de la section : un repère nommé, pas une étape. */
export function NoeudCercle({ data }: NodeProps) {
  const g = useGraphe();
  const id = (data as DonneesNoeud).cercleId ?? '';
  const sectionId = (data as DonneesNoeud).sectionId ?? '';
  const s = g.sections.get(sectionId);
  const c = s?.ports.find((x) => x.id === id);
  if (!c) return null;
  const entree = c.sens === 'Entree';
  const vue = g.sectionVue !== null;
  const ouvrir = g.onOuvrirCercle;
  const contenu = (
    <>
      <span className="size-4 shrink-0 rounded-full border-2 border-accent bg-surface" aria-hidden />
      <span className="truncate">{c.titre}</span>
    </>
  );
  return (
    <div
      className="group relative flex items-center justify-center"
      style={{ width: CERCLE.largeur, height: CERCLE.hauteur }}
      title={c.description || undefined}
    >
      {(!vue || !entree) && (
        <Handle
          type="target"
          id={POIGNEE_HAUT}
          position={Position.Top}
          isConnectable={g.edition}
          className={poignee(g.edition)}
        />
      )}
      {ouvrir ? (
        <button
          type="button"
          onClick={() => ouvrir(id)}
          aria-label={`${entree ? 'Entrée' : 'Sortie'} ${c.titre} : modifier`}
          className="nodrag nopan flex max-w-full items-center gap-1.5 rounded-full border border-accent bg-accent-soft px-2 py-1 text-xs font-semibold text-accent-text shadow-sm hover:bg-accent hover:text-accent-fg"
        >
          {contenu}
        </button>
      ) : (
        <span
          role="img"
          aria-label={`${entree ? 'Entrée' : 'Sortie'} ${c.titre}`}
          className="flex max-w-full items-center gap-1.5 rounded-full border border-accent bg-accent-soft px-2 py-1 text-xs font-semibold text-accent-text"
        >
          {contenu}
        </span>
      )}
      {(!vue || entree) && (
        <Handle
          type="source"
          id={POIGNEE_BAS}
          position={Position.Bottom}
          isConnectable={g.edition}
          className={poignee(g.edition)}
        />
      )}
    </div>
  );
}

export const TYPES_NOEUDS = {
  etape: NoeudCarte,
  section: NoeudSection,
  cadre: NoeudCadre,
  titre: NoeudTitreCadre,
  cercle: NoeudCercle,
};
