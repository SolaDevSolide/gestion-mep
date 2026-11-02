// Vue « Calendrier » de la liste des MEP (#129) : mois, semaine ou liste, avec les filtres de la liste. Chaque MEP est
// colorée par sa phase, marquée quand elle touche la production, et signale ses conflits et ses périodes de gel.
import { CalendarDays, ChevronLeft, ChevronRight, Download, Rss, Snowflake, TriangleAlert } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../../api';
import {
  colonnes,
  dernierJourGel,
  deplacer,
  FUSEAU_INSTANCE,
  gelsDuJour,
  grilleMois,
  heuresDecimales,
  heureLocale,
  horaire,
  jourComplet,
  jourLocal,
  joursSemaine,
  JOURS_COURTS,
  jourSemaine,
  mepsParJour,
  numeroJour,
  plageGel,
  plageVue,
  texteConflit,
  texteGelMep,
  titreVue,
  type VueCalendrier,
} from '../../calendrier';
import { useNow } from '../../hooks';
import { PuceSystemeMep } from '../../mep/SystemeCliquable';
import { parametresCalendrier, urlExportCalendrier, type FiltreMeps } from '../../rechercheMeps';
import { metaPhase } from '../../statuts';
import { telecharger } from '../../telechargement';
import type { ElementCalendrier, OccurrenceGel, PageCalendrier } from '../../types';
import { Avatar } from '../../ui/Avatar';
import { BoutonIcone, Button } from '../../ui/Button';
import { cn } from '../../ui/cn';
import { EmptyState } from '../../ui/EmptyState';
import { ErreurChargement } from '../../ui/ErreurChargement';
import { Segmented } from '../../ui/Segmented';
import { EnChargement, Squelette } from '../../ui/Squelette';
import { StatusPill } from '../../ui/StatusPill';
import { TON } from '../../ui/tons';
import { AbonnementDialog } from './AbonnementDialog';
import { DELAI_RECHERCHE } from './listeMeps';

/** Pastille qui passe à la ligne sur un écran étroit plutôt que d'être coupée. */
export const PILULE = 'max-w-full whitespace-normal leading-tight [&>span]:whitespace-normal';

const LIBELLES_VUES: Record<VueCalendrier, string> = { mois: 'Mois', semaine: 'Semaine', liste: 'Liste' };

/** Toutes les MEP de la plage, pages suivantes comprises (500 par page, une plage en compte rarement plus). */
async function lireTout(parametres: URLSearchParams): Promise<PageCalendrier> {
  const p = new URLSearchParams(parametres);
  p.set('limite', '500');
  const premiere = await api.calendrier(p);
  let page = premiere;
  const meps = [...premiere.meps];
  while (page.suivant) {
    page = await api.calendrier(p, page.suivant);
    meps.push(...page.meps);
  }
  return { ...premiere, meps, suivant: null };
}

/** Calendrier lu pour ces filtres et cette plage ; le précédent reste affiché jusqu'à la réponse. */
function useCalendrier(f: FiltreMeps, du: string, au: string) {
  const [recherche, setRecherche] = useState(f.recherche);
  useEffect(() => {
    const t = window.setTimeout(() => setRecherche(f.recherche), DELAI_RECHERCHE);
    return () => window.clearTimeout(t);
  }, [f.recherche]);
  const cle = parametresCalendrier({ ...f, recherche }, du, au).toString();
  const [essai, setEssai] = useState(0);
  const [lu, setLu] = useState<{ cle: string; page: PageCalendrier } | null>(null);
  const [echec, setEchec] = useState<{ cle: string; erreur: unknown } | null>(null);
  const [enCours, setEnCours] = useState(true);
  useEffect(() => {
    let annule = false;
    setEnCours(true);
    lireTout(new URLSearchParams(cle))
      .then((page) => {
        if (annule) return;
        setLu({ cle, page });
        setEchec(null);
      })
      .catch((e: unknown) => {
        if (!annule) setEchec({ cle, erreur: e ?? new Error('Chargement impossible.') });
      })
      .finally(() => {
        if (!annule) setEnCours(false);
      });
    return () => {
      annule = true;
    };
  }, [cle, essai]);
  return {
    page: lu?.page ?? null,
    aJour: lu?.cle === cle,
    echec: echec?.cle === cle ? echec.erreur : null,
    enCours,
    recharger: () => setEssai((n) => n + 1),
  };
}

