import { ClipboardList } from 'lucide-react';
import { formatDateHeure } from '../format';
import type { Bilan } from '../types';
import { Card, CardHeader } from '../ui/Card';

/** Bilan de clôture, affiché en tête d'une MEP clôturée. */
export function BilanCarte({ bilan }: { bilan: Bilan }) {
  const blocs: [string, string][] = [
    ['Ce qui a marché', bilan.ceQuiAMarche],
    ['Incidents', bilan.incidents],
    ['Actions', bilan.actions],
  ];
  return (
    <Card className="mx-6 mt-4">
      <CardHeader
        titre={
          <span className="flex items-center gap-2">
            <ClipboardList className="size-4 text-accent-text" aria-hidden />
            Bilan de clôture
          </span>
        }
        description={
          bilan.redigePar
            ? `Rédigé par ${bilan.redigePar}${bilan.le ? ` le ${formatDateHeure(bilan.le)}` : ''}`
            : undefined
        }
      />
      <dl className="grid gap-x-8 gap-y-4 p-5 md:grid-cols-3">
        {blocs.map(([k, v]) => (
          <div key={k}>
            <dt className="text-xs font-semibold tracking-wide text-fg-subtle uppercase">{k}</dt>
            <dd className="mt-1 text-sm whitespace-pre-line">
              {v.trim() || <span className="text-fg-subtle">Non renseigné</span>}
            </dd>
          </div>
        ))}
      </dl>
    </Card>
  );
}
