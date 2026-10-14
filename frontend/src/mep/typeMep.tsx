import { Feather } from 'lucide-react';
import type { Etape, Mep, ResolutionType } from '../types';
import { cn } from '../ui/cn';
import { Badge } from '../ui/StatusPill';

/**
 * MEP simplifiée : un pilote dont toutes les autres personnes sont suppléantes (le serveur les renvoie déjà dans
 * `suppleantIds`), ni versions, ni durée, ni criticité, ni résolutions liées aux étapes.
 */
export const estSimplifiee = (mep: Pick<Mep, 'type'>) => mep.type === 'Simplifiee';

/** Repère du type : affiché seulement pour une MEP simplifiée (une MEP complète reste comme avant). */
export function BadgeSimplifiee({ className }: { className?: string }) {
  return (
    <Badge ton="done" className={cn('text-xs', className)}>
      <Feather className="size-3.5" aria-hidden />
      MEP simplifiée
    </Badge>
  );
}

/** Groupe de résolutions proposées le jour J : un système de l'étape, ou « Tous les systèmes » (résolutions sans système). */
export interface GroupeResolutions {
  /** Nom du système de la MEP ; null pour les résolutions qui ne visent aucun système. */
  systeme: string | null;
  resolutions: ResolutionType[];
}

/**
 * Résolutions proposées pour une étape d'une MEP simplifiée (même règle que le serveur) : toutes les résolutions actives des
 * systèmes de l'étape, regroupées par système, puis celles qui ne visent aucun système. Une résolution qui vise plusieurs
 * systèmes de l'étape n'apparaît que dans le premier.
 */
export function resolutionsParSysteme(
  mep: Pick<Mep, 'systemes'>,
  etape: Pick<Etape, 'systemeIds'>,
  catalogue: ResolutionType[],
): GroupeResolutions[] {
  const actives = catalogue.filter((r) => r.actif);
  const vues = new Set<string>();
  const groupes: GroupeResolutions[] = [];
  for (const s of mep.systemes.filter((x) => etape.systemeIds.includes(x.id))) {
    const liste = actives.filter(
      (r) => !!s.catalogueId && r.systemeCatalogueIds.includes(s.catalogueId) && !vues.has(r.id),
    );
    liste.forEach((r) => vues.add(r.id));
    if (liste.length > 0) groupes.push({ systeme: s.nom, resolutions: liste });
  }
  const communes = actives.filter((r) => r.systemeCatalogueIds.length === 0);
  if (communes.length > 0) groupes.push({ systeme: null, resolutions: communes });
  return groupes;
}
