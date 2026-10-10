import { estAVous, executantEffectif, nomParticipant } from '../../domaine';
import { LIBELLE_CRITICITE } from '../../format';
import type { Etape, Mep } from '../../types';
import { Avatar } from '../../ui/Avatar';
import { cn } from '../../ui/cn';
import { TON } from '../../ui/tons';
import { BORDURE_TON, etatCarte, TEXTE_TON } from './etatCarte';
import { BandeauSystemes, systemesDe } from './systemes';

export type ModeGraphe = 'edition' | 'execution';

interface Props {
  mep: Mep;
  etape: Etape;
  mode: ModeGraphe;
  descriptions: Map<string, string>;
  selectionnee?: boolean;
  estompee?: boolean;
  /** Fiche saisie par le glisser-déposer : cible de lien possible (vert) ou impossible (rouge). */
  cible?: 'ok' | 'ko' | null;
  className?: string;
}

/** Fiche d'une étape dans le graphe : systèmes, code, criticité ou état, action, responsable, durée, badges. */
export function CarteEtape({ mep, etape: e, mode, descriptions, selectionnee, estompee, cible, className }: Props) {
  const exec = mode === 'execution';
  const etat = etatCarte(e);
  const executant = executantEffectif(e);
  const nom = nomParticipant(mep, executant);
  const priseEnMain = e.executantId !== null && e.executantId !== e.responsableId;
  const systemes = systemesDe(mep, e.systemeIds);

  return (
    <div
      className={cn(
        'w-[212px] overflow-hidden rounded-lg bg-surface text-fg transition-[box-shadow,border-color,opacity] duration-150',
        e.pointDeNonRetour ? 'border-[2.5px] border-rollback-solid' : cn('border-[1.5px]', exec ? BORDURE_TON[etat.ton] : 'border-line-strong hover:border-accent'),
        exec && etat.teinte && TON[etat.ton].fond,
        selectionnee && 'border-accent shadow-[0_0_0_3px_var(--accent-soft)]',
        cible === 'ok' && 'shadow-[0_0_0_3px_var(--ok-solid)]',
        cible === 'ko' && 'shadow-[0_0_0_3px_var(--danger-solid)]',
        estompee && !selectionnee && 'opacity-35',
        className,
      )}
    >
      {e.pointDeNonRetour && (
        <div className="bg-rollback-bg px-2.5 py-1 text-[11px] font-bold tracking-[0.08em] text-rollback-fg">⛔ POINT DE NON-RETOUR</div>
      )}
      <BandeauSystemes systemes={systemes} descriptions={descriptions} />
      <div className="grid gap-1 px-2.5 pt-1.5 pb-2.5">
        <div className="flex items-center justify-between gap-2">
          <span className="font-mono text-xs text-fg-subtle">{e.code}</span>
          {exec ? (
            <span title={etat.detail} className={cn('inline-flex items-center gap-1 rounded-full border bg-surface px-2 text-[11px] font-semibold whitespace-nowrap', TEXTE_TON[etat.ton])}>
              <etat.Icone className={cn('size-3', etat.anime && 'animate-spin')} aria-hidden />{etat.libelle}
            </span>
          ) : (
            <span className="font-mono text-xs text-fg-subtle">{e.criticite ? LIBELLE_CRITICITE[e.criticite] : 'Criticité ?'}</span>
          )}
        </div>
        <p className="text-[14px] leading-snug font-semibold">{e.action || <em className="font-normal text-fg-subtle">Action à décrire</em>}</p>
        <div className="flex flex-wrap items-center gap-1.5 text-xs text-fg-muted">
          {nom ? <Avatar nom={nom} taille="xs" /> : <span className="size-5 rounded-full border border-dashed border-line-strong" aria-hidden />}
          <span className="text-fg">{nom ?? 'Sans responsable'}</span>
          {priseEnMain && <span title="A pris la main" aria-label="a pris la main">⤳</span>}
          {e.dureeEstimeeMinutes !== null && <span>· {e.dureeEstimeeMinutes} min</span>}
          {e.type === 'Sauvegarde' && <span className="rounded border px-1 text-[10.5px] font-semibold" style={{ color: 'var(--sys-0)', borderColor: 'var(--sys-0)' }}>Sauvegarde</span>}
          {e.resolutionIds.length > 0 && <span className="rounded border border-accent px-1 text-[10.5px] font-semibold text-accent-text">{e.resolutionIds.length} résol.</span>}
          {exec && estAVous(mep, e) && <span className="rounded bg-accent px-1 text-[10px] font-bold tracking-wide text-accent-fg uppercase">À vous</span>}
        </div>
      </div>
    </div>
  );
}

/** Nom accessible d'une fiche. */
export function libelleCarte(mep: Mep, e: Etape, mode: ModeGraphe): string {
  const morceaux = [`${e.code} ${e.action || 'sans libellé'}`];
  if (mode === 'execution') morceaux.push(etatCarte(e).libelle);
  const sys = systemesDe(mep, e.systemeIds).map((s) => s.nom).join(', ');
  if (sys) morceaux.push(sys);
  if (e.pointDeNonRetour) morceaux.push('point de non-retour');
  if (e.type === 'Sauvegarde') morceaux.push('sauvegarde');
  return morceaux.join(', ');
}
