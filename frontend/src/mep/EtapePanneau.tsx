import { Check, CircleHelp, Clock, Flag, HandMetal, History, Lock, OctagonAlert, Play, ShieldCheck, Undo2, Wrench, XCircle } from 'lucide-react';
import { useContext, useState } from 'react';
import { AuthContext } from '../auth';
import { bloqueurs, estAVous, nomParticipant } from '../domaine';
import { formatDateHeure, LIBELLE_CRITICITE, LIBELLE_STATUT_RESOLUTION } from '../format';
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
import { Bloc, Surcouche } from './graphe/EspaceEtape';
import { etatCarte } from './graphe/etatCarte';
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

/** Liste compacte d'étapes liées avec leur état (préalables, débloque). */
function ListeEtats({ etapes, vide, onSelectionner }: { etapes: Etape[]; vide: string; onSelectionner: (id: string) => void }) {
  if (etapes.length === 0) return <p className="text-[13px] text-fg-subtle">{vide}</p>;
  return (
    <ul className="grid grid-cols-[minmax(0,1fr)] gap-1">
      {etapes.map((p) => {
        const ep = etatCarte(p);
        return (
          <li key={p.id}>
            <button type="button" onClick={() => onSelectionner(p.id)} className="flex w-full items-center justify-between gap-2 rounded-md bg-surface-2/60 px-2 py-1 text-left text-[13px] hover:bg-surface-2">
              <span className="min-w-0 truncate"><b className="mr-1.5 font-mono text-xs font-semibold">{p.code}</b>{p.action || 'sans libellé'}</span>
              <StatusPill taille="sm" className="shrink-0" ton={ep.ton} icone={ep.Icone}>{ep.libelle}</StatusPill>
            </button>
          </li>
        );
      })}
    </ul>
  );
}

/**
 * Espace d'une étape en exécution : barre du haut, colonnes État et actions / Instructions / Liens, résolutions et pièces.
 * Démarrer : tout participant relié à un compte, le pilote ou un administrateur ; il en devient l'exécutant.
 */
