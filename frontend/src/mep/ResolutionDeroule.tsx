import { Check, CircleDashed, OctagonAlert, Stethoscope, Wrench, XCircle } from 'lucide-react';
import { useRef, useState } from 'react';
import { premiereLigne } from '../blocs';
import { formatDateHeure, LIBELLE_VERIFICATION } from '../format';
import type { ExecutionResolution, ResultatControle } from '../types';
import { Alerte } from '../ui/Alerte';
import { Button } from '../ui/Button';
import { cn } from '../ui/cn';
import { ConfirmDialog } from '../ui/Dialog';
import { Instructions, Markdown } from '../ui/Instructions';
import { Field, Textarea } from '../ui/Input';

export interface ActionsResolution {
  controlerCorrection: (
    r: ExecutionResolution,
    ordre: number,
    resultat: ResultatControle,
    observation: string,
  ) => Promise<boolean | void> | void;
  controlerVerification: (
    r: ExecutionResolution,
    resultat: ResultatControle,
    observation: string,
  ) => Promise<boolean | void> | void;
}

interface ControleProps {
  libelle: string;
  instruction: string;
  attendu: string;
  observationObligatoire: boolean;
  peutAgir: boolean;
  onValider: (resultat: ResultatControle, observation: string) => Promise<boolean | void> | void;
}

/** Étape courante de la résolution : quoi faire, résultat attendu, observation, puis Conforme / Échec. */
function Controle({ libelle, instruction, attendu, observationObligatoire, peutAgir, onValider }: ControleProps) {
  const [observation, setObservation] = useState('');
  const [echec, setEchec] = useState(false);
  const [occupe, setOccupe] = useState(false);
  const [erreur, setErreur] = useState('');
  const zone = useRef<HTMLTextAreaElement>(null);
  const vide = observation.trim() === '';

  async function valider(r: ResultatControle) {
    setOccupe(true);
    try {
      return await onValider(r, observation.trim());
    } finally {
      setOccupe(false);
    }
  }
  function conforme() {
    if (observationObligatoire && vide) {
      setErreur('Indiquez ce que vous avez constaté pour valider la vérification.');
      zone.current?.focus();
      return;
    }
    void valider('Conforme');
  }
  function signalerEchec() {
    if (vide) {
      setErreur('Décrivez ce que vous avez constaté pour déclarer l’échec.');
      zone.current?.focus();
      return;
    }
    setEchec(true);
  }

  return (
    <div
      role="group"
      aria-label={`${libelle} : à faire maintenant`}
      className="grid gap-3 rounded-xl border-2 border-run-solid bg-surface p-4 shadow-md"
    >
      <p className="flex flex-wrap items-center gap-2 text-base font-semibold">
        <span className="rounded bg-run-solid px-1.5 py-0.5 text-[11px] font-bold tracking-wide text-run-solid-fg uppercase">
          À faire maintenant
        </span>
        {libelle}
      </p>
      {instruction.trim() && <Instructions source={instruction} vide={null} />}
      {attendu.trim() && (
        <div className="rounded-lg border border-ok-bd bg-ok-bg px-3 py-2 text-sm text-ok-fg">
          <p className="text-[11px] font-semibold tracking-wide uppercase">Résultat attendu</p>
          <p className="whitespace-pre-line">{attendu}</p>
        </div>
      )}
      <Field
        label={
          observationObligatoire
            ? 'Ce que vous constatez (obligatoire)'
            : 'Ce que vous constatez (obligatoire en cas d’échec)'
        }
        erreur={erreur || undefined}
      >
        {(id) => (
          <Textarea
            id={id}
            ref={zone}
            rows={2}
            value={observation}
            onChange={(e) => {
              setObservation(e.target.value);
              setErreur('');
            }}
            placeholder="Sortie de commande, constat…"
            disabled={!peutAgir}
          />
        )}
      </Field>
      {peutAgir ? (
        <div className="flex flex-wrap items-center gap-2">
          <Button
            variante="succes"
            taille="lg"
            icone={<Check className="size-5" aria-hidden />}
            chargement={occupe}
            onClick={conforme}
          >
            Conforme
          </Button>
          <Button
            variante="danger-doux"
            taille="lg"
            icone={<XCircle className="size-5" aria-hidden />}
            onClick={signalerEchec}
          >
            Échec
          </Button>
          <span className="flex items-center gap-1.5 text-xs text-danger-fg">
            <OctagonAlert className="size-3.5" aria-hidden />
            Un échec arrête la résolution : l’étape reste en échec, le pilote décide.
          </span>
        </div>
      ) : (
        <p className="text-sm text-fg-muted">
          Seul l’exécutant de l’étape ou le pilote peut valider. Prenez la main si nécessaire.
        </p>
      )}
      <ConfirmDialog
        open={echec}
        onOpenChange={setEchec}
        titre="Déclarer l’échec ?"
        description="La résolution s’arrêtera en échec."
        libelleConfirmer="Déclarer l’échec"
        variante="danger"
        onConfirmer={() => valider('Echec')}
      >
        <Alerte ton="danger" icone={OctagonAlert}>
          La résolution s’arrête et l’étape reste en échec : le pilote décide du retour arrière ou d’une nouvelle
          tentative. Tracé au journal.
        </Alerte>
      </ConfirmDialog>
    </div>
  );
}

