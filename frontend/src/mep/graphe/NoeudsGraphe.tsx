import { Handle, Position, type NodeProps } from '@xyflow/react';
import { ChevronDown, ChevronRight, Pencil, Plus } from 'lucide-react';
import { memo, useEffect, useMemo, useRef, type CSSProperties } from 'react';
import { MarquePresence, useEditeurs } from '../../presence';
import { cn } from '../../ui/cn';
import { useGraphe } from './contexteGraphe';
import { POIGNEE_BAS, POIGNEE_HAUT } from './modeleGraphe';
import { NoeudEtape } from './NoeudEtape';
import { PresentsSection } from './PresentsGraphe';
import { CARTE, CERCLE } from './rangement';
import { CERCLE_BORD, TITRE_BORD } from './rangementBordure';
import { avancementSection, cerclesTries, libelleAvancement, type CercleSection, type Section } from './sections';
import { PuceSysteme, systemesDe } from './systemes';
import { cadreEnsemble, LigneEnsemble } from '../ensemble/LigneEnsemble';

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

/**
 * Carte d'une étape : poignées en haut (attend) et en bas (débloque), « + » dessous (après) et à droite (en parallèle).
 * Dans une section dépliée du plan, la carte se lit seulement (ni poignée, ni « + ») : on la modifie dans la vue de la section.
 */
