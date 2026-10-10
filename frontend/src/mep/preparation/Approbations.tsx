import { Check, Hourglass, RotateCcw, Save, ShieldCheck, ThumbsDown, ThumbsUp, UserCheck, XCircle } from 'lucide-react';
import { useMemo, useState } from 'react';
import { api } from '../../api';
import { useAuth } from '../../auth';
import { anomaliesHorsApprobation, participantPar } from '../../domaine';
import { formatDateHeure } from '../../format';
import { useAsync, useDraft, useReportDirty } from '../../hooks';
import { useMep } from '../../mepContext';
import type { DecisionApprobation } from '../../types';
import { Alerte } from '../../ui/Alerte';
import { Avatar } from '../../ui/Avatar';
import { AvisConflit } from '../../ui/AvisConflit';
import { Button } from '../../ui/Button';
import { MultiSelect } from '../../ui/Combobox';
import { Dialog } from '../../ui/Dialog';
import { Field, Textarea } from '../../ui/Input';
import { StatusPill } from '../../ui/StatusPill';

function DecisionDialog({ decision, onClose }: { decision: DecisionApprobation | null; onClose: () => void }) {
  const { mep, run } = useMep();
  const [commentaire, setCommentaire] = useState('');
  const [envoi, setEnvoi] = useState(false);
  const refus = decision === 'Refusee';

  async function valider() {
    if (!decision) return;
    setEnvoi(true);
    const ok = await run(
      () => api.decider(mep.id, mep.version, decision, commentaire.trim()),
      refus ? 'Refus enregistré.' : 'MEP approuvée.',
    );
    setEnvoi(false);
    if (ok) {
      setCommentaire('');
      onClose();
    }
  }

  return (
    <Dialog
      open={decision !== null}
      onOpenChange={(o) => {
        if (!o) {
          setCommentaire('');
          onClose();
        }
      }}
      taille="sm"
      titre={refus ? 'Refuser la MEP' : 'Approuver la MEP'}
      description={
        refus
          ? 'Le refus bloque le Go tant que la MEP n’est pas corrigée. Dites précisément ce qu’il faut changer.'
          : 'Vous confirmez avoir relu le plan et jugé l’intervention prête.'
      }
      pied={
        <>
          <Button variante="fantome" onClick={onClose}>
            Annuler
          </Button>
          <Button
            variante={refus ? 'danger' : 'succes'}
            icone={refus ? <ThumbsDown className="size-4" aria-hidden /> : <ThumbsUp className="size-4" aria-hidden />}
            chargement={envoi}
            disabled={refus && !commentaire.trim()}
            onClick={() => void valider()}
          >
            {refus ? 'Refuser' : 'Approuver'}
          </Button>
        </>
      }
    >
      <Field label={refus ? 'Commentaire (obligatoire)' : 'Commentaire (facultatif)'}>
        {(id) => (
          <Textarea
            id={id}
            rows={3}
            value={commentaire}
            onChange={(e) => setCommentaire(e.target.value)}
            autoFocus
            placeholder={refus ? 'ex. Le plan de retour arrière ne couvre pas la base.' : 'ex. Relu, RAS.'}
          />
        )}
      </Field>
    </Dialog>
  );
}

const STATUT = {
  attente: { ton: 'wait', libelle: 'En attente', Icone: Hourglass },
  Approuvee: { ton: 'ok', libelle: 'Approuvée', Icone: Check },
  Refusee: { ton: 'danger', libelle: 'Refusée', Icone: XCircle },
} as const;

