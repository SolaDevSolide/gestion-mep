import { CheckCircle2, Circle, FlaskConical, OctagonAlert, XCircle } from 'lucide-react';
import { api } from '../api';
import { compte } from '../format';
import { useAsync } from '../hooks';
import type { TestUnitaire } from '../types';
import { Alerte } from '../ui/Alerte';
import { Squelette } from '../ui/Squelette';
import { StatusPill } from '../ui/StatusPill';

/** État d'un test unitaire : réussi, en échec ou non fait (étape pas encore terminée, ou reprise). */
function EtatTest({ test }: { test: TestUnitaire }) {
  if (test.fait && test.etat === 'Reussie')
    return (
      <StatusPill ton="ok" icone={CheckCircle2} taille="sm">
        Réussi
      </StatusPill>
    );
  if (test.fait)
    return (
      <StatusPill ton="danger" icone={XCircle} taille="sm">
        Échec
      </StatusPill>
    );
  return (
    <StatusPill ton="wait" icone={Circle} taille="sm">
      Non fait
    </StatusPill>
  );
}

/**
 * Encadré de l'onglet Journal : tests unitaires de la MEP, faits ou non, avec le résultat attendu et le résultat constaté
 * (`GET /meps/{id}/tests-unitaires`). Les étapes du retour arrière n'y figurent qu'une fois celui-ci déclenché.
 */
export function TestsUnitairesEncadre({ mepId, tick }: { mepId: string; tick: number }) {
  const { donnees, erreur } = useAsync(() => api.testsUnitaires(mepId), [mepId, tick]);
  if (erreur)
    return (
      <Alerte ton="danger" icone={OctagonAlert} role="alert" className="mb-6">
        {erreur}
      </Alerte>
    );
  // Premier chargement : sa place est tenue (le journal dessous ne descend pas à son arrivée).
  if (!donnees) return <Squelette className="mb-6 h-12 w-full rounded-lg" />;
  if (donnees.tests.length === 0 && donnees.verifications === 0) return null;
  return (
    <section aria-label="Tests unitaires" className="mb-6 rounded-lg border border-line bg-surface">
      <details>
        <summary className="flex cursor-pointer flex-wrap items-center gap-x-3 gap-y-1 px-4 py-3">
          <FlaskConical className="size-4 text-fg-muted" aria-hidden />
          <span className="text-sm font-semibold">Tests unitaires</span>
          <span className="text-sm text-fg-muted tnum">
            {[
              compte(donnees.faits, 'fait'),
              compte(donnees.nonFaits, 'non fait'),
              ...(donnees.verifications > 0
                ? [compte(donnees.verifications, 'simple vérification', 'simples vérifications')]
                : []),
            ].join(' · ')}
          </span>
        </summary>
        {donnees.tests.length === 0 ? (
          <p className="border-t border-line px-4 py-3 text-sm text-fg-subtle">
            Aucun test unitaire : toutes les étapes sont de simples vérifications.
          </p>
        ) : (
          <ul className="divide-y divide-line border-t border-line">
            {donnees.tests.map((t) => (
              <li
                key={t.etapeId}
                aria-label={`Test ${t.code}`}
                className="grid gap-x-3 gap-y-1 px-4 py-2.5 sm:grid-cols-[4.5rem_minmax(0,1fr)_auto]"
              >
                <span className="font-mono text-sm font-semibold">{t.code}</span>
                <div className="min-w-0 space-y-0.5">
                  <p className="text-sm">{t.action || 'Étape sans libellé'}</p>
                  <p className="text-xs whitespace-pre-line text-fg-muted">
                    <span className="font-medium">Attendu :</span> {t.resultatAttendu || '—'}
                  </p>
                  {t.fait && (
                    <p className="text-xs whitespace-pre-line text-fg-muted">
                      <span className="font-medium">Constaté :</span> {t.resultatConstate || '—'}
                      {t.executant && <span className="text-fg-subtle"> · {t.executant}</span>}
                    </p>
                  )}
                </div>
                <span>
                  <EtatTest test={t} />
                </span>
              </li>
            ))}
          </ul>
        )}
      </details>
    </section>
  );
}
