import {
  ArrowRight,
  Ban,
  CalendarClock,
  CheckCircle2,
  FileText,
  Maximize2,
  Minimize2,
  Play,
  TimerReset,
  TriangleAlert,
  Wrench,
  XCircle,
  type LucideIcon,
} from 'lucide-react';
import { useEffect, useRef, useState, type ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api';
import { PuceSystemeMep } from '../mep/SystemeCliquable';
import { etapesDuPlan, nomParticipant, segmentsProgression } from '../domaine';
import { ouvrirFlux } from '../flux';
import { calculerCompteARebours, formatDate, type Ton } from '../format';
import { useAsync, useNow } from '../hooks';
import {
  causesBlocage,
  chiffres,
  duree,
  etapesParSection,
  etatSection,
  etatVue,
  heure,
  jourRelatif,
  LIBELLE_ETAT_VUE,
  planSuivi,
  TON_ETAT_VUE,
  tonRetard,
  trierSuivi,
} from '../mep/ensemble/calculs';
import { EnDirect } from '../mep/ensemble/VueEnsembleTab';
import { sectionsDuPlan } from '../mep/graphe/sections';
import { BadgeSimplifiee, estSimplifiee } from '../mep/typeMep';
import { metaPhase } from '../statuts';
import type { Mep, MepCloturee, MepPrevue, Suivi } from '../types';
import { ErreurChargement } from '../ui/ErreurChargement';
import { Apparition } from '../ui/Apparition';
import { Avatar } from '../ui/Avatar';
import { Button } from '../ui/Button';
import { cn } from '../ui/cn';
import { EmptyState } from '../ui/EmptyState';
import { SqueletteSuivi } from '../mep/ensemble/Squelettes';
import { ProgressBar } from '../ui/ProgressBar';
import { StatusPill } from '../ui/StatusPill';
import { TON } from '../ui/tons';

/** Pastille d'une section (ou d'une étape hors section) dans la carte d'une MEP, colorée par état. */
const PUCE_ETAT: Partial<Record<Ton, string>> = {
  ok: 'border-ok-bd bg-ok-bg text-ok-fg',
  run: 'border-run-bd bg-run-bg text-run-fg',
  danger: 'border-danger-bd bg-danger-bg text-danger-fg',
  warn: 'border-warn-bd bg-warn-bg text-warn-fg',
  wait: 'border-line bg-surface text-fg-muted',
};

/** Délai de regroupement des relectures quand le flux signale plusieurs modifications d'affilée. */
const REGROUPEMENT_MS = 1500;

const titreMep = (m: { outil: string; versionCible: string; type: Mep['type'] }) =>
  m.type === 'Simplifiee' || !m.versionCible.trim() ? m.outil : `${m.outil} → ${m.versionCible}`;

/** Résumé d'une MEP lancée : avancement, sections colorées, fin estimée et retard, échecs, qui fait quoi. */
function CarteSuivi({ mep, maintenant }: { mep: Mep; maintenant: number }) {
  const plan = planSuivi(mep);
  const repli = plan === 'RetourArriere';
  const etapes = etapesDuPlan(mep, plan);
  const sections = sectionsDuPlan(mep, plan).filter((s) => s.placee);
  const parSection = etapesParSection(etapes, sections);
  const bloquees = causesBlocage(etapes);
  const c = chiffres(mep, etapes, maintenant);
  const phase = metaPhase(mep.phase, mep.etat);
  const pilote = nomParticipant(mep, mep.piloteId);
  const echecs = etapes.filter((e) => e.etat === 'Echec');
  const actifs = etapes.filter((e) => e.etat === 'EnCours');
  const rebours =
    mep.retourArriereActive && mep.echeanceRepli && mep.phase === 'Execution'
      ? calculerCompteARebours(mep.echeanceRepli, maintenant)
      : null;
  const horsSection = etapes.filter((e) => !e.sectionId || !parSection.has(e.sectionId));

  return (
    <li
      aria-label={titreMep(mep)}
      className={cn(
        'rounded-xl border bg-surface p-5 shadow-sm',
        repli ? 'border-2 border-rollback-solid' : echecs.length > 0 ? 'border-danger-bd' : 'border-line',
      )}
    >
      <div className="flex flex-wrap items-start justify-between gap-x-6 gap-y-2">
        <div className="flex min-w-0 flex-wrap items-center gap-x-3 gap-y-1.5">
          <h2 className="flex min-w-0 items-center gap-2 text-base font-semibold tracking-tight">
            <span className="truncate">{mep.outil}</span>
            {!estSimplifiee(mep) && mep.versionCible.trim() && (
              <>
                <ArrowRight className="size-4 shrink-0 text-fg-subtle" aria-hidden />
                <span className="font-mono text-[15px] font-semibold">{mep.versionCible}</span>
              </>
            )}
          </h2>
          <StatusPill ton={phase.ton} icone={phase.Icone} anime={phase.anime}>
            {phase.libelle}
          </StatusPill>
          {estSimplifiee(mep) && <BadgeSimplifiee className="text-[11px]" />}
        </div>
        <div className="flex flex-wrap items-center gap-x-5 gap-y-1 text-sm text-fg-muted">
          <span className="inline-flex items-center gap-1.5 tnum">
            <Play className="size-4 text-fg-subtle" aria-hidden />
            {repli ? 'Repli déclenché à' : 'Go à'} {heure(c.depart)}
          </span>
          {rebours && rebours.niveau !== 'aucun' && (
            <span
              className="inline-flex items-center gap-1.5 font-mono text-xs text-fg-muted tnum"
              title="Temps restant avant l’échéance de retour arrière"
            >
              <TimerReset className="size-3.5" aria-hidden />
              {rebours.texte}
            </span>
          )}
          {pilote && (
            <span className="inline-flex items-center gap-2">
              <Avatar nom={pilote} taille="sm" />
              {pilote}
            </span>
          )}
        </div>
      </div>

      <div className="mt-4 grid gap-6 md:grid-cols-[minmax(0,1fr)_190px] xl:grid-cols-[minmax(0,1fr)_190px_300px]">
        <div className="min-w-0">
          <p className="text-[13px] font-semibold tnum">
            {repli ? 'Retour arrière : ' : ''}
            {c.faites}/{c.total} étapes faites · {c.pourcentage} %
          </p>
          <ProgressBar className="mt-1.5" hauteur="h-2.5" legende segments={segmentsProgression(etapes)} />
          <ul className="mt-3 flex flex-wrap gap-1.5" aria-label="Sections">
            {sections.map((s) => {
              const es = parSection.get(s.id) ?? [];
              const etat = etatSection(es, bloquees);
              return (
                <li
                  key={s.id}
                  title={`${s.titre} : ${LIBELLE_ETAT_VUE[etat]}`}
                  className={cn(
                    'inline-flex items-center gap-1.5 rounded-md border px-2 py-0.5 text-[12px] font-medium',
                    PUCE_ETAT[TON_ETAT_VUE[etat]],
                    etat === 'bloquee' && 'border-dashed',
                  )}
                >
                  {s.titre}
                  <span className="font-semibold tnum">
                    {es.filter((e) => e.etat === 'Reussie').length}/{es.length}
                  </span>
                </li>
              );
            })}
            {horsSection.map((e) => {
              const etat = etatVue(e, bloquees);
              return (
                <li
                  key={e.id}
                  title={`${e.code} ${e.action} : ${LIBELLE_ETAT_VUE[etat]}`}
                  className={cn(
                    'inline-flex items-center rounded-md border px-1.5 py-0.5 font-mono text-[11.5px]',
                    PUCE_ETAT[TON_ETAT_VUE[etat]],
                    etat === 'bloquee' && 'border-dashed',
                  )}
                >
                  {e.code}
                </li>
              );
            })}
          </ul>
        </div>

        <dl className="grid content-start gap-2 text-[13px]">
          {c.sansDuree ? (
            <div>
              <dt className="text-[11px] font-semibold tracking-[0.06em] text-fg-subtle uppercase">Fin estimée</dt>
              <dd className="text-fg-muted">Durées non estimées</dd>
            </div>
          ) : (
            <>
              <div>
                <dt className="text-[11px] font-semibold tracking-[0.06em] text-fg-subtle uppercase">
                  {c.terminee ? (repli ? 'Retour arrière fini' : 'Déploiement fini') : 'Fin estimée'}
                </dt>
                <dd className="text-lg leading-tight font-bold tnum">
                  {heure(c.finEstimee)}{' '}
                  <span className="text-[13px] font-normal text-fg-muted">prévue {heure(c.finPrevue)}</span>
                </dd>
              </div>
              <div>
                <dt className="text-[11px] font-semibold tracking-[0.06em] text-fg-subtle uppercase">Retard</dt>
                <dd className={cn('text-lg leading-tight font-bold tnum', TON[tonRetard(c.retardMin)].texte)}>
                  {c.retardMin > 0 ? `+${duree(c.retardMin)}` : 'À l’heure'}
                </dd>
              </div>
            </>
          )}
        </dl>

        <div className="grid content-start gap-1.5 text-[13px] md:col-span-2 xl:col-span-1">
          {echecs.map((e) => {
            const resolution = e.disponibilite.statut === 'ResolutionEnCours';
            const bloque = [...bloquees.values()].filter((cs) => cs.some((x) => x.id === e.id)).length;
            return (
              <p key={e.id} className={cn('font-medium', resolution ? 'text-warn-fg' : 'text-danger-fg')}>
                {resolution ? (
                  <Wrench className="mr-1 inline size-3.5" aria-hidden />
                ) : (
                  <XCircle className="mr-1 inline size-3.5" aria-hidden />
                )}
                <span className="font-mono text-[12px]">{e.code}</span>{' '}
                {resolution ? 'résolution en cours' : 'en échec'}
                {bloque > 0 && (
                  <span>
                    {' '}
                    · <Ban className="inline size-3" aria-hidden /> bloque {bloque}
                  </span>
                )}
              </p>
            );
          })}
          {repli && mep.retourArriere?.motif && (
            <p className="font-semibold text-rollback-solid">
              <TriangleAlert className="mr-1 inline size-3.5" aria-hidden />
              {mep.retourArriere.motif}
            </p>
          )}
          {actifs.length > 0 && (
            <ul className="grid gap-1">
              {actifs.map((e) => {
                const nom = nomParticipant(mep, e.executantId);
                return (
                  <li key={e.id} className="flex min-w-0 items-center gap-1.5 text-fg-muted">
                    {nom && <Avatar nom={nom} taille="xs" />}
                    <span className="shrink-0 text-fg">{nom}</span>
                    <span className="truncate">
                      · <span className="font-mono text-[12px]">{e.code}</span> {e.action}
                    </span>
                  </li>
                );
              })}
            </ul>
          )}
          {mep.phase === 'Recette' && (
            <p className="text-fg-muted">
              Recette : {mep.fichesRecette.length} fiche{mep.fichesRecette.length > 1 ? 's' : ''} à dérouler
            </p>
          )}
        </div>
      </div>

      <div className="mt-3 flex flex-wrap items-center justify-between gap-2">
        {/* Systèmes touchés (#59) : chaque puce ouvre l'historique du système ; alerte s'il a une étape en échec. */}
        <ul className="flex flex-wrap gap-1" aria-label="Systèmes">
          {mep.systemes.map((s) => (
            <li key={s.id}>
              <PuceSystemeMep s={{ ...s, echecs: echecs.filter((e) => e.systemeIds.includes(s.id)).length }} />
            </li>
          ))}
        </ul>
        <Link
          to={`/meps/${mep.id}?onglet=ensemble`}
          className="inline-flex h-8 items-center gap-1.5 rounded-lg border border-line-strong bg-surface px-3 text-[13px] font-medium shadow-sm hover:bg-surface-2"
        >
          Vue d’ensemble <ArrowRight className="size-3.5" aria-hidden />
        </Link>
      </div>
    </li>
  );
}

function Tuile({
  titre,
  Icone,
  valeur,
  detail,
  ton,
}: {
  titre: string;
  Icone: LucideIcon;
  valeur: ReactNode;
  detail?: ReactNode;
  ton?: Ton;
}) {
  return (
    <div className="min-w-0 rounded-xl border border-line bg-surface px-4 py-3 shadow-sm">
      <p className="flex items-center gap-1.5 text-[11px] font-semibold tracking-[0.06em] text-fg-subtle uppercase">
        <Icone className="size-3.5" aria-hidden />
        {titre}
      </p>
      <p className={cn('mt-1 truncate text-[24px] leading-tight font-bold tnum', ton ? TON[ton].texte : 'text-fg')}>
        {valeur}
      </p>
      {detail && <p className="truncate text-[13px] text-fg-muted">{detail}</p>}
    </div>
  );
}

const quandPrevue = (m: MepPrevue, maintenant: number) =>
  `${jourRelatif(m.date, maintenant)}${m.heure ? ` ${m.heure.slice(0, 5)}` : ''}`;

function ListePrevues({ prevues, maintenant }: { prevues: MepPrevue[]; maintenant: number }) {
  return (
    <section aria-labelledby="titre-prevues" className="rounded-xl border border-line bg-surface p-4 shadow-sm">
      <h2 id="titre-prevues" className="flex items-center gap-1.5 text-[13px] font-semibold">
        <CalendarClock className="size-4 text-fg-subtle" aria-hidden />
        MEP prévues aujourd’hui et demain
      </h2>
      {prevues.length === 0 ? (
        <p className="mt-2 text-[13px] text-fg-subtle">Aucune MEP annoncée.</p>
      ) : (
        <ul className="mt-2 divide-y divide-line">
          {prevues.map((m) => (
            <li key={m.id} className="flex items-center justify-between gap-3 py-2 text-[13px]">
              <Link to={`/meps/${m.id}`} className="min-w-0 truncate font-medium hover:underline">
                {titreMep(m)}
              </Link>
              <span className="flex shrink-0 items-center gap-3 text-fg-muted tnum">
                {m.pilote && <span>{m.pilote}</span>}
                <span className="font-medium text-fg">{quandPrevue(m, maintenant)}</span>
              </span>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

function ListeCloturees({ cloturees }: { cloturees: MepCloturee[] }) {
  return (
    <section aria-labelledby="titre-cloturees" className="rounded-xl border border-line bg-surface p-4 shadow-sm">
      <h2 id="titre-cloturees" className="flex items-center gap-1.5 text-[13px] font-semibold">
        <CheckCircle2 className="size-4 text-fg-subtle" aria-hidden />
        MEP clôturées dans les dernières 24 heures
      </h2>
      {cloturees.length === 0 ? (
        <p className="mt-2 text-[13px] text-fg-subtle">Aucune MEP clôturée.</p>
      ) : (
        <ul className="mt-2 divide-y divide-line">
          {cloturees.map((m) => {
            const phase = metaPhase('Cloturee', m.etat);
            return (
              <li key={m.id} className="flex items-center justify-between gap-3 py-2 text-[13px]">
                <span className="flex min-w-0 items-center gap-2">
                  <Link to={`/meps/${m.id}`} className="truncate font-medium hover:underline">
                    {titreMep(m)}
                  </Link>
                  <StatusPill taille="sm" ton={phase.ton} icone={phase.Icone}>
                    {phase.libelle}
                  </StatusPill>
                </span>
                <span className="flex shrink-0 items-center gap-3 text-fg-muted tnum">
                  {heure(Date.parse(m.clotureeLe))}
                  <Link
                    to={`/meps/${m.id}/rapport`}
                    className="inline-flex items-center gap-1 font-medium text-accent-text hover:underline"
                  >
                    <FileText className="size-3.5" aria-hidden />
                    Rapport
                  </Link>
                </span>
              </li>
            );
          })}
        </ul>
      )}
    </section>
  );
}

/**
 * Suivi en direct (#61) : tableau de bord des MEP lancées, ouvert à tous. Chiffres du jour, une carte par MEP (retours
 * arrière puis échecs d'abord), MEP prévues aujourd'hui et demain, clôturées depuis 24 heures. Le flux `/api/suivi/flux`
 * dit ce qui a changé (#96) : la page ne relit que les cartes des MEP lancées concernées (`/api/meps/{id}`), ou tout
 * `/api/suivi` quand les listes changent (MEP lancée, clôturée, annoncée…) ; les écritures arrivées ensemble sont regroupées.
 */
export default function SuiviPage() {
  const maintenant = useNow(30_000);
  const { donnees: suivi, erreur, echec, chargement, recharger } = useAsync(() => api.suivi(), []);
  // Cartes relues une à une depuis le dernier chargement complet (`base`) : elles remplacent celles de `suivi`.
  const [cartes, setCartes] = useState<{ base: Suivi | null; meps: Map<string, Mep> }>({ base: null, meps: new Map() });
  const rechargerRef = useRef(recharger);
  const suiviRef = useRef(suivi);
  useEffect(() => {
    rechargerRef.current = recharger;
    suiviRef.current = suivi;
  });
  const zone = useRef<HTMLElement>(null);
  const [plein, setPlein] = useState(false);

  useEffect(() => {
    let attente: number | undefined;
    let arrete = false;
    let tout = false;
    const ids = new Set<string>();
    const relire = async () => {
      attente = undefined;
      const base = suiviRef.current;
      const aRelire = [...ids];
      const listes = tout;
      ids.clear();
      tout = false;
      // Une MEP sans carte (pas encore chargée, ou tout juste lancée) : les listes ont changé.
      if (listes || base === null || aRelire.some((id) => !base.enCours.some((m) => m.id === id))) {
        rechargerRef.current();
        return;
      }
      try {
        const lues = await Promise.all(aRelire.map((id) => api.mep(id, true)));
        if (arrete) return;
        setCartes((c) => {
          const meps = new Map(c.base === base ? c.meps : []);
          for (const m of lues) {
            const avant = meps.get(m.id) ?? base.enCours.find((x) => x.id === m.id);
            if (!avant || m.version >= avant.version) meps.set(m.id, m);
          }
          return { base, meps };
        });
      } catch {
        if (!arrete) rechargerRef.current();
      }
    };
    const planifier = () => {
      if (attente === undefined) attente = window.setTimeout(() => void relire(), REGROUPEMENT_MS);
    };
    const fermer = ouvrirFlux(
      '/api/suivi/flux',
      (_version, maj) => {
        if (maj.liste || !maj.meps) tout = true;
        else for (const id of maj.meps) ids.add(id);
        if (tout || ids.size > 0) planifier();
      },
      () => {
        // Reconnexion : des modifications ont pu passer pendant la coupure.
        tout = true;
        planifier();
      },
    );
    return () => {
      arrete = true;
      fermer();
      window.clearTimeout(attente);
    };
  }, []);

  useEffect(() => {
    const suivre = () => setPlein(zone.current !== null && document.fullscreenElement === zone.current);
    document.addEventListener('fullscreenchange', suivre);
    return () => document.removeEventListener('fullscreenchange', suivre);
  }, []);
  const basculerPlein = () => {
    if (document.fullscreenElement) void document.exitFullscreen();
    else void zone.current?.requestFullscreen();
  };

  const meps = trierSuivi(
    (suivi?.enCours ?? []).map((m) => (cartes.base === suivi ? cartes.meps.get(m.id) : undefined) ?? m),
  );
  const nbEchecs = meps.reduce(
    (n, m) => n + m.etapes.filter((e) => e.plan === planSuivi(m) && e.etat === 'Echec').length,
    0,
  );
  const nbRepli = meps.filter((m) => m.phase === 'RetourArriere').length;
  const prochaine = suivi?.prochaine ?? null;

  return (
    <main
      ref={zone}
      className={cn('mx-auto w-full max-w-6xl px-4 py-8 sm:px-6', plein && 'max-w-none overflow-y-auto bg-bg')}
    >
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Suivi en direct</h1>
          <p className="mt-1 text-sm text-fg-subtle">
            L’avancement de chaque MEP lancée, en temps réel. Ouvrez une MEP pour voir sa vue d’ensemble.
          </p>
        </div>
        <div className="flex items-center gap-2">
          <EnDirect maintenant={maintenant} />
          <Button
            taille="sm"
            icone={plein ? <Minimize2 className="size-4" aria-hidden /> : <Maximize2 className="size-4" aria-hidden />}
            onClick={basculerPlein}
          >
            {plein ? 'Quitter le plein écran' : 'Plein écran'}
          </Button>
        </div>
      </div>

      {echec !== null && (
        <ErreurChargement
          className="mt-6"
          erreur={echec}
          titre="Impossible de charger le suivi en direct"
          onReessayer={recharger}
          enCours={chargement}
        />
      )}

      <Apparition pret={suivi !== null || erreur !== ''} libelle="Chargement du suivi…" squelette={<SqueletteSuivi />}>
        <div className="mt-6 grid grid-cols-2 gap-3 lg:grid-cols-4">
          <Tuile titre="MEP en cours" Icone={Play} valeur={meps.length} />
          <Tuile titre="Échecs à traiter" Icone={XCircle} valeur={nbEchecs} ton={nbEchecs > 0 ? 'danger' : undefined} />
          <Tuile
            titre="Retours arrière"
            Icone={TriangleAlert}
            valeur={nbRepli}
            ton={nbRepli > 0 ? 'rollback' : undefined}
          />
          <Tuile
            titre="Prochaine MEP"
            Icone={CalendarClock}
            valeur={prochaine ? quandPrevue(prochaine, maintenant) : '—'}
            detail={prochaine ? titreMep(prochaine) : 'Aucune MEP annoncée'}
          />
        </div>

        {meps.length === 0 ? (
          <div className="mt-6">
            <EmptyState icone={Play} titre="Aucune MEP en cours" description="Les MEP apparaissent ici dès leur Go." />
          </div>
        ) : (
          <ul className="mt-6 space-y-3" aria-label="MEP en cours">
            {meps.map((m) => (
              <CarteSuivi key={m.id} mep={m} maintenant={maintenant} />
            ))}
          </ul>
        )}

        <div className="mt-6 grid gap-3 lg:grid-cols-2">
          <ListePrevues prevues={suivi?.prevues ?? []} maintenant={maintenant} />
          <ListeCloturees cloturees={suivi?.cloturees ?? []} />
        </div>
        {suivi && (
          <p className="mt-3 text-[12px] text-fg-subtle">
            Mis à jour le {formatDate(suivi.maintenant)} à {heure(Date.parse(suivi.maintenant))}.
          </p>
        )}
      </Apparition>
    </main>
  );
}
