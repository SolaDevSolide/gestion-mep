// Périodes de gel et conflits sur la fiche d'une MEP (#129) : signalés dès la préparation, Go bloqué pendant un gel sauf
// dérogation (motif obligatoire, accord d'un approbateur de la période, les administrateurs par défaut ; tout au journal).
// Une dérogation tombe si la date de la MEP change ensuite : elle se redemande.
import {
  CalendarClock,
  CalendarDays,
  Check,
  Send,
  ShieldCheck,
  Snowflake,
  TriangleAlert,
  Undo2,
  X,
} from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../../api';
import {
  finDuGel,
  FUSEAU_INSTANCE,
  heureLocale,
  horaireConflit,
  jourAbrege,
  jourLocal,
  plageGel,
} from '../../calendrier';
import { formatDateHeure } from '../../format';
import { useMep } from '../../mepContext';
import type { CalendrierMep, DerogationGel, Mep, OccurrenceGel } from '../../types';
import { Alerte } from '../../ui/Alerte';
import { Button } from '../../ui/Button';
import { Dialog } from '../../ui/Dialog';
import { Field, Textarea } from '../../ui/Input';
import { StatusPill } from '../../ui/StatusPill';

const cleOccurrence = (o: Pick<OccurrenceGel, 'periodeGelId' | 'debut'>) => `${o.periodeGelId}|${Date.parse(o.debut)}`;

/** Dernière demande de dérogation de la MEP pour cette occurrence (null : aucune). */
export function derogationDe(cal: CalendrierMep, o: OccurrenceGel): DerogationGel | null {
  const cle = cleOccurrence(o);
  return (
    [...cal.derogations]
      .reverse()
      .find((d) => cleOccurrence({ periodeGelId: d.periodeGelId, debut: d.occurrence }) === cle) ?? null
  );
}

/** Occurrences à montrer : celles du créneau, puis celles qu'un Go maintenant toucherait, sans doublon. */
export function occurrencesGel(cal: CalendrierMep): { o: OccurrenceGel; auGo: boolean; creneau: boolean }[] {
  const vues = new Map<string, { o: OccurrenceGel; auGo: boolean; creneau: boolean }>();
  for (const o of cal.gels) vues.set(cleOccurrence(o), { o, auGo: false, creneau: true });
  for (const o of cal.gelsAuGo) {
    const deja = vues.get(cleOccurrence(o));
    vues.set(cleOccurrence(o), { o, auGo: true, creneau: deja?.creneau ?? false });
  }
  return [...vues.values()];
}

/** Créneau de la MEP en clair : « ven. 27/11/2026, 21:00 → 23:30 ». */
function creneauMep(cal: CalendrierMep, fuseau: string): string {
  if (!cal.debut) return '';
  const jour = jourLocal(cal.debut, fuseau);
  const date = `${jourAbrege(jour)}/${jour.slice(0, 4)}`;
  if (cal.journeeEntiere) return `${date}, journée entière`;
  return cal.fin
    ? `${date}, ${heureLocale(cal.debut, fuseau)} → ${heureLocale(cal.fin, fuseau)}`
    : `${date}, ${heureLocale(cal.debut, fuseau)}`;
}

/** Description d'une occurrence : plage, portée et motif. */
function porteeGel(o: OccurrenceGel, fuseau: string): string {
  return [
    `du ${plageGel(o, fuseau).replace(' → ', ' au ')}`,
    o.globale ? 'tous les systèmes' : '',
    o.motif.trim() && `motif : ${o.motif.trim()}`,
  ]
    .filter(Boolean)
    .join(' · ');
}

/** Pastille de l'en-tête : la MEP est en préparation et son créneau touche un gel. */
export function PastilleGel({ mep }: { mep: Mep }) {
  const cal = mep.calendrier;
  if (!cal || mep.phase !== 'Preparation' || (cal.gels.length === 0 && cal.gelsAuGo.length === 0)) return null;
  return (
    <StatusPill ton="run" icone={Snowflake} taille="lg">
      Période de gel
    </StatusPill>
  );
}

