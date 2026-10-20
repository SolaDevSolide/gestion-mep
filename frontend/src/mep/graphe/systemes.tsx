import { X } from 'lucide-react';
import { couleurSysteme, styleSysteme } from '../../couleursSysteme';
import { environnementsEtape } from '../../environnements';
import type { Etape, Mep, Systeme, SystemeCatalogue } from '../../types';
import { cn } from '../../ui/cn';
import { PastilleEnvironnement } from '../../ui/PastilleEnvironnement';

/** Description affichée d'un système de la MEP : celle du catalogue, sinon la cible. */
export function descriptionsSystemes(
  mep: Pick<Mep, 'systemes'>,
  catalogue: SystemeCatalogue[] | null | undefined,
): Map<string, string> {
  const parCatalogue = new Map((catalogue ?? []).map((c) => [c.id, c.description || c.nom]));
  return new Map(mep.systemes.map((s) => [s.id, (s.catalogueId && parCatalogue.get(s.catalogueId)) || s.cible || '']));
}

/** Systèmes de la MEP touchés par une étape, dans l'ordre de la MEP. */
export const systemesDe = (mep: Pick<Mep, 'systemes'>, ids: string[]): Systeme[] =>
  mep.systemes.filter((s) => ids.includes(s.id));

/** Puce colorée d'un système (sections, panneaux). */
export function PuceSysteme({
  nom,
  description,
  onRetirer,
  className,
}: {
  nom: string;
  description?: string;
  onRetirer?: () => void;
  className?: string;
}) {
  return (
    <span
      className={cn(
        'inline-flex max-w-full items-center gap-1.5 rounded-full border-[1.5px] px-2.5 py-0.5 text-ui font-semibold',
        className,
      )}
      style={styleSysteme(nom)}
    >
      <i className="size-2 shrink-0 rounded-full" style={{ background: couleurSysteme(nom) }} aria-hidden />
      <span className="truncate">
        {nom}
        {description && <small className="font-normal"> · {description}</small>}
      </span>
      {onRetirer && (
        <button
          type="button"
          onClick={onRetirer}
          aria-label={`Retirer ${nom} de la MEP`}
          title="Retirer de la MEP"
          className="-mr-1 rounded-full px-0.5 leading-none hover:opacity-70"
        >
          <X className="size-3.5" aria-hidden />
        </button>
      )}
    </span>
  );
}

/**
 * Bandeau des systèmes en tête d'une fiche : couleur par système, partagé si plusieurs. Chaque système porte les
 * environnements de l'étape (« BDD Préprod Prod ») ; un environnement retiré du système depuis est barré. Sans
 * environnement, un système seul affiche sa description.
 */
export function BandeauSystemes({
  systemes,
  descriptions,
  etape,
}: {
  systemes: Systeme[];
  descriptions: Map<string, string>;
  etape?: Pick<Etape, 'environnements'>;
}) {
  if (systemes.length === 0) {
    return (
      <div className="flex">
        <span className="flex-1 bg-wait-bg px-2 py-1 text-xs font-bold text-fg-subtle">Aucun système</span>
      </div>
    );
  }
  return (
    <div className="flex flex-wrap">
      {systemes.map((s) => {
        const envs = etape ? environnementsEtape(etape, s) : [];
        return (
          <span
            key={s.id}
            className="flex min-w-0 flex-auto items-center gap-1.5 truncate px-2 py-1 text-xs font-bold whitespace-nowrap"
            style={{ ...styleSysteme(s.nom), borderColor: undefined }}
          >
            <i className="size-[7px] shrink-0 rounded-full" style={{ background: couleurSysteme(s.nom) }} aria-hidden />
            {s.nom}
            {envs.map((e) => (
              <PastilleEnvironnement
                key={e.nom}
                nom={e.nom}
                production={e.production}
                retire={e.retire}
                abrege
                className="border-transparent bg-transparent px-0.5 text-2xs font-semibold"
              />
            ))}
            {systemes.length === 1 && envs.length === 0 && descriptions.get(s.id) && (
              <small className="truncate font-normal">· {descriptions.get(s.id)}</small>
            )}
          </span>
        );
      })}
    </div>
  );
}
