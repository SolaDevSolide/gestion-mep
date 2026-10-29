import {
  AlarmClock,
  Ban,
  CheckCircle2,
  Clock,
  FileText,
  Flag,
  HandMetal,
  Info,
  ListChecks,
  Play,
  Radio,
  Send,
  Siren,
  Timer,
  TriangleAlert,
  Webhook,
  Wrench,
  XCircle,
  type LucideIcon,
} from 'lucide-react';
import { useCallback, useMemo, useState, type ReactNode } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { etapesDuPlan, nomParticipant, segmentsProgression } from '../../domaine';
import { type Ton } from '../../format';
import { useNow } from '../../hooks';
import { useMep } from '../../mepContext';
import { useAutresPresents } from '../../presence';
import { useReferentiels, type DonneePartagee } from '../../referentiels';
import { SqueletteEnsemble, SqueletteEvenements } from './Squelettes';
import { metaPhase } from '../../statuts';
import type { EchecEnvoi, Etape, Evenement, Mep } from '../../types';
import { api } from '../../api';
import { Alerte } from '../../ui/Alerte';
import { Apparition } from '../../ui/Apparition';
import { Avatar } from '../../ui/Avatar';
import { Button } from '../../ui/Button';
import { ErreurChargement } from '../../ui/ErreurChargement';
import { cn } from '../../ui/cn';
import { ProgressBar } from '../../ui/ProgressBar';
import { StatusPill } from '../../ui/StatusPill';
import { TON } from '../../ui/tons';
import { useToast } from '../../ui/Toast';
import { useConnexionsMep } from '../connexionsMep';
import { GrapheLibre } from '../graphe/GrapheLibre';
import { sectionsDuPlan } from '../graphe/sections';
import { useJournalMep } from '../preparation/donnees';
import { descriptionsSystemes } from '../graphe/systemes';
import {
  causesBlocage,
  chiffres,
  depasse,
  duree,
  ecoulee,
  estLancee,
  estMarquant,
  heure,
  LIBELLE_ETAT_VUE,
  ouSeTrouve,
  planSuivi,
  TON_ETAT_VUE,
  tonRetard,
  type Chiffres,
  type EtatVue,
} from './calculs';

/** Motif cité dans une phrase qui a son propre point final. */
const sansPoint = (motif: string) => motif.trim().replace(/\.+$/, '');

/** Une tuile de chiffre clé. */
function Tuile({
  titre,
  icone: Icone,
  valeur,
  detail,
  ton,
  children,
  grand,
}: {
  titre: string;
  icone: LucideIcon;
  valeur: ReactNode;
  detail?: ReactNode;
  ton?: Ton;
  children?: ReactNode;
  grand?: boolean;
}) {
  return (
    <div
      className={cn(
        'min-w-0 rounded-xl border border-line bg-surface px-4 py-3 shadow-sm',
        grand && 'col-span-2 lg:col-span-2',
      )}
    >
      <p className="flex items-center gap-1.5 text-2xs font-semibold tracking-[0.06em] text-fg-subtle uppercase">
        <Icone className="size-3.5" aria-hidden />
        {titre}
      </p>
      <p
        className={cn(
          'mt-1 truncate text-2xl leading-tight font-bold tracking-tight tnum',
          ton ? TON[ton].texte : 'text-fg',
        )}
      >
        {valeur}
      </p>
      {detail && <p className="text-ui text-fg-muted tnum">{detail}</p>}
      {children}
    </div>
  );
}

