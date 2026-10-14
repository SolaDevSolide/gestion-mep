import { Check, Clock, HandMetal, History, OctagonAlert, Undo2, Wrench, XCircle } from 'lucide-react';
import { bloqueurs, nomParticipant } from '../../domaine';
import { formatDateHeure, LIBELLE_STATUT_RESOLUTION } from '../../format';
import type { Etape, ExecutionResolution, Mep, ResolutionType } from '../../types';
import { Alerte } from '../../ui/Alerte';
import { Avatar } from '../../ui/Avatar';
import { Button } from '../../ui/Button';
import { StatusPill } from '../../ui/StatusPill';
import { AppelerCarte } from '../AppelerCarte';
import { Bloc } from '../graphe/EspaceEtape';
import { etatCarte } from '../graphe/etatCarte';
import { environnementsEtape } from '../../environnements';
import { PastilleEnvironnement } from '../../ui/PastilleEnvironnement';
import { PuceSysteme, systemesDe } from '../graphe/systemes';

/** Liste compacte d'étapes liées avec leur état (préalables, débloque). */
function ListeEtats({
  etapes,
  vide,
  onSelectionner,
}: {
  etapes: Etape[];
  vide: string;
  onSelectionner: (id: string) => void;
}) {
  if (etapes.length === 0) return <p className="text-[13px] text-fg-subtle">{vide}</p>;
  return (
    <ul className="grid grid-cols-[minmax(0,1fr)] gap-1">
      {etapes.map((p) => {
        const ep = etatCarte(p);
        return (
          <li key={p.id}>
            <button
              type="button"
              onClick={() => onSelectionner(p.id)}
              className="flex w-full items-center justify-between gap-2 rounded-md bg-surface-2/60 px-2 py-1 text-left text-[13px] hover:bg-surface-2"
            >
              <span className="min-w-0 truncate">
                <b className="mr-1.5 font-mono text-xs font-semibold">{p.code}</b>
                {p.action || 'sans libellé'}
              </span>
              <StatusPill taille="sm" className="shrink-0" ton={ep.ton} icone={ep.Icone}>
                {ep.libelle}
              </StatusPill>
            </button>
          </li>
        );
      })}
    </ul>
  );
}

/** Historique déroulable des résolutions terminées d'une étape. */
function HistoriqueResolutions({ historique }: { historique: ExecutionResolution[] }) {
  return (
    <section>
      <h3 className="mb-1.5 flex items-center gap-1.5 text-[11px] font-semibold tracking-[0.06em] text-fg-subtle uppercase">
        <History className="size-3.5" aria-hidden />
        Résolutions déjà appliquées
      </h3>
      <ul className="grid gap-1.5">
        {historique.map((r) => (
          <li key={r.id}>
            <details className="rounded-md border border-line bg-surface-2/40">
              <summary className="flex cursor-pointer flex-wrap items-center gap-2 px-2.5 py-1.5 text-[13px]">
                <span className="font-medium">{r.titre}</span>
                <StatusPill
                  taille="sm"
                  ton={r.statut === 'Reussie' ? 'ok' : 'danger'}
                  icone={r.statut === 'Reussie' ? Check : XCircle}
                >
                  {LIBELLE_STATUT_RESOLUTION[r.statut]}
                </StatusPill>
                <span className="text-xs text-fg-subtle">
                  {r.demarreePar} · {formatDateHeure(r.demarreeLe)}
                </span>
              </summary>
              <ol className="space-y-1 border-t border-line px-2.5 py-1.5 text-xs text-fg-muted">
                {r.corrections.map((c) => (
                  <li key={c.ordre}>
                    <span className="font-mono">Correction {c.ordre}</span> : {c.resultat ?? 'non réalisée'}
                    {c.observation && ` — ${c.observation}`}
                  </li>
                ))}
                <li>
                  <span className="font-mono">Vérification</span> : {r.verificationResultat ?? 'non réalisée'}
                  {r.verificationObservation && ` — ${r.verificationObservation}`}
                </li>
              </ol>
            </details>
          </li>
        ))}
      </ul>
    </section>
  );
}

interface Props {
  mep: Mep;
  etape: Etape;
  /** Résolutions types liées à l'étape. */
  liees: ResolutionType[];
  /** Résolutions déjà terminées sur l'étape. */
  historique: ExecutionResolution[];
  peutPrendreLaMain: boolean;
  onPrendreLaMain: () => void;
  onSelectionner: (id: string) => void;
}

