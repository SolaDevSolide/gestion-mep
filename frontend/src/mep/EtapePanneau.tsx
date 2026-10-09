import { Check, CircleHelp, Clock, Flag, HandMetal, History, Lock, OctagonAlert, Play, ShieldCheck, Undo2, Wrench, XCircle } from 'lucide-react';
import { useState } from 'react';
import { bloqueurs, executantEffectif, nomParticipant } from '../domaine';
import { formatDateHeure, LIBELLE_CRITICITE, LIBELLE_ETAT, LIBELLE_STATUT_RESOLUTION } from '../format';
import { metaEtape } from '../statuts';
import type { Etape, ExecutionResolution, Mep, PieceJointe, ResolutionType, ResultatControle } from '../types';
import { Alerte } from '../ui/Alerte';
import { Avatar } from '../ui/Avatar';
import { Button } from '../ui/Button';
import { Card } from '../ui/Card';
import { CodeBlock } from '../ui/CodeBlock';
import { ConfirmDialog } from '../ui/Dialog';
import { Field, Textarea } from '../ui/Input';
import { PiecesJointes } from '../ui/PiecesJointes';
import { Badge, StatusPill } from '../ui/StatusPill';
import { BadgesEtape } from './BadgesEtape';
import { ResolutionDeroule } from './ResolutionDeroule';

export interface ActionsEtape {
  demarrer: (e: Etape) => Promise<boolean | void> | void;
  terminer: (e: Etape, resultat: 'Reussie' | 'Echec', preuve: string) => Promise<boolean | void> | void;
  autoriserReprise: (e: Etape) => Promise<boolean | void> | void;
  prendreLaMain: (e: Etape) => Promise<boolean | void> | void;
  autoriserNonRetour: (e: Etape) => Promise<boolean | void> | void;
  deposerPiece: (e: Etape, fichier: File, signaler: (message: string) => void) => Promise<boolean>;
  supprimerPiece: (p: PieceJointe, signaler: (message: string) => void) => Promise<boolean>;
  appliquerResolution: (e: Etape, resolutionTypeId: string) => Promise<boolean | void> | void;
  controlerCorrection: (r: ExecutionResolution, ordre: number, resultat: ResultatControle, observation: string) => Promise<boolean | void> | void;
  controlerVerification: (r: ExecutionResolution, resultat: ResultatControle, observation: string) => Promise<boolean | void> | void;
}

interface Props {
  mep: Mep;
  etape: Etape;
  /** Résolutions types actives (pour afficher celles liées à l'étape). */
  resolutionsTypes: ResolutionType[];
  actions: ActionsEtape;
  onSelectionner: (id: string) => void;
}

function Titre({ children, icone: Icone }: { children: React.ReactNode; icone?: typeof Check }) {
  return <h3 className="mb-2 flex items-center gap-1.5 text-xs font-semibold tracking-wide text-fg-subtle uppercase">{Icone && <Icone className="size-3.5" aria-hidden />}{children}</h3>;
}