/** Circuit d'approbation : désignation des approbateurs, statut de chaque décision, décision de l'approbateur connecté. */
export function ApprobationsBloc() {
  const { mep, run } = useMep();
  const { utilisateur } = useAuth();
  const { donnees: utilisateurs } = useAsync(() => api.utilisateurs(), []);
  const { donnees: journal } = useAsync(() => api.journal(mep.id), [mep.id, mep.version]);
  const [decision, setDecision] = useState<DecisionApprobation | null>(null);

  const enPreparation = mep.phase === 'Preparation';
  const compteDuPilote = participantPar(mep, mep.piloteId)?.utilisateurId ?? null;
  const idsDesignes = useMemo(() => mep.approbations.map((a) => a.utilisateurId), [mep.approbations]);
  const d = useDraft(idsDesignes);
  useReportDirty(d.dirty);

  const options = (utilisateurs ?? [])
    .filter((u) => u.id !== compteDuPilote)
    .map((u) => ({ valeur: u.id, libelle: u.nomAffiche, detail: u.login }));
  const approuvees = mep.approbations.filter((a) => a.decision === 'Approuvee').length;
  const refus = mep.approbations.filter((a) => a.decision === 'Refusee');
  const requis = mep.approbationsMinimum;
  const bloquees = anomaliesHorsApprobation(mep);
  const aReinitialiser = useMemo(() => {
    const evts = (journal ?? []).filter((e) => e.type.startsWith('approbation'));
    const dernier = evts.reduce<(typeof evts)[number] | null>((m, e) => (m === null || e.id > m.id ? e : m), null);
    return (
      dernier?.type === 'approbations.reinitialisees' &&
      mep.approbations.length > 0 &&
      mep.approbations.every((a) => a.decision === null)
    );
  }, [journal, mep.approbations]);

  async function enregistrer() {
    if (d.conflit) return;
    const ok = await run(() => api.modifierApprobateurs(mep.id, mep.version, d.value), 'Approbateurs enregistrés.');
    if (ok) d.reset();
  }

  return (
    <div className="border-t border-line pt-5">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h3 className="flex items-center gap-2 text-[13px] font-semibold">
          <UserCheck className="size-4 text-accent-text" aria-hidden />
          Approbations
        </h3>
        <p className="text-xs font-medium text-fg-muted tnum" aria-live="polite">
          {requis === 0
            ? 'Aucune approbation exigée'
            : `${approuvees}/${requis} approbation${requis > 1 ? 's' : ''} requise${requis > 1 ? 's' : ''}`}
        </p>
      </div>

      {aReinitialiser && enPreparation && (
        <Alerte ton="warn" icone={RotateCcw} role="status" className="mt-3" titre="Approbations à refaire">
          La MEP a été modifiée : les décisions précédentes ont été effacées et chaque approbateur doit se prononcer de
          nouveau.
        </Alerte>
      )}

      {enPreparation && (
        <div className="mt-3 space-y-2.5">
          <Field
            label="Approbateurs"
            hint={
              compteDuPilote
                ? 'Choisis parmi les comptes. Le compte du pilote est exclu : on n’approuve pas sa propre MEP.'
                : 'Choisis parmi les comptes.'
            }
          >
            {(id) => (
              <MultiSelect
                id={id}
                options={options}
                valeurs={d.value}
                onChange={(ids) => d.setValeur(ids)}
                placeholder="Aucun approbateur désigné"
                placeholderRecherche="Rechercher un compte…"
                vide="Aucun compte."
                libelleInconnu="Compte inconnu"
              />
            )}
          </Field>
          <AvisConflit brouillon={d} />
          {d.dirty && (
            <div className="flex flex-wrap items-center gap-3">
              <Button
                variante="primaire"
                taille="sm"
                icone={<Save className="size-4" aria-hidden />}
                disabled={d.conflit}
                onClick={() => void enregistrer()}
              >
                Enregistrer les approbateurs
              </Button>
              <Button variante="fantome" taille="sm" onClick={d.reset}>
                Annuler
              </Button>
              {mep.approbations.some((a) => a.decision !== null) && (
                <span className="text-xs text-warn-fg">Les décisions déjà prises seront effacées.</span>
              )}
            </div>
          )}
        </div>
      )}

      {mep.approbations.length === 0 ? (
        <p className="mt-3 rounded-lg border border-dashed border-line-strong px-3.5 py-3 text-sm text-fg-subtle">
          {requis === 0
            ? 'Aucune approbation n’est exigée pour cette MEP.'
            : `Aucun approbateur désigné : ${requis} approbation${requis > 1 ? 's' : ''} exigée${requis > 1 ? 's' : ''} avant le Go.`}
        </p>
      ) : (
        <ul
          className="mt-3 divide-y divide-line overflow-hidden rounded-lg border border-line"
          aria-label="Décisions des approbateurs"
        >
          {mep.approbations.map((a) => {
            const st = STATUT[a.decision ?? 'attente'];
            const moi = utilisateur?.id === a.utilisateurId;
            return (
              <li key={a.id} className="flex flex-wrap items-start gap-x-4 gap-y-2 px-4 py-3">
                <div className="flex min-w-0 flex-1 basis-56 items-start gap-3">
                  <Avatar nom={a.nom} />
                  <div className="min-w-0">
                    <p className="font-medium">
                      {a.nom}
                      {moi && <span className="ml-2 text-xs font-normal text-fg-subtle">(vous)</span>}
                    </p>
                    {a.decision && <p className="text-xs text-fg-subtle">{a.le ? formatDateHeure(a.le) : ''}</p>}
                    {a.commentaire.trim() && (
                      <p className="mt-1 text-sm whitespace-pre-line text-fg-muted">« {a.commentaire} »</p>
                    )}
                  </div>
                </div>
                <StatusPill taille="md" ton={st.ton} icone={st.Icone}>
                  {st.libelle}
                </StatusPill>
                {moi && enPreparation && a.decision === null && (
                  <div className="flex w-full flex-wrap items-center gap-2 sm:w-auto">
                    <Button
                      variante="succes"
                      taille="sm"
                      icone={<ThumbsUp className="size-4" aria-hidden />}
                      disabled={bloquees.length > 0}
                      onClick={() => setDecision('Approuvee')}
                    >
                      Approuver
                    </Button>
                    <Button
                      variante="danger-doux"
                      taille="sm"
                      icone={<ThumbsDown className="size-4" aria-hidden />}
                      disabled={bloquees.length > 0}
                      onClick={() => setDecision('Refusee')}
                    >
                      Refuser
                    </Button>
                  </div>
                )}
              </li>
            );
          })}
        </ul>
      )}

      {mep.droits.estApprobateur &&
        enPreparation &&
        bloquees.length > 0 &&
        mep.approbations.some((a) => a.utilisateurId === utilisateur?.id && a.decision === null) && (
          <Alerte
            ton="wait"
            icone={ShieldCheck}
            className="mt-3"
            titre="Plan incomplet : décision impossible pour l’instant"
          >
            {bloquees.join('\n')}
          </Alerte>
        )}
      {refus.length > 0 && enPreparation && (
        <Alerte ton="danger" icone={XCircle} className="mt-3" titre="Refus à traiter">
          {refus.map((r) => `${r.nom} : ${r.commentaire}`).join('\n')}
        </Alerte>
      )}
      <DecisionDialog decision={decision} onClose={() => setDecision(null)} />
    </div>
  );
}