function ChiffresCles({ c, etapes, compact }: { c: Chiffres; etapes: Etape[]; compact?: boolean }) {
  const avantGo = c.depart === null;
  return (
    <section
      aria-label="Chiffres clés"
      className={cn('grid grid-cols-2 gap-3 lg:grid-cols-6', compact && 'bg-surface-2/60 px-3 py-3')}
    >
      <Tuile
        grand
        titre="Avancement"
        icone={ListChecks}
        valeur={
          <>
            {c.faites}/{c.total}{' '}
            <span className="text-base font-semibold text-fg-muted">
              étape{c.faites > 1 ? 's' : ''} réussie{c.faites > 1 ? 's' : ''} · {c.pourcentage} %
            </span>
          </>
        }
      >
        <ProgressBar className="mt-2" hauteur="h-2.5" legende segments={segmentsProgression(etapes)} />
      </Tuile>
      <Tuile
        titre="Temps passé"
        icone={Timer}
        valeur={avantGo ? '—' : duree(c.passeMin)}
        detail={
          avantGo ? (
            <>avant le Go{c.prevuMin > 0 && <> · {duree(c.prevuMin)} prévues</>}</>
          ) : (
            <>
              depuis {heure(c.depart)}
              {c.prevuMin > 0 && <> · {duree(c.prevuMin)} prévues</>}
            </>
          )
        }
      />
      <Tuile
        titre={c.terminee ? 'Fin réelle' : 'Fin estimée'}
        icone={Flag}
        valeur={heure(c.finEstimee)}
        detail={
          c.sansDuree && !c.terminee
            ? 'durées non estimées'
            : c.finPrevue !== null
              ? `prévue à ${heure(c.finPrevue)}`
              : 'calculée au Go'
        }
      />
      <Tuile
        titre="Retard"
        icone={AlarmClock}
        ton={avantGo || c.sansDuree ? undefined : tonRetard(c.retardMin)}
        valeur={avantGo || c.sansDuree ? '—' : c.retardMin > 0 ? `+${duree(c.retardMin)}` : 'À l’heure'}
        detail={
          avantGo || c.sansDuree ? 'pas d’estimation' : c.retardMin > 0 ? 'sur la fin prévue' : 'pas de retard estimé'
        }
      />
      <Tuile
        titre="À traiter"
        icone={TriangleAlert}
        ton={c.echecs > 0 ? 'danger' : undefined}
        valeur={
          <>
            {c.echecs} échec{c.echecs > 1 ? 's' : ''}
          </>
        }
        detail={
          <>
            {c.bloquees} étape{c.bloquees > 1 ? 's' : ''} bloquée{c.bloquees > 1 ? 's' : ''}
          </>
        }
      />
    </section>
  );
}

/** Légende des couleurs du plan coloré. */
export function Legende() {
  const etats: EtatVue[] = ['faite', 'en-cours', 'echec', 'resolution', 'a-venir', 'bloquee'];
  return (
    <ul aria-label="Légende" className="flex flex-wrap items-center gap-x-3 gap-y-1 px-1 text-xs text-fg-muted">
      {etats.map((e) => (
        <li key={e} className="inline-flex items-center gap-1.5 whitespace-nowrap">
          <span
            className={cn(
              'size-3 rounded-[4px] border-2',
              e === 'bloquee'
                ? 'border-dashed border-danger-solid'
                : e === 'a-venir'
                  ? 'border-line-strong bg-surface'
                  : cn(TON[TON_ETAT_VUE[e]].bgSolide, 'border-transparent'),
            )}
            aria-hidden
          />
          {LIBELLE_ETAT_VUE[e]}
        </li>
      ))}
    </ul>
  );
}

/** Pastille « En direct » (heure de la dernière mise à jour de l'affichage). */
export function EnDirect({ maintenant }: { maintenant: number }) {
  return (
    <span className="inline-flex shrink-0 items-center gap-1.5 rounded-full border border-ok-bd bg-ok-bg px-2 py-0.5 text-xs font-semibold text-ok-fg">
      <span className="relative flex size-2">
        <span className="absolute inline-flex size-full animate-ping rounded-full bg-ok-solid opacity-60 motion-reduce:hidden" />
        <span className="relative inline-flex size-2 rounded-full bg-ok-solid" />
      </span>
      En direct · {heure(maintenant)}
    </span>
  );
}

