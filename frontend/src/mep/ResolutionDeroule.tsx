import { Check, CircleDashed, ClipboardCheck, OctagonAlert, Stethoscope, Wrench, XCircle } from 'lucide-react';
import { useState } from 'react';
import { formatDateHeure, LIBELLE_RESULTAT } from '../format';
import type { ExecutionResolution, ResultatControle } from '../types';
import { Alerte } from '../ui/Alerte';
import { Button } from '../ui/Button';
import { premiereLigne } from '../blocs';
import { ConfirmDialog } from '../ui/Dialog';
import { Instructions, Markdown } from '../ui/Instructions';
import { Field, Textarea } from '../ui/Input';
import { cn } from '../ui/cn';

export interface ActionsResolution {
  controlerCorrection: (r: ExecutionResolution, ordre: number, resultat: ResultatControle, observation: string) => Promise<boolean | void> | void;
  controlerVerification: (r: ExecutionResolution, resultat: ResultatControle, observation: string) => Promise<boolean | void> | void;
}

interface ControleProps {
  libelle: string;
  instruction: string;
  attendu: string;
  observationObligatoire: boolean;
  peutAgir: boolean;
  onValider: (resultat: ResultatControle, observation: string) => Promise<boolean | void> | void;
  verification?: boolean;
}

/** Instruction à exécuter, résultat attendu, observation, Conforme / Échec. */
function Controle({ libelle, instruction, attendu, observationObligatoire, peutAgir, onValider, verification }: ControleProps) {
  const [observation, setObservation] = useState('');
  const [echec, setEchec] = useState(false);
  const [occupe, setOccupe] = useState(false);
  const vide = observation.trim() === '';

  async function valider(r: ResultatControle) {
    setOccupe(true);
    try {
      return await onValider(r, observation.trim());
    } finally {
      setOccupe(false);
    }
  }

  return (
    <div className="space-y-3 rounded-lg border-2 border-run-solid bg-surface p-3.5 shadow-sm">
      <p className="flex items-center gap-2 text-sm font-semibold">
        <span className="rounded bg-run-solid px-1.5 py-0.5 text-[11px] font-bold tracking-wide text-white uppercase">À faire maintenant</span>
        {libelle}
      </p>
      {instruction.trim() && <Instructions source={instruction} vide={null} className={verification ? 'border-l-2 border-ok-bd pl-2' : undefined} />}
      {attendu.trim() && (
        <div className="rounded-lg border border-ok-bd bg-ok-bg px-3 py-2 text-sm text-ok-fg">
          <p className="text-[11px] font-semibold tracking-wide uppercase">Résultat attendu</p>
          <p className="whitespace-pre-line">{attendu}</p>
        </div>
      )}
      <Field label={observationObligatoire ? 'Observation (obligatoire)' : 'Observation (obligatoire en cas d’échec)'}>
        {(id) => <Textarea id={id} rows={2} value={observation} onChange={(e) => setObservation(e.target.value)} placeholder="Ce que vous avez observé, sortie de commande…" disabled={!peutAgir} />}
      </Field>
      {!peutAgir && <p className="text-xs text-fg-subtle">Seul l’exécutant de l’étape peut valider cette action. Prenez la main si nécessaire.</p>}
      <div className="flex flex-wrap items-center gap-2">
        <Button variante="succes" taille="lg" icone={<Check className="size-5" aria-hidden />} disabled={!peutAgir || (observationObligatoire && vide)} chargement={occupe} onClick={() => void valider('Conforme')}>Conforme</Button>
        <Button variante="danger-doux" taille="lg" icone={<XCircle className="size-5" aria-hidden />} disabled={!peutAgir || vide} onClick={() => setEchec(true)}>Échec</Button>
      </div>
      <p className="flex items-center gap-1.5 text-xs text-danger-fg"><OctagonAlert className="size-3.5" aria-hidden />Un échec ici déclenche automatiquement le retour arrière.</p>
      <ConfirmDialog
        open={echec}
        onOpenChange={setEchec}
        titre="Déclarer l’échec ?"
        description="Cela déclenchera le retour arrière."
        libelleConfirmer="Échec : déclencher le retour arrière"
        variante="danger"
        onConfirmer={() => valider('Echec')}
      >
        <Alerte ton="danger" icone={OctagonAlert}>La résolution s’arrête et le plan de retour arrière démarre. Cette décision est tracée au journal.</Alerte>
      </ConfirmDialog>
    </div>
  );
}

