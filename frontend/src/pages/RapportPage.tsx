import { ArrowLeft, CheckCircle2, Download, FileText, OctagonAlert, Printer, Wrench, XCircle } from 'lucide-react';
import { useMemo, type ReactNode } from 'react';
import { Link, useParams } from 'react-router-dom';
import { api } from '../api';
import { etapesDuPlan, nomParticipant, segmentsProgression } from '../domaine';
import {
  formatDate,
  formatDateHeure,
  LIBELLE_ETAT,
  LIBELLE_FAMILLE_EVENEMENT,
  LIBELLE_RESULTAT,
  LIBELLE_STATUT_RESOLUTION,
  familleEvenement,
} from '../format';
import { useAsync } from '../hooks';
import { SqueletteRapport } from '../mep/ensemble/Squelettes';
import { causesBlocage, duree } from '../mep/ensemble/calculs';
import { Legende } from '../mep/ensemble/VueEnsembleTab';
import { GrapheLibre } from '../mep/graphe/GrapheLibre';
import { sectionsDuPlan } from '../mep/graphe/sections';
import { descriptionsSystemes } from '../mep/graphe/systemes';
import { PuceSystemeMep } from '../mep/SystemeCliquable';
import { estSimplifiee } from '../mep/typeMep';
import { MepContext } from '../mepContext';
import { useReferentiels } from '../referentiels';
import { metaPhase } from '../statuts';
import { telecharger } from '../telechargement';
import type { Etape, Mep, RapportFin } from '../types';
import { Alerte } from '../ui/Alerte';
import { Apparition } from '../ui/Apparition';
import { Avatar } from '../ui/Avatar';
import { Button } from '../ui/Button';
import { Markdown } from '../ui/Instructions';
import { ProgressBar } from '../ui/ProgressBar';
import { StatusPill } from '../ui/StatusPill';

const minutesEntre = (a: string | null, b: string | null) =>
  a && b ? Math.round((Date.parse(b) - Date.parse(a)) / 60_000) : null;
const dureeOuTiret = (min: number | null | undefined) => (min === null || min === undefined ? '—' : duree(min));
const heureDe = (iso: string | null | undefined) =>
  iso ? new Date(iso).toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' }) : '—';

function Partie({ id, titre, children }: { id: string; titre: string; children: ReactNode }) {
  return (
    <section aria-labelledby={id} className="mt-8 print:mt-6">
      <h2
        id={id}
        className="border-b border-line pb-1.5 text-[17px] font-semibold tracking-tight break-after-avoid print:text-[15px]"
      >
        {titre}
      </h2>
      <div className="mt-3">{children}</div>
    </section>
  );
}

const th =
  'border-b border-line px-2 py-1.5 text-left text-[11px] font-semibold tracking-[0.04em] text-fg-subtle uppercase';
const td = 'border-b border-line px-2 py-1.5 align-top';