function TitrePanneau({ children, nombre, ton }: { children: ReactNode; nombre?: number; ton?: Ton }) {
  return (
    <h3 className="flex items-center gap-2 text-2xs font-semibold tracking-[0.06em] text-fg-subtle uppercase">
      {children}
      {nombre !== undefined && (
        <span
          className={cn(
            'rounded-full px-1.5 text-2xs font-bold tnum',
            ton ? cn(TON[ton].bgSolide, TON[ton].texteSolide) : 'bg-surface-2 text-fg-muted',
          )}
        >
          {nombre}
        </span>
      )}
    </h3>
  );
}

/** Blocages et échecs : étapes en échec (et leur résolution), étapes qui dépassent leur durée, ce qu'elles bloquent. */
function Blocages({ mep, etapes, maintenant }: { mep: Mep; etapes: Etape[]; maintenant: number }) {
  const bloquees = causesBlocage(etapes);
  const echecs = etapes.filter((e) => e.etat === 'Echec');
  const depassements = etapes.filter((e) => depasse(e, maintenant));
  // Envois aux API externes définitivement en échec (#111) : relus à chaque version et à chaque fin d'envoi.
  const connexions = useConnexionsMep(mep.id, mep.estModele, mep.version);
  const envois = connexions.donnees?.echecs ?? [];
  const nb = echecs.length + depassements.length + envois.length;
  return (
    <section aria-label="Blocages et échecs" className="grid gap-2">
      <TitrePanneau nombre={nb} ton={nb > 0 ? 'danger' : undefined}>
        Blocages et échecs
      </TitrePanneau>
      {nb === 0 && <p className="text-ui text-fg-subtle">Rien de bloquant.</p>}
      {echecs.map((e) => {
        const resolution = mep.resolutions.find((r) => r.etapeId === e.id && r.statut === 'EnCours');
        const faites = resolution?.corrections.filter((x) => x.resultat !== null).length ?? 0;
        const bloque = etapes.filter((x) => bloquees.get(x.id)?.some((c) => c.id === e.id));
        const nom = nomParticipant(mep, e.executantId);
        return (
          <div
            key={e.id}
            className={cn(
              'rounded-lg border-l-4 bg-surface px-3 py-2 shadow-sm ring-1 ring-line',
              resolution ? 'border-l-warn-solid' : 'border-l-danger-solid',
            )}
          >
            <div className="flex items-center justify-between gap-2">
              <span className="font-mono text-xs text-fg-subtle">{e.code}</span>
              <StatusPill taille="sm" ton={resolution ? 'warn' : 'danger'} icone={resolution ? Wrench : XCircle}>
                {resolution ? 'Résolution en cours' : 'Échec'}
              </StatusPill>
            </div>
            <p className="mt-0.5 text-sm leading-snug font-semibold">{e.action}</p>
            {e.preuve && <p className="mt-0.5 line-clamp-2 text-ui text-fg-muted">{e.preuve}</p>}
            {resolution && (
              <p className="mt-1 text-ui text-fg">
                <Wrench className="mr-1 inline size-3.5 text-warn-solid" aria-hidden />« {resolution.titre} » :
                correction
                {resolution.corrections.length > 1 ? 's' : ''} {faites}/{resolution.corrections.length},{' '}
                {resolution.verificationResultat ? 'vérification faite' : 'vérification à faire'}
              </p>
            )}
            <p className="mt-1 flex flex-wrap items-center gap-1.5 text-xs text-fg-muted">
              {nom && <Avatar nom={nom} taille="xs" />}
              {nom}
              {e.finReelle && <> · en échec depuis {heure(Date.parse(e.finReelle))}</>}
            </p>
            {bloque.length > 0 && (
              <p className="mt-1 text-xs font-semibold text-danger-fg">
                <Ban className="mr-1 inline size-3.5" aria-hidden />
                Bloque {bloque.length} étape{bloque.length > 1 ? 's' : ''} : {bloque.map((x) => x.code).join(', ')}
              </p>
            )}
          </div>
        );
      })}
      {depassements.map((e) => {
        const nom = nomParticipant(mep, e.executantId);
        return (
          <div
            key={e.id}
            className="rounded-lg border-l-4 border-l-warn-solid bg-surface px-3 py-2 shadow-sm ring-1 ring-line"
          >
            <div className="flex items-center justify-between gap-2">
              <span className="font-mono text-xs text-fg-subtle">{e.code}</span>
              <StatusPill taille="sm" ton="warn" icone={Clock}>
                Dépasse sa durée
              </StatusPill>
            </div>
            <p className="mt-0.5 text-sm leading-snug font-semibold">{e.action}</p>
            <p className="mt-1 flex items-center gap-1.5 text-xs text-fg-muted tnum">
              {nom && <Avatar nom={nom} taille="xs" />}
              {nom} · {Math.round(ecoulee(e, maintenant))} min pour {e.dureeEstimeeMinutes} prévues
            </p>
          </div>
        );
      })}
      {envois.map((x) => (
        <EchecEnvoiCarte
          key={x.appelId}
          mepId={mep.id}
          echec={x}
          peutRenvoyer={connexions.donnees?.peutEnvoyer ?? false}
          onRenvoye={connexions.recharger}
        />
      ))}
    </section>
  );
}

