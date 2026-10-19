// Onglet « Historique des MEP » de la fiche d'un système du catalogue (#59) : toutes les MEP qui l'ont touché, avec leur
// date, leurs environnements sur ce système, leur issue et les échecs d'étape survenus sur ce système.
import { ArrowRight, FileText, History, ListFilter, OctagonAlert, TriangleAlert, Wrench } from 'lucide-react';
import { useState, type ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api';
import { couleurSysteme } from '../couleursSysteme';
import { formatDate, formatHeureLocale } from '../format';
import { useAsync } from '../hooks';
import { PuceSystemeMep } from '../mep/SystemeCliquable';
import { BadgeSimplifiee } from '../mep/typeMep';
import { PastilleEtatMep } from '../pages/liste/TableauMeps';
import { lienListeSysteme } from '../rechercheMeps';
import { metaEtatMep } from '../statuts';
import type { EchecSysteme, EtatMep, HistoriqueSysteme as Historique, MepHistorique, SystemeCatalogue } from '../types';
import { Alerte } from '../ui/Alerte';
import { Avatar } from '../ui/Avatar';
import { BoutonExportExcel } from '../ui/BoutonExportExcel';
import { Button } from '../ui/Button';
import { Checkbox } from '../ui/Checkbox';
import { cn } from '../ui/cn';
import { EmptyState } from '../ui/EmptyState';
import { PastilleEnvironnement } from '../ui/PastilleEnvironnement';
import { Segmented } from '../ui/Segmented';
import { EnChargement, Squelette, SqueletteChronologie } from '../ui/Squelette';
import { TON } from '../ui/tons';
import { Tooltip } from '../ui/Tooltip';

const fmtMois = new Intl.DateTimeFormat('fr-FR', { month: 'long', year: 'numeric' });
const fmtMoisCourt = new Intl.DateTimeFormat('fr-FR', { month: 'short' });
const fmtJour = new Intl.DateTimeFormat('fr-FR', { day: 'numeric', month: 'short' });

/** Jour d'une MEP (« 2026-07-14 ») en date locale, sans décalage de fuseau. */
function dateLocale(jour: string): Date {
  const [a, m, j] = jour.split('-').map(Number);
  return new Date(a, m - 1, j);
}

/** MEP pas encore lancée (aucun Go) : rangée « À venir ». */
const aVenir = (m: MepHistorique) => !m.go && m.etat === 'Preparation';

function ilYa(jour: string, maintenant: Date): string {
  const j = Math.round((maintenant.getTime() - dateLocale(jour).getTime()) / 864e5);
  if (j < 0) return j === -1 ? 'demain' : `dans ${-j} jours`;
  if (j === 0) return 'aujourd’hui';
  if (j === 1) return 'hier';
  return j < 60 ? `il y a ${j} jours` : `il y a ${Math.round(j / 30)} mois`;
}

const pastille = (e: EtatMep) => TON[metaEtatMep(e).ton].bgSolide;

function Tuile({
  titre,
  valeur,
  ton,
  children,
}: {
  titre: string;
  valeur: ReactNode;
  ton?: string;
  children?: ReactNode;
}) {
  return (
    <div className="rounded-xl border border-line bg-surface p-4 shadow-sm">
      <p className="text-xs font-semibold tracking-wider text-fg-subtle uppercase">{titre}</p>
      <p className={cn('mt-1 text-2xl font-semibold tracking-tight tnum', ton)}>{valeur}</p>
      {children && <div className="mt-1 text-[13px] text-fg-muted">{children}</div>}
    </div>
  );
}

/** Frise des douze derniers mois : une pastille par MEP, de la couleur de son état ; les MEP proches s'empilent. */
function Frise({ meps, maintenant }: { meps: MepHistorique[]; maintenant: Date }) {
  const fin = new Date(maintenant.getFullYear(), maintenant.getMonth() + 1, 1);
  const debut = new Date(fin.getFullYear(), fin.getMonth() - 12, 1);
  const duree = fin.getTime() - debut.getTime();
  const graduations = Array.from({ length: 12 }, (_, i) => new Date(debut.getFullYear(), debut.getMonth() + i, 1));
  const places: { m: MepHistorique; x: number; rang: number }[] = [];
  meps
    .filter((m) => !aVenir(m))
    .map((m) => ({ m, t: dateLocale(m.jour).getTime() }))
    .filter(({ t }) => t >= debut.getTime() && t < fin.getTime())
    .sort((a, b) => a.t - b.t)
    .forEach(({ m, t }) => {
      const x = ((t - debut.getTime()) / duree) * 100;
      places.push({ m, x, rang: places.filter((p) => Math.abs(p.x - x) < 1.6).length });
    });
  const legende: { etat: EtatMep; libelle: string }[] = [
    { etat: 'Reussie', libelle: 'Réussie' },
    { etat: 'AvecIncidents', libelle: 'Avec incidents' },
    { etat: 'Repliee', libelle: 'Repliée' },
    { etat: 'Execution', libelle: 'En cours' },
  ];
  return (
    <figure className="rounded-xl border border-line bg-surface p-4 shadow-sm">
      <figcaption className="flex flex-wrap items-center justify-between gap-2">
        <span className="text-sm font-semibold">12 derniers mois</span>
        <ul className="flex flex-wrap gap-x-3 gap-y-1 text-xs text-fg-muted">
          {legende.map((l) => (
            <li key={l.etat} className="inline-flex items-center gap-1.5">
              <span className={cn('size-2.5 rounded-full', pastille(l.etat))} aria-hidden />
              {l.libelle}
            </li>
          ))}
        </ul>
      </figcaption>
      {places.length === 0 ? (
        <p className="mt-3 text-sm text-fg-subtle">Aucune MEP lancée sur les douze derniers mois.</p>
      ) : (
        <div
          className="relative mt-4"
          style={{ height: `${44 + Math.max(0, ...places.map((p) => p.rang)) * 13}px` }}
          role="list"
          aria-label="MEP des douze derniers mois"
        >
          <div className="absolute inset-x-0 bottom-5 h-px bg-line-strong" aria-hidden />
          {graduations.map((d, i) => (
            <div
              key={i}
              className={cn(
                'absolute bottom-0 text-[11px] text-fg-subtle',
                i % 3 !== 0 && 'max-sm:[&>span:last-child]:hidden',
              )}
              style={{ left: `${(i / 12) * 100}%` }}
              aria-hidden
            >
              <span className="absolute bottom-5 h-1.5 w-px bg-line-strong" />
              <span className="block pl-1">{fmtMoisCourt.format(d).replace('.', '')}</span>
            </div>
          ))}
          {places.map(({ m, x, rang }) => {
            const texte = `${formatDate(m.jour)} · ${m.outil}${m.versionCible ? ` ${m.versionCible}` : ''} · ${metaEtatMep(m.etat).libelle}`;
            return (
              <Tooltip key={m.id} content={texte}>
                <Link
                  to={`/meps/${m.id}`}
                  role="listitem"
                  className={cn(
                    'absolute size-3 -translate-x-1/2 rounded-full ring-2 ring-surface outline-none focus-visible:ring-accent',
                    pastille(m.etat),
                  )}
                  style={{ left: `${x}%`, bottom: `${26 + rang * 13}px` }}
                  aria-label={texte}
                />
              </Tooltip>
            );
          })}
        </div>
      )}
    </figure>
  );
}

function Echec({ e }: { e: EchecSysteme }) {
  const suite = e.repris
    ? e.resolution
      ? `Repris après la résolution « ${e.resolution} »`
      : 'Repris avec succès'
    : e.resolution
      ? `Résolution « ${e.resolution} » sans succès`
      : 'Non repris';
  return (
    <div
      className={cn(
        'mt-2.5 rounded-lg border px-3 py-2 text-[13px]',
        e.repris ? 'border-warn-bd bg-warn-bg text-warn-fg' : 'border-danger-bd bg-danger-bg text-danger-fg',
      )}
    >
      <p className="flex items-start gap-1.5">
        <TriangleAlert className="mt-0.5 size-3.5 shrink-0" aria-hidden />
        <span>
          <span className="font-mono font-semibold">{e.code}</span> {e.action}
          {e.plan === 'RetourArriere' && <span className="opacity-80"> (retour arrière)</span>}
          {e.motif && <> — {e.motif}</>}
        </span>
      </p>
      <p className="mt-0.5 flex items-center gap-1.5 pl-5 text-xs opacity-90">
        {e.resolution && <Wrench className="size-3" aria-hidden />}
        {suite} · {formatDate(e.echecLe)} {formatHeureLocale(e.echecLe)}
      </p>
    </div>
  );
}

function Element({ m, nomSysteme }: { m: MepHistorique; nomSysteme: string }) {
  const d = dateLocale(m.jour);
  return (
    <li className="grid grid-cols-[3.5rem_1fr] gap-2 pb-5 sm:grid-cols-[5.5rem_1fr] sm:gap-3">
      <div className="pt-0.5 text-right">
        <p className="text-sm font-semibold tnum">{fmtJour.format(d).replace('.', '')}</p>
        <p className="text-xs text-fg-subtle tnum">{m.go ? formatHeureLocale(m.go) : 'pas de Go'}</p>
      </div>
      <div className="relative border-l-2 border-line pl-3 sm:pl-5">
        <span
          className={cn('absolute top-1.5 -left-[7px] size-3 rounded-full ring-4 ring-bg', pastille(m.etat))}
          aria-hidden
        />
        <div className="rounded-xl border border-line bg-surface p-3.5 shadow-sm">
          <div className="flex flex-wrap items-start justify-between gap-2">
            <div className="min-w-0">
              <p className="flex flex-wrap items-center gap-x-2 gap-y-1 font-semibold">
                <Link to={`/meps/${m.id}`} className="hover:text-accent-text hover:underline">
                  {m.outil || 'Sans nom'}
                </Link>
                {m.type === 'Simplifiee' ? (
                  <BadgeSimplifiee className="text-[10px]" />
                ) : (
                  m.versionCible.trim() && (
                    <span className="inline-flex items-center gap-1 font-mono text-[13px]">
                      <ArrowRight className="size-3.5 text-fg-subtle" aria-hidden />
                      {m.versionCible}
                    </span>
                  )
                )}
                <PastilleEtatMep m={m} court />
              </p>
              {m.objet && <p className="mt-0.5 line-clamp-1 text-[13px] text-fg-muted">{m.objet}</p>}
            </div>
            {m.rapport && (
              <Link
                to={`/meps/${m.id}/rapport`}
                className="inline-flex shrink-0 items-center gap-1.5 rounded-lg border border-line px-2.5 py-1 text-[13px] font-medium transition-colors duration-150 hover:border-line-strong hover:bg-surface-2"
              >
                <FileText className="size-3.5" aria-hidden />
                Rapport de fin
              </Link>
            )}
          </div>
          <div className="mt-2.5 flex flex-wrap items-center gap-x-4 gap-y-1.5 text-[13px]">
            <span className="inline-flex flex-wrap items-center gap-1.5">
              <span className="text-fg-subtle">{nomSysteme} :</span>
              {m.environnements.length ? (
                m.environnements.map((e) => <PastilleEnvironnement key={e.nom} nom={e.nom} production={e.production} />)
              ) : (
                <span className="text-fg-subtle">aucun environnement choisi</span>
              )}
            </span>
            {m.autresSystemes.length > 0 && (
              <span className="inline-flex flex-wrap items-center gap-1">
                <span className="text-fg-subtle">avec</span>
                {m.autresSystemes.map((s, i) => (
                  <PuceSystemeMep key={`${s.nom}-${i}`} s={s} compacte />
                ))}
              </span>
            )}
            {m.pilote && (
              <span className="inline-flex items-center gap-1.5 text-fg-muted">
                <Avatar nom={m.pilote} taille="sm" />
                {m.pilote}
              </span>
            )}
          </div>
          {m.echecs.map((e, i) => (
            <Echec key={`${e.code}-${i}`} e={e} />
          ))}
        </div>
      </div>
    </li>
  );
}

export function SqueletteHistorique() {
  return (
    <EnChargement libelle="Chargement de l’historique…">
      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        {[0, 1, 2, 3].map((i) => (
          <div key={i} className="grid gap-2 rounded-xl border border-line bg-surface p-4">
            <Squelette className="h-3 w-24" />
            <Squelette className="h-7 w-16" />
            <Squelette className="h-3.5 w-40" />
          </div>
        ))}
      </div>
      <Squelette className="mt-4 h-28 w-full rounded-xl" />
      <SqueletteChronologie evenements={4} className="mt-6" />
    </EnChargement>
  );
}

/** Contenu de l'onglet, une fois l'historique chargé. */
export function VueHistorique({
  h,
  systeme,
  maintenant,
}: {
  h: Historique;
  systeme: SystemeCatalogue;
  maintenant: Date;
}) {
  const [env, setEnv] = useState('tous');
  const [avecEchec, setAvecEchec] = useState(false);
  const toutes = h.meps;
  if (toutes.length === 0)
    return (
      <div className="rounded-xl border border-dashed border-line-strong bg-surface">
        <EmptyState
          icone={History}
          titre={`Aucune MEP n’a encore touché ${systeme.nom}`}
          description={`Les MEP qui retiennent ${systeme.nom} dans Préparation › Systèmes apparaîtront ici, avec leur date, leurs environnements et leur issue.`}
        />
      </div>
    );

  const r = h.resume;
  const meps = toutes.filter(
    (m) => (env === 'tous' || m.environnements.some((e) => e.nom === env)) && (!avecEchec || m.echecs.length > 0),
  );
  const derniere = toutes.find((m) => m.id === r.derniereMepId) ?? toutes[0];
  const problemes = r.avecIncidents + r.repliees;
  const taux = r.cloturees ? Math.round((problemes / r.cloturees) * 100) : 0;
  const part = (n: number) => `${(100 * n) / Math.max(1, r.cloturees)}%`;
  // Environnements du catalogue, puis ceux saisis dans d'anciennes MEP et retirés depuis.
  const envs = [
    ...systeme.environnements.map((e) => e.nom),
    ...new Set(toutes.flatMap((m) => m.environnements.map((e) => e.nom))),
  ].filter((e, i, t) => t.indexOf(e) === i);
  const groupes = new Map<string, MepHistorique[]>();
  for (const m of meps) {
    const cle = aVenir(m) ? 'avenir' : m.jour.slice(0, 7);
    groupes.set(cle, [...(groupes.get(cle) ?? []), m]);
  }
  const titreGroupe = (cle: string) =>
    cle === 'avenir' ? 'À venir : en préparation' : fmtMois.format(dateLocale(`${cle}-01`));
  const lien = lienListeSysteme(systeme.id, { environnements: env === 'tous' ? [] : [env], echec: avecEchec });

  return (
    <div className="apparition">
      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <Tuile titre={`MEP sur ${systeme.nom}`} valeur={r.total}>
          {r.douzeMois} sur 12 mois{r.enCours > 0 && ` · ${r.enCours} en cours ou à venir`}
        </Tuile>
        <Tuile titre="Dernière MEP" valeur={<span className="text-xl">{ilYa(derniere.jour, maintenant)}</span>}>
          <span className="flex flex-wrap items-center gap-1.5">
            <Link to={`/meps/${derniere.id}`} className="hover:text-accent-text hover:underline">
              {derniere.outil}
              {derniere.versionCible.trim() && ` → ${derniere.versionCible}`}
            </Link>
            <PastilleEtatMep m={derniere} court />
          </span>
        </Tuile>
        <Tuile
          titre="Clôturées avec problème"
          valeur={r.cloturees ? `${taux} %` : '—'}
          ton={taux >= 25 ? 'text-danger-solid' : taux > 0 ? 'text-warn-solid' : undefined}
        >
          {r.cloturees ? (
            <>
              {r.repliees} repliée{r.repliees > 1 ? 's' : ''} · {r.avecIncidents} avec incidents, sur {r.cloturees}{' '}
              clôturée{r.cloturees > 1 ? 's' : ''}
              <span className="mt-2 flex h-1.5 overflow-hidden rounded-full bg-sunken" aria-hidden>
                <span className={pastille('Reussie')} style={{ width: part(r.reussies) }} />
                <span className={pastille('AvecIncidents')} style={{ width: part(r.avecIncidents) }} />
                <span className={pastille('Repliee')} style={{ width: part(r.repliees) }} />
              </span>
            </>
          ) : (
            'Aucune MEP clôturée'
          )}
        </Tuile>
        <Tuile
          titre={`Échecs d’étape sur ${systeme.nom}`}
          valeur={r.echecs}
          ton={r.echecs ? 'text-danger-solid' : undefined}
        >
          {r.echecsRepris} repris · {r.echecs - r.echecsRepris} non repris
        </Tuile>
      </div>

      <div className="mt-4">
        <Frise meps={toutes} maintenant={maintenant} />
      </div>

      <div className="mt-6 flex flex-wrap items-center gap-3">
        {envs.length > 0 && (
          <Segmented
            label="Environnement"
            valeur={env}
            onChange={setEnv}
            className="max-w-full overflow-x-auto"
            options={[
              { valeur: 'tous', libelle: <span className="whitespace-nowrap">Tous ({toutes.length})</span> },
              ...envs.map((e) => ({
                valeur: e,
                libelle: (
                  <span className="whitespace-nowrap">
                    {e} ({toutes.filter((m) => m.environnements.some((x) => x.nom === e)).length})
                  </span>
                ),
              })),
            ]}
          />
        )}
        <Checkbox checked={avecEchec} onChange={setAvecEchec} label={`Avec un échec sur ${systeme.nom}`} />
        <div className="flex flex-wrap gap-2 sm:ml-auto">
          <Link
            to={lien}
            className="inline-flex h-8 items-center gap-1.5 rounded-md border border-line-strong bg-surface px-2.5 text-[13px] font-medium whitespace-nowrap shadow-sm transition-colors duration-150 hover:bg-surface-2"
          >
            <ListFilter className="size-4" aria-hidden />
            Ouvrir dans la liste des MEP
          </Link>
          <BoutonExportExcel
            url={api.urlExportHistorique(systeme.id)}
            title={`Toutes les MEP de ${systeme.nom}, avec leurs échecs sur ce système.`}
          />
        </div>
      </div>

      {meps.length === 0 ? (
        <div className="mt-4 rounded-xl border border-dashed border-line-strong bg-surface">
          <EmptyState
            icone={History}
            titre={`Aucune MEP sur ${systeme.nom}${env !== 'tous' ? ` en ${env}` : ''}${avecEchec ? ' avec un échec' : ''}`}
            description={
              avecEchec
                ? `Aucun échec d’étape sur ${systeme.nom} parmi ces MEP.`
                : 'Les MEP de ce système ont touché d’autres environnements.'
            }
            action={
              <Button
                onClick={() => {
                  setEnv('tous');
                  setAvecEchec(false);
                }}
              >
                Voir toutes les MEP
              </Button>
            }
          />
        </div>
      ) : (
        <div className="mt-5">
          {[...groupes]
            .sort(([a], [b]) => (a === 'avenir' ? -1 : b === 'avenir' ? 1 : b.localeCompare(a)))
            .map(([cle, liste]) => (
              <section key={cle} aria-label={titreGroupe(cle)}>
                <h3 className="mb-3 flex items-center gap-2 text-sm font-semibold first-letter:uppercase">
                  <span
                    className="size-2 rounded-full"
                    style={{ backgroundColor: couleurSysteme(systeme.nom) }}
                    aria-hidden
                  />
                  <span className="first-letter:uppercase">{titreGroupe(cle)}</span>
                  <span className="font-normal text-fg-subtle">· {liste.length} MEP</span>
                </h3>
                <ul>
                  {liste.map((m) => (
                    <Element key={m.id} m={m} nomSysteme={systeme.nom} />
                  ))}
                </ul>
              </section>
            ))}
        </div>
      )}
    </div>
  );
}

/** Onglet « Historique des MEP » : charge l'historique du système et l'affiche. */
export function HistoriqueSysteme({ systeme }: { systeme: SystemeCatalogue }) {
  const { donnees, erreur } = useAsync(() => api.historiqueSysteme(systeme.id), [systeme.id]);
  const [maintenant] = useState(() => new Date());
  if (erreur)
    return (
      <Alerte ton="danger" icone={OctagonAlert} role="alert">
        {erreur}
      </Alerte>
    );
  if (!donnees) return <SqueletteHistorique />;
  return <VueHistorique h={donnees} systeme={systeme} maintenant={maintenant} />;
}