const titreMep = (m: Pick<ElementCalendrier, 'outil' | 'versionCible' | 'type'>) =>
  m.type === 'Simplifiee' || !m.versionCible.trim()
    ? m.outil || 'Sans nom'
    : `${m.outil || 'Sans nom'} ${m.versionCible}`;

function Prod() {
  return (
    <span className="rounded-sm bg-warn-solid px-1 text-2xs leading-[14px] font-bold text-warn-solid-fg">PROD</span>
  );
}

/** Une MEP dans une case du mois : heure, PROD, conflit, gel, nom. Ouvre sa fiche. */
function EvenementMois({ m, fuseau }: { m: ElementCalendrier; fuseau: string }) {
  const meta = metaPhase(m.phase, m.etat);
  const details = [
    meta.libelle,
    m.toucheProduction ? 'touche la production' : '',
    m.conflits.length ? 'conflit' : '',
    m.gels.length ? 'période de gel' : '',
  ].filter(Boolean);
  return (
    <Link
      to={`/meps/${m.id}`}
      title={`${horaire(m, fuseau)} · ${titreMep(m)} · ${details.join(' · ')}`}
      className={cn(
        'block min-w-0 rounded-md border border-l-[3px] px-1.5 py-0.5 text-2xs leading-tight transition-shadow duration-150 hover:shadow-sm focus-visible:ring-2 focus-visible:ring-accent focus-visible:outline-none',
        TON[meta.ton].pastille,
        m.phase === 'Annulee' && 'line-through opacity-70',
      )}
    >
      <span className="flex items-center gap-1">
        <span className="opacity-80 tnum">{m.journeeEntiere ? 'Jour' : heureLocale(m.debut, fuseau)}</span>
        {m.toucheProduction && <Prod />}
        {m.conflits.length > 0 && <TriangleAlert className="size-3 text-danger-solid" aria-hidden />}
        {m.gels.length > 0 && <Snowflake className="size-3 text-run-solid" aria-hidden />}
      </span>
      <span className={cn('block truncate', m.toucheProduction && 'font-semibold')}>
        {titreMep(m)}
        <span className="sr-only">
          {' '}
          : {horaire(m, fuseau)}, {details.join(', ')}
        </span>
      </span>
    </Link>
  );
}