/**
 * Envoi à une API externe définitivement en échec : le pilote ne doit pas croire l'outil prévenu. « Renvoyer » (pilote,
 * suppléants, administrateurs) repart avec le même message ; la carte disparaît au premier envoi réussi ou à la clôture.
 */
function EchecEnvoiCarte({
  mepId,
  echec: x,
  peutRenvoyer,
  onRenvoye,
}: {
  mepId: string;
  echec: EchecEnvoi;
  peutRenvoyer: boolean;
  onRenvoye: () => void;
}) {
  const toast = useToast();
  const resultat = `${x.code ?? 'pas de réponse'}${x.tentatives > 1 ? ` après ${x.tentatives} tentatives` : ''}`;
  return (
    <div className="rounded-lg border-l-4 border-l-danger-solid bg-surface px-3 py-2 shadow-sm ring-1 ring-line">
      <div className="flex items-center justify-between gap-2">
        <span className="font-mono text-xs text-fg-subtle">ENVOI</span>
        <StatusPill taille="sm" ton={x.renvoiEnCours ? 'wait' : 'danger'} icone={x.renvoiEnCours ? Clock : Webhook}>
          {x.renvoiEnCours ? 'Renvoi en cours' : 'Échec de l’envoi'}
        </StatusPill>
      </div>
      <p className="mt-0.5 text-sm leading-snug font-semibold">{x.connexion}</p>
      <p className="mt-0.5 text-ui text-fg-muted">
        {x.libelleEvenement} : {resultat}
      </p>
      {x.erreur && <p className="mt-0.5 line-clamp-2 text-ui text-fg-muted">{x.erreur}</p>}
      <p className="mt-1 text-xs text-fg-subtle tnum">depuis {heure(Date.parse(x.quand))}</p>
      <div className="mt-1.5 flex flex-wrap gap-1.5">
        {peutRenvoyer && (
          <Button
            taille="sm"
            disabled={x.renvoiEnCours}
            icone={<Send className="size-3.5" aria-hidden />}
            action={async () => {
              try {
                await api.renvoyerAppelMep(mepId, x.appelId);
                toast.succes(`Message « ${x.connexion} » renvoyé : le résultat s’inscrira au journal.`);
                onRenvoye();
              } catch (e) {
                toast.erreur(e instanceof Error ? e.message : 'Renvoi impossible.');
              }
            }}
          >
            Renvoyer
          </Button>
        )}
        <Link
          to="?onglet=journal"
          className="inline-flex h-8 items-center px-2 text-xs font-medium text-accent-text hover:underline"
        >
          Voir le journal
        </Link>
      </div>
    </div>
  );
}

/**
 * En ce moment : les personnes qui ont des étapes dans le plan suivi (ce qu'elles exécutent, sinon leur prochaine étape)
 * et les autres présentes sur la MEP, avec l'endroit où chacune se trouve (présence localisée).
 */