/** Demande de dérogation : motif obligatoire ; les approbateurs de la période la voient dans « Suivi en direct ». */
export function DemandeDerogationDialog({
  occurrence,
  onOpenChange,
}: {
  occurrence: OccurrenceGel | null;
  onOpenChange: (o: boolean) => void;
}) {
  const { mep, run } = useMep();
  const [motif, setMotif] = useState('');
  const [envoi, setEnvoi] = useState(false);
  async function envoyer() {
    if (!occurrence) return;
    setEnvoi(true);
    const ok = await run(
      () => api.demanderDerogation(mep.id, occurrence.periodeGelId, occurrence.debut, motif.trim()),
      'Dérogation demandée : les approbateurs de la période la voient dans « Suivi en direct ».',
    );
    setEnvoi(false);
    if (ok) {
      setMotif('');
      onOpenChange(false);
    }
  }
  return (
    <Dialog
      open={occurrence !== null}
      onOpenChange={onOpenChange}
      titre="Demander une dérogation au gel"
      description={
        occurrence
          ? `Période de gel « ${occurrence.nom} » (${plageGel(occurrence, occurrence.fuseauHoraire)}).`
          : undefined
      }
      pied={
        <>
          <Button variante="fantome" onClick={() => onOpenChange(false)}>
            Annuler
          </Button>
          <Button
            variante="primaire"
            icone={<Send className="size-4" aria-hidden />}
            chargement={envoi}
            disabled={!motif.trim()}
            onClick={() => void envoyer()}
          >
            Demander la dérogation
          </Button>
        </>
      }
    >
      <ChampsDerogation motif={motif} setMotif={setMotif} decideurs={occurrence?.decideurs} />
    </Dialog>
  );
}

function ChampsDerogation({
  motif,
  setMotif,
  decideurs,
}: {
  motif: string;
  setMotif: (m: string) => void;
  /** Qui peut accorder la dérogation, en clair (« Claire Durand, équipe Comptabilité », « les administrateurs »). */
  decideurs: string | null | undefined;
}) {
  return (
    <div className="space-y-3">
      <Field
        label="Motif de la dérogation (obligatoire)"
        hint="Inscrit au journal de la MEP et repris dans la variable {{gel}} des textes."
      >
        {(id) => (
          <Textarea
            id={id}
            rows={3}
            value={motif}
            onChange={(e) => setMotif(e.target.value)}
            placeholder="ex. Le correctif doit être en production avant l’envoi des factures ; retour arrière testé en préproduction."
          />
        )}
      </Field>
      <p className="text-xs text-fg-subtle">
        {`Peut accorder la dérogation : ${decideurs ?? 'un approbateur de la période'}, depuis « Suivi en direct » ou la fiche de la MEP. Jamais la personne qui demande, ni le pilote, un suppléant ou un responsable technique de la MEP.`}
      </p>
    </div>
  );
}

/** Décision d'un approbateur : accorder (commentaire facultatif) ou refuser (commentaire obligatoire). */
export function DecisionDerogation({
  decider,
}: {
  decider: (accorder: boolean, commentaire: string) => Promise<boolean>;
}) {
  const [commentaire, setCommentaire] = useState('');
  const [envoi, setEnvoi] = useState<'oui' | 'non' | null>(null);
  async function envoyer(accorder: boolean) {
    setEnvoi(accorder ? 'oui' : 'non');
    try {
      if (await decider(accorder, commentaire.trim())) setCommentaire('');
    } finally {
      setEnvoi(null);
    }
  }
  return (
    <div className="space-y-2.5">
      <Field label="Commentaire (facultatif pour accorder, obligatoire pour refuser)">
        {(id) => (
          <Textarea
            id={id}
            rows={2}
            value={commentaire}
            onChange={(e) => setCommentaire(e.target.value)}
            placeholder="Conditions, par exemple : prévenir la comptabilité à 20:45."
          />
        )}
      </Field>
      <div className="flex flex-wrap justify-end gap-2">
        <Button
          variante="danger-doux"
          icone={<X className="size-4" aria-hidden />}
          chargement={envoi === 'non'}
          disabled={!commentaire.trim() || envoi !== null}
          title={commentaire.trim() ? undefined : 'Un refus doit être expliqué : écrivez un commentaire.'}
          onClick={() => void envoyer(false)}
        >
          Refuser
        </Button>
        <Button
          variante="primaire"
          icone={<Check className="size-4" aria-hidden />}
          chargement={envoi === 'oui'}
          disabled={envoi !== null}
          onClick={() => void envoyer(true)}
        >
          Accorder la dérogation
        </Button>
      </div>
    </div>
  );
}