/**
 * Colonne de gauche de l'espace d'une étape : état d'attente, exécutant, personnes à appeler, systèmes,
 * étapes liées et résolutions (prévues, déjà appliquées).
 */
export function EtapeInfos({
  mep,
  etape,
  liees,
  historique,
  peutPrendreLaMain,
  onPrendreLaMain,
  onSelectionner,
}: Props) {
  const statut = etape.disponibilite.statut;
  const nomExec = nomParticipant(mep, etape.executantId);
  const systemes = systemesDe(mep, etape.systemeIds);
  const memePlan = mep.etapes.filter((e) => e.plan === etape.plan);
  const prealables = etape.dependances.map((id) => memePlan.find((e) => e.id === id)).filter((e): e is Etape => !!e);
  const suivants = memePlan.filter((e) => e.dependances.includes(etape.id));
  const attendus = bloqueurs(etape, memePlan);

  return (
    <>
      {statut === 'Attend' && (
        <Alerte ton="wait" icone={Clock} titre="En attente">
          <ul className="mt-1 space-y-1">
            {attendus.length === 0 && <li>{etape.disponibilite.libelle}</li>}
            {attendus.map((b) => (
              <li key={b.id}>
                Bloquée par{' '}
                <button
                  type="button"
                  className="font-mono font-bold underline underline-offset-2 hover:text-accent-text"
                  onClick={() => onSelectionner(b.id)}
                >
                  {b.code}
                </button>
              </li>
            ))}
          </ul>
        </Alerte>
      )}
      {statut === 'SiRetourArriere' && (
        <Alerte ton="later" icone={Undo2}>
          Seulement en cas de retour arrière.
        </Alerte>
      )}
      {statut === 'RetourArriere' && etape.etat !== 'Echec' && (
        <Alerte ton="rollback" icone={OctagonAlert} titre="Retour arrière">
          Ne plus exécuter cette étape.
        </Alerte>
      )}
      {['PlanInvalide', 'EcheanceARenseigner', 'FicheACompleter', 'InstructionsACompleter'].includes(statut) && (
        <Alerte ton="warn" icone={OctagonAlert}>
          {etape.disponibilite.libelle}
        </Alerte>
      )}

      <Bloc titre="Exécutant">
        <div className="flex items-center gap-2.5 text-sm">
          {nomExec ? (
            <Avatar nom={nomExec} taille="sm" />
          ) : (
            <span className="size-6 rounded-full border border-dashed border-line-strong" />
          )}
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
        {peutPrendreLaMain && (
          <Button taille="sm" icone={<HandMetal className="size-4" aria-hidden />} onClick={onPrendreLaMain}>
            Prendre la main
          </Button>
        )}
      </Bloc>

      <AppelerCarte mep={mep} etape={etape} />

      <Bloc titre="Systèmes et environnements">
        {systemes.length > 0 ? (
          <ul className="space-y-1.5">
            {systemes.map((s) => (
              <li key={s.id} className="flex flex-wrap items-center gap-1.5">
                <PuceSysteme nom={s.nom} />
                {environnementsEtape(etape, s).map((e) => (
                  <PastilleEnvironnement key={e.nom} nom={e.nom} production={e.production} retire={e.retire} />
                ))}
              </li>
            ))}
          </ul>
        ) : (
          <span className="text-[13px] text-fg-subtle">Non précisé</span>
        )}
      </Bloc>
      <Bloc titre={`Attend (${prealables.length})`}>
        <ListeEtats etapes={prealables} vide="Aucun préalable." onSelectionner={onSelectionner} />
      </Bloc>
      <Bloc titre={`Débloque (${suivants.length})`}>
        <ListeEtats etapes={suivants} vide="Aucune." onSelectionner={onSelectionner} />
      </Bloc>
      {etape.etat !== 'Echec' && (
        <Bloc titre={`Résolutions prévues (${liees.length})`}>
          {liees.length === 0 ? (
            <p className="text-[13px] text-fg-subtle">Aucune.</p>
          ) : (
            <ul className="grid gap-1">
              {liees.map((r) => (
                <li key={r.id} className="flex items-center gap-1.5 text-[13px]">
                  <Wrench className="size-3.5 shrink-0 text-warn-solid" aria-hidden />
                  {r.titre}
                </li>
              ))}
            </ul>
          )}
        </Bloc>
      )}
      {historique.length > 0 && <HistoriqueResolutions historique={historique} />}
    </>
  );
}