function EnCeMoment({ mep, etapes, maintenant }: { mep: Mep; etapes: Etape[]; maintenant: number }) {
  const presents = useAutresPresents();
  const bloquees = causesBlocage(etapes);
  const concernes = mep.participants.filter(
    (p) =>
      p.id === mep.piloteId ||
      etapes.some((e) => e.executantId === p.id || e.responsableId === p.id) ||
      presents.some((x) => x.utilisateurId === p.utilisateurId),
  );
  const visiteurs = presents.filter((x) => !mep.participants.some((p) => p.utilisateurId === x.utilisateurId));
  return (
    <section aria-label="En ce moment" className="grid gap-2">
      <TitrePanneau>En ce moment</TitrePanneau>
      <ul className="grid gap-1.5">
        {concernes.map((p) => {
          const enCours = etapes.filter((e) => e.executantId === p.id && e.etat === 'EnCours');
          const resolutions = mep.resolutions.filter((r) => r.statut === 'EnCours' && r.demarreePar === p.nom);
          const prochaine = etapes.find(
            (e) => e.etat === 'AFaire' && (e.responsableId ?? e.executantId) === p.id && !bloquees.has(e.id),
          );
          const actif = enCours.length > 0 || resolutions.length > 0;
          const presence = presents.find((x) => x.utilisateurId === p.utilisateurId);
          const vous = p.id === mep.droits.participantId;
          const enLigne = vous || !!presence;
          const lieu = presence ? ouSeTrouve(presence, mep) : null;
          return (
            <li key={p.id} className="flex items-start gap-2.5 rounded-lg px-1 py-1">
              <span className="relative">
                <Avatar nom={p.nom} taille="sm" />
                <span
                  className={cn(
                    'absolute -right-0.5 -bottom-0.5 size-2.5 rounded-full ring-2 ring-surface',
                    enLigne ? 'bg-ok-solid' : 'bg-wait-solid/50',
                  )}
                  title={enLigne ? 'Sur la MEP' : 'Absent de la MEP'}
                />
              </span>
              <div className="min-w-0 flex-1 text-ui leading-snug">
                <p>
                  <span className="font-semibold">{p.nom}</span>
                  {vous && <span className="text-fg-subtle"> (vous)</span>}{' '}
                  <span className="text-fg-subtle">· {p.id === mep.piloteId ? 'Pilote' : p.role}</span>
                  {lieu && <span className="text-fg-subtle"> · {lieu}</span>}
                </p>
                {enCours.map((e) => (
                  <p key={e.id} className="text-run-fg">
                    <Play className="mr-1 inline size-3" aria-hidden />
                    <span className="font-mono text-xs">{e.code}</span> {e.action}
                    <span className="text-fg-subtle tnum"> · {Math.round(ecoulee(e, maintenant))} min</span>
                  </p>
                ))}
                {resolutions.map((r) => (
                  <p key={r.id} className="text-warn-fg">
                    <Wrench className="mr-1 inline size-3" aria-hidden />
                    Résolution sur {mep.etapes.find((e) => e.id === r.etapeId)?.code}
                  </p>
                ))}
                {!actif && (
                  <p className="text-fg-subtle">
                    {prochaine ? (
                      <>
                        Prochaine : <span className="font-mono text-xs">{prochaine.code}</span> {prochaine.action}
                      </>
                    ) : (
                      'Rien à exécuter pour l’instant'
                    )}
                  </p>
                )}
              </div>
            </li>
          );
        })}
        {visiteurs.map((x) => {
          const lieu = ouSeTrouve(x, mep);
          return (
            <li key={x.utilisateurId} className="flex items-center gap-2.5 rounded-lg px-1 py-1 text-ui">
              <span className="relative">
                <Avatar nom={x.nom} taille="sm" />
                <span className="absolute -right-0.5 -bottom-0.5 size-2.5 rounded-full bg-ok-solid ring-2 ring-surface" />
              </span>
              <p className="min-w-0">
                <span className="font-semibold">{x.nom}</span>{' '}
                <span className="text-fg-subtle">· consulte la MEP{lieu && ` · ${lieu}`}</span>
              </p>
            </li>
          );
        })}
      </ul>
    </section>
  );
}

