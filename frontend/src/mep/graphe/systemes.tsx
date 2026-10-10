import { X } from 'lucide-react';
import { couleurSysteme, styleSysteme } from '../../couleursSysteme';
import type { Mep, Systeme, SystemeCatalogue } from '../../types';
import { cn } from '../../ui/cn';

/** Description affichée d'un système de la MEP : celle du catalogue, sinon la cible. */
export function descriptionsSystemes(mep: Pick<Mep, 'systemes'>, catalogue: SystemeCatalogue[] | null | undefined): Map<string, string> {
  const parCatalogue = new Map((catalogue ?? []).map((c) => [c.id, c.description || c.nom]));
  return new Map(mep.systemes.map((s) => [s.id, (s.catalogueId && parCatalogue.get(s.catalogueId)) || s.cible || '']));
}

/** Systèmes de la MEP touchés par une étape, dans l'ordre de la MEP. */
export const systemesDe = (mep: Pick<Mep, 'systemes'>, ids: string[]): Systeme[] => mep.systemes.filter((s) => ids.includes(s.id));

/** Puce colorée d'un système (barre « Systèmes impactés », panneaux). */
export function PuceSysteme({ nom, description, onRetirer, className }: { nom: string; description?: string; onRetirer?: () => void; className?: string }) {
  return (
    <span className={cn('inline-flex max-w-full items-center gap-1.5 rounded-full border-[1.5px] px-2.5 py-0.5 text-[13px] font-semibold', className)} style={styleSysteme(nom)}>
      <i className="size-2 shrink-0 rounded-full" style={{ background: couleurSysteme(nom) }} aria-hidden />
      <span className="truncate">{nom}{description && <small className="font-normal"> · {description}</small>}</span>
      {onRetirer && (
        <button type="button" onClick={onRetirer} aria-label={`Retirer ${nom} de la MEP`} title="Retirer de la MEP" className="-mr-1 rounded-full px-0.5 leading-none hover:opacity-70">
          <X className="size-3.5" aria-hidden />
        </button>
      )}
    </span>
  );
}

/** Bandeau des systèmes en tête d'une fiche : couleur par système, partagé si plusieurs, nom + description si un seul. */
export function BandeauSystemes({ systemes, descriptions }: { systemes: Systeme[]; descriptions: Map<string, string> }) {
  if (systemes.length === 0) {
    return <div className="flex"><span className="flex-1 bg-wait-bg px-2 py-1 text-[11.5px] font-bold text-fg-subtle">Aucun système</span></div>;
  }
  return (
    <div className="flex flex-wrap">
      {systemes.map((s) => (
        <span key={s.id} className="flex min-w-0 flex-auto items-center gap-1.5 truncate px-2 py-1 text-[11.5px] font-bold whitespace-nowrap" style={{ ...styleSysteme(s.nom), borderColor: undefined }}>
          <i className="size-[7px] shrink-0 rounded-full" style={{ background: couleurSysteme(s.nom) }} aria-hidden />
          {s.nom}
          {systemes.length === 1 && descriptions.get(s.id) && <small className="truncate font-normal">· {descriptions.get(s.id)}</small>}
        </span>
      ))}
    </div>
  );
}
