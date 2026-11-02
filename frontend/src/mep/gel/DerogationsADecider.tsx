import { Hourglass, Snowflake } from 'lucide-react';
import { Link } from 'react-router-dom';
import { api } from '../../api';
import { heureLocale, jourAbrege, jourLocal, plageGel } from '../../calendrier';
import { formatDateHeure } from '../../format';
import type { DerogationEnAttente } from '../../types';
import { Card } from '../../ui/Card';
import { StatusPill } from '../../ui/StatusPill';
import { useToast } from '../../ui/Toast';
import { DecisionDerogation } from './GelMep';

/** Créneau prévu de la MEP : « ven. 27/11, 21:00 → 23:30 ». */
function creneau(d: DerogationEnAttente): string {
  if (!d.debut) return 'sans date prévue';
  const jour = jourAbrege(jourLocal(d.debut, d.fuseauHoraire));
  if (d.journeeEntiere) return `${jour}, journée entière`;
  return d.fin
    ? `${jour}, ${heureLocale(d.debut, d.fuseauHoraire)} → ${heureLocale(d.fin, d.fuseauHoraire)}`
    : `${jour}, ${heureLocale(d.debut, d.fuseauHoraire)}`;
}

const systemes = (d: DerogationEnAttente) =>
  d.systemes.map((s) => (s.environnements.some((e) => e.production) ? `${s.nom} Prod` : s.nom)).join(', ');

/**
 * « Suivi en direct » d'un approbateur (#129) : les demandes de dérogation au gel qui attendent sa décision, avec la MEP,
 * le gel, le motif et la décision. `onDecide` relit le suivi.
 */
export function DerogationsADecider({
  derogations,
  onDecide,
}: {
  derogations: DerogationEnAttente[];
  onDecide: () => void;
}) {
  const toast = useToast();
  if (derogations.length === 0) return null;
  return (
    <section aria-label="Dérogations au gel à décider" className="mt-6 space-y-3">
      {derogations.map((x) => {
        const d = x.derogation;
        const titre = x.type === 'Simplifiee' || !x.versionCible.trim() ? x.outil : `${x.outil} ${x.versionCible}`;
        return (
          <Card key={d.id} className="apparition">
            <div className="flex flex-wrap items-start justify-between gap-3 border-b border-line px-5 py-4">
              <div className="min-w-0 flex-1 basis-64">
                <h2 className="flex items-center gap-2 text-md font-semibold tracking-tight">
                  <Snowflake className="size-4 shrink-0 text-run-solid" aria-hidden />
                  Dérogation demandée · {titre}
                </h2>
                <p className="mt-0.5 text-ui text-fg-subtle">
                  Par {d.demandeePar ?? 'le pilote'}, le {formatDateHeure(d.demandeeLe)} · MEP prévue {creneau(x)}
                  {x.systemes.length > 0 && ` · ${systemes(x)}`}
                </p>
              </div>
              <StatusPill ton="warn" icone={Hourglass} taille="sm">
                En attente de vous
              </StatusPill>
            </div>
            <div className="space-y-4 px-5 py-4">
              <dl className="grid gap-x-6 gap-y-1.5 text-sm sm:grid-cols-[8rem_minmax(0,1fr)]">
                <dt className="text-fg-subtle">Période</dt>
                <dd>
                  {d.nomPeriode} ({plageGel({ debut: d.occurrence, fin: d.occurrenceFin }, x.fuseauHoraire)})
                </dd>
                <dt className="text-fg-subtle">Motif</dt>
                <dd className="whitespace-pre-line">{d.motif}</dd>
                <dt className="text-fg-subtle">Plan</dt>
                <dd>
                  {x.nbEtapes} étape{x.nbEtapes > 1 ? 's' : ''}
                  {x.retourArriereActive ? ', retour arrière prévu' : ', sans retour arrière'} ·{' '}
                  <Link to={`/meps/${x.mepId}`} className="font-medium text-accent-text hover:underline">
                    ouvrir la MEP
                  </Link>
                  {x.pilote && <span className="text-fg-subtle"> · pilote : {x.pilote}</span>}
                </dd>
              </dl>
              <DecisionDerogation
                decider={async (accorder, commentaire) => {
                  try {
                    await api.deciderDerogation(x.mepId, d.id, accorder, commentaire);
                    toast.succes(accorder ? `Dérogation accordée pour ${titre}.` : `Dérogation refusée pour ${titre}.`);
                    onDecide();
                    return true;
                  } catch (e) {
                    toast.erreur(e instanceof Error ? e.message : 'Décision impossible.');
                    return false;
                  }
                }}
              />
            </div>
          </Card>
        );
      })}
    </section>
  );
}