/** Ligne d'une MEP (vue liste, jour choisi, semaine sur téléphone) : horaire, systèmes, pilote, phase et avertissements. */
function LigneMep({ m, fuseau }: { m: ElementCalendrier; fuseau: string }) {
  const meta = metaPhase(m.phase, m.etat);
  return (
    <li className="relative grid grid-cols-[minmax(0,1fr)] gap-x-4 gap-y-1.5 border-t border-line px-4 py-3 transition-colors duration-150 first:border-t-0 hover:bg-surface-2/50 sm:grid-cols-[8.5rem_minmax(0,1fr)_auto] sm:items-center">
      <span className="text-sm text-fg-muted tnum">{horaire(m, fuseau)}</span>
      <div className="min-w-0">
        <Link
          to={`/meps/${m.id}`}
          className={cn(
            'text-sm outline-none after:absolute after:inset-0 focus-visible:after:ring-2 focus-visible:after:ring-accent',
            m.toucheProduction ? 'font-semibold' : 'font-medium',
            m.phase === 'Annulee' && 'line-through',
          )}
        >
          {m.outil || 'Sans nom'}
          {m.type !== 'Simplifiee' && m.versionCible.trim() && (
            <span className="ml-1.5 font-mono text-xs">{m.versionCible}</span>
          )}
        </Link>
        {m.systemes.length > 0 && (
          <span className="relative z-10 mt-1 flex w-fit max-w-full flex-wrap gap-1" aria-label="Systèmes">
            {m.systemes.map((s, i) => (
              <PuceSystemeMep key={`${s.nom}-${i}`} s={s} />
            ))}
          </span>
        )}
      </div>
      <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
        <span className="inline-flex items-center gap-1.5 text-sm text-fg-muted">
          {m.pilote && <Avatar nom={m.pilote} taille="xs" />}
          {m.pilote ?? 'Pas de pilote'}
        </span>
        <StatusPill ton={meta.ton} icone={meta.Icone} taille="sm">
          {meta.libelle}
        </StatusPill>
      </div>
      {(m.conflits.length > 0 || m.gels.length > 0) && (
        <div className="flex flex-wrap gap-1.5 sm:col-span-3">
          {m.conflits.map((c) => (
            <StatusPill key={c.mepId} ton="danger" icone={TriangleAlert} taille="sm" className={PILULE}>
              {texteConflit(c, fuseau)}
            </StatusPill>
          ))}
          {m.gels.map((g) => (
            <StatusPill key={g.periodeGelId + g.debut} ton="run" icone={Snowflake} taille="sm" className={PILULE}>
              {texteGelMep(g, m.phase)}
            </StatusPill>
          ))}
        </div>
      )}
    </li>
  );
}

/** En-tête d'un jour (vue liste, jour choisi) : date, « aujourd'hui », gels du jour, nombre de conflits. */
function EnTeteJour({
  jour,
  aujourdhui,
  gels,
  meps,
  fuseau,
}: {
  jour: string;
  aujourdhui: string;
  gels: OccurrenceGel[];
  meps?: ElementCalendrier[];
  fuseau: string;
}) {
  const conflits = meps?.filter((m) => m.conflits.length > 0).length ?? 0;
  return (
    <h3 className="flex flex-wrap items-center gap-2 border-t border-line bg-surface-2/60 px-4 py-2 text-ui font-semibold first:border-t-0">
      <span className="first-letter:uppercase">{jourComplet(jour)}</span>
      {jour === aujourdhui && <span className="font-normal text-fg-subtle">· aujourd’hui</span>}
      {meps && <span className="font-normal text-fg-subtle">· {meps.length} MEP</span>}
      {conflits > 0 && (
        <StatusPill ton="danger" icone={TriangleAlert} taille="sm">
          {conflits} en conflit
        </StatusPill>
      )}
      {gels.map((g) => (
        <StatusPill key={g.periodeGelId + g.debut} ton="run" icone={Snowflake} taille="sm" className={PILULE}>
          Gel : {g.nom} ({plageGel(g, fuseau)})
        </StatusPill>
      ))}
    </h3>
  );
}

function Legende() {
  const phases: [string, keyof typeof TON][] = [
    ['Préparation', 'wait'],
    ['Exécution', 'run'],
    ['Recette', 'warn'],
    ['Clôturée', 'done'],
    ['Annulée', 'later'],
    ['Retour arrière', 'rollback'],
  ];
  return (
    <div className="flex flex-wrap items-center gap-x-4 gap-y-1.5 border-t border-line px-4 py-2.5 text-xs text-fg-muted">
      {phases.map(([libelle, ton]) => (
        <span key={libelle} className="inline-flex items-center gap-1.5">
          <i className={cn('size-2.5 rounded-sm', TON[ton].bgSolide)} aria-hidden />
          {libelle}
        </span>
      ))}
      <span className="inline-flex items-center gap-1.5">
        <Prod /> touche la production
      </span>
      <span className="inline-flex items-center gap-1.5">
        <i className="hachures-gel size-3 rounded-sm border border-run-bd" aria-hidden />
        Période de gel
      </span>
      <span className="inline-flex items-center gap-1.5">
        <TriangleAlert className="size-3.5 text-danger-solid" aria-hidden />
        Conflit
      </span>
    </div>
  );
}