function Fait({
  numero,
  libelle,
  resultat,
  observation,
  par,
  le,
}: {
  numero: string;
  libelle: string;
  resultat: ResultatControle;
  observation: string;
  par: string | null;
  le: string | null;
}) {
  const ok = resultat === 'Conforme';
  return (
    <div
      className={cn(
        'flex items-start gap-3 rounded-lg border px-3 py-2',
        ok ? 'border-ok-bd bg-ok-bg/50' : 'border-danger-bd bg-danger-bg/60',
      )}
    >
      {ok ? (
        <Check className="mt-0.5 size-4 shrink-0 text-ok-solid" aria-hidden />
      ) : (
        <XCircle className="mt-0.5 size-4 shrink-0 text-danger-solid" aria-hidden />
      )}
      <div className="min-w-0 flex-1 text-sm">
        <p className="font-medium">
          <span className="font-mono text-xs">{numero}</span> · {LIBELLE_VERIFICATION[resultat]}
        </p>
        <p className="truncate font-mono text-xs text-fg-muted" title={libelle}>
          {premiereLigne(libelle)}
        </p>
        {observation && <p className="mt-0.5 text-xs text-fg-muted">« {observation} »</p>}
        {(par || le) && (
          <p className="mt-0.5 text-[11px] text-fg-subtle">
            {par}
            {par && le && ' · '}
            {le && formatDateHeure(le)}
          </p>
        )}
      </div>
    </div>
  );
}

interface EtapeProgression {
  cle: string;
  libelle: string;
  etat: 'fait' | 'echec' | 'courant' | 'avenir';
}

/**
 * Déroulé guidé d'une résolution : progression (Correction 1/2 → 2/2 → Vérification finale), l'étape courante en tête
 * avec son action, puis le diagnostic et l'historique.
 */