function iconeEvenement(type: string): { Icone: LucideIcon; ton: Ton } {
  if (type === 'etape.echec' || type === 'resolution.echouee') return { Icone: XCircle, ton: 'danger' };
  if (type.startsWith('resolution.')) return { Icone: Wrench, ton: 'warn' };
  if (type === 'etape.reussie' || type === 'mep.cloturee') return { Icone: CheckCircle2, ton: 'ok' };
  if (type === 'repli.declenche') return { Icone: Siren, ton: 'rollback' };
  if (type === 'etape.prise-en-main') return { Icone: HandMetal, ton: 'warn' };
  if (type === 'etape.non-retour-autorise') return { Icone: Flag, ton: 'rollback' };
  return { Icone: Play, ton: 'run' };
}

/** Suite des événements marquants (journal filtré), les plus récents en haut. */
function Evenements({ lu }: { lu: DonneePartagee<Evenement[]> }) {
  const journal = lu.donnees;
  const marquants = (journal ?? []).filter(estMarquant).slice(0, 8);
  return (
    <section aria-label="Événements marquants" className="grid gap-2">
      <div className="flex items-center justify-between">
        <TitrePanneau>Événements marquants</TitrePanneau>
        <Link to="?onglet=journal" className="text-xs font-medium text-accent-text hover:underline">
          Tout le journal
        </Link>
      </div>
      {journal && marquants.length === 0 && <p className="text-ui text-fg-subtle">Rien de marquant pour l’instant.</p>}
      {!journal && lu.erreur != null && (
        <ErreurChargement
          variante="ligne"
          erreur={lu.erreur}
          titre="Derniers événements non chargés"
          onReessayer={lu.recharger}
          enCours={lu.enCours}
        />
      )}
      {!journal && lu.erreur == null && <SqueletteEvenements />}
      <ol className="relative grid gap-2 border-l border-line pl-4">
        {marquants.map((ev) => {
          const { Icone, ton } = iconeEvenement(ev.type);
          return (
            <li key={ev.id} className="relative text-ui leading-snug">
              <span
                className={cn(
                  'absolute top-0 -left-[25px] grid size-[18px] place-content-center rounded-full ring-2 ring-surface',
                  TON[ton].bgSolide,
                  TON[ton].texteSolide,
                )}
              >
                <Icone className="size-3" aria-hidden />
              </span>
              <p className="text-fg-subtle tnum">
                {heure(Date.parse(ev.quand))} · {ev.auteur}
              </p>
              <p className="line-clamp-2 text-fg">{ev.detail}</p>
            </li>
          );
        })}
      </ol>
    </section>
  );
}

/**
 * Vue d'ensemble d'une MEP (#61) : chiffres clés, plan coloré (graphe du plan suivi en lecture seule, sections repliées
 * qu'un clic déplie), blocages et échecs, qui fait quoi en ce moment, événements marquants. Tout suit la MEP en temps réel
 * (flux de la MEP) ; le plein écran du graphe emporte toute la vue.
 */