export function NoeudCarte({ data, parentId }: NodeProps) {
  const g = useGraphe();
  const edition = g.edition && !parentId;
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
      edition={edition}
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
        isConnectable={edition}
        className={poignee(edition)}
      />
      <Handle
        type="source"
        id={POIGNEE_BAS}
        position={Position.Bottom}
        isConnectable={edition}
        title={edition ? 'Tirer vers l’étape qui doit attendre celle-ci' : undefined}
        className={poignee(edition)}
      />
      {edition && g.onAjouter && (
        <>
          <button
            type="button"
            aria-label={`${ajout} après ${e.code}`}
            title={`${ajout} après ${e.code} (elle l’attendra)`}
            aria-haspopup={g.placement ? undefined : 'menu'}
            onClick={(ev) => g.onAjouter?.({ apres: id }, id, ev.currentTarget)}
            className={cn(bouton, 'top-[calc(100%+16px)] left-1/2 -ml-3.5', visible)}
          >
            <Plus className="size-4" aria-hidden />
          </button>
          <button
            type="button"
            aria-label={`${ajout} en parallèle de ${e.code}`}
            title={`${ajout} en parallèle de ${e.code} (mêmes préalables, mêmes suivants)`}
            aria-haspopup={g.placement ? undefined : 'menu'}
            onClick={(ev) => g.onAjouter?.({ paralleleA: id }, id, ev.currentTarget)}
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
        title={deplie ? 'Replier la section' : 'Déplier la section'}
        className="nodrag nopan -mt-0.5 -ml-1.5 grid size-9 shrink-0 place-content-center rounded-lg border border-line bg-surface text-fg-muted shadow-sm hover:bg-surface-2 hover:text-fg"
      >
        <Chevron className="size-5" aria-hidden />
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
        {g.ensemble && <LigneEnsemble etapes={etapes} bloquees={g.ensemble.bloquees} />}
        <p hidden={!!g.ensemble} className="text-xs text-fg-muted tnum">
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
          title="Éditer la section (ou double-cliquer dessus)"
          className="nodrag nopan -mt-0.5 grid size-9 shrink-0 place-content-center rounded-lg bg-accent text-accent-fg shadow-sm hover:opacity-90 focus-visible:ring-2 focus-visible:ring-accent focus-visible:ring-offset-2 focus-visible:outline-none"
        >
          <Pencil className="size-4" aria-hidden />
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

/**
 * Cercle d'une section dépliée, sur sa bordure comme sur une section repliée : le point est sur le bord (8 px dans la
 * boîte du rangement, côté extérieur) et l'étiquette à l'intérieur. Les liens extérieurs touchent le point ; les liens
 * intérieurs partent (ou arrivent) sous l'étiquette, au bord intérieur de la boîte. Dans le plan, seul le côté extérieur
 * se relie : l'intérieur se modifie dans la vue de la section.
 */
function CercleCadre({ cercle: c, edition }: { cercle: CercleSection; edition: boolean }) {
  const entree = c.sens === 'Entree';
  const point = entree ? CERCLE_BORD.dehors : CERCLE_BORD.hauteur - CERCLE_BORD.dehors;
  const libelle = `${entree ? 'Entrée' : 'Sortie'} ${c.titre}`;
  return (
    <div
      className="relative"
      style={{ width: CERCLE_BORD.largeur, height: CERCLE_BORD.hauteur }}
      title={c.description || c.titre}
    >
      {/* Extérieur : sur le point. Intérieur : bord intérieur de la boîte (jamais reliable depuis le plan). */}
      <Handle
        type={entree ? 'target' : 'source'}
        id={entree ? POIGNEE_HAUT : POIGNEE_BAS}
        position={entree ? Position.Top : Position.Bottom}
        isConnectable={edition}
        style={entree ? { top: point } : { top: point, bottom: 'auto' }}
        aria-label={libelle}
        className="!z-10 !size-4 !rounded-full !border-2 !border-accent !bg-surface hover:!bg-accent"
      />
      <Handle
        type={entree ? 'source' : 'target'}
        id={entree ? POIGNEE_BAS : POIGNEE_HAUT}
        position={entree ? Position.Bottom : Position.Top}
        isConnectable={false}
        className="!opacity-0"
      />
      <span
        role="img"
        aria-label={libelle}
        className={cn(
          'pointer-events-none absolute left-1/2 max-w-[128px] -translate-x-1/2 truncate rounded-full bg-accent-soft px-1.5 text-[11px] leading-4 font-semibold text-accent-text',
          entree ? 'top-[14px]' : 'bottom-[14px]',
        )}
      >
        {c.titre}
      </span>
    </div>
  );
}

/** Section repliée : un cadre (titre, systèmes, nombre d'étapes, auteur) et ses cercles sur les bords. */
/** Délai d'un clic sur une section repliée avant de la déplier : un double-clic ouvre plutôt sa vue d'édition. */
export const DELAI_DOUBLE_CLIC_MS = 250;

export function NoeudSection({ data }: NodeProps) {
  const g = useGraphe();
  const id = (data as DonneesNoeud).sectionId ?? '';
  const s = g.sections.get(id);
  const minuterie = useRef<number | undefined>(undefined);
  useEffect(() => () => window.clearTimeout(minuterie.current), []);
  if (!s) return null;
  // Sans vue d'édition, rien à attendre ; sinon le second clic d'un double-clic annule le dépliage.
  const cliquer = (detail: number) => {
    window.clearTimeout(minuterie.current);
    if (!g.onEditerSection) g.onBasculer(id);
    else if (detail <= 1) minuterie.current = window.setTimeout(() => g.onBasculer(id), DELAI_DOUBLE_CLIC_MS);
  };
  const selectionnee = g.selection === `s:${id}` || (g.enAvant?.noeuds.has(`s:${id}`) ?? false);
  const cadreEns = g.ensemble ? cadreEnsemble(g.etapesParSection.get(id) ?? [], g.ensemble.bloquees) : null;
  return (
    <div
      data-section={id}
      role="group"
      aria-label={`Section ${s.titre}, repliée`}
      className={cn(
        'group relative size-full rounded-xl border-2 bg-surface px-3 pt-9 pb-9 shadow-sm',
        selectionnee ? 'border-accent shadow-[0_0_0_3px_var(--accent-soft)]' : (cadreEns ?? 'border-accent/60'),
      )}
    >
      <Handle type="target" id={POIGNEE_HAUT} position={Position.Top} isConnectable={false} className="!opacity-0" />
      <Handle type="source" id={POIGNEE_BAS} position={Position.Bottom} isConnectable={false} className="!opacity-0" />
      {cerclesTries(s, 'Entree').map((c) => (
        <CercleBord key={c.id} cercle={c} x={g.cercles.get(c.id)} edition={g.edition} />
      ))}
      <button
        type="button"
        onClick={(ev) => cliquer(ev.detail)}
        className={cn('absolute inset-0 rounded-xl', g.edition ? 'cursor-grab' : 'cursor-pointer')}
        title={
          (g.edition
            ? 'Cliquer pour déplier ; glisser à gauche ou à droite pour changer l’ordre'
            : 'Cliquer pour déplier') + (g.onEditerSection ? ' ; double-cliquer pour ouvrir la section' : '')
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
      <PresenceSection id={id} />
    </div>
  );
}

/**
 * « X modifie » sur une section dont quelqu'un a ouvert la vue d'édition, et les avatars de qui s'y trouve (vue de la
 * section ou fiche d'une de ses étapes ouverte).
 */
function PresenceSection({ id, deplie = false }: { id: string; deplie?: boolean }) {
  const g = useGraphe();
  const editeurs = useEditeurs(`section:${id}`);
  const etapes = g.etapesParSection.get(id);
  const etapeIds = useMemo(() => (etapes ?? []).map((e) => e.id), [etapes]);
  return (
    <>
      {/* Section dépliée : titre et cercles sont sur la bordure haute ; « X modifie » va sur la bordure basse. */}
      <MarquePresence editeurs={editeurs} etiquette={deplie ? 'top-auto -bottom-3' : undefined} />
      {/* Avatars de la section dépliée : dans l'étiquette du titre, sur la bordure. */}
      {!deplie && <PresentsSection sectionId={id} titre={g.sections.get(id)?.titre ?? ''} etapeIds={etapeIds} />}
    </>
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
      className={cn(
        'relative size-full rounded-2xl border-2 border-dashed',
        g.ensemble
          ? cadreEnsemble(g.etapesParSection.get(id) ?? [], g.ensemble.bloquees)
          : 'border-accent/60 bg-accent-soft/40',
      )}
    >
      <PresenceSection id={id} deplie />
    </div>
  );
}

/**
 * Titre d'une section dépliée : étiquette posée sur sa bordure haute, à gauche des cercles d'entrée (port sans lien du
 * rangement : rien ne passe dessus). Repli, titre, nombre d'étapes (ou avancement), avatars de qui s'y trouve, édition.
 */
export function NoeudTitreCadre({ data }: NodeProps) {
  const g = useGraphe();
  const id = (data as DonneesNoeud).sectionId ?? '';
  const s = g.sections.get(id);
  const etapes = g.etapesParSection.get(id);
  const etapeIds = useMemo(() => (etapes ?? []).map((e) => e.id), [etapes]);
  if (!s) return null;
  const av = avancementSection(etapes ?? []);
  const n = etapes?.length ?? 0;
  return (
    <div
      className="flex items-center gap-1.5 rounded-full border-2 border-accent/60 bg-surface py-0.5 pr-0.5 pl-0.5 shadow-sm"
      style={{ width: TITRE_BORD.largeur, height: TITRE_BORD.hauteur }}
      title={`${s.titre}${s.auteur ? ` · par ${s.auteur}` : ''}`}
    >
      <button
        type="button"
        onClick={() => g.onBasculer(id)}
        aria-expanded
        aria-label={`Replier la section ${s.titre}`}
        title="Replier la section"
        className="nodrag nopan grid size-7 shrink-0 place-content-center rounded-full text-fg-muted hover:bg-surface-2 hover:text-fg"
      >
        <ChevronDown className="size-5" aria-hidden />
      </button>
      <p className="min-w-0 flex-1 truncate text-[13px] leading-tight font-semibold">
        {s.titre || 'Section sans titre'}
      </p>
      <span
        className={cn(
          'shrink-0 text-[11px] text-fg-muted tnum',
          g.mode === 'execution' && av.echecs > 0 && 'font-semibold text-danger-fg',
        )}
      >
        {g.mode === 'execution' ? libelleAvancement(av) : `${n} ét.`}
      </span>
      <PresentsSection sectionId={id} titre={s.titre} etapeIds={etapeIds} className="relative" />
      {g.onEditerSection && (
        <button
          type="button"
          onClick={() => g.onEditerSection?.(id)}
          aria-label={`Ouvrir la section ${s.titre}`}
          title="Éditer la section (ou double-cliquer dessus)"
          className="nodrag nopan grid size-7 shrink-0 place-content-center rounded-full bg-accent text-accent-fg shadow-sm hover:opacity-90 focus-visible:ring-2 focus-visible:ring-accent focus-visible:ring-offset-2 focus-visible:outline-none"
        >
          <Pencil className="size-4" aria-hidden />
        </button>
      )}
    </div>
  );
}

/**
 * Cercle d'une section dépliée ou de la vue de la section : un repère nommé, pas une étape. Dans le plan, seul son côté
 * extérieur se relie (le haut d'une entrée, le bas d'une sortie) : ses liens vers l'intérieur se tracent dans la vue.
 */
export function NoeudCercle({ data, parentId }: NodeProps) {
  const g = useGraphe();
  const id = (data as DonneesNoeud).cercleId ?? '';
  const sectionId = (data as DonneesNoeud).sectionId ?? '';
  const s = g.sections.get(sectionId);
  const c = s?.ports.find((x) => x.id === id);
  if (!c) return null;
  const entree = c.sens === 'Entree';
  if (parentId) return <CercleCadre cercle={c} edition={g.edition} />;
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
          isConnectable={g.edition && !(parentId && !entree)}
          className={poignee(g.edition && !(parentId && !entree))}
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
          isConnectable={g.edition && !(parentId && entree)}
          className={poignee(g.edition && !(parentId && entree))}
        />
      )}
    </div>
  );
}

/**
 * Pendant un glisser, React Flow rend le nœud glissé (et les nœuds d'une section déplacée) à chaque mouvement, avec sa
 * nouvelle position absolue. Le contenu n'en dépend pas : on ne le rend à nouveau que si autre chose change (données,
 * contexte du graphe, état de glisser), pour un glisser fluide.
 */
function horsPosition(a: NodeProps, b: NodeProps): boolean {
  for (const k of Object.keys(a) as (keyof NodeProps)[]) {
    if (k === 'positionAbsoluteX' || k === 'positionAbsoluteY') continue;
    if (a[k] !== b[k]) return false;
  }
  return Object.keys(a).length === Object.keys(b).length;
}

export const TYPES_NOEUDS = {
  etape: memo(NoeudCarte, horsPosition),
  section: memo(NoeudSection, horsPosition),
  cadre: memo(NoeudCadre, horsPosition),
  titre: memo(NoeudTitreCadre, horsPosition),
  cercle: memo(NoeudCercle, horsPosition),
};