/** État d'une demande : en attente, accordée, refusée (avec qui et quand). */
function EtatDerogation({ d, peutRetirer }: { d: DerogationGel; peutRetirer: boolean }) {
  const { mep, run } = useMep();
  const retirer = peutRetirer && (d.statut === 'Demandee' || d.statut === 'Accordee') && (
    <Button
      taille="sm"
      variante="fantome"
      icone={<Undo2 className="size-4" aria-hidden />}
      onClick={() => void run(() => api.retirerDerogation(mep.id, d.id), 'Demande de dérogation retirée.')}
    >
      Retirer la demande
    </Button>
  );
  if (d.statut === 'Accordee')
    return (
      <Alerte
        ton="ok"
        icone={ShieldCheck}
        titre={`Dérogation accordée par ${d.decideePar ?? 'un approbateur'} le ${formatDateHeure(d.decideeLe)}`}
        action={retirer || undefined}
      >
        {d.commentaire.trim() ? `« ${d.commentaire.trim()} » ` : ''}Valable pour cette occurrence du gel seulement : si
        la date de la MEP change, il faut la redemander.
      </Alerte>
    );
  if (d.statut === 'Refusee')
    return (
      <Alerte
        ton="danger"
        icone={X}
        titre={`Dérogation refusée par ${d.decideePar ?? 'un approbateur'} le ${formatDateHeure(d.decideeLe)}`}
      >
        {d.commentaire.trim() ? `« ${d.commentaire.trim()} »` : ''}
      </Alerte>
    );
  if (d.statut === 'Demandee')
    return (
      <Alerte
        ton="wait"
        icone={Send}
        titre={`Dérogation demandée par ${d.demandeePar ?? 'le pilote'} le ${formatDateHeure(d.demandeeLe)}`}
        action={retirer || undefined}
      >
        « {d.motif} » En attente de la décision d’un approbateur de la période.
      </Alerte>
    );
  if (d.statut === 'Caduque')
    return (
      <Alerte
        ton="warn"
        icone={CalendarClock}
        titre="Dérogation tombée : la date de la MEP a changé"
        action={retirer || undefined}
      >
        Demandée par {d.demandeePar ?? 'le pilote'} le {formatDateHeure(d.demandeeLe)}
        {d.decideePar ? `, accordée par ${d.decideePar}` : ''}. Elle valait pour l’ancien créneau : redemandez-la pour
        la nouvelle date.
      </Alerte>
    );
  return null;
}

/**
 * Encadrés de la préparation : chaque gel du créneau (ou d'un Go donné maintenant) avec sa dérogation, puis chaque conflit
 * avec une autre MEP. `allerIntervention` : ouvre la section où se change la date.
 */