export default function VueEnsembleTab() {
  const { mep } = useMep();
  const [params] = useSearchParams();
  const maintenant = useNow(30_000);
  const plan = planSuivi(mep);
  const etapes = useMemo(() => etapesDuPlan(mep, plan), [mep, plan]);
  const sections = useMemo(() => sectionsDuPlan(mep, plan).filter((s) => s.placee), [mep, plan]);
  const [deplies, setDeplies] = useState<ReadonlySet<string>>(() => new Set());
  const basculer = useCallback(
    (id: string) =>
      setDeplies((d) => {
        const n = new Set(d);
        if (n.has(id)) n.delete(id);
        else n.add(id);
        return n;
      }),
    [],
  );
  const [plein, setPlein] = useState(params.get('plein') === '1');
  const [selection, setSelection] = useState<string | null>(null);
  const { catalogue, pret } = useReferentiels();
  const descriptions = useMemo(() => descriptionsSystemes(mep, catalogue), [mep, catalogue]);
  // Derniers événements, puis seulement les nouveaux à chaque version (#96) ; une erreur se montre avec « Réessayer ».
  const journal = useJournalMep(mep);
  const bloquees = useMemo(() => causesBlocage(etapes), [etapes]);
  const ensemble = useMemo(() => ({ bloquees }), [bloquees]);
  const c = chiffres(mep, etapes, maintenant);
  const phase = metaPhase(mep.phase, mep.etat, mep.report?.dateVisee);
  const enDirect = estLancee(mep);

  const lateral = (
    <aside
      aria-label="Suivi de la MEP"
      className={cn(
        'grid w-[340px] shrink-0 content-start gap-5 overflow-y-auto border-l border-line bg-surface p-4',
        plein && 'w-[380px]',
      )}
    >
      <Blocages mep={mep} etapes={etapes} maintenant={maintenant} />
      <EnCeMoment mep={mep} etapes={etapes} maintenant={maintenant} />
      <Evenements lu={journal} />
    </aside>
  );

  return (
    <Apparition pret={pret} libelle="Chargement de la vue d’ensemble…" squelette={<SqueletteEnsemble />}>
      <div className="mx-auto grid w-full max-w-[1700px] gap-4 px-4 py-5 sm:px-6">
        {mep.phase === 'Preparation' && (
          <Alerte ton="run" icone={Info}>
            MEP en préparation : la vue d’ensemble s’anime au Go (avancement, fin estimée, blocages).
          </Alerte>
        )}
        {mep.phase === 'Annulee' && (
          <Alerte ton="later" icone={Ban}>
            MEP annulée{mep.annulation ? ` : ${sansPoint(mep.annulation.motif)}` : ''}. La vue montre où en était
            l’exécution.
          </Alerte>
        )}
        {mep.phase === 'Cloturee' && (
          <Alerte ton="ok" icone={FileText}>
            MEP clôturée : la vue telle qu’à la clôture est dans le{' '}
            <Link to={`/meps/${mep.id}/rapport`} className="font-semibold underline">
              rapport de fin
            </Link>
            .
          </Alerte>
        )}
        {plan === 'RetourArriere' && (
          <Alerte ton="rollback" icone={Siren}>
            Retour arrière{mep.retourArriere?.motif ? ` : ${sansPoint(mep.retourArriere.motif)}` : ''}. La vue suit le
            plan de retour arrière.
          </Alerte>
        )}
        {!plein && <ChiffresCles c={c} etapes={etapes} />}
        <div>
          <GrapheLibre
            mep={mep}
            plan={plan}
            etapes={etapes}
            sections={sections}
            mode="execution"
            descriptions={descriptions}
            selection={selection}
            onSelection={setSelection}
            deplies={deplies}
            onBasculer={basculer}
            plein={plein}
            onPlein={setPlein}
            ensemble={ensemble}
            enTete={
              <div className="flex flex-wrap items-center gap-3">
                {plein && (
                  <span className="flex items-center gap-2 pl-1 text-lg font-semibold tracking-tight">
                    {mep.outil}
                    {mep.versionCible && <span className="font-mono text-base">→ {mep.versionCible}</span>}
                    <StatusPill ton={phase.ton} icone={phase.Icone} anime={phase.anime}>
                      {phase.libelle}
                    </StatusPill>
                  </span>
                )}
                {enDirect && <EnDirect maintenant={maintenant} />}
                <Legende />
              </div>
            }
            bandeau={plein ? <ChiffresCles c={c} etapes={etapes} compact /> : undefined}
            lateral={lateral}
          />
          <p className="mt-2 flex items-center gap-1.5 text-xs text-fg-subtle">
            <Radio className="size-3.5" aria-hidden />
            Lecture seule : cliquer une section pour la déplier ; l’exécution se fait dans l’onglet Exécution.
          </p>
        </div>
      </div>
    </Apparition>
  );
}