function Tableau({ entetes, vide, children }: { entetes: string[]; vide: string; children: ReactNode[] }) {
  if (children.length === 0) return <p className="text-[13px] text-fg-subtle">{vide}</p>;
  return (
    <div className="overflow-x-auto print:overflow-visible">
      <table className="w-full border-collapse text-[13px] print:text-[11px]">
        <thead>
          <tr>
            {entetes.map((e) => (
              <th key={e} className={th}>
                {e}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>{children}</tbody>
      </table>
    </div>
  );
}

function Chiffre({
  titre,
  valeur,
  detail,
  children,
}: {
  titre: string;
  valeur: ReactNode;
  detail?: ReactNode;
  children?: ReactNode;
}) {
  return (
    <div className="min-w-0 rounded-lg border border-line px-4 py-3 break-inside-avoid">
      <p className="text-[12px] text-fg-subtle">{titre}</p>
      <p className="text-[22px] font-bold tnum">{valeur}</p>
      {detail && <p className="text-[12px] text-fg-muted">{detail}</p>}
      {children}
    </div>
  );
}

function lignesEtapes({ mep, etapes }: { mep: Mep; etapes: Etape[] }) {
  const titres = new Map(mep.sections.map((s) => [s.id, s.titre]));
  return etapes.map((e) => {
    const nom = nomParticipant(mep, e.executantId);
    const reel = minutesEntre(e.debutReel, e.finReelle);
    return (
      <tr key={e.id} className="break-inside-avoid">
        <td className={td}>
          <span className="font-mono text-[12px] text-fg-subtle">{e.code}</span> {e.action}
          {e.sectionId && titres.has(e.sectionId) && (
            <span className="block text-[12px] text-fg-subtle">{titres.get(e.sectionId)}</span>
          )}
        </td>
        <td className={td}>{LIBELLE_ETAT[e.etat]}</td>
        <td className={td}>
          {nom ? (
            <span className="inline-flex items-center gap-1.5">
              <Avatar nom={nom} taille="xs" className="print:hidden" />
              {nom}
            </span>
          ) : (
            '—'
          )}
        </td>
        <td className={`${td} tnum`}>{heureDe(e.debutReel)}</td>
        <td className={`${td} tnum`}>{heureDe(e.finReelle)}</td>
        <td className={`${td} tnum`}>
          {dureeOuTiret(reel)} / {dureeOuTiret(e.dureeEstimeeMinutes)}
        </td>
      </tr>
    );
  });
}

/** Rapport figé : vue d'ensemble à la clôture, points bloquants, résolutions, tests unitaires, exécutants, bilan, annexes. */
function Rapport({ r }: { r: RapportFin }) {
  const mep = r.mep;
  const s = r.synthese;
  const { catalogue } = useReferentiels();
  const descriptions = useMemo(() => descriptionsSystemes(mep, catalogue), [mep, catalogue]);
  const deploiement = useMemo(() => etapesDuPlan(mep, 'Deploiement'), [mep]);
  const retour = useMemo(() => etapesDuPlan(mep, 'RetourArriere'), [mep]);
  const sections = useMemo(() => sectionsDuPlan(mep, 'Deploiement').filter((x) => x.placee), [mep]);
  const ensemble = useMemo(() => ({ bloquees: causesBlocage(deploiement) }), [deploiement]);
  const phase = metaPhase(mep.phase, mep.issue);
  const pilote = nomParticipant(mep, mep.piloteId);
  const codes = new Map(mep.etapes.map((e) => [e.id, e.code]));
  const systemes = new Map(mep.systemes.map((x) => [x.id, x.nom]));

  return (
    <article className="mx-auto mt-4 max-w-[1040px] rounded-xl border border-line bg-surface px-5 py-8 shadow-sm sm:px-12 sm:py-10 print:mt-0 print:max-w-none print:border-0 print:p-0 print:shadow-none">
      <header className="flex flex-wrap items-start justify-between gap-4">
        <div className="min-w-0">
          <p className="text-[11px] font-semibold tracking-[0.12em] text-fg-subtle uppercase">Rapport de fin de MEP</p>
          <h1 className="mt-1 flex flex-wrap items-baseline gap-x-2 text-[26px] font-semibold tracking-tight">
            {mep.outil}
            {!estSimplifiee(mep) && (
              <span className="font-mono text-[20px] text-fg-muted">
                {mep.versionActuelle} → {mep.versionCible}
              </span>
            )}
          </h1>
          {mep.objet && <p className="mt-1 max-w-2xl text-[14px] text-fg-muted">{mep.objet}</p>}
        </div>
        <StatusPill taille="lg" ton={phase.ton} icone={phase.Icone}>
          {phase.libelle}
        </StatusPill>
      </header>
      <dl className="mt-5 grid grid-cols-2 gap-4 rounded-lg bg-surface-2/60 px-4 py-3 text-[13px] md:grid-cols-4">
        <div>
          <dt className="text-fg-subtle">Pilote</dt>
          <dd className="mt-0.5 font-medium">{pilote ?? '—'}</dd>
        </div>
        <div>
          <dt className="text-fg-subtle">Go</dt>
          <dd className="mt-0.5 font-medium tnum">{s.go ? formatDateHeure(s.go) : '—'}</dd>
        </div>
        <div>
          <dt className="text-fg-subtle">Clôture</dt>
          <dd className="mt-0.5 font-medium tnum">
            {mep.cloture ? formatDateHeure(mep.cloture.le) : '—'}
            {mep.cloture?.par && <> par {mep.cloture.par}</>}
          </dd>
        </div>
        <div>
          <dt className="text-fg-subtle">Systèmes</dt>
          <dd className="mt-1 flex flex-wrap gap-1">
            {mep.systemes.length === 0
              ? '—'
              : mep.systemes.map((x) => (
                  <PuceSystemeMep
                    key={x.id}
                    s={{
                      ...x,
                      echecs: r.pointsBloquants.filter((p) =>
                        mep.etapes.some((e) => e.code === p.code && e.plan === p.plan && e.systemeIds.includes(x.id)),
                      ).length,
                    }}
                  />
                ))}
          </dd>
        </div>
      </dl>

      <Partie id="rapport-ensemble" titre="1. Vue d’ensemble à la clôture">
        <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
          <Chiffre titre="Étapes de déploiement" valeur={`${s.etapesReussies}/${s.etapes} réussies`}>
            <ProgressBar className="mt-1.5" segments={segmentsProgression(deploiement)} />
          </Chiffre>
          <Chiffre
            titre="Durée du déploiement"
            valeur={dureeOuTiret(s.dureeReelleMinutes)}
            detail={s.dureePrevueMinutes > 0 ? `prévue ${duree(s.dureePrevueMinutes)}` : 'durées non estimées'}
          />
          <Chiffre
            titre="Échecs d’étape"
            valeur={s.echecs === 0 ? 'Aucun' : `${s.echecs}`}
            detail={
              s.echecs === 0 ? undefined : `${s.echecsRepris} repris avec succès · ${s.resolutions} résolution(s)`
            }
          />
          <Chiffre
            titre="Recette"
            valeur={s.recetteTests === 0 ? '—' : `${s.recetteConformes}/${s.recetteTests}`}
            detail={s.recetteTests === 0 ? 'aucun test' : `conformes · ${s.recetteEchecs} en échec`}
          />
        </div>
        {s.retourArriere && mep.retourArriere && (
          <Alerte ton="rollback" icone={OctagonAlert} className="mt-3">
            Retour arrière déclenché le {formatDateHeure(mep.retourArriere.le)}
            {mep.retourArriere.par ? ` par ${mep.retourArriere.par}` : ' (automatiquement)'}
            {mep.retourArriere.motif && ` : ${mep.retourArriere.motif}`} (voir l’annexe).
          </Alerte>
        )}
        {deploiement.length > 0 && (
          <div className="break-inside-avoid">
            <div className="mt-3 flex flex-wrap items-center justify-between gap-2">
              <p className="text-[12px] text-fg-subtle">Plan coloré tel qu’à la clôture (lecture seule)</p>
              <Legende />
            </div>
            {/* Taille fixe, celle d'une page A4 : sinon le graphe prend la hauteur restante de la fenêtre, et l'impression
                le couperait (il ne se recadre pas au passage à l'impression). */}
            <div className="mx-auto mt-1.5 max-w-[700px] [&_.graphe-libre]:!h-[700px]">
              <GrapheLibre
                mep={mep}
                plan="Deploiement"
                etapes={deploiement}
                sections={sections}
                mode="execution"
                descriptions={descriptions}
                selection={null}
                onSelection={() => undefined}
                deplies={new Set()}
                onBasculer={() => undefined}
                ensemble={ensemble}
              />
            </div>
          </div>
        )}
      </Partie>

      <Partie id="rapport-points" titre="2. Points bloquants">
        {r.pointsBloquants.length === 0 ? (
          <p className="text-[13px] text-fg-subtle">Aucun échec d’étape.</p>
        ) : (
          <ul className="grid gap-2">
            {r.pointsBloquants.map((p) => (
              <li
                key={`${p.code}-${p.echecLe}`}
                className="rounded-lg border-l-4 border-l-danger-solid bg-surface-2/50 px-3 py-2 text-[13.5px] break-inside-avoid"
              >
                <p className="font-semibold">
                  <XCircle className="mr-1 inline size-4 text-danger-solid" aria-hidden />
                  {p.code} {p.action} : échec à {heureDe(p.echecLe)} ({p.par})
                  {p.plan === 'RetourArriere' && ' · retour arrière'}
                </p>
                <p className="mt-0.5 text-fg-muted">{p.motif}</p>
                <p className="mt-0.5">
                  {p.resolution ? (
                    <>
                      <Wrench className="mr-1 inline size-3.5 text-warn-solid" aria-hidden />
                      Résolution type « {p.resolution} »
                      {p.statutResolution && ` (${LIBELLE_STATUT_RESOLUTION[p.statutResolution].toLowerCase()})`}
                      {p.dureeResolutionMinutes !== null && ` en ${duree(p.dureeResolutionMinutes)}`}.{' '}
                    </>
                  ) : (
                    'Sans résolution type. '
                  )}
                  {p.repriseLe
                    ? `Étape reprise à ${heureDe(p.repriseLe)}${p.repriseReussieLe ? `, réussie à ${heureDe(p.repriseReussieLe)}` : ''}. Arrêt : ${dureeOuTiret(p.arretMinutes)}.`
                    : 'Étape non reprise.'}
                </p>
              </li>
            ))}
          </ul>
        )}
      </Partie>

      <Partie id="rapport-resolutions" titre="3. Résolutions utilisées">
        <Tableau
          entetes={['Résolution', 'Étape', 'Par', 'Corrections', 'Vérification', 'Durée']}
          vide="Aucune résolution exécutée."
        >
          {mep.resolutions.map((x) => (
            <tr key={x.id} className="break-inside-avoid">
              <td className={td}>
                {x.titre}
                <span className="block text-[12px] text-fg-subtle">{LIBELLE_STATUT_RESOLUTION[x.statut]}</span>
              </td>
              <td className={`${td} font-mono text-[12px]`}>{codes.get(x.etapeId) ?? '—'}</td>
              <td className={td}>{x.demarreePar}</td>
              <td className={td}>
                {x.corrections.filter((c) => c.resultat === 'Conforme').length}/{x.corrections.length} conforme(s)
              </td>
              <td className={td}>
                {x.verificationResultat ? LIBELLE_RESULTAT[x.verificationResultat] : '—'}
                {x.verificationObservation && ` : ${x.verificationObservation}`}
              </td>
              <td className={`${td} tnum`}>{dureeOuTiret(minutesEntre(x.demarreeLe, x.termineeLe))}</td>
            </tr>
          ))}
        </Tableau>
      </Partie>

      <Partie id="rapport-tests" titre="4. Tests unitaires">
        <p className="mb-2 text-[12.5px] text-fg-muted">
          {r.testsUnitaires.faits} fait(s), {r.testsUnitaires.nonFaits} non fait(s) · {r.testsUnitaires.verifications}{' '}
          étape(s) terminée(s) par une simple vérification.
        </p>
        <Tableau
          entetes={['Étape', 'Résultat attendu', 'Résultat constaté', 'Exécutant', 'Bilan']}
          vide="Aucun test unitaire."
        >
          {r.testsUnitaires.tests.map((t) => (
            <tr key={t.etapeId} className="break-inside-avoid">
              <td className={td}>
                <span className="font-mono text-[12px] text-fg-subtle">{t.code}</span> {t.action}
              </td>
              <td className={td}>
                <Markdown source={t.resultatAttendu} className="text-[13px] print:text-[11px]" />
              </td>
              <td className={td}>{t.resultatConstate || '—'}</td>
              <td className={td}>{t.executant ?? '—'}</td>
              <td className={td}>
                {t.fait ? (
                  <span className="inline-flex items-center gap-1 font-medium text-ok-fg">
                    <CheckCircle2 className="size-3.5" aria-hidden /> Fait
                  </span>
                ) : (
                  <span className="text-fg-muted">Non fait</span>
                )}
              </td>
            </tr>
          ))}
        </Tableau>
      </Partie>

      <Partie id="rapport-etapes" titre="5. Qui a fait chaque étape">
        <Tableau
          entetes={['Étape', 'État', 'Exécutant', 'Début', 'Fin', 'Durée réelle / prévue']}
          vide="Aucune étape de déploiement."
        >
          {lignesEtapes({ mep, etapes: deploiement })}
        </Tableau>
      </Partie>

      <Partie id="rapport-bilan" titre="6. Bilan">
        {mep.bilan ? (
          <dl className="grid gap-4 text-[13.5px] md:grid-cols-3">
            {(
              [
                ['Ce qui a marché', mep.bilan.ceQuiAMarche],
                ['Incidents', mep.bilan.incidents],
                ['Actions', mep.bilan.actions],
              ] as const
            ).map(([titre, texte]) => (
              <div key={titre}>
                <dt className="font-semibold">{titre}</dt>
                <dd className="mt-1 whitespace-pre-line text-fg-muted">{texte || '—'}</dd>
              </div>
            ))}
          </dl>
        ) : (
          <p className="text-[13px] text-fg-subtle">Aucun bilan rédigé.</p>
        )}
      </Partie>

      <Partie id="rapport-recette" titre="Annexe A. Recette">
        {mep.fichesRecette.length === 0 && <p className="text-[13px] text-fg-subtle">Aucune fiche de recette.</p>}
        {[...mep.fichesRecette]
          .sort((a, b) => a.ordre - b.ordre)
          .map((f) => (
            <div key={f.id} className="mt-3 first:mt-0">
              <h3 className="text-[14px] font-semibold">
                {f.titre}
                {f.systemeId && systemes.has(f.systemeId) && (
                  <span className="font-normal text-fg-subtle"> · {systemes.get(f.systemeId)}</span>
                )}
              </h3>
              <Tableau entetes={['N°', 'Résultat attendu', 'Résultat', 'Observation', 'Par']} vide="Fiche vide.">
                {[...f.etapes]
                  .sort((a, b) => a.ordre - b.ordre)
                  .map((t) => (
                    <tr key={t.id} className="break-inside-avoid">
                      <td className={`${td} tnum`}>{t.ordre}</td>
                      <td className={td}>
                        <Markdown source={t.resultatAttendu} className="text-[13px] print:text-[11px]" />
                      </td>
                      <td className={td}>{t.resultat ? LIBELLE_RESULTAT[t.resultat] : 'Non fait'}</td>
                      <td className={td}>{t.observation || '—'}</td>
                      <td className={td}>
                        {t.par ?? '—'}
                        {t.le && <span className="block text-[12px] text-fg-subtle">{formatDateHeure(t.le)}</span>}
                      </td>
                    </tr>
                  ))}
              </Tableau>
            </div>
          ))}
      </Partie>

      {mep.retourArriere && (
        <Partie id="rapport-retour" titre="Annexe B. Retour arrière">
          <p className="mb-2 text-[13px] text-fg-muted">
            Déclenché le {formatDateHeure(mep.retourArriere.le)}
            {mep.retourArriere.par ? ` par ${mep.retourArriere.par}` : ' (automatiquement)'}
            {mep.retourArriere.motif && ` : ${mep.retourArriere.motif}`}
          </p>
          <Tableau
            entetes={['Étape', 'État', 'Exécutant', 'Début', 'Fin', 'Durée réelle / prévue']}
            vide="Aucune étape de retour arrière."
          >
            {lignesEtapes({ mep, etapes: retour })}
          </Tableau>
        </Partie>
      )}

      <Partie id="rapport-journal" titre={`Annexe ${mep.retourArriere ? 'C' : 'B'}. Journal complet`}>
        <Tableau entetes={['Quand', 'Auteur', 'Type', 'Détail']} vide="Aucun événement.">
          {r.journal.map((ev) => (
            <tr key={ev.id} className="break-inside-avoid">
              <td className={`${td} whitespace-nowrap tnum`}>{formatDateHeure(ev.quand)}</td>
              <td className={td}>{ev.auteur}</td>
              <td className={td}>{LIBELLE_FAMILLE_EVENEMENT[familleEvenement(ev.type)] ?? ev.type}</td>
              <td className={td}>{ev.detail}</td>
            </tr>
          ))}
        </Tableau>
      </Partie>

      <p className="mt-8 text-[12px] text-fg-subtle">
        Rapport figé le {formatDate(r.figeLe)} à {heureDe(r.figeLe)}
        {r.calculeApres
          ? ' : MEP clôturée avant l’existence des rapports de fin, rapport établi à sa première consultation.'
          : ', à la clôture de la MEP.'}{' '}
        Il ne change plus, même si la MEP est modifiée ensuite.
      </p>
    </article>
  );
}

const sansAction = () => Promise.resolve(false);

/** Le graphe lit la MEP dans son contexte : celle du rapport, figée, sans aucune action possible. */
function ContexteRapport({ r }: { r: RapportFin }) {
  const valeur = useMemo(() => ({ mep: r.mep, run: sansAction }), [r.mep]);
  return (
    <MepContext.Provider value={valeur}>
      <Rapport r={r} />
    </MepContext.Provider>
  );
}

/**
 * Rapport de fin d'une MEP clôturée (#61) : la version figée par le serveur à la clôture. Export Excel servi par l'API ;
 * le PDF passe par l'impression du navigateur (feuille de style d'impression soignée : « Enregistrer au format PDF »).
 */
export default function RapportPage() {
  const { id = '' } = useParams();
  const { donnees: rapport, erreur } = useAsync(() => api.rapport(id), [id]);
  return (
    <main className="w-full px-4 py-6 sm:px-6 print:p-0">
      <div className="mx-auto flex max-w-[1040px] flex-wrap items-center justify-between gap-3 print:hidden">
        <div className="flex items-center gap-2">
          <Link
            to={`/meps/${id}`}
            className="grid size-8 place-content-center rounded-lg text-fg-muted hover:bg-surface-2"
            aria-label="Retour à la MEP"
          >
            <ArrowLeft className="size-5" aria-hidden />
          </Link>
          <FileText className="size-5 text-fg-subtle" aria-hidden />
          <span className="text-[15px] font-semibold">Rapport de fin</span>
        </div>
        {rapport && (
          <div className="flex flex-wrap items-center gap-2">
            <Button taille="sm" icone={<Printer className="size-4" aria-hidden />} onClick={() => window.print()}>
              Imprimer
            </Button>
            <Button
              taille="sm"
              icone={<Download className="size-4" aria-hidden />}
              onClick={() => telecharger(api.urlExportRapport(id))}
            >
              Exporter en Excel
            </Button>
            <Button
              taille="sm"
              variante="primaire"
              icone={<Download className="size-4" aria-hidden />}
              title="Ouvre l’impression du navigateur : choisissez « Enregistrer au format PDF »."
              onClick={() => window.print()}
            >
              Télécharger en PDF
            </Button>
          </div>
        )}
      </div>
      {erreur ? (
        <div className="mx-auto mt-6 max-w-[1040px]">
          <Alerte ton="warn" icone={OctagonAlert} role="alert">
            {erreur}
          </Alerte>
        </div>
      ) : (
        <Apparition pret={rapport !== null} libelle="Chargement du rapport…" squelette={<SqueletteRapport />}>
          {rapport && <ContexteRapport r={rapport} />}
        </Apparition>
      )}
    </main>
  );
}
