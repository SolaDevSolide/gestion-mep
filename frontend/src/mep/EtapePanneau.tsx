import { Check, CircleHelp, Clock, Flag, HandMetal, History, Lock, OctagonAlert, Play, ShieldCheck, Undo2, Wrench, XCircle } from 'lucide-react';
import { useEffect, useState } from 'react';
import { bloqueurs, estAVous, executantEffectif, nomParticipant } from '../domaine';
import { formatDateHeure, LIBELLE_CRITICITE, LIBELLE_ETAT, LIBELLE_STATUT_RESOLUTION } from '../format';
import { metaEtape } from '../statuts';
import type { Etape, ExecutionResolution, Mep, PieceJointe, ResolutionType, ResultatControle } from '../types';
import { Alerte } from '../ui/Alerte';
import { Avatar } from '../ui/Avatar';
import { Button } from '../ui/Button';
import { ConfirmDialog } from '../ui/Dialog';
import { Instructions, Markdown } from '../ui/Instructions';
import { Field, Textarea } from '../ui/Input';
import { PiecesJointes } from '../ui/PiecesJointes';
import { Badge, StatusPill } from '../ui/StatusPill';
import { AppelerCarte } from './AppelerCarte';
import { BadgesEtape } from './BadgesEtape';
import { etatCarte } from './graphe/etatCarte';
import { Section, StructurePanneau } from './graphe/PanneauLateral';
import { PuceSysteme, systemesDe } from './graphe/systemes';
import { ResolutionDeroule } from './ResolutionDeroule';

export interface ActionsEtape {
  demarrer: (e: Etape) => Promise<boolean | void> | void;
  terminer: (e: Etape, resultat: 'Reussie' | 'Echec', preuve: string) => Promise<boolean | void> | void;
  autoriserReprise: (e: Etape) => Promise<boolean | void> | void;
  prendreLaMain: (e: Etape, motif: string) => Promise<boolean | void> | void;
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
  onFermer?: () => void;
}

/** Liste d'étapes liées avec leur état (préalables, débloque). */
function ListeEtats({ etapes, vide, onSelectionner }: { etapes: Etape[]; vide: string; onSelectionner: (id: string) => void }) {
  if (etapes.length === 0) return <p className="text-sm text-fg-subtle">{vide}</p>;
  return (
    <ul className="grid gap-1.5">
      {etapes.map((p) => {
        const ep = etatCarte(p);
        return (
          <li key={p.id}>
            <button type="button" onClick={() => onSelectionner(p.id)} className="flex w-full items-center justify-between gap-2 rounded-lg border border-line bg-surface-2/50 px-3 py-2 text-left text-sm hover:border-accent">
              <span className="min-w-0 truncate"><b className="mr-2 font-mono text-xs font-semibold">{p.code}</b>{p.action || 'sans libellé'}</span>
              <StatusPill taille="sm" ton={ep.ton} icone={ep.Icone}>{ep.libelle}</StatusPill>
            </button>
          </li>
        );
      })}
    </ul>
  );
}

