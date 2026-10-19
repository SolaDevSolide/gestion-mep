import { ClipboardList, FileText, XCircle } from 'lucide-react';
import { Link } from 'react-router-dom';
import { formatDateHeure } from '../format';
import type { Bilan, ClotureInfo } from '../types';
import { Alerte } from '../ui/Alerte';
import { Card, CardHeader } from '../ui/Card';

/**
 * Bilan de clôture, affiché en tête d'une MEP clôturée, avec le lien vers son rapport de fin (#61). Clôturée en échec
 * (`echec`, #93) : l'état du service laissé en production et la référence de l'incident en tête.
 */
export function BilanCarte({ bilan, mepId, echec }: { bilan: Bilan; mepId?: string; echec?: ClotureInfo | null }) {
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
        action={
          mepId && (
            <Link
              to={`/meps/${mepId}/rapport`}
              className="inline-flex h-8 items-center gap-1.5 rounded-lg border border-line-strong bg-surface px-3 text-ui font-medium shadow-sm hover:bg-surface-2"
            >
              <FileText className="size-4" aria-hidden />
              Rapport de fin
            </Link>
          )
        }
      />
      {echec && (
        <div className="px-5 pt-5">
          <Alerte
            ton="danger"
            icone={XCircle}
            titre={`Service dégradé${echec.referenceIncident ? ` · incident ${echec.referenceIncident}` : ''}`}
          >
            {echec.etatService}
          </Alerte>
        </div>
      )}
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