export function ResolutionDeroule({
  resolution: r,
  peutAgir,
  actions,
}: {
  resolution: ExecutionResolution;
  peutAgir: boolean;
  actions: ActionsResolution;
}) {
  const corrections = [...r.corrections].sort((a, b) => a.ordre - b.ordre);
  const n = corrections.length;
  const indexCourant = corrections.findIndex((c) => c.resultat === null);
  const toutesConformes = corrections.every((c) => c.resultat === 'Conforme');
  const verificationActive = toutesConformes && r.verificationResultat === null;
  const courante = indexCourant >= 0 ? corrections[indexCourant] : null;

  const progression: EtapeProgression[] = [
    ...corrections.map((c, i): EtapeProgression => ({
      cle: `c${c.ordre}`,
      libelle: `Correction ${c.ordre}/${n}`,
      etat:
        c.resultat === 'Conforme'
          ? 'fait'
          : c.resultat === 'Echec'
            ? 'echec'
            : i === indexCourant
              ? 'courant'
              : 'avenir',
    })),
    {
      cle: 'verif',
      libelle: 'Vérification finale',
      etat:
        r.verificationResultat === 'Conforme'
          ? 'fait'
          : r.verificationResultat === 'Echec'
            ? 'echec'
            : verificationActive
              ? 'courant'
              : 'avenir',
    },
  ];

  return (
    <section
      aria-label={`Résolution en cours : ${r.titre}`}
      className="grid gap-3 rounded-xl border-2 border-warn-bd bg-warn-bg/20 p-4"
    >
      <header className="grid gap-1">
        <p className="flex items-center gap-2 text-sm font-semibold text-warn-fg">
          <Wrench className="size-4" aria-hidden />
          Résolution en cours
        </p>
        <h3 className="text-base font-semibold">{r.titre}</h3>
        <p className="text-xs text-fg-subtle">
          Démarrée par {r.demarreePar} · {formatDateHeure(r.demarreeLe)}
        </p>
      </header>

      <ol aria-label="Progression de la résolution" className="flex flex-wrap items-center gap-1.5">
        {progression.map((p, i) => (
          <li key={p.cle} className="flex items-center gap-1.5">
            {i > 0 && <span className="h-px w-3 bg-line-strong" aria-hidden />}
            <span
              aria-current={p.etat === 'courant' ? 'step' : undefined}
              className={cn(
                'inline-flex items-center gap-1 rounded-full border px-2.5 py-0.5 text-xs font-semibold',
                p.etat === 'fait' && 'border-ok-bd bg-ok-bg text-ok-fg',
                p.etat === 'echec' && 'border-danger-bd bg-danger-bg text-danger-fg',
                p.etat === 'courant' && 'border-run-solid bg-run-solid text-run-solid-fg',
                p.etat === 'avenir' && 'border-line-strong text-fg-subtle',
              )}
            >
              {p.etat === 'fait' ? (
                <Check className="size-3.5" aria-hidden />
              ) : p.etat === 'echec' ? (
                <XCircle className="size-3.5" aria-hidden />
              ) : (
                <CircleDashed className="size-3.5" aria-hidden />
              )}
              {p.libelle}
              <span className="sr-only">
                {p.etat === 'fait'
                  ? ' : conforme'
                  : p.etat === 'echec'
                    ? ' : échec'
                    : p.etat === 'courant'
                      ? ' : en cours'
                      : ' : à venir'}
              </span>
            </span>
          </li>
        ))}
      </ol>

      {courante ? (
        <Controle
          key={`c${courante.ordre}`}
          libelle={`Correction ${courante.ordre} sur ${n}`}
          instruction={courante.instruction}
          attendu={courante.resultatAttendu}
          observationObligatoire={false}
          peutAgir={peutAgir}
          onValider={(res, obs) => actions.controlerCorrection(r, courante.ordre, res, obs)}
        />
      ) : verificationActive ? (
        <Controle
          key="verif"
          libelle="Vérification finale"
          instruction={r.verificationInstruction}
          attendu={r.verificationAttendu}
          observationObligatoire
          peutAgir={peutAgir}
          onValider={(res, obs) => actions.controlerVerification(r, res, obs)}
        />
      ) : null}

      {(r.symptomes.trim() || r.diagnostic.trim()) && (
        <details open={indexCourant === 0} className="rounded-lg border border-line bg-surface px-3 py-2">
          <summary className="cursor-pointer text-sm font-medium">Symptômes et diagnostic</summary>
          <div className="mt-2 grid gap-2">
            {r.symptomes.trim() && <Markdown source={`**Symptômes** : ${r.symptomes}`} />}
            {r.diagnostic.trim() && (
              <div>
                <p className="mb-1.5 flex items-center gap-1.5 text-xs font-semibold tracking-wide text-fg-subtle uppercase">
                  <Stethoscope className="size-3.5" aria-hidden />
                  Diagnostic
                </p>
                <Instructions source={r.diagnostic} vide={null} />
              </div>
            )}
          </div>
        </details>
      )}

      <ol aria-label="Étapes de la résolution" className="grid grid-cols-[minmax(0,1fr)] gap-1.5">
        {corrections.map((c, i) => (
          <li key={c.ordre}>
            {c.resultat ? (
              <Fait
                numero={`Correction ${c.ordre}`}
                libelle={c.instruction}
                resultat={c.resultat}
                observation={c.observation}
                par={c.par}
                le={c.le}
              />
            ) : (
              i !== indexCourant && (
                <p className="flex items-center gap-2 rounded-lg border border-dashed border-line-strong px-3 py-1.5 text-sm text-fg-subtle">
                  <CircleDashed className="size-4" aria-hidden />
                  Correction {c.ordre} sur {n} : à venir
                </p>
              )
            )}
          </li>
        ))}
        <li>
          {r.verificationResultat ? (
            <Fait
              numero="Vérification finale"
              libelle={r.verificationInstruction}
              resultat={r.verificationResultat}
              observation={r.verificationObservation}
              par={null}
              le={r.termineeLe}
            />
          ) : (
            !verificationActive && (
              <p className="flex items-center gap-2 rounded-lg border border-dashed border-line-strong px-3 py-1.5 text-sm text-fg-subtle">
                <CircleDashed className="size-4" aria-hidden />
                Vérification finale : après les corrections
              </p>
            )
          )}
        </li>
      </ol>
    </section>
  );
}