export function EtapePanneau({ mep, etape, resolutionsTypes, actions, onSelectionner, onFermer = () => undefined }: Props) {
  const [preuve, setPreuve] = useState('');
  const [confirmation, setConfirmation] = useState<'reussie' | 'echec' | 'main' | null>(null);
  const [motifMain, setMotifMain] = useState('');
  const admin = useContext(AuthContext)?.utilisateur?.estAdmin ?? false;

  const meta = metaEtape(etape);
  const statut = etape.disponibilite.statut;
  const moi = mep.droits.participantId;
  const pilote = mep.droits.estPilote;
  const estExecutant = moi !== null && etape.executantId === moi;
  const nomExec = nomParticipant(mep, etape.executantId);
  const systemes = systemesDe(mep, etape.systemeIds);
  const memePlan = mep.etapes.filter((e) => e.plan === etape.plan);
  const prealables = etape.dependances.map((id) => memePlan.find((e) => e.id === id)).filter((e): e is Etape => !!e);
  const suivants = memePlan.filter((e) => e.dependances.includes(etape.id));
  const attendus = bloqueurs(etape, memePlan);

  const planAgissant = (mep.phase === 'Execution' && etape.plan === 'Deploiement') || (mep.phase === 'RetourArriere' && etape.plan === 'RetourArriere');
  const nonRetourAttendu = etape.pointDeNonRetour && etape.etat === 'AFaire' && etape.nonRetourAutorise === null;
  const peutDemarrer = (moi !== null || pilote || admin) && statut === 'Disponible' && etape.etat === 'AFaire' && !nonRetourAttendu;
  const peutTerminer = etape.etat === 'EnCours' && (estExecutant || pilote);
  const peutAgirResolution = estExecutant || pilote;
  const peutPrendre = planAgissant && moi !== null && !estExecutant && etape.executantId !== null && (etape.etat === 'EnCours' || etape.etat === 'Echec');
  const enEchec = etape.etat === 'Echec';

  const resolutionsEtape = mep.resolutions.filter((r) => r.etapeId === etape.id);
  const enCours = resolutionsEtape.find((r) => r.statut === 'EnCours');
  const historique = resolutionsEtape.filter((r) => r.statut !== 'EnCours');
  const liees = etape.resolutionIds.map((id) => resolutionsTypes.find((r) => r.id === id)).filter((r): r is ResolutionType => !!r);
  const pieces = mep.piecesJointes.filter((p) => p.etapeId === etape.id);

  const barre = (
    <div className="flex flex-wrap items-center gap-x-3 gap-y-1.5">
      <span className="rounded-md bg-surface-2 px-2 py-0.5 font-mono text-sm font-bold">{etape.code}</span>
      <StatusPill ton={meta.ton} icone={meta.Icone} anime={meta.anime}>{meta.libelle}</StatusPill>
      {estAVous(mep, etape) && <Badge ton="run" className="text-xs">À vous</Badge>}
      <BadgesEtape etape={etape} />
      <h2 className="min-w-0 text-lg leading-snug font-semibold tracking-tight">{etape.action || 'Étape sans libellé'}</h2>
      <span className="flex flex-wrap items-center gap-1.5">
        {systemes.map((s) => <PuceSysteme key={s.id} nom={s.nom} />)}
      </span>
      <span className="text-[13px] text-fg-muted">
        {etape.criticite && LIBELLE_CRITICITE[etape.criticite]}
        {etape.dureeEstimeeMinutes !== null && <>{etape.criticite && ' · '}{etape.dureeEstimeeMinutes} min</>}
      </span>
    </div>
  );

  const gauche = (
    <>
      {statut === 'Attend' && (
        <Alerte ton="wait" icone={Clock} titre="En attente">
          <ul className="mt-1 space-y-1">
            {attendus.length === 0 && <li>{etape.disponibilite.libelle}</li>}
            {attendus.map((b) => (
              <li key={b.id}>
                Bloquée par{' '}
                <button type="button" className="font-mono font-bold underline underline-offset-2 hover:text-accent-text" onClick={() => onSelectionner(b.id)}>{b.code}</button>
              </li>
            ))}
          </ul>
        </Alerte>
      )}
      {statut === 'SiRetourArriere' && <Alerte ton="later" icone={Undo2}>Seulement en cas de retour arrière.</Alerte>}
      {statut === 'RetourArriere' && etape.etat !== 'Echec' && <Alerte ton="rollback" icone={OctagonAlert} titre="Retour arrière">Ne plus exécuter cette étape.</Alerte>}
      {['PlanInvalide', 'EcheanceARenseigner', 'FicheACompleter', 'InstructionsACompleter'].includes(statut) && (
        <Alerte ton="warn" icone={OctagonAlert}>{etape.disponibilite.libelle}</Alerte>
      )}
      {etape.pointDeNonRetour && etape.etat === 'AFaire' && planAgissant && (
        <Alerte
          ton="warn"
          icone={Flag}
          role="alert"
          titre="Point de non-retour"
          action={nonRetourAttendu && pilote ? (
            <Button variante="primaire" taille="sm" icone={<ShieldCheck className="size-4" aria-hidden />} onClick={() => void actions.autoriserNonRetour(etape)}>Autoriser le passage</Button>
          ) : undefined}
        >
          Après le démarrage, <strong>plus de retour arrière</strong>.
          {nonRetourAttendu
            ? (pilote ? ' Autorisez le passage.' : ' En attente de l’accord du pilote.')
            : ` Autorisé par ${etape.nonRetourAutorise?.par ?? 'le pilote'}${etape.nonRetourAutorise ? ` le ${formatDateHeure(etape.nonRetourAutorise.le)}` : ''}.`}
        </Alerte>
      )}
      {etape.pointDeNonRetour && etape.etat !== 'AFaire' && (
        <p className="flex items-center gap-1.5 text-[13px] font-medium text-warn-fg"><Flag className="size-4 text-warn-solid" aria-hidden />Point de non-retour franchi.</p>
      )}

      <Bloc titre="Exécutant">
        <div className="flex items-center gap-2.5 text-sm">
          {nomExec ? <Avatar nom={nomExec} taille="sm" /> : <span className="size-6 rounded-full border border-dashed border-line-strong" />}
          <div className="min-w-0">
            <p className="font-medium">{nomExec ?? 'Pas encore démarrée'}</p>
            {(etape.debutReel || etape.finReelle) && (
              <p className="text-xs text-fg-subtle">
                {etape.debutReel && `Démarrée le ${formatDateHeure(etape.debutReel)}`}
                {etape.finReelle && ` · terminée le ${formatDateHeure(etape.finReelle)}`}
              </p>
            )}
          </div>
        </div>
        {peutPrendre && <Button taille="sm" icone={<HandMetal className="size-4" aria-hidden />} onClick={() => setConfirmation('main')}>Prendre la main</Button>}
      </Bloc>

      {peutDemarrer && (
        <Button variante="primaire" taille="lg" className="h-12 w-full" icone={<Play className="size-5" aria-hidden />} onClick={() => void actions.demarrer(etape)}>Démarrer</Button>
      )}
      {peutTerminer && (
        <div className="grid gap-2.5">
          <Field label="Preuve" hint="Obligatoire en cas d’échec.">
            {(id) => <Textarea id={id} rows={3} value={preuve} onChange={(e) => setPreuve(e.target.value)} placeholder="Ce que vous avez constaté" />}
          </Field>
          <div className="grid grid-cols-2 gap-2">
            <Button variante="succes" taille="lg" className="h-12" icone={<Check className="size-5" aria-hidden />} onClick={() => setConfirmation('reussie')}>Réussie</Button>
            <Button variante="danger" taille="lg" className="h-12" icone={<XCircle className="size-5" aria-hidden />} disabled={!preuve.trim()} onClick={() => setConfirmation('echec')}>Échec</Button>
          </div>
        </div>
      )}
      {etape.preuve.trim() && etape.etat === 'Reussie' && (
        <Bloc titre="Preuve"><p className="rounded-md bg-surface-2/60 px-2.5 py-1.5 text-sm whitespace-pre-line">{etape.preuve}</p></Bloc>
      )}
      {etape.etat !== 'Reussie' && <AppelerCarte mep={mep} etape={etape} />}
    </>
  );

  const resolutionsEchec = enEchec && (
    <div className="grid gap-3">
      <Alerte ton="danger" icone={OctagonAlert} role="alert" titre="Arrêter, prévenir le pilote, ne rien improviser.">
        {etape.preuve.trim() ? <>Preuve : {etape.preuve}</> : 'L’étape est en échec.'}
      </Alerte>
      {enCours ? (
        <ResolutionDeroule resolution={enCours} peutAgir={peutAgirResolution} actions={actions} />
      ) : liees.length > 0 ? (
        <section>
          <h3 className="mb-2 flex items-center gap-1.5 text-sm font-semibold"><CircleHelp className="size-4" aria-hidden />Problème connu ?</h3>
          <ul className="grid gap-2">
            {liees.map((r) => (
              <li key={r.id} className="rounded-lg border border-warn-bd bg-warn-bg/40 p-3">
                <p className="flex items-center gap-2 font-semibold"><Wrench className="size-4 text-warn-solid" aria-hidden />{r.titre}</p>
                {r.symptomes.trim() && <Markdown className="mt-1 text-fg-muted" source={`**Symptômes** : ${r.symptomes}`} />}
                <div className="mt-2 flex flex-wrap items-center gap-3">
                  <Button variante="primaire" taille="sm" disabled={!peutAgirResolution} icone={<Wrench className="size-4" aria-hidden />} onClick={() => void actions.appliquerResolution(etape, r.id)}>Appliquer cette résolution</Button>
                  {!peutAgirResolution && <span className="flex items-center gap-1 text-xs text-fg-subtle"><Lock className="size-3.5" aria-hidden />Réservé à l’exécutant.</span>}
                </div>
              </li>
            ))}
          </ul>
        </section>
      ) : (
        <Alerte ton="warn" icone={OctagonAlert}>Aucune résolution prévue : arrêter et prévenir le pilote.</Alerte>
      )}
      {pilote && !enCours && (
        <div><Button icone={<Play className="size-4" aria-hidden />} onClick={() => void actions.autoriserReprise(etape)}>Autoriser la reprise</Button></div>
      )}
    </div>
  );

  const centre = (
    <>
      {resolutionsEchec}
      <Bloc titre="Instructions exactes">
        <Instructions source={etape.instructions} vide="Aucune instruction renseignée." />
      </Bloc>
      {etape.resultatAttendu.trim() && (
        <section className="rounded-lg border border-ok-bd bg-ok-bg px-3.5 py-3 text-ok-fg">
          <h3 className="mb-1.5 flex items-center gap-1.5 text-[11px] font-semibold tracking-[0.06em] uppercase"><Check className="size-3.5" aria-hidden />Résultat attendu</h3>
          <Markdown source={etape.resultatAttendu} />
        </section>
      )}
    </>
  );

  const droite = (
    <>
      <Bloc titre={`Attend (${prealables.length})`}><ListeEtats etapes={prealables} vide="Aucun préalable." onSelectionner={onSelectionner} /></Bloc>
      <Bloc titre={`Débloque (${suivants.length})`}><ListeEtats etapes={suivants} vide="Aucune." onSelectionner={onSelectionner} /></Bloc>
      {!enEchec && (
        <Bloc titre={`Résolutions prévues (${liees.length})`}>
          {liees.length === 0 ? <p className="text-[13px] text-fg-subtle">Aucune.</p> : (
            <ul className="grid gap-1">{liees.map((r) => <li key={r.id} className="flex items-center gap-1.5 text-[13px]"><Wrench className="size-3.5 shrink-0 text-warn-solid" aria-hidden />{r.titre}</li>)}</ul>
          )}
        </Bloc>
      )}
      {historique.length > 0 && (
        <section>
          <h3 className="mb-1.5 flex items-center gap-1.5 text-[11px] font-semibold tracking-[0.06em] text-fg-subtle uppercase"><History className="size-3.5" aria-hidden />Résolutions déjà appliquées</h3>
          <ul className="grid gap-1.5">
            {historique.map((r) => (
              <li key={r.id}>
                <details className="rounded-md border border-line bg-surface-2/40">
                  <summary className="flex cursor-pointer flex-wrap items-center gap-2 px-2.5 py-1.5 text-[13px]">
                    <span className="font-medium">{r.titre}</span>
                    <StatusPill taille="sm" ton={r.statut === 'Reussie' ? 'ok' : 'danger'} icone={r.statut === 'Reussie' ? Check : XCircle}>{LIBELLE_STATUT_RESOLUTION[r.statut]}</StatusPill>
                    <span className="text-xs text-fg-subtle">{r.demarreePar} · {formatDateHeure(r.demarreeLe)}</span>
                  </summary>
                  <ol className="space-y-1 border-t border-line px-2.5 py-1.5 text-xs text-fg-muted">
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
      <PiecesJointes
        compact
        lectureSeule={mep.phase === 'Cloturee'}
        pilote={pilote}
        pieces={pieces}
        onDeposer={(f, signaler) => actions.deposerPiece(etape, f, signaler)}
        onSupprimer={(p, signaler) => actions.supprimerPiece(p, signaler)}
      />
    </>
  );

  return (
    <>
      <Surcouche etiquette={`Étape ${etape.code}`} onDemandeFermeture={onFermer} barre={barre} gauche={gauche} centre={centre} droite={droite} />

      <ConfirmDialog
        open={confirmation === 'reussie'}
        onOpenChange={(o) => { if (!o) setConfirmation(null); }}
        titre={`${etape.code} réussie ?`}
        description="Confirmez que vous avez contrôlé le résultat."
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
        <Alerte ton="danger" icone={OctagonAlert}>Arrêtez et prévenez le pilote. Les résolutions liées vous seront proposées.</Alerte>
      </ConfirmDialog>

      <ConfirmDialog
        open={confirmation === 'main'}
        onOpenChange={(o) => { if (!o) { setConfirmation(null); setMotifMain(''); } }}
        titre={`Prendre la main sur ${etape.code} ?`}
        description={`Vous poursuivrez cette étape${nomExec ? ` à la place de ${nomExec}` : ''}. Tracé au journal.`}
        libelleConfirmer="Prendre la main"
        variante="primaire"
        taille="md"
        desactive={!motifMain.trim()}
        onConfirmer={async () => { const r = await actions.prendreLaMain(etape, motifMain.trim()); if (r !== false) setMotifMain(''); return r; }}
      >
        <Field label="Pourquoi prenez-vous la main ? *">
          {(id) => <Textarea id={id} value={motifMain} onChange={(e) => setMotifMain(e.target.value)} rows={3} placeholder={nomExec ? `ex. ${nomExec} est injoignable.` : 'ex. L’exécutant est injoignable.'} autoFocus />}
        </Field>
      </ConfirmDialog>
    </>
  );
}