/** Panneau de l'étape sélectionnée : état, raison du blocage, instructions, actions, résolutions. */
export function EtapePanneau({ mep, etape, resolutionsTypes, actions, onSelectionner }: Props) {
  const [preuve, setPreuve] = useState('');
  const [confirmation, setConfirmation] = useState<'reussie' | 'echec' | 'main' | null>(null);

  const meta = metaEtape(etape);
  const statut = etape.disponibilite.statut;
  const moi = mep.droits.participantId;
  const execId = executantEffectif(etape);
  const estMoi = moi !== null && execId === moi;
  const nomExec = nomParticipant(mep, execId);
  const nomPrevu = nomParticipant(mep, etape.responsableId);
  const priseEnMain = etape.executantId !== null && etape.executantId !== etape.responsableId;
  const systeme = mep.systemes.find((s) => s.id === etape.systemeId);
  const memePlan = mep.etapes.filter((e) => e.plan === etape.plan);
  const attendus = bloqueurs(etape, memePlan);

  const planAgissant = (mep.phase === 'Execution' && etape.plan === 'Deploiement') || (mep.phase === 'RetourArriere' && etape.plan === 'RetourArriere');
  const nonRetourAttendu = etape.pointDeNonRetour && etape.etat === 'AFaire' && etape.nonRetourAutorise === null;
  const peutDemarrer = estMoi && statut === 'Disponible' && etape.etat === 'AFaire' && !nonRetourAttendu;
  const peutTerminer = estMoi && etape.etat === 'EnCours';
  const peutPrendre = planAgissant && !estMoi && etape.etat !== 'Reussie' && statut !== 'SiRetourArriere' && statut !== 'RetourArriere';

  const resolutionsEtape = mep.resolutions.filter((r) => r.etapeId === etape.id);
  const enCours = resolutionsEtape.find((r) => r.statut === 'EnCours');
  const historique = resolutionsEtape.filter((r) => r.statut !== 'EnCours');
  const liees = etape.resolutionIds.map((id) => resolutionsTypes.find((r) => r.id === id)).filter((r): r is ResolutionType => !!r);
  const enEchec = etape.etat === 'Echec';

  return (
    <Card className="overflow-hidden">
      <div className="space-y-5 p-5">
        <header>
          <div className="flex flex-wrap items-center gap-2">
            <span className="rounded-md bg-surface-2 px-2 py-1 font-mono text-sm font-bold">{etape.code}</span>
            <StatusPill ton={meta.ton} icone={meta.Icone} anime={meta.anime} taille="lg">{meta.libelle}</StatusPill>
            {estMoi && <Badge ton="run" className="text-xs">À vous</Badge>}
            <BadgesEtape etape={etape} />
          </div>
          <h2 className="mt-3 text-lg leading-snug font-semibold tracking-tight">{etape.action || 'Étape sans libellé'}</h2>
          <p className="mt-1 text-[13px] text-fg-muted">
            {systeme ? <>{systeme.nom}{systeme.cible && <> · <span className="font-mono">{systeme.cible}</span></>}</> : 'Système non précisé'}
            {etape.criticite && <> · Criticité {LIBELLE_CRITICITE[etape.criticite].toLowerCase()}</>}
            <> · {LIBELLE_ETAT[etape.etat]}</>
          </p>
        </header>

        {/* Raison exacte d'un blocage */}
        {statut === 'Attend' && (
          <Alerte ton="wait" icone={Clock} titre="En attente">
            <ul className="mt-1 space-y-1">
              {attendus.length === 0 && <li>{etape.disponibilite.libelle}</li>}
              {attendus.map((b) => (
                <li key={b.id}>
                  Bloquée par{' '}
                  <button type="button" className="font-mono font-bold underline underline-offset-2 hover:text-accent-text" onClick={() => onSelectionner(b.id)}>{b.code}</button>
                  {' '}— {b.action || 'sans libellé'} <span className="text-fg-subtle">({LIBELLE_ETAT[b.etat].toLowerCase()})</span>
                </li>
              ))}
            </ul>
          </Alerte>
        )}
        {statut === 'SiRetourArriere' && <Alerte ton="later" icone={Undo2}>Cette étape ne sera disponible qu’en cas de retour arrière.</Alerte>}
        {statut === 'RetourArriere' && etape.etat !== 'Echec' && <Alerte ton="rollback" icone={OctagonAlert} titre="Retour arrière">Le déploiement est arrêté : cette étape ne doit plus être exécutée.</Alerte>}
        {['PlanInvalide', 'EcheanceARenseigner', 'FicheACompleter', 'InstructionsACompleter'].includes(statut) && (
          <Alerte ton="warn" icone={OctagonAlert}>{etape.disponibilite.libelle}</Alerte>
        )}

        {/* Point de non-retour : avertissement avant démarrage */}
        {etape.pointDeNonRetour && etape.etat === 'AFaire' && planAgissant && (
          <Alerte
            ton="warn"
            icone={Flag}
            role="alert"
            titre="Point de non-retour"
            action={nonRetourAttendu && mep.droits.estPilote ? (
              <Button variante="primaire" taille="sm" icone={<ShieldCheck className="size-4" aria-hidden />} onClick={() => void actions.autoriserNonRetour(etape)}>Autoriser le passage</Button>
            ) : undefined}
          >
            Une fois cette étape démarrée, <strong>il ne sera plus possible de revenir en arrière</strong> : l’échéance ne s’applique plus et tout problème devra être corrigé en avant.
            {nonRetourAttendu
              ? (mep.droits.estPilote ? ' Autorisez le passage pour permettre le démarrage.' : ' Le démarrage attend l’accord du pilote.')
              : ` Passage autorisé par ${etape.nonRetourAutorise?.par ?? 'le pilote'}${etape.nonRetourAutorise ? ` le ${formatDateHeure(etape.nonRetourAutorise.le)}` : ''}.`}
          </Alerte>
        )}
        {etape.pointDeNonRetour && etape.etat !== 'AFaire' && (
          <p className="flex items-center gap-1.5 text-[13px] font-medium text-warn-fg"><Flag className="size-4 text-warn-solid" aria-hidden />Point de non-retour franchi : on corrige en avant.</p>
        )}

        {/* Échec */}
        {enEchec && (
          <div className="space-y-4">
            <Alerte ton="danger" icone={OctagonAlert} role="alert" titre="Arrêter, prévenir le pilote, ne rien improviser.">
              {etape.preuve.trim() ? <>Preuve : {etape.preuve}</> : 'L’étape est en échec.'}
            </Alerte>

            {enCours ? (
              <ResolutionDeroule resolution={enCours} peutAgir={estMoi} actions={actions} />
            ) : liees.length > 0 ? (
              <section>
                <Titre icone={CircleHelp}>Problème connu ?</Titre>
                <ul className="space-y-2.5">
                  {liees.map((r) => (
                    <li key={r.id} className="rounded-lg border border-warn-bd bg-warn-bg/40 p-3.5">
                      <p className="flex items-center gap-2 font-semibold"><Wrench className="size-4 text-warn-solid" aria-hidden />{r.titre}</p>
                      {r.symptomes.trim() && <p className="mt-1 text-sm whitespace-pre-line text-fg-muted"><span className="font-medium text-fg">Symptômes : </span>{r.symptomes}</p>}
                      <div className="mt-3 flex flex-wrap items-center gap-3">
                        <Button variante="primaire" disabled={!estMoi} icone={<Wrench className="size-4" aria-hidden />} onClick={() => void actions.appliquerResolution(etape, r.id)}>Appliquer cette résolution</Button>
                        {!estMoi && <span className="flex items-center gap-1 text-xs text-fg-subtle"><Lock className="size-3.5" aria-hidden />Prenez la main pour l’appliquer.</span>}
                      </div>
                    </li>
                  ))}
                </ul>
              </section>
            ) : (
              <Alerte ton="warn" icone={OctagonAlert}>Aucune résolution prévue : arrêter et prévenir le pilote.</Alerte>
            )}

            {mep.droits.estPilote && !enCours && (
              <Button variante="secondaire" icone={<Play className="size-4" aria-hidden />} onClick={() => void actions.autoriserReprise(etape)}>Autoriser la reprise</Button>
            )}
          </div>
        )}

        {/* Qui exécute */}
        <div className="flex flex-wrap items-center gap-3 rounded-lg border border-line bg-surface-2/50 px-3.5 py-2.5 text-sm">
          {nomExec ? <Avatar nom={nomExec} /> : <span className="size-8 rounded-full border border-dashed border-line-strong" />}
          <div className="min-w-0">
            {priseEnMain ? (
              <p className="font-medium">Exécutée par {nomExec} à la place de {nomPrevu ?? 'son responsable prévu'}</p>
            ) : (
              <p className="font-medium">{nomExec ? `Responsable : ${nomExec}` : 'Aucun responsable désigné'}</p>
            )}
            <p className="text-xs text-fg-subtle">
              {etape.debutReel ? `Démarrée le ${formatDateHeure(etape.debutReel)}` : 'Pas encore démarrée'}
              {etape.finReelle && ` · terminée le ${formatDateHeure(etape.finReelle)}`}
            </p>
          </div>
        </div>

        {/* Instructions */}
        <section>
          <Titre icone={Play}>Instructions exactes</Titre>
          {etape.instructions.trim() ? <CodeBlock code={etape.instructions} titre="Instructions" /> : <p className="text-sm text-fg-subtle">Aucune instruction renseignée.</p>}
        </section>
        {etape.resultatAttendu.trim() && (
          <section className="rounded-lg border border-ok-bd bg-ok-bg px-3.5 py-3 text-ok-fg">
            <Titre icone={Check}>Résultat attendu</Titre>
            <p className="text-sm whitespace-pre-line">{etape.resultatAttendu}</p>
          </section>
        )}

        {/* Actions */}
        {peutDemarrer && (
          <Button variante="primaire" taille="lg" className="h-14 w-full text-lg" icone={<Play className="size-5" aria-hidden />} onClick={() => void actions.demarrer(etape)}>Démarrer</Button>
        )}
        {peutTerminer && (
          <div className="space-y-3">
            <Field label="Preuve" hint="Sortie de commande, capture, constat… Obligatoire en cas d’échec.">
              {(id) => <Textarea id={id} rows={3} value={preuve} onChange={(e) => setPreuve(e.target.value)} placeholder="Ce que vous avez constaté" />}
            </Field>
            <div className="grid gap-3 sm:grid-cols-2">
              <Button variante="succes" taille="lg" className="h-14 text-lg" icone={<Check className="size-5" aria-hidden />} onClick={() => setConfirmation('reussie')}>Réussie</Button>
              <Button variante="danger" taille="lg" className="h-14 text-lg" icone={<XCircle className="size-5" aria-hidden />} disabled={!preuve.trim()} onClick={() => setConfirmation('echec')}>Échec</Button>
            </div>
            {!preuve.trim() && <p className="text-xs text-fg-subtle">Saisissez une preuve pour pouvoir déclarer un échec.</p>}
          </div>
        )}
        {peutPrendre && (
          <div className="rounded-lg border border-line p-3.5">
            <p className="mb-2 text-sm text-fg-muted">{nomExec ? `Cette étape est confiée à ${nomExec}.` : 'Cette étape n’a pas d’exécutant.'}</p>
            <Button icone={<HandMetal className="size-4" aria-hidden />} onClick={() => setConfirmation('main')}>Prendre la main</Button>
          </div>
        )}

        {etape.etat === 'Reussie' && etape.preuve.trim() && (
          <section>
            <Titre icone={Check}>Preuve enregistrée</Titre>
            <p className="rounded-lg border border-line bg-surface-2/50 px-3 py-2 text-sm whitespace-pre-line">{etape.preuve}</p>
          </section>
        )}

        <PiecesJointes
          compact
          lectureSeule={mep.phase === 'Cloturee'}
          pilote={mep.droits.estPilote}
          pieces={mep.piecesJointes.filter((p) => p.etapeId === etape.id)}
          onDeposer={(f, signaler) => actions.deposerPiece(etape, f, signaler)}
          onSupprimer={(p, signaler) => actions.supprimerPiece(p, signaler)}
        />

        {historique.length > 0 && (
          <section>
            <Titre icone={History}>Résolutions déjà appliquées</Titre>
            <ul className="space-y-2">
              {historique.map((r) => (
                <li key={r.id}>
                  <details className="group rounded-lg border border-line bg-surface-2/40">
                    <summary className="flex cursor-pointer flex-wrap items-center gap-2 px-3 py-2 text-sm">
                      <span className="font-medium">{r.titre}</span>
                      <StatusPill taille="sm" ton={r.statut === 'Reussie' ? 'ok' : 'danger'} icone={r.statut === 'Reussie' ? Check : XCircle}>{LIBELLE_STATUT_RESOLUTION[r.statut]}</StatusPill>
                      <span className="text-xs text-fg-subtle">{r.demarreePar} · {formatDateHeure(r.demarreeLe)}</span>
                    </summary>
                    <ol className="space-y-1 border-t border-line px-3 py-2 text-xs text-fg-muted">
                      {r.corrections.map((c) => (
                        <li key={c.ordre}><span className="font-mono">Correction {c.ordre}</span> : {c.resultat ?? 'non réalisée'}{c.observation && ` — ${c.observation}`}</li>
                      ))}
                      <li><span className="font-mono">Vérification</span> : {r.verificationResultat ?? 'non réalisée'}{r.verificationObservation && ` — ${r.verificationObservation}`}</li>
                    </ol>
                  </details>
                </li>
              ))}
            </ul>
          </section>
        )}
      </div>

      <ConfirmDialog
        open={confirmation === 'reussie'}
        onOpenChange={(o) => { if (!o) setConfirmation(null); }}
        titre={`${etape.code} réussie ?`}
        description="Confirmez que vous avez bien contrôlé le résultat avant de passer à la suite."
        libelleConfirmer="J’ai vérifié et obtenu le résultat attendu"
        variante="succes"
        taille="md"
        onConfirmer={async () => { const r = await actions.terminer(etape, 'Reussie', preuve.trim()); if (r !== false) setPreuve(''); return r; }}
      >
        {etape.resultatAttendu.trim() && (
          <div className="rounded-lg border border-ok-bd bg-ok-bg px-3.5 py-3 text-sm text-ok-fg">
            <p className="text-[11px] font-semibold tracking-wide uppercase">Résultat attendu</p>
            <p className="whitespace-pre-line">{etape.resultatAttendu}</p>
          </div>
        )}
      </ConfirmDialog>

      <ConfirmDialog
        open={confirmation === 'echec'}
        onOpenChange={(o) => { if (!o) setConfirmation(null); }}
        titre={`Déclarer l’échec de ${etape.code} ?`}
        libelleConfirmer="Déclarer l’échec"
        variante="danger"
        onConfirmer={async () => { const r = await actions.terminer(etape, 'Echec', preuve.trim()); if (r !== false) setPreuve(''); return r; }}
      >
        <Alerte ton="danger" icone={OctagonAlert}>Arrêtez, prévenez le pilote, n’improvisez rien. Les résolutions types liées à l’étape vous seront proposées.</Alerte>
      </ConfirmDialog>

      <ConfirmDialog
        open={confirmation === 'main'}
        onOpenChange={(o) => { if (!o) setConfirmation(null); }}
        titre={`Prendre la main sur ${etape.code} ?`}
        description={`Vous exécuterez cette étape${nomExec ? ` à la place de ${nomExec}` : ''}. Cette prise en main est tracée au journal : qui, quand et à la place de qui.`}
        libelleConfirmer="Prendre la main"
        variante="primaire"
        onConfirmer={() => actions.prendreLaMain(etape)}
      />
    </Card>
  );
}
