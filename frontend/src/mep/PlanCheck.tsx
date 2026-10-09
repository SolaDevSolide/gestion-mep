import { CheckCircle2, ListChecks, TriangleAlert } from 'lucide-react';
import { Button } from '../ui/Button';
import { Popover, PopoverContent, PopoverTrigger } from '../ui/Popover';
import type { Mep } from '../types';

/** Bouton « Vérifier le plan » : pastille avec le nombre d'anomalies, popover listant erreursPlan. */
export function PlanCheck({ mep }: { mep: Mep }) {
  const n = mep.erreursPlan.length;
  return (
    <Popover>
      <PopoverTrigger asChild>
        <Button icone={<ListChecks className="size-4" aria-hidden />} aria-label={`Vérifier le plan : ${n === 0 ? 'aucune anomalie' : `${n} anomalie(s)`}`}>
          Vérifier le plan
          {n > 0 ? (
            <span className="ml-0.5 inline-flex h-5 min-w-5 items-center justify-center rounded-full bg-warn-solid px-1.5 text-[11px] font-bold text-white tnum">{n}</span>
          ) : (
            <CheckCircle2 className="ml-0.5 size-4 text-ok-solid" aria-hidden />
          )}
        </Button>
      </PopoverTrigger>
      <PopoverContent align="end" className="w-[min(28rem,calc(100vw-2rem))] p-3">
        {n === 0 ? (
          <p className="flex items-center gap-2 px-1 py-1 text-sm text-ok-fg"><CheckCircle2 className="size-4 text-ok-solid" aria-hidden /> Le plan est complet : aucune anomalie.</p>
        ) : (
          <>
            <p className="px-1 pb-2 text-sm font-semibold">{n} point(s) à corriger avant l’exécution</p>
            <ul className="max-h-72 space-y-1 overflow-y-auto">
              {mep.erreursPlan.map((e) => (
                <li key={e} className="flex items-start gap-2 rounded-lg bg-warn-bg px-2.5 py-1.5 text-[13px] text-warn-fg">
                  <TriangleAlert className="mt-0.5 size-3.5 shrink-0" aria-hidden />{e}
                </li>
              ))}
            </ul>
          </>
        )}
      </PopoverContent>
    </Popover>
  );
}