// ---------- Vue mois ----------

function VueMois({
  page,
  ancre,
  aujourdhui,
  jourChoisi,
  choisir,
}: {
  page: PageCalendrier;
  ancre: string;
  aujourdhui: string;
  jourChoisi: string;
  choisir: (jour: string) => void;
}) {
  const fuseau = page.fuseauHoraire;
  const jours = grilleMois(ancre);
  const parJour = mepsParJour(page.meps, fuseau, jours[0]);
  const mois = ancre.slice(0, 7);
  const duJour = parJour.get(jourChoisi) ?? [];
  return (
    <>
      <div className="grid grid-cols-7 border-b border-line bg-surface-2/60 text-center text-xs font-medium text-fg-subtle">
        {JOURS_COURTS.map((j) => (
          <div key={j} className="py-1.5">
            {j}
          </div>
        ))}
      </div>
      <div className="grid grid-cols-7">
        {jours.map((jour, i) => {
          const meps = parJour.get(jour) ?? [];
          const gels = gelsDuJour(page.gels, jour, fuseau);
          // Nom du gel à son premier jour, et au début de chaque semaine qu'il traverse.
          const etiquettes = gels.filter((g) => jourLocal(g.debut, fuseau) === jour || i % 7 === 0);
          const dehors = jour.slice(0, 7) !== mois;
          const choisi = jour === jourChoisi;
          const libelleJour = `${jourComplet(jour)}${jour === aujourdhui ? ', aujourd’hui' : ''} : ${meps.length} MEP${gels.length ? `, gel ${gels.map((g) => g.nom).join(', ')}` : ''}`;
          return (
            <div
              key={jour}
              className={cn(
                'relative flex min-h-16 min-w-0 flex-col gap-0.5 border-line p-1 sm:min-h-28 sm:p-1.5',
                i % 7 !== 6 && 'border-r',
                i < 35 && 'border-b',
                dehors && 'bg-surface-2/50',
                !dehors && jourSemaine(jour) > 4 && 'bg-surface-2/30',
                gels.length > 0 && 'hachures-gel',
                choisi && 'ring-2 ring-accent ring-inset',
              )}
            >
              <button
                type="button"
                onClick={() => choisir(jour)}
                aria-pressed={choisi}
                aria-label={libelleJour}
                className={cn(
                  'flex size-6 items-center justify-center rounded-full text-xs tnum outline-none hover:bg-surface-2 focus-visible:ring-2 focus-visible:ring-accent max-sm:after:absolute max-sm:after:inset-0',
                  dehors ? 'text-fg-subtle' : 'text-fg',
                  jour === aujourdhui && 'bg-accent font-semibold text-accent-fg hover:bg-accent',
                )}
              >
                {numeroJour(jour)}
              </button>
              {etiquettes.map((g) => (
                <span
                  key={g.periodeGelId + g.debut}
                  className="hidden items-center gap-1 truncate text-2xs font-medium text-run-fg sm:flex"
                >
                  <Snowflake className="size-3 shrink-0" aria-hidden />
                  <span className="truncate">{g.nom}</span>
                </span>
              ))}
              {/* Écran large : les MEP du jour ; téléphone : une pastille par MEP, la liste sous la grille. */}
              <div className="hidden min-w-0 flex-col gap-0.5 sm:flex">
                {meps.slice(0, 3).map((m) => (
                  <EvenementMois key={m.id} m={m} fuseau={fuseau} />
                ))}
                {meps.length > 3 && (
                  <button
                    type="button"
                    onClick={() => choisir(jour)}
                    className="self-start rounded px-1 text-2xs font-medium text-accent-text hover:underline"
                  >
                    + {meps.length - 3} autre{meps.length - 3 > 1 ? 's' : ''}
                  </button>
                )}
              </div>
              <div className="flex flex-wrap gap-0.5 sm:hidden" aria-hidden>
                {meps.slice(0, 6).map((m) => (
                  <i
                    key={m.id}
                    className={cn(
                      'size-1.5 rounded-full',
                      TON[metaPhase(m.phase, m.etat).ton].bgSolide,
                      m.conflits.length > 0 && 'ring-1 ring-danger-solid',
                    )}
                  />
                ))}
              </div>
            </div>
          );
        })}
      </div>
      <section aria-label={`MEP du ${jourComplet(jourChoisi)}`} className="border-t border-line">
        <EnTeteJour
          jour={jourChoisi}
          aujourdhui={aujourdhui}
          gels={gelsDuJour(page.gels, jourChoisi, fuseau)}
          meps={duJour}
          fuseau={fuseau}
        />
        {duJour.length > 0 ? (
          <ul>
            {duJour.map((m) => (
              <LigneMep key={m.id} m={m} fuseau={fuseau} />
            ))}
          </ul>
        ) : (
          <p className="px-4 py-3 text-sm text-fg-subtle">Aucune MEP ce jour-là.</p>
        )}
      </section>
    </>
  );
}