/** Panneau de l'étape sélectionnée en exécution : onglets Exécution, Liens, Résolutions, Pièces jointes. */
export function EtapePanneau({ mep, etape, resolutionsTypes, actions, onSelectionner, onFermer }: Props) {
  const [preuve, setPreuve] = useState('');
  const [confirmation, setConfirmation] = useState<'reussie' | 'echec' | 'main' | null>(null);
  const [motifMain, setMotifMain] = useState('');
  const enEchec = etape.etat === 'Echec';
  const [onglet, setOnglet] = useState(enEchec ? 'resolutions' : 'execution');
  // Une étape qui passe en échec pendant qu'on la regarde : on montre les résolutions.
  useEffect(() => { if (enEchec) setOnglet('resolutions'); }, [enEchec]);

  const meta = metaEtape(etape);
  const statut = etape.disponibilite.statut;
  const moi = mep.droits.participantId;
  const execId = executantEffectif(etape);
  const estMoi = moi !== null && execId === moi;
  const nomExec = nomParticipant(mep, execId);
  const nomPrevu = nomParticipant(mep, etape.responsableId);
  const priseEnMain = etape.executantId !== null && etape.executantId !== etape.responsableId;
  const systemes = systemesDe(mep, etape.systemeIds);
  const memePlan = mep.etapes.filter((e) => e.plan === etape.plan);
  const prealables = etape.dependances.map((id) => memePlan.find((e) => e.id === id)).filter((e): e is Etape => !!e);
  const suivants = memePlan.filter((e) => e.dependances.includes(etape.id));
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
  const pieces = mep.piecesJointes.filter((p) => p.etapeId === etape.id);

  const entete = (
    <div className="grid gap-2">
      <div className="flex flex-wrap items-center gap-2">
        <span className="rounded-md bg-surface-2 px-2 py-0.5 font-mono text-sm font-bold">{etape.code}</span>
        <StatusPill ton={meta.ton} icone={meta.Icone} anime={meta.anime}>{meta.libelle}</StatusPill>
        {estAVous(mep, etape) && <Badge ton="run" className="text-xs">À vous</Badge>}
        <BadgesEtape etape={etape} />
      </div>
      <h2 className="text-lg leading-snug font-semibold tracking-tight">{etape.action || 'Étape sans libellé'}</h2>
      <p className="text-[13px] text-fg-muted">
        {etape.criticite ? <>Criticité {LIBELLE_CRITICITE[etape.criticite].toLowerCase()}</> : 'Criticité non précisée'}
        <> · {LIBELLE_ETAT[etape.etat]}</>
        {etape.dureeEstimeeMinutes !== null && <> · {etape.dureeEstimeeMinutes} min prévues</>}
      </p>
      <div className="flex flex-wrap gap-1.5">
        {systemes.length > 0 ? systemes.map((s) => <PuceSysteme key={s.id} nom={s.nom} description={s.cible || undefined} />) : <span className="text-[13px] text-fg-subtle">Système non précisé</span>}
      </div>
    </div>
  );

  const ongletExecution = (
    <>
      {enEchec && (
        <Alerte ton="danger" icone={OctagonAlert} titre="Étape en échec" action={<Button taille="sm" onClick={() => setOnglet('resolutions')}>Voir les résolutions</Button>}>
          Arrêtez et prévenez le pilote ; les procédures prévues sont dans l’onglet Résolutions.
        </Alerte>
      )}
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

      <Section titre="Qui exécute">
        <div className="flex flex-wrap items-center gap-3 rounded-lg border border-line bg-surface-2/50 px-3.5 py-2.5 text-sm">
          {nomExec ? <Avatar nom={nomExec} /> : <span className="size-8 rounded-full border border-dashed border-line-strong" />}
          <div className="min-w-0 flex-1">
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
          {peutPrendre && <Button taille="sm" icone={<HandMetal className="size-4" aria-hidden />} onClick={() => setConfirmation('main')}>Prendre la main</Button>}
        </div>
        {peutPrendre && <p className="text-xs text-fg-subtle">{nomExec ? `Cette étape est confiée à ${nomExec}.` : 'Cette étape n’a pas d’exécutant.'}</p>}
        {etape.etat !== 'Reussie' && <AppelerCarte mep={mep} etape={etape} />}
      </Section>

      <Section titre="Instructions exactes">
        <Instructions source={etape.instructions} vide="Aucune instruction renseignée." />
      </Section>
      {etape.resultatAttendu.trim() && (
        <section className="rounded-lg border border-ok-bd bg-ok-bg px-3.5 py-3 text-ok-fg">
          <h3 className="mb-2 flex items-center gap-1.5 text-xs font-semibold tracking-wide uppercase"><Check className="size-3.5" aria-hidden />Résultat attendu</h3>
          <Markdown source={etape.resultatAttendu} />
        </section>
      )}

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
      {etape.etat === 'Reussie' && etape.preuve.trim() && (
        <Section titre="Preuve enregistrée">
          <p className="rounded-lg border border-line bg-surface-2/50 px-3 py-2 text-sm whitespace-pre-line">{etape.preuve}</p>
        </Section>
      )}
    </>
  );

  const ongletResolutions = (
    <>
      {enEchec && (
        <div className="space-y-4">
          <Alerte ton="danger" icone={OctagonAlert} role="alert" titre="Arrêter, prévenir le pilote, ne rien improviser.">
            {etape.preuve.trim() ? <>Preuve : {etape.preuve}</> : 'L’étape est en échec.'}
          </Alerte>
          {enCours ? (
            <ResolutionDeroule resolution={enCours} peutAgir={estMoi} actions={actions} />
          ) : liees.length > 0 ? (
            <section>
              <h3 className="mb-2 flex items-center gap-1.5 text-sm font-semibold"><CircleHelp className="size-4" aria-hidden />Problème connu ?</h3>
              <ul className="space-y-2.5">
                {liees.map((r) => (
                  <li key={r.id} className="rounded-lg border border-warn-bd bg-warn-bg/40 p-3.5">
                    <p className="flex items-center gap-2 font-semibold"><Wrench className="size-4 text-warn-solid" aria-hidden />{r.titre}</p>
                    {r.symptomes.trim() && <Markdown className="mt-1 text-fg-muted" source={`**Symptômes** : ${r.symptomes}`} />}
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
      {!enEchec && (
        <Section titre="Prévues en cas d’échec" description="Proposées ici si l’étape échoue.">
          {liees.length === 0 ? <p className="text-sm text-fg-subtle">Aucune résolution type liée.</p> : (
            <ul className="grid gap-1.5">{liees.map((r) => <li key={r.id} className="flex items-center gap-2 rounded-lg border border-line px-3 py-2 text-sm"><Wrench className="size-4 text-warn-solid" aria-hidden />{r.titre}</li>)}</ul>
          )}
        </Section>
      )}
      {historique.length > 0 && (
        <section>
          <h3 className="mb-2 flex items-center gap-1.5 text-sm font-semibold"><History className="size-4" aria-hidden />Résolutions déjà appliquées</h3>
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
    </>
  );

  return (
    <>
      <StructurePanneau
        entete={entete}
        onglet={onglet}
        onOnglet={setOnglet}
        onFermer={onFermer}
        onglets={[
          { valeur: 'execution', libelle: 'Exécution', contenu: ongletExecution },
          {
            valeur: 'liens', libelle: 'Liens', compteur: prealables.length + suivants.length, alerte: attendus.length > 0 && 'Préalables pas encore réussis',
            contenu: (
              <>
                <Section titre={`Préalables (${prealables.length})`}><ListeEtats etapes={prealables} vide="Aucun : l’étape peut démarrer dès le Go." onSelectionner={onSelectionner} /></Section>
                <Section titre={`Débloque (${suivants.length})`}><ListeEtats etapes={suivants} vide="Aucune étape n’attend celle-ci." onSelectionner={onSelectionner} /></Section>
              </>
            ),
          },
          { valeur: 'resolutions', libelle: 'Résolutions', compteur: liees.length, alerte: enEchec && 'Étape en échec', contenu: ongletResolutions },
          {
            valeur: 'pieces', libelle: 'Pièces jointes', compteur: pieces.length,
            contenu: (
              <PiecesJointes
                lectureSeule={mep.phase === 'Cloturee'}
                pilote={mep.droits.estPilote}
                pieces={pieces}
                onDeposer={(f, signaler) => actions.deposerPiece(etape, f, signaler)}
                onSupprimer={(p, signaler) => actions.supprimerPiece(p, signaler)}
              />
            ),
          },
        ]}
      />

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
            <Markdown source={etape.resultatAttendu} />
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
        onOpenChange={(o) => { if (!o) { setConfirmation(null); setMotifMain(''); } }}
        titre={`Prendre la main sur ${etape.code} ?`}
        description={`Vous exécuterez cette étape${nomExec ? ` à la place de ${nomExec}` : ''}. Cette prise en main est tracée au journal : qui, quand, à la place de qui et pourquoi.`}
        libelleConfirmer="Prendre la main"
        variante="primaire"
        taille="md"
        desactive={!motifMain.trim()}
        onConfirmer={async () => { const r = await actions.prendreLaMain(etape, motifMain.trim()); if (r !== false) setMotifMain(''); return r; }}
      >
        <Field label="Pourquoi prenez-vous la main ? *" hint="Visible de tous dans le journal.">
          {(id) => <Textarea id={id} value={motifMain} onChange={(e) => setMotifMain(e.target.value)} rows={3} placeholder={nomExec ? `ex. ${nomExec} est injoignable, l’étape est bloquante.` : 'ex. Le responsable est injoignable.'} autoFocus />}
        </Field>
      </ConfirmDialog>
    </>
  );
}
