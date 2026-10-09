import { Check, ClipboardCheck, FlaskConical, RotateCcw, ShieldAlert, XCircle } from 'lucide-react';
import { useState } from 'react';
import { api } from '../api';
import { nomParticipant, testsRecetteTries } from '../domaine';
import { formatDateHeure, LIBELLE_RESULTAT } from '../format';
import { useMep } from '../mepContext';
import type { TestRecette } from '../types';
import { Alerte } from '../ui/Alerte';
import { Avatar } from '../ui/Avatar';
import { Button } from '../ui/Button';
import { Card } from '../ui/Card';
import { cn } from '../ui/cn';
import { EmptyState } from '../ui/EmptyState';
import { Field, Textarea } from '../ui/Input';
import { ProgressBar } from '../ui/ProgressBar';
import { StatusPill } from '../ui/StatusPill';
import { BoutonCloturer, BoutonDeclencherRepli } from './ActionsPilote';

function TestCarte({ test, numero, peutAgir }: { test: TestRecette; numero: number; peutAgir: boolean }) {
  const { mep, run } = useMep();
  const [observation, setObservation] = useState('');
  const resp = nomParticipant(mep, test.responsableId);
  const enRecette = mep.phase === 'Recette';
  const fait = test.resultat !== null;
  const conforme = test.resultat === 'Conforme';

  async function valider(resultat: 'Conforme' | 'Echec') {
    const ok = await run(
      () => api.resultatTestRecette(mep.id, test.id, resultat, observation.trim()),
      resultat === 'Conforme' ? `Test ${numero} conforme.` : `Test ${numero} en échec.`,
    );
    if (ok) setObservation('');
  }

  return (
    <li>
      <Card className={cn('overflow-hidden', fait && (conforme ? 'border-ok-bd' : 'border-danger-bd'))}>
        <div className="space-y-4 p-5">
          <div className="flex flex-wrap items-start gap-x-4 gap-y-2">
            <span className="flex size-8 shrink-0 items-center justify-center rounded-lg bg-surface-2 font-mono text-sm font-bold">{numero}</span>
            <div className="min-w-0 flex-1 basis-64">
              <h3 className="text-base leading-snug font-semibold">{test.libelle}</h3>
              <p className="mt-1 flex items-center gap-2 text-[13px] text-fg-muted">
                {resp ? <><Avatar nom={resp} taille="sm" />Responsable : {resp}</> : 'Sans responsable'}
              </p>
            </div>
            {fait ? (
              <StatusPill taille="lg" ton={conforme ? 'ok' : 'danger'} icone={conforme ? Check : XCircle}>{LIBELLE_RESULTAT[test.resultat!]}</StatusPill>
            ) : (
              <StatusPill taille="lg" ton="wait" icone={FlaskConical}>À réaliser</StatusPill>
            )}
          </div>

          <div className="rounded-lg border border-ok-bd bg-ok-bg px-3.5 py-3 text-ok-fg">
            <p className="text-[11px] font-semibold tracking-wide uppercase">Résultat attendu</p>
            <p className="mt-0.5 text-sm whitespace-pre-line">{test.resultatAttendu}</p>
          </div>

          {fait && (
            <div className="rounded-lg border border-line bg-surface-2/50 px-3.5 py-3 text-sm">
              <p className="whitespace-pre-line">{test.observation || 'Aucune observation.'}</p>
              <p className="mt-1 text-xs text-fg-subtle">{test.par ?? 'Quelqu’un'}{test.le && ` · ${formatDateHeure(test.le)}`}</p>
            </div>
          )}

          {enRecette && !fait && peutAgir && (
            <div className="space-y-3">
              <Field label="Observation (obligatoire)" hint="Ce que vous avez constaté : sera conservé comme preuve.">
                {(id) => <Textarea id={id} rows={2} value={observation} onChange={(e) => setObservation(e.target.value)} placeholder="ex. La page d’accueil s’affiche en 1,2 s, version 4.2 visible dans le pied de page." />}
              </Field>
              <div className="grid gap-3 sm:grid-cols-2">
                <Button variante="succes" taille="lg" icone={<Check className="size-5" aria-hidden />} disabled={!observation.trim()} onClick={() => void valider('Conforme')}>Conforme</Button>
                <Button variante="danger" taille="lg" icone={<XCircle className="size-5" aria-hidden />} disabled={!observation.trim()} onClick={() => void valider('Echec')}>Échec</Button>
              </div>
            </div>
          )}
          {enRecette && !fait && !peutAgir && <p className="text-xs text-fg-subtle">Seul le responsable du test ou le pilote peut saisir le résultat.</p>}

          {enRecette && fait && mep.droits.estPilote && (
            <Button icone={<RotateCcw className="size-4" aria-hidden />} onClick={() => void run(() => api.refaireTestRecette(mep.id, test.id), `Test ${numero} à refaire.`)}>Refaire</Button>
          )}
        </div>
      </Card>
    </li>
  );
}

/** Onglet Recette : un test par carte, Conforme / Échec avec observation, « Refaire » pour le pilote. */
export default function RecetteTab() {
  const { mep } = useMep();
  const tests = testsRecetteTries(mep);
  const conformes = tests.filter((t) => t.resultat === 'Conforme').length;
  const echecs = tests.filter((t) => t.resultat === 'Echec').length;
  const enRecette = mep.phase === 'Recette';

  return (
    <div className="mx-auto w-full max-w-4xl space-y-5 px-6 py-6">
      {tests.length === 0 ? (
        <Card><EmptyState icone={ClipboardCheck} titre="Aucun test de recette" description="Les tests se définissent en préparation, dans la section « Recette »." /></Card>
      ) : (
        <>
          <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
            <ProgressBar className="min-w-48 flex-1" segments={[
              { valeur: conformes, ton: 'ok', libelle: 'Réussies' },
              { valeur: echecs, ton: 'danger', libelle: 'Échecs' },
              { valeur: tests.length - conformes - echecs, ton: 'wait', libelle: 'Restantes' },
            ]} />
            <p className="text-sm font-medium text-fg-muted tnum">{conformes}/{tests.length} conforme{conformes > 1 ? 's' : ''}{echecs > 0 && <span className="font-semibold text-danger-solid"> · {echecs} en échec</span>}</p>
          </div>

          {enRecette && echecs > 0 && (
            <Alerte ton="danger" icone={XCircle} role="alert" titre="Un test est en échec">
              La clôture réussie est impossible tant qu’un test n’est pas conforme. Le pilote peut faire refaire le test ou déclencher le retour arrière.
            </Alerte>
          )}
          {enRecette && mep.droits.estPilote && (
            <Alerte
              ton={mep.nonRetourFranchi ? 'warn' : 'rollback'}
              icone={ShieldAlert}
              titre="Décision du pilote"
              action={<div className="flex flex-wrap items-center gap-2"><BoutonDeclencherRepli taille="md" /><BoutonCloturer taille="md" /></div>}
            >
              {mep.nonRetourFranchi
                ? `Point de non-retour franchi (${mep.nonRetourFranchi}) : le retour arrière n’est plus possible, on corrige en avant.`
                : 'Le retour arrière reste possible pendant la recette. La clôture réussie exige que tous les tests soient conformes.'}
            </Alerte>
          )}

          <ol className="space-y-4">
            {tests.map((t, i) => (
              <TestCarte key={t.id} test={t} numero={i + 1} peutAgir={mep.droits.estPilote || (mep.droits.participantId !== null && t.responsableId === mep.droits.participantId)} />
            ))}
          </ol>
        </>
      )}
    </div>
  );
}