// ---------- Vue semaine ----------

const PX_HEURE = 32;

function VueSemaine({ page, ancre, aujourdhui }: { page: PageCalendrier; ancre: string; aujourdhui: string }) {
  const fuseau = page.fuseauHoraire;
  const maintenant = useNow(60_000);
  const jours = joursSemaine(ancre);
  const parJour = mepsParJour(page.meps, fuseau, jours[0]);
  const horaires = page.meps.filter((m) => !m.journeeEntiere);
  const journees = page.meps.filter((m) => m.journeeEntiere);
  // De 6 h (ou plus tôt si une MEP commence avant) à minuit.
  const h0 = Math.min(
    6,
    ...horaires
      .filter((m) => jourLocal(m.debut, fuseau) >= jours[0])
      .map((m) => Math.floor(heuresDecimales(m.debut, fuseau))),
  );
  const heures = Array.from({ length: 24 - h0 }, (_, i) => h0 + i);
  return (
    <>
      {/* Écran large : grille horaire. */}
      <div className="hidden md:block">
        <div className="grid grid-cols-[3.5rem_repeat(7,minmax(0,1fr))] border-b border-line bg-surface-2/60">
          <div />
          {jours.map((jour, i) => (
            <div
              key={jour}
              className={cn(
                'border-l border-line py-1.5 text-center text-xs text-fg-subtle',
                gelsDuJour(page.gels, jour, fuseau).length > 0 && 'hachures-gel',
              )}
            >
              {JOURS_COURTS[i]}
              <b
                className={cn(
                  'mx-auto mt-0.5 flex size-7 items-center justify-center rounded-full text-md text-fg tnum',
                  jour === aujourdhui && 'bg-accent text-accent-fg',
                )}
              >
                {numeroJour(jour)}
              </b>
            </div>
          ))}
        </div>
        {journees.length > 0 && (
          <div className="grid grid-cols-[3.5rem_repeat(7,minmax(0,1fr))] border-b border-line">
            <div className="px-1 py-1.5 text-right text-2xs text-fg-subtle">Journée</div>
            {jours.map((jour) => (
              <div key={jour} className="flex min-w-0 flex-col gap-0.5 border-l border-line p-1">
                {(parJour.get(jour) ?? [])
                  .filter((m) => m.journeeEntiere)
                  .map((m) => (
                    <EvenementMois key={m.id} m={m} fuseau={fuseau} />
                  ))}
              </div>
            ))}
          </div>
        )}
        <div className="grid grid-cols-[3.5rem_repeat(7,minmax(0,1fr))]">
          <div>
            {heures.map((h) => (
              <div
                key={h}
                style={{ height: PX_HEURE }}
                className="relative -top-1.5 pr-2 text-right text-2xs text-fg-subtle tnum"
              >
                {h === h0 ? '' : `${h}:00`}
              </div>
            ))}
          </div>
          {jours.map((jour) => {
            const meps = (parJour.get(jour) ?? []).filter((m) => !m.journeeEntiere);
            const places = colonnes(meps, maintenant);
            const gels = gelsDuJour(page.gels, jour, fuseau);
            return (
              <div
                key={jour}
                className={cn('relative border-l border-line', jourSemaine(jour) > 4 && 'bg-surface-2/30')}
                style={{ height: heures.length * PX_HEURE }}
              >
                {heures.map((h) => (
                  <div
                    key={h}
                    className="absolute inset-x-0 border-t border-line/60"
                    style={{ top: (h - h0) * PX_HEURE }}
                    aria-hidden
                  />
                ))}
                {gels.map((g) => {
                  const debut = jourLocal(g.debut, fuseau) < jour ? 0 : heuresDecimales(g.debut, fuseau);
                  const fin = dernierJourGel(g, fuseau) > jour ? 24 : heuresDecimales(g.fin, fuseau) || 24;
                  return (
                    <div
                      key={g.periodeGelId + g.debut}
                      className="hachures-gel absolute inset-x-0"
                      style={{
                        top: Math.max(0, debut - h0) * PX_HEURE,
                        height: (fin - Math.max(debut, h0)) * PX_HEURE,
                      }}
                      title={`Période de gel : ${g.nom}`}
                      aria-hidden
                    />
                  );
                })}
                {meps.map((m) => {
                  const meta = metaPhase(m.phase, m.etat);
                  const debut = jourLocal(m.debut, fuseau) < jour ? 0 : heuresDecimales(m.debut, fuseau);
                  const finMs = m.fin ? Date.parse(m.fin) : m.enCours ? maintenant : Date.parse(m.debut) + 3_600_000;
                  const finIso = new Date(finMs).toISOString();
                  const fin = jourLocal(finIso, fuseau) > jour ? 24 : heuresDecimales(finIso, fuseau);
                  const place = places.get(m.id) ?? { colonne: 0, colonnes: 1 };
                  const largeur = 100 / place.colonnes;
                  return (
                    <Link
                      key={m.id}
                      to={`/meps/${m.id}`}
                      title={`${titreMep(m)} · ${horaire(m, fuseau)}`}
                      className={cn(
                        'absolute overflow-hidden rounded-md border border-l-[3px] px-1.5 py-1 text-2xs leading-tight shadow-sm transition-shadow duration-150 hover:shadow-md focus-visible:ring-2 focus-visible:ring-accent focus-visible:outline-none',
                        TON[meta.ton].pastille,
                        m.conflits.length > 0 && 'outline-2 outline-danger-solid',
                      )}
                      style={{
                        top: Math.max(0, debut - h0) * PX_HEURE + 1,
                        height: Math.max(26, (fin - Math.max(debut, h0)) * PX_HEURE - 2),
                        left: `calc(${place.colonne * largeur}% + 2px)`,
                        width: `calc(${largeur}% - 4px)`,
                      }}
                    >
                      <span className={cn('block truncate', m.toucheProduction && 'font-semibold')}>{titreMep(m)}</span>
                      <span className="flex items-center gap-1 opacity-80 tnum">
                        {horaire(m, fuseau)}
                        {m.toucheProduction && <Prod />}
                        {m.conflits.length > 0 && (
                          <TriangleAlert className="size-3 text-danger-solid" aria-label="conflit" />
                        )}
                        {m.gels.length > 0 && (
                          <Snowflake className="size-3 text-run-solid" aria-label="période de gel" />
                        )}
                      </span>
                    </Link>
                  );
                })}
              </div>
            );
          })}
        </div>
      </div>
      {/* Écran étroit : la semaine jour par jour. */}
      <div className="md:hidden">
        {jours.map((jour) => {
          const meps = parJour.get(jour) ?? [];
          const gels = gelsDuJour(page.gels, jour, fuseau);
          if (meps.length === 0 && gels.length === 0) return null;
          return (
            <section key={jour}>
              <EnTeteJour jour={jour} aujourdhui={aujourdhui} gels={gels} meps={meps} fuseau={fuseau} />
              {meps.length > 0 ? (
                <ul>
                  {meps.map((m) => (
                    <LigneMep key={m.id} m={m} fuseau={fuseau} />
                  ))}
                </ul>
              ) : (
                <p className="px-4 py-3 text-sm text-fg-subtle">Aucune MEP ce jour-là.</p>
              )}
            </section>
          );
        })}
        {page.meps.length === 0 && (
          <p className="px-4 py-6 text-center text-sm text-fg-subtle">Aucune MEP cette semaine.</p>
        )}
      </div>
    </>
  );
}