function Fait({ numero, libelle, resultat, observation, par, le }: { numero: string; libelle: string; resultat: ResultatControle; observation: string; par: string | null; le: string | null }) {
  const ok = resultat === 'Conforme';
  return (
    <div className={cn('flex items-start gap-3 rounded-lg border px-3 py-2', ok ? 'border-ok-bd bg-ok-bg/50' : 'border-danger-bd bg-danger-bg/60')}>
      {ok ? <Check className="mt-0.5 size-4 shrink-0 text-ok-solid" aria-hidden /> : <XCircle className="mt-0.5 size-4 shrink-0 text-danger-solid" aria-hidden />}
      <div className="min-w-0 flex-1 text-sm">
        <p className="font-medium"><span className="font-mono text-xs">{numero}</span> · {LIBELLE_RESULTAT[resultat]}</p>
        <p className="truncate font-mono text-xs text-fg-muted" title={libelle}>{premiereLigne(libelle)}</p>
        {observation && <p className="mt-0.5 text-xs text-fg-muted">« {observation} »</p>}
        {(par || le) && <p className="mt-0.5 text-[11px] text-fg-subtle">{par}{par && le && ' · '}{le && formatDateHeure(le)}</p>}
      </div>
    </div>
  );
}

/** Déroulé guidé d'une résolution : diagnostic, corrections dans l'ordre, puis vérification finale. */
export function ResolutionDeroule({ resolution: r, peutAgir, actions }: { resolution: ExecutionResolution; peutAgir: boolean; actions: ActionsResolution }) {
  const corrections = [...r.corrections].sort((a, b) => a.ordre - b.ordre);
  const indexCourant = corrections.findIndex((c) => c.resultat === null);
  const toutesConformes = corrections.every((c) => c.resultat === 'Conforme');
  const verificationActive = toutesConformes && r.verificationResultat === null;

  return (
    <section aria-label={`Résolution en cours : ${r.titre}`} className="space-y-3 rounded-xl border-2 border-warn-bd bg-warn-bg/30 p-4">
      <header>
        <p className="flex items-center gap-2 text-sm font-semibold text-warn-fg"><Wrench className="size-4" aria-hidden />Résolution en cours</p>
        <h3 className="mt-0.5 text-base font-semibold">{r.titre}</h3>
        <p className="text-xs text-fg-subtle">Démarrée par {r.demarreePar} · {formatDateHeure(r.demarreeLe)}</p>
      </header>

      {r.symptomes.trim() && <Markdown source={`**Symptômes** : ${r.symptomes}`} />}

      {r.diagnostic.trim() && (
        <div>
          <p className="mb-1.5 flex items-center gap-1.5 text-xs font-semibold tracking-wide text-fg-subtle uppercase"><Stethoscope className="size-3.5" aria-hidden />Diagnostic</p>
          <Instructions source={r.diagnostic} vide={null} />
        </div>
      )}

      <ol className="space-y-2.5">
        {corrections.map((c, i) => {
          const libelle = `Correction ${c.ordre} sur ${corrections.length}`;
          return (
            <li key={c.ordre}>
              {c.resultat ? (
                <Fait numero={`Correction ${c.ordre}`} libelle={c.instruction} resultat={c.resultat} observation={c.observation} par={c.par} le={c.le} />
              ) : i === indexCourant ? (
                <Controle
                  key={`c${c.ordre}`}
                  libelle={libelle}
                  instruction={c.instruction}
                  attendu={c.resultatAttendu}
                  observationObligatoire={false}
                  peutAgir={peutAgir}
                  onValider={(res, obs) => actions.controlerCorrection(r, c.ordre, res, obs)}
                />
              ) : (
                <p className="flex items-center gap-2 rounded-lg border border-dashed border-line-strong px-3 py-2 text-sm text-fg-subtle"><CircleDashed className="size-4" aria-hidden />{libelle} : à venir</p>
              )}
            </li>
          );
        })}
        <li>
          {r.verificationResultat ? (
            <Fait numero="Vérification finale" libelle={r.verificationInstruction} resultat={r.verificationResultat} observation={r.verificationObservation} par={null} le={r.termineeLe} />
          ) : verificationActive ? (
            <Controle
              key="verif"
              verification
              libelle="Vérification finale"
              instruction={r.verificationInstruction}
              attendu={r.verificationAttendu}
              observationObligatoire
              peutAgir={peutAgir}
              onValider={(res, obs) => actions.controlerVerification(r, res, obs)}
            />
          ) : (
            <p className="flex items-center gap-2 rounded-lg border border-dashed border-line-strong px-3 py-2 text-sm text-fg-subtle"><ClipboardCheck className="size-4" aria-hidden />Vérification finale : après les corrections</p>
          )}
        </li>
      </ol>
    </section>
  );
}
