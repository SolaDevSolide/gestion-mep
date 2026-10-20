import { Check, FileSpreadsheet, OctagonAlert, XCircle } from 'lucide-react';
import { useEffect, useId, useState } from 'react';
import { api, ApiError } from '../../api';
import { formatDate, LIBELLE_RESULTAT } from '../../format';
import { useMep } from '../../mepContext';
import type { ApercuValidation } from '../../types';
import { Alerte } from '../../ui/Alerte';
import { Button } from '../../ui/Button';
import { Dialog } from '../../ui/Dialog';
import { StatusPill } from '../../ui/StatusPill';

/**
 * « Importer la validation » : le fichier Excel rempli par les métiers est d'abord analysé (aperçu des étapes qu'il va
 * renseigner, rien n'est modifié), puis reporté d'un coup. Un fichier incohérent est refusé en entier, raisons listées.
 */
export function ImportValidationDialog({ open, onOpenChange }: { open: boolean; onOpenChange: (o: boolean) => void }) {
  const { mep, run } = useMep();
  const champId = useId();
  const [fichier, setFichier] = useState<File | null>(null);
  const [apercu, setApercu] = useState<ApercuValidation | null>(null);
  const [refus, setRefus] = useState<{ message: string; raisons: string[] } | null>(null);
  const [analyse, setAnalyse] = useState(false);
  const [envoi, setEnvoi] = useState(false);

  useEffect(() => {
    if (open) {
      setFichier(null);
      setApercu(null);
      setRefus(null);
    }
  }, [open]);

  async function analyser(f: File) {
    setFichier(f);
    setApercu(null);
    setRefus(null);
    setAnalyse(true);
    try {
      setApercu(await api.apercuValidation(mep.id, f));
    } catch (e) {
      setRefus(
        e instanceof ApiError && e.erreurs.length > 0
          ? { message: 'Fichier refusé, rien n’a été modifié :', raisons: e.erreurs }
          : { message: e instanceof Error ? e.message : 'Analyse impossible.', raisons: [] },
      );
    } finally {
      setAnalyse(false);
    }
  }

  async function reporter() {
    if (!fichier || !apercu) return;
    setEnvoi(true);
    try {
      const n = apercu.changements.length;
      const ok = await run(
        () => api.importerValidation(mep.id, fichier),
        `${n} validation${n > 1 ? 's' : ''} reportée${n > 1 ? 's' : ''} dans la recette.`,
        (message) => setRefus({ message, raisons: [] }),
      );
      if (ok) onOpenChange(false);
    } finally {
      setEnvoi(false);
    }
  }

  const n = apercu?.changements.length ?? 0;
  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      titre="Importer la validation"
      description="Choisissez le fichier Excel exporté puis rempli par les métiers : vous verrez ce qu’il va renseigner avant de l’appliquer."
      taille="lg"
      pied={
        <>
          <Button variante="fantome" onClick={() => onOpenChange(false)}>
            Annuler
          </Button>
          <Button variante="primaire" disabled={n === 0} chargement={envoi} onClick={() => void reporter()}>
            {n > 0 ? `Reporter ${n} validation${n > 1 ? 's' : ''}` : 'Reporter'}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <div className="flex flex-wrap items-center gap-3">
          <label
            htmlFor={champId}
            className="inline-flex cursor-pointer items-center gap-2 rounded-lg border border-line-strong bg-surface px-3 py-2 text-sm font-medium hover:bg-surface-2"
          >
            <FileSpreadsheet className="size-4" aria-hidden />
            {fichier ? 'Choisir un autre fichier' : 'Choisir le fichier Excel'}
          </label>
          <input
            id={champId}
            type="file"
            accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            className="sr-only"
            aria-label="Fichier Excel de la recette"
            onChange={(e) => {
              const f = e.target.files?.[0];
              e.target.value = '';
              if (f) void analyser(f);
            }}
          />
          {fichier && <span className="truncate text-sm text-fg-muted">{fichier.name}</span>}
          {analyse && <span className="text-sm text-fg-subtle">Analyse du fichier…</span>}
        </div>

        {refus && (
          <Alerte ton="danger" icone={OctagonAlert} role="alert" titre={refus.message}>
            {refus.raisons.length > 0 && (
              <ul className="mt-1 list-disc space-y-1 pl-5">
                {refus.raisons.map((r) => (
                  <li key={r}>{r}</li>
                ))}
              </ul>
            )}
          </Alerte>
        )}

        {apercu && (
          <div className="space-y-3">
            <p className="text-sm text-fg-muted">
              {n === 0
                ? 'Aucune nouvelle validation dans ce fichier.'
                : `${n} étape${n > 1 ? 's' : ''} de test ${n > 1 ? 'vont être renseignées' : 'va être renseignée'}.`}
              {apercu.inchangees > 0 && ` ${apercu.inchangees} déjà à jour.`}
              {apercu.sansValidation > 0 &&
                ` ${apercu.sansValidation} ligne${apercu.sansValidation > 1 ? 's' : ''} commencée${apercu.sansValidation > 1 ? 's' : ''} sans validation, ignorée${apercu.sansValidation > 1 ? 's' : ''}.`}
            </p>
            {n > 0 && (
              <ul className="max-h-[45vh] space-y-2 overflow-y-auto" aria-label="Validations à reporter">
                {apercu.changements.map((c) => (
                  <li key={c.etapeId} className="rounded-lg border border-line px-3 py-2 text-sm">
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="font-semibold">
                        {c.systeme} · {c.fiche || 'Fiche sans titre'} · étape {c.etape}
                      </span>
                      <StatusPill
                        ton={c.resultat === 'Conforme' ? 'ok' : 'danger'}
                        icone={c.resultat === 'Conforme' ? Check : XCircle}
                        taille="sm"
                      >
                        {LIBELLE_RESULTAT[c.resultat]}
                      </StatusPill>
                    </div>
                    <p className="mt-1 whitespace-pre-line text-fg-muted">{c.observation}</p>
                    <p className="mt-0.5 text-xs text-fg-subtle">
                      Par {c.validePar}
                      {c.date && ` le ${formatDate(c.date)}`}
                    </p>
                  </li>
                ))}
              </ul>
            )}
          </div>
        )}
      </div>
    </Dialog>
  );
}