// ---------- Vue liste ----------

function VueListe({ page, ancre, aujourdhui }: { page: PageCalendrier; ancre: string; aujourdhui: string }) {
  const fuseau = page.fuseauHoraire;
  // Le mois en cours : à partir d'aujourd'hui.
  const depuis = ancre.slice(0, 7) === aujourdhui.slice(0, 7) ? aujourdhui : plageVue('liste', ancre).du;
  const parJour = mepsParJour(page.meps, fuseau, depuis);
  const jours = [...parJour.keys()].filter((j) => j >= depuis).sort();
  if (jours.length === 0)
    return (
      <EmptyState
        icone={CalendarDays}
        titre="Aucune MEP sur cette période"
        description="Aucune MEP ne correspond aux filtres ce mois-ci. Changez de mois ou de filtres."
      />
    );
  return (
    <div>
      {jours.map((jour) => (
        <section key={jour}>
          <EnTeteJour jour={jour} aujourdhui={aujourdhui} gels={gelsDuJour(page.gels, jour, fuseau)} fuseau={fuseau} />
          <ul>
            {(parJour.get(jour) ?? []).map((m) => (
              <LigneMep key={m.id} m={m} fuseau={fuseau} />
            ))}
          </ul>
        </section>
      ))}
    </div>
  );
}

