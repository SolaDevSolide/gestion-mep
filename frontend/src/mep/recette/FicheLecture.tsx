import { Check, FlaskConical, RotateCcw, XCircle } from 'lucide-react';
import { useState } from 'react';
import { api } from '../../api';
import { formatDate, formatDateHeure, LIBELLE_RESULTAT } from '../../format';
import { useMep } from '../../mepContext';
import type { EtapeTest, FicheRecette } from '../../types';
import { Button } from '../../ui/Button';
import { cn } from '../../ui/cn';
import { Field, Textarea } from '../../ui/Input';
import { StatusPill } from '../../ui/StatusPill';
import { RenduBlocs } from '../ChampBlocs';

/* ------------------------------------------------------------------ Lecture et déroulé */

/**
 * Une étape de test en lecture : instructions, résultat attendu, résultat saisi ;
 * en phase Recette, saisie Conforme / Non conforme avec observation, et « Refaire » pour le pilote.
 */
function EtapeTestLigne({
  fiche,
  etape,
  numero,
  peutAgir,
}: {
  fiche: FicheRecette;
  etape: EtapeTest;
  numero: number;
  peutAgir: boolean;
}) {
  const { mep, run } = useMep();
  const [observation, setObservation] = useState('');
  const enRecette = mep.phase === 'Recette';
  const fait = etape.resultat !== null;
  const conforme = etape.resultat === 'Conforme';

  async function valider(resultat: 'Conforme' | 'Echec') {
    const ok = await run(
      () => api.resultatEtapeTest(mep.id, fiche.id, etape.id, resultat, observation.trim()),
      resultat === 'Conforme' ? `Étape ${numero} conforme.` : `Étape ${numero} en échec.`,
    );
    if (ok) setObservation('');
  }

  return (
    <li
      className={cn(
        'space-y-3 rounded-lg border p-3',
        fait ? (conforme ? 'border-ok-bd' : 'border-danger-bd') : 'border-line',
      )}
    >
      <div className="flex flex-wrap items-center gap-2">
        <span className="flex size-6 items-center justify-center rounded-md bg-surface-2 font-mono text-xs font-bold">
          {numero}
        </span>
        <h4 className="flex-1 text-sm font-semibold">Étape {numero}</h4>
        {fait ? (
          <StatusPill ton={conforme ? 'ok' : 'danger'} icone={conforme ? Check : XCircle}>
            {LIBELLE_RESULTAT[etape.resultat!]}
          </StatusPill>
        ) : enRecette ? (
          <StatusPill ton="wait" icone={FlaskConical}>
            À réaliser
          </StatusPill>
        ) : null}
        {enRecette && fait && mep.droits.estPilote && (
          <Button
            taille="sm"
            icone={<RotateCcw className="size-4" aria-hidden />}
            aria-label={`Refaire l’étape ${numero} de ${fiche.titre}`}
            action={() => run(() => api.refaireEtapeTest(mep.id, fiche.id, etape.id), `Étape ${numero} à refaire.`)}
          >
            Refaire
          </Button>
        )}
      </div>
      <div>
        <p className="text-[11px] font-semibold tracking-wide text-fg-subtle uppercase">Déroulé du test</p>
        {etape.instructions.trim() ? (
          <RenduBlocs source={etape.instructions} />
        ) : (
          <p className="text-sm text-fg-subtle">Déroulé à compléter.</p>
        )}
      </div>
      <div className="rounded-lg border border-ok-bd bg-ok-bg px-3.5 py-2.5 text-ok-fg">
        <p className="text-[11px] font-semibold tracking-wide uppercase">Résultat attendu</p>
        {etape.resultatAttendu.trim() ? (
          <RenduBlocs source={etape.resultatAttendu} />
        ) : (
          <p className="text-sm">Résultat attendu à compléter.</p>
        )}
      </div>

      {fait && (
        <div className="rounded-lg border border-line bg-surface-2/50 px-3.5 py-2.5 text-sm">
          <p className="whitespace-pre-line">{etape.observation || 'Aucune observation.'}</p>
          {etape.valideePar ? (
            <p className="mt-1 text-xs text-fg-subtle">
              Validation de {etape.valideePar}
              {etape.valideeLe && ` le ${formatDate(etape.valideeLe)}`} · fichier Excel importé par{' '}
              {etape.par ?? 'quelqu’un'}
              {etape.le && ` · ${formatDateHeure(etape.le)}`}
            </p>
          ) : (
            <p className="mt-1 text-xs text-fg-subtle">
              {etape.par ?? 'Quelqu’un'}
              {etape.le && ` · ${formatDateHeure(etape.le)}`}
            </p>
          )}
        </div>
      )}

      {enRecette && !fait && peutAgir && (
        <div className="space-y-3">
          <Field
            label={`Observation de l’étape ${numero} (obligatoire)`}
            hint="Ce que vous avez constaté : sera conservé comme preuve."
          >
            {(id) => (
              <Textarea
                id={id}
                rows={2}
                value={observation}
                onChange={(e) => setObservation(e.target.value)}
                placeholder="ex. La page d’accueil s’affiche en 1,2 s, version 4.2 visible."
              />
            )}
          </Field>
          <div className="grid gap-3 sm:grid-cols-2">
            <Button
              variante="succes"
              taille="lg"
              icone={<Check className="size-5" aria-hidden />}
              disabled={!observation.trim()}
              action={() => valider('Conforme')}
            >
              Conforme
            </Button>
            <Button
              variante="danger"
              taille="lg"
              icone={<XCircle className="size-5" aria-hidden />}
              disabled={!observation.trim()}
              action={() => valider('Echec')}
            >
              Non conforme
            </Button>
          </div>
        </div>
      )}
      {enRecette && !fait && !peutAgir && (
        <p className="text-xs text-fg-subtle">
          Seul un participant relié à un compte, le pilote ou un administrateur peut saisir le résultat.
        </p>
      )}
    </li>
  );
}

/** Étapes de test d'une fiche, en lecture et déroulé. */
export function FicheLecture({ fiche, peutAgir }: { fiche: FicheRecette; peutAgir: boolean }) {
  if (fiche.etapes.length === 0) return <p className="text-sm text-fg-subtle">Aucune étape de test.</p>;
  return (
    <ol className="space-y-3" aria-label="Étapes de test">
      {fiche.etapes.map((e, i) => (
        <EtapeTestLigne key={e.id} fiche={fiche} etape={e} numero={i + 1} peutAgir={peutAgir} />
      ))}
    </ol>
  );
}
