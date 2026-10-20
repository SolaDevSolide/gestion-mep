import {
  CheckCircle2,
  ClipboardCheck,
  DatabaseBackup,
  Info,
  ListChecks,
  TriangleAlert,
  UserCheck,
  type LucideIcon,
} from 'lucide-react';
import { Button } from '../ui/Button';
import { Popover, PopoverContent, PopoverTrigger } from '../ui/Popover';
import type { Mep } from '../types';

/** Icône d'une anomalie selon son thème (approbation, recette, sauvegarde pendant la MEP). */
export function iconeAnomalie(texte: string): LucideIcon {
  if (/approbation|a refusé :|approuver/i.test(texte)) return UserCheck;
  if (/test de recette/i.test(texte)) return ClipboardCheck;
  if (/^Sauvegarde pendant/i.test(texte)) return DatabaseBackup;
  return TriangleAlert;
}

/** Liste d'avertissements non bloquants (en ambre, à part des anomalies). */
export function ListeAvertissements({ avertissements, className }: { avertissements: string[]; className?: string }) {
  if (avertissements.length === 0) return null;
  return (
    <section aria-label="Avertissements" className={className}>
      <p className="px-1 pb-1.5 text-xs font-semibold tracking-wide text-warn-fg uppercase">
        Avertissements <span className="font-normal tracking-normal normal-case">· ne bloquent pas le Go</span>
      </p>
      <ul className="space-y-1">
        {avertissements.map((a) => (
          <li
            key={a}
            className="flex items-start gap-2 rounded-lg border border-dashed border-warn-bd px-2.5 py-1.5 text-ui text-warn-fg"
          >
            <Info className="mt-0.5 size-3.5 shrink-0 text-warn-solid" aria-hidden />
            {a}
          </li>
        ))}
      </ul>
    </section>
  );
}

/** Bouton « Vérifier le plan » : pastille avec le nombre d'anomalies, popover listant erreursPlan puis les avertissements. */
export function PlanCheck({ mep }: { mep: Mep }) {
  const n = mep.erreursPlan.length;
  const nAvert = mep.avertissements.length;
  return (
    <Popover>
      <PopoverTrigger asChild>
        <Button
          icone={<ListChecks className="size-4" aria-hidden />}
          aria-label={`Vérifier le plan : ${n === 0 ? 'aucune anomalie' : `${n} anomalie(s)`}${nAvert > 0 ? `, ${nAvert} avertissement(s)` : ''}`}
        >
          Vérifier le plan
          {n > 0 ? (
            <span className="ml-0.5 inline-flex h-5 min-w-5 items-center justify-center rounded-full bg-warn-solid px-1.5 text-2xs font-bold text-warn-solid-fg tnum">
              {n}
            </span>
          ) : (
            <CheckCircle2 className="ml-0.5 size-4 text-ok-solid" aria-hidden />
          )}
          {nAvert > 0 && (
            <span
              className="inline-flex h-5 min-w-5 items-center justify-center gap-0.5 rounded-full border border-dashed border-warn-bd px-1.5 text-2xs font-bold text-warn-fg tnum"
              title="Avertissements non bloquants"
            >
              <Info className="size-3" aria-hidden />
              {nAvert}
            </span>
          )}
        </Button>
      </PopoverTrigger>
      <PopoverContent align="end" className="w-[min(28rem,calc(100vw-2rem))] p-3">
        <div className="max-h-96 space-y-3 overflow-y-auto">
          {n === 0 ? (
            <p className="flex items-center gap-2 px-1 py-1 text-sm text-ok-fg">
              <CheckCircle2 className="size-4 text-ok-solid" aria-hidden /> Le plan est complet : aucune anomalie.
            </p>
          ) : (
            <section aria-label="Anomalies">
              <p className="px-1 pb-2 text-sm font-semibold">{n} point(s) à corriger avant le Go</p>
              <ul className="space-y-1">
                {mep.erreursPlan.map((e) => {
                  const Icone = iconeAnomalie(e);
                  return (
                    <li
                      key={e}
                      className="flex items-start gap-2 rounded-lg bg-warn-bg px-2.5 py-1.5 text-ui text-warn-fg"
                    >
                      <Icone className="mt-0.5 size-3.5 shrink-0" aria-hidden />
                      {e}
                    </li>
                  );
                })}
              </ul>
            </section>
          )}
          <ListeAvertissements avertissements={mep.avertissements} />
        </div>
      </PopoverContent>
    </Popover>
  );
}