function SqueletteCalendrier({ vue }: { vue: VueCalendrier }) {
  return (
    <EnChargement libelle="Chargement du calendrier…">
      {vue === 'liste' ? (
        <div className="divide-y divide-line">
          {Array.from({ length: 6 }, (_, i) => (
            <div key={i} className="flex items-center gap-4 px-4 py-3.5">
              <Squelette className="h-4 w-24" />
              <Squelette className="h-4 w-56 max-w-full" />
              <Squelette className="ml-auto hidden h-5 w-24 rounded-full sm:block" />
            </div>
          ))}
        </div>
      ) : (
        <div className="grid grid-cols-7 gap-px bg-line">
          {Array.from({ length: vue === 'mois' ? 35 : 7 }, (_, i) => (
            <div key={i} className={cn('bg-surface p-1.5', vue === 'mois' ? 'h-16 sm:h-28' : 'h-80')}>
              <Squelette className="size-5 rounded-full" />
              <Squelette className="mt-2 hidden h-8 w-full sm:block" />
            </div>
          ))}
        </div>
      )}
    </EnChargement>
  );
}

export function CalendrierMeps({
  f,
  vue,
  ancre,
  jourChoisi,
  changer,
}: {
  f: FiltreMeps;
  vue: VueCalendrier;
  /** Jour qui fixe la période affichée (AAAA-MM-JJ). */
  ancre: string;
  /** Jour choisi dans la vue mois (sa liste s'affiche sous la grille). */
  jourChoisi: string;
  changer: (p: { vue?: VueCalendrier; ancre?: string; jour?: string }) => void;
}) {
  const { du, au } = plageVue(vue, ancre);
  const { page, aJour, echec, enCours, recharger } = useCalendrier(f, du, au);
  const [abonnement, setAbonnement] = useState(false);
  const fuseau = page?.fuseauHoraire ?? FUSEAU_INSTANCE;
  const maintenant = useNow(60_000);
  const aujourdhui = useMemo(() => jourLocal(new Date(maintenant).toISOString(), fuseau), [maintenant, fuseau]);
  const titre = titreVue(vue, ancre, aujourdhui);
  return (
    <section
      className="mt-6 overflow-hidden rounded-xl border border-line bg-surface shadow-sm"
      aria-label="Calendrier des MEP"
    >
      <div className="flex flex-wrap items-center gap-x-3 gap-y-2 border-b border-line px-3 py-2.5 sm:px-4">
        <div className="flex items-center gap-1">
          <BoutonIcone
            label="Période précédente"
            taille="sm"
            onClick={() => changer({ ancre: deplacer(vue, ancre, -1) })}
          >
            <ChevronLeft className="size-4" aria-hidden />
          </BoutonIcone>
          <BoutonIcone label="Période suivante" taille="sm" onClick={() => changer({ ancre: deplacer(vue, ancre, 1) })}>
            <ChevronRight className="size-4" aria-hidden />
          </BoutonIcone>
          <Button taille="sm" onClick={() => changer({ ancre: aujourdhui, jour: aujourdhui })}>
            Aujourd’hui
          </Button>
        </div>
        <h2 className="min-w-0 flex-1 basis-40 text-md font-semibold tracking-tight" aria-live="polite">
          {titre}
        </h2>
        <Segmented
          label="Vue du calendrier"
          valeur={vue}
          onChange={(v) => changer({ vue: v })}
          options={(['mois', 'semaine', 'liste'] as const).map((v) => ({ valeur: v, libelle: LIBELLES_VUES[v] }))}
        />
        <div className="flex flex-wrap items-center gap-2 sm:ml-auto">
          <Button
            taille="sm"
            icone={<Download className="size-4" aria-hidden />}
            title="Télécharge les MEP de la période affichée, filtres compris, et les périodes de gel (iCalendar)."
            onClick={() => telecharger(urlExportCalendrier(f, du, au))}
          >
            Exporter .ics
          </Button>
          <Button taille="sm" icone={<Rss className="size-4" aria-hidden />} onClick={() => setAbonnement(true)}>
            S’abonner
          </Button>
        </div>
      </div>
      {echec !== null && (
        <ErreurChargement
          className="m-4"
          erreur={echec}
          titre="Impossible de charger le calendrier"
          onReessayer={recharger}
          enCours={enCours}
        />
      )}
      {page === null && echec === null && <SqueletteCalendrier vue={vue} />}
      {page !== null && (
        <div
          aria-busy={!aJour || undefined}
          className={cn('apparition transition-opacity duration-200', !aJour && 'opacity-50')}
        >
          {vue === 'mois' && (
            <VueMois
              page={page}
              ancre={ancre}
              aujourdhui={aujourdhui}
              jourChoisi={jourChoisi}
              choisir={(jour) => changer({ jour })}
            />
          )}
          {vue === 'semaine' && <VueSemaine page={page} ancre={ancre} aujourdhui={aujourdhui} />}
          {vue === 'liste' && <VueListe page={page} ancre={ancre} aujourdhui={aujourdhui} />}
          {vue !== 'liste' && page.meps.length === 0 && page.gels.length === 0 && aJour && (
            <p className="border-t border-line px-4 py-2.5 text-sm text-fg-subtle">
              Aucune MEP ne correspond aux filtres sur cette période.
            </p>
          )}
        </div>
      )}
      <Legende />
      <AbonnementDialog open={abonnement} onOpenChange={setAbonnement} f={f} />
    </section>
  );
}