export function BandeauGelConflits({ allerIntervention }: { allerIntervention: () => void }) {
  const { mep, run } = useMep();
  const [demande, setDemande] = useState<OccurrenceGel | null>(null);
  const cal = mep.calendrier;
  if (!cal || mep.estModele || mep.phase === 'Annulee' || mep.phase === 'Cloturee') return null;
  const fuseau = mep.fuseauHoraire || FUSEAU_INSTANCE;
  const preparation = mep.phase === 'Preparation';
  const occurrences = preparation ? occurrencesGel(cal) : [];
  if (occurrences.length === 0 && cal.conflits.length === 0) return null;
  const pilote = mep.droits.estPilote;
  return (
    <div className="space-y-3">
      {occurrences.map(({ o, creneau }) => {
        const d = derogationDe(cal, o);
        const enAttente = d?.statut === 'Demandee';
        const accordee = d?.statut === 'Accordee';
        return (
          <div key={cleOccurrence(o)} className="space-y-2">
            <Alerte
              ton="run"
              icone={Snowflake}
              titre={
                creneau
                  ? `Cette MEP tombe pendant la période de gel « ${o.nom} ».`
                  : `Un Go donné maintenant tomberait dans la période de gel « ${o.nom} ».`
              }
              action={
                pilote && !enAttente && !accordee ? (
                  <span className="flex flex-wrap items-center gap-2">
                    <Button taille="sm" icone={<Send className="size-4" aria-hidden />} onClick={() => setDemande(o)}>
                      Demander une dérogation
                    </Button>
                    {creneau && (
                      <Button
                        taille="sm"
                        icone={<CalendarDays className="size-4" aria-hidden />}
                        onClick={allerIntervention}
                      >
                        Choisir une autre date
                      </Button>
                    )}
                    <span className="text-xs">{finDuGel(o, fuseau)}</span>
                  </span>
                ) : undefined
              }
            >
              {[creneau && cal.debut ? creneauMep(cal, fuseau) : '', porteeGel(o, fuseau)].filter(Boolean).join(' · ')}.{' '}
              {accordee
                ? 'Le Go est permis par la dérogation.'
                : `Le Go sera bloqué sauf dérogation, accordée par ${o.decideurs ?? 'un approbateur de la période'}.`}
            </Alerte>
            {d && <EtatDerogation d={d} peutRetirer={pilote} />}
            {d?.peutDecider && (
              <div className="rounded-lg border border-line bg-surface p-3.5">
                <p className="mb-2 text-sm font-medium">
                  Vous êtes approbateur de cette période de gel : votre décision ?
                </p>
                <DecisionDerogation
                  decider={(accorder, commentaire) =>
                    run(
                      () => api.deciderDerogation(mep.id, d.id, accorder, commentaire),
                      accorder ? 'Dérogation accordée.' : 'Dérogation refusée.',
                    )
                  }
                />
              </div>
            )}
          </div>
        );
      })}
      {cal.conflits.map((c) => (
        <Alerte
          key={c.mepId}
          ton="warn"
          icone={TriangleAlert}
          titre={`Conflit possible : ${c.systemes.join(', ')} ${c.systemes.length > 1 ? 'sont aussi touchés' : 'est aussi touché'} par une autre MEP.`}
        >
          <Link to={`/meps/${c.mepId}`} className="font-medium underline underline-offset-2">
            {c.versionCible.trim() ? `${c.outil} ${c.versionCible}` : c.outil}
          </Link>
          {` : ${jourAbrege(jourLocal(c.debut, fuseau))}, ${horaireConflit(c, fuseau)}. Signalé seulement : rien n’est bloqué.`}
        </Alerte>
      ))}
      <DemandeDerogationDialog occurrence={demande} onOpenChange={(o) => !o && setDemande(null)} />
    </div>
  );
}

/**
 * Bloc du dialogue Go / no-go quand un Go maintenant tomberait dans un gel sans dérogation accordée : la raison, et la
 * demande de dérogation (ou son état). Rien sinon.
 */
export function BlocageGoGel() {
  const { mep, run } = useMep();
  const [motif, setMotif] = useState('');
  const [envoi, setEnvoi] = useState(false);
  const cal = mep.calendrier;
  if (!cal || cal.gelsAuGo.length === 0) return null;
  const fuseau = mep.fuseauHoraire || FUSEAU_INSTANCE;
  async function demander(o: OccurrenceGel) {
    setEnvoi(true);
    const ok = await run(
      () => api.demanderDerogation(mep.id, o.periodeGelId, o.debut, motif.trim()),
      'Dérogation demandée : le Go sera possible une fois accordée.',
    );
    setEnvoi(false);
    if (ok) setMotif('');
  }
  return (
    <div className="space-y-3">
      {cal.gelsAuGo.map((o) => {
        const d = derogationDe(cal, o);
        return (
          <div key={cleOccurrence(o)} className="space-y-2">
            <Alerte ton="run" icone={Snowflake} titre={`Go bloqué : période de gel « ${o.nom} »`}>
              {porteeGel(o, fuseau)}. Demandez une dérogation (accordée par{' '}
              {o.decideurs ?? 'un approbateur de la période'}), ou changez la date.
            </Alerte>
            {d?.statut === 'Demandee' ? (
              <EtatDerogation d={d} peutRetirer={false} />
            ) : (
              <div className="rounded-lg border border-line p-3.5">
                {(d?.statut === 'Refusee' || d?.statut === 'Caduque') && <EtatDerogation d={d} peutRetirer={false} />}
                <ChampsDerogation motif={motif} setMotif={setMotif} decideurs={o.decideurs} />
                <div className="mt-3 flex justify-end">
                  <Button
                    variante="primaire"
                    icone={<Send className="size-4" aria-hidden />}
                    chargement={envoi}
                    disabled={!motif.trim()}
                    onClick={() => void demander(o)}
                  >
                    Demander la dérogation
                  </Button>
                </div>
              </div>
            )}
          </div>
        );
      })}
    </div>
  );
}

/** Raison qui grise le bouton Go (null : rien à voir avec un gel). */
export function raisonGoBloque(mep: Mep): string | null {
  const o = mep.calendrier?.gelsAuGo[0];
  return o ? `Bloqué : période de gel « ${o.nom} »` : null;
}
