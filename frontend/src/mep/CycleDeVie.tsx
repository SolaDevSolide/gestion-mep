import {
  AlertTriangle,
  ArrowRightLeft,
  Ban,
  CalendarClock,
  Ellipsis,
  FileText,
  History,
  LifeBuoy,
  OctagonAlert,
  Plus,
  Trash2,
  Wrench,
  XCircle,
  type LucideIcon,
} from 'lucide-react';
import { useContext, useEffect, useState, type ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../api';
import { AuthContext } from '../auth';
import { etapesDuPlan, nomParticipant } from '../domaine';
import { formatDate, formatJourLong, LIBELLE_CRITICITE, LIBELLE_ETAT } from '../format';
import { useMep } from '../mepContext';
import { useAutresPresents } from '../presence';
import type { Criticite, Etape, Mep, CompteAnnuaire } from '../types';
import { Alerte } from '../ui/Alerte';
import { BoutonIcone, Button, type TailleBouton } from '../ui/Button';
import { Checkbox } from '../ui/Checkbox';
import { cn } from '../ui/cn';
import { Dialog } from '../ui/Dialog';
import { Field, Input, Select, Textarea } from '../ui/Input';
import { Menu, MenuContent, MenuItem, MenuLabel, MenuSeparator, MenuTrigger } from '../ui/Menu';
import { SqueletteLignes } from '../ui/Squelette';
import { useToast } from '../ui/Toast';
import { estSimplifiee } from './typeMep';

/*
 * Sorties du cycle de vie d'une MEP (#93) : menu « ⋯ Plus d'actions » (reporter, annuler, passer le pilotage, pilotage de
 * secours, supprimer), bandeaux visibles de tous (annulée, reportée, pilotage de secours), reprise de la préparation,
 * retour en exécution depuis la recette, étape corrective et clôture en échec.
 */

/** Nom de la MEP à saisir pour confirmer sa suppression : outil, puis version cible (comme `Regles.NomComplet`). */
export const nomComplet = (mep: Pick<Mep, 'outil' | 'versionCible'>): string =>
  [mep.outil.trim(), mep.versionCible.trim()].filter(Boolean).join(' ');

/** De l'exécution au retour arrière : le pilotage se passe et une personne s'ajoute (comme `Regles.EstApresGoEnCours`). */
export const estApresGoEnCours = (mep: Pick<Mep, 'phase'>): boolean =>
  mep.phase === 'Execution' || mep.phase === 'Recette' || mep.phase === 'RetourArriere';

/** Raison pour laquelle le retour arrière est impossible (comme `Regles.RetourArriereImpossible`), ou null. */
export function retourArriereImpossible(mep: Pick<Mep, 'retourArriereActive' | 'nonRetourFranchi'>): string | null {
  if (!mep.retourArriereActive) return 'Cette MEP n’a pas de plan de retour arrière.';
  if (mep.nonRetourFranchi) return `Point de non-retour franchi (${mep.nonRetourFranchi}).`;
  return null;
}

/** Étapes du déploiement exécutées en production (terminées ou démarrées). */
export const etapesExecutees = (mep: Mep): Etape[] =>
  etapesDuPlan(mep, 'Deploiement').filter((e) => e.etat !== 'AFaire' || e.debutReel !== null);

const heure = (iso: string) => new Date(iso).toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' });

/** « le 19/10/2026 à 16:02 ». */
const leA = (iso: string) => `le ${formatDate(iso)} à ${heure(iso)}`;

/* ------------------------------------------------------------------ Menu « ⋯ » */

function ItemMenu({
  icone: Icone,
  libelle,
  description,
  danger,
  disabled,
  onSelect,
}: {
  icone: LucideIcon;
  libelle: string;
  description: string;
  danger?: boolean;
  disabled?: boolean;
  onSelect: () => void;
}) {
  return (
    <MenuItem disabled={disabled} onSelect={onSelect} className="items-start">
      <Icone className={cn('mt-0.5 size-4 shrink-0', danger ? 'text-danger-fg' : 'text-fg-muted')} aria-hidden />
      <span className="min-w-0">
        <span className={cn('block', danger && 'text-danger-fg')}>{libelle}</span>
        <span className="block text-xs text-fg-subtle">{description}</span>
      </span>
    </MenuItem>
  );
}

type DialogueCycle = 'reporter' | 'annuler' | 'pilotage' | 'supprimer' | null;

/**
 * Menu « ⋯ Plus d'actions » de l'en-tête, pour les actions rares. Il n'apparaît que s'il propose au moins une action :
 * reporter (pilote, préparation), annuler (pilote ou administrateur ; grisé après un Go tant que la MEP n'est pas revenue en
 * préparation), passer le pilotage (pilote, après le Go), pilotage de secours (administrateur qui ne pilote pas, après le
 * Go) et, pour un administrateur, la suppression définitive (grisée après un Go).
 */
export function MenuPlusActions() {
  const { mep } = useMep();
  const [dialogue, setDialogue] = useState<DialogueCycle>(null);
  const d = mep.droits;
  const termine = mep.phase === 'Cloturee' || mep.phase === 'Annulee';
  const voitAnnuler = (d.estPilote || d.estAdmin) && !mep.estModele && !termine;
  const passation = d.peutChangerPilote && d.estPilote;
  const secours = d.peutChangerPilote && !d.estPilote;
  const rien = !d.peutReporter && !voitAnnuler && !d.peutChangerPilote && !d.estAdmin;
  if (rien) return null;
  const ouvrir = (x: DialogueCycle) => () => setDialogue(x);
  const premiers = d.peutReporter || voitAnnuler || passation || secours;
  return (
    <>
      <Menu>
        <MenuTrigger asChild>
          <BoutonIcone label="Plus d’actions" taille="sm" variante="secondaire">
            <Ellipsis className="size-4" aria-hidden />
          </BoutonIcone>
        </MenuTrigger>
        <MenuContent className="w-72">
          {d.peutReporter && (
            <ItemMenu
              icone={CalendarClock}
              libelle={mep.report ? 'Modifier le report…' : 'Reporter la MEP…'}
              description="Motif et date visée ; la préparation continue"
              onSelect={ouvrir('reporter')}
            />
          )}
          {voitAnnuler && (
            <ItemMenu
              icone={Ban}
              libelle="Annuler la MEP…"
              description={
                d.peutAnnuler ? 'Lecture seule, journal conservé, duplicable' : 'Revenir d’abord en préparation'
              }
              danger
              disabled={!d.peutAnnuler}
              onSelect={ouvrir('annuler')}
            />
          )}
          {passation && (
            <ItemMenu
              icone={ArrowRightLeft}
              libelle="Passer le pilotage…"
              description="Confier la MEP à une autre personne"
              onSelect={ouvrir('pilotage')}
            />
          )}
          {secours && (
            <ItemMenu
              icone={LifeBuoy}
              libelle="Pilotage de secours…"
              description="Prendre le pilotage ou désigner un pilote"
              onSelect={ouvrir('pilotage')}
            />
          )}
          {d.estAdmin && (
            <>
              {premiers && <MenuSeparator />}
              <MenuLabel>Administration</MenuLabel>
              <ItemMenu
                icone={Trash2}
                libelle="Supprimer définitivement…"
                description={
                  d.peutSupprimer
                    ? `${mep.estModele ? 'Modèle' : 'MEP jamais lancée'} ; tracé au journal d’administration`
                    : 'Impossible après un Go : annuler plutôt'
                }
                danger
                disabled={!d.peutSupprimer}
                onSelect={ouvrir('supprimer')}
              />
            </>
          )}
        </MenuContent>
      </Menu>
      <ReporterDialog open={dialogue === 'reporter'} onOpenChange={(o) => !o && setDialogue(null)} />
      <AnnulerDialog open={dialogue === 'annuler'} onOpenChange={(o) => !o && setDialogue(null)} />
      <PilotageDialog open={dialogue === 'pilotage'} onOpenChange={(o) => !o && setDialogue(null)} />
      <SuppressionDialog open={dialogue === 'supprimer'} onOpenChange={(o) => !o && setDialogue(null)} />
    </>
  );
}

/* ------------------------------------------------------------------ Annuler */

interface DialogueProps {
  open: boolean;
  onOpenChange: (o: boolean) => void;
}

export function AnnulerDialog({ open, onOpenChange }: DialogueProps) {
  const { mep, run } = useMep();
  const [motif, setMotif] = useState('');
  const [etat, setEtat] = useState('');
  useEffect(() => {
    if (open) {
      setMotif('');
      setEtat('');
    }
  }, [open]);
  const go = mep.premierGo;
  const executees = go ? etapesExecutees(mep) : [];
  const valide = motif.trim() !== '' && (!go || etat.trim() !== '');
  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      taille="md"
      titre={`Annuler la MEP « ${nomComplet(mep) || 'sans nom'} » ?`}
      description="Elle passe en lecture seule et reste dans la liste, la recherche et l’historique des systèmes, avec son journal. Vous pourrez la dupliquer pour la reprogrammer."
      pied={
        <>
          <Button variante="fantome" onClick={() => onOpenChange(false)}>
            Garder la MEP
          </Button>
          <Button
            variante="danger"
            icone={<Ban className="size-4" aria-hidden />}
            disabled={!valide}
            action={async () => {
              const ok = await run(
                () => api.annuler(mep.id, { motif: motif.trim(), ...(go ? { etatProduction: etat.trim() } : {}) }),
                'MEP annulée : elle est en lecture seule.',
              );
              if (ok) onOpenChange(false);
            }}
          >
            Annuler la MEP
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        {go && (
          <Alerte
            ton="warn"
            icone={AlertTriangle}
            titre={
              executees.length > 0
                ? `Go donné ${leA(go)} : ${executees.length} étape${executees.length > 1 ? 's ont' : ' a'} été exécutée${executees.length > 1 ? 's' : ''} en production`
                : `Go donné ${leA(go)} : aucune étape n’a été exécutée`
            }
          >
            {executees.length > 0 && (
              <>
                <ul className="mt-1 list-disc space-y-0.5 pl-5">
                  {executees.map((e) => (
                    <li key={e.id}>
                      <span className="font-mono">{e.code}</span> {e.action}
                    </li>
                  ))}
                </ul>
                <p className="mt-1">L’annulation ne les défait pas.</p>
              </>
            )}
          </Alerte>
        )}
        <Field label="Motif (obligatoire)">
          {(id) => (
            <Textarea
              id={id}
              rows={3}
              value={motif}
              onChange={(e) => setMotif(e.target.value)}
              placeholder="ex. Projet suspendu par la direction : MEP à reprogrammer."
              autoFocus
            />
          )}
        </Field>
        {go && (
          <Field label="État laissé en production (obligatoire après un Go)">
            {(id) => (
              <Textarea
                id={id}
                rows={3}
                value={etat}
                onChange={(e) => setEtat(e.target.value)}
                placeholder="ex. Index en place, sans effet sur la version en service. Sauvegardes conservées 30 jours."
              />
            )}
          </Field>
        )}
      </div>
    </Dialog>
  );
}

/* ------------------------------------------------------------------ Reporter */

export function ReporterDialog({ open, onOpenChange }: DialogueProps) {
  const { mep, run } = useMep();
  const [motif, setMotif] = useState('');
  const [date, setDate] = useState('');
  useEffect(() => {
    if (open) {
      setMotif(mep.report?.motif ?? '');
      setDate(mep.report?.dateVisee ?? '');
    }
  }, [open, mep.report]);
  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      taille="md"
      titre={`${mep.report ? 'Modifier le report de' : 'Reporter'} la MEP « ${nomComplet(mep) || 'sans nom'} »`}
      description="La MEP reste en préparation : le plan, les personnes et les approbations sont conservés. Le report est visible de tous et tracé au journal."
      pied={
        <>
          <Button variante="fantome" onClick={() => onOpenChange(false)}>
            Fermer
          </Button>
          <Button
            variante="primaire"
            icone={<CalendarClock className="size-4" aria-hidden />}
            disabled={!motif.trim()}
            action={async () => {
              const ok = await run(
                () => api.reporter(mep.id, { motif: motif.trim(), dateVisee: date || null }),
                'MEP reportée : la préparation reste ouverte.',
              );
              if (ok) onOpenChange(false);
            }}
          >
            Reporter la MEP
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <Field label="Motif (obligatoire)">
          {(id) => (
            <Textarea
              id={id}
              rows={3}
              value={motif}
              onChange={(e) => setMotif(e.target.value)}
              placeholder="ex. Environnement de préproduction indisponible jusqu’à la fin du mois."
              autoFocus
            />
          )}
        </Field>
        <ChampDateVisee valeur={date} onChange={setDate} />
      </div>
    </Dialog>
  );
}

/** Date visée d'un report : facultative, indicative. */
export function ChampDateVisee({ valeur, onChange }: { valeur: string; onChange: (v: string) => void }) {
  return (
    <Field
      label="Date visée (facultative)"
      hint="Indicative : le Go reste la décision du pilote. Sans date, la MEP est « reportée sans date »."
    >
      {(id) => <Input id={id} type="date" value={valeur} onChange={(e) => onChange(e.target.value)} />}
    </Field>
  );
}

/** « Reprendre la préparation » : lève le report (le Go le lève aussi). */
export function BoutonReprendrePreparation({ taille = 'sm' }: { taille?: TailleBouton }) {
  const { mep, run } = useMep();
  return (
    <Button
      taille={taille}
      icone={<History className="size-4" aria-hidden />}
      action={() => run(() => api.reprendrePreparation(mep.id), 'Report levé : la préparation reprend.')}
    >
      Reprendre la préparation
    </Button>
  );
}

/* ------------------------------------------------------------------ Pilotage */

/**
 * Passer le pilotage (pilote, suppléant) ou pilotage de secours (administrateur qui ne pilote pas) : prendre le pilotage
 * soi-même ou désigner une personne de la MEP ou un compte ; l'ancien pilote reste suppléant par défaut ; motif obligatoire.
 */
export function PilotageDialog({ open, onOpenChange }: DialogueProps) {
  const { mep, run } = useMep();
  const moi = useContext(AuthContext)?.utilisateur ?? null;
  const secours = !mep.droits.estPilote;
  const jeSuisPilote = mep.droits.participantId !== null && mep.droits.participantId === mep.piloteId;
  const [choix, setChoix] = useState<'moi' | 'autre'>(jeSuisPilote ? 'autre' : 'moi');
  const [cible, setCible] = useState('');
  const [ancienSuppleant, setAncienSuppleant] = useState(true);
  const [gardes, setGardes] = useState<string[]>([]);
  const [motif, setMotif] = useState('');
  const [comptes, setComptes] = useState<CompteAnnuaire[] | null>(null);
  const [erreurComptes, setErreurComptes] = useState('');
  const simplifiee = estSimplifiee(mep);
  const ancien = nomParticipant(mep, mep.piloteId);
  const suppleants = simplifiee ? [] : mep.suppleantIds;

  useEffect(() => {
    if (!open) return;
    setChoix(jeSuisPilote ? 'autre' : 'moi');
    setCible('');
    setAncienSuppleant(true);
    setGardes(mep.suppleantIds);
    setMotif('');
    // eslint-disable-next-line react-hooks/exhaustive-deps -- remis à zéro à l'ouverture seulement
  }, [open]);

  // Comptes proposés (hors MEP) : lus à la première demande.
  useEffect(() => {
    if (!open || choix !== 'autre' || comptes !== null) return;
    let annule = false;
    api
      .utilisateurs()
      .then((l) => {
        if (!annule) setComptes(l);
      })
      .catch((e: unknown) => {
        if (!annule) setErreurComptes(e instanceof Error ? e.message : 'Comptes indisponibles.');
      });
    return () => {
      annule = true;
    };
  }, [open, choix, comptes]);

  const personnes = mep.participants.filter((p) => p.utilisateurId !== null && p.id !== mep.piloteId);
  const dansLaMep = new Set(mep.participants.map((p) => p.utilisateurId));
  const autresComptes = (comptes ?? []).filter((c) => c.actif && !dansLaMep.has(c.id));
  const monParticipant = mep.participants.find((p) => p.utilisateurId === moi?.id) ?? null;

  const nouveau =
    choix === 'moi'
      ? monParticipant
        ? { participantId: monParticipant.id }
        : moi
          ? { utilisateurId: moi.id }
          : null
      : cible.startsWith('p:')
        ? { participantId: cible.slice(2) }
        : cible.startsWith('u:')
          ? { utilisateurId: cible.slice(2) }
          : null;
  const nouveauParticipant = nouveau && 'participantId' in nouveau ? nouveau.participantId : null;
  const valide = nouveau !== null && motif.trim() !== '';
  const libelleBouton = secours
    ? choix === 'moi'
      ? 'Prendre le pilotage'
      : 'Confier le pilotage'
    : choix === 'moi'
      ? 'Prendre le pilotage'
      : 'Passer le pilotage';

  const option = (valeur: 'moi' | 'autre', titre: string, description: string) => (
    <label
      className={cn(
        'grid cursor-pointer grid-cols-[auto_1fr] items-start gap-x-3 rounded-lg border px-3.5 py-3 transition-colors duration-150',
        choix === valeur ? 'border-accent bg-accent-soft/60' : 'border-line hover:bg-surface-2',
      )}
    >
      <input
        type="radio"
        name="choix-pilote"
        className="row-span-2 mt-1 accent-[var(--accent)]"
        checked={choix === valeur}
        onChange={() => setChoix(valeur)}
      />
      <span className="text-sm font-medium">{titre}</span>
      <span className="text-xs text-fg-subtle">{description}</span>
    </label>
  );

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      taille="md"
      titre={secours ? 'Pilotage de secours' : 'Passer le pilotage'}
      description={
        secours
          ? 'À utiliser quand le pilote et les suppléants sont injoignables. Le changement est tracé au journal de la MEP et au journal d’administration, et un bandeau le signale à tous.'
          : 'Le nouveau pilote prend la décision des phases et des étapes. Le changement est tracé au journal de la MEP.'
      }
      pied={
        <>
          <Button variante="fantome" onClick={() => onOpenChange(false)}>
            Annuler
          </Button>
          <Button
            variante="primaire"
            icone={
              secours ? <LifeBuoy className="size-4" aria-hidden /> : <ArrowRightLeft className="size-4" aria-hidden />
            }
            disabled={!valide}
            action={async () => {
              if (!nouveau) return;
              const ok = await run(
                () =>
                  api.changerPilote(mep.id, {
                    ...nouveau,
                    motif: motif.trim(),
                    ancienPiloteSuppleant: ancienSuppleant,
                    ...(simplifiee ? {} : { suppleantsGardes: gardes }),
                  }),
                secours ? 'Pilotage de secours enregistré.' : 'Pilotage passé.',
              );
              if (ok) onOpenChange(false);
            }}
          >
            {libelleBouton}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <div className="space-y-2" role="radiogroup" aria-label="Nouveau pilote">
          {!jeSuisPilote &&
            option(
              'moi',
              'Je prends le pilotage',
              monParticipant
                ? `${moi?.nomAffiche ?? 'Vous'} devient pilote.`
                : `${moi?.nomAffiche ?? 'Vous'} devient pilote et est ajouté(e) aux personnes de la MEP.`,
            )}
          {option(
            'autre',
            'Désigner un autre pilote',
            'Une personne de la MEP ou un compte, ajouté à la MEP s’il n’en fait pas partie.',
          )}
        </div>
        {choix === 'autre' && (
          <Field label="Nouveau pilote">
            {(id) =>
              comptes === null && !erreurComptes ? (
                <SqueletteLignes lignes={1} />
              ) : (
                <Select id={id} value={cible} onChange={(e) => setCible(e.target.value)}>
                  <option value="">Choisir…</option>
                  <optgroup label="Personnes de la MEP">
                    {personnes
                      .filter((p) => p.utilisateurId !== moi?.id || !jeSuisPilote)
                      .map((p) => (
                        <option key={p.id} value={`p:${p.id}`}>
                          {p.nom}
                          {p.role ? ` · ${p.role}` : ''}
                        </option>
                      ))}
                  </optgroup>
                  {autresComptes.length > 0 && (
                    <optgroup label="Autres comptes">
                      {autresComptes.map((c) => (
                        <option key={c.id} value={`u:${c.id}`}>
                          {c.nomAffiche}
                        </option>
                      ))}
                    </optgroup>
                  )}
                </Select>
              )
            }
          </Field>
        )}
        {erreurComptes && (
          <Alerte ton="warn" icone={OctagonAlert}>
            {erreurComptes} Seules les personnes de la MEP sont proposées.
          </Alerte>
        )}
        {!simplifiee && ancien && (
          <div className="space-y-2.5">
            <Checkbox
              checked={ancienSuppleant}
              onChange={setAncienSuppleant}
              label={`${ancien} reste suppléant`}
              description="Il retrouve les droits de suppléant dès son retour."
            />
            {suppleants
              .filter((s) => s !== nouveauParticipant)
              .map((s) => (
                <Checkbox
                  key={s}
                  checked={gardes.includes(s)}
                  onChange={(v) => setGardes((l) => (v ? [...l, s] : l.filter((x) => x !== s)))}
                  label={`${nomParticipant(mep, s) ?? 'Suppléant'} reste suppléant`}
                />
              ))}
          </div>
        )}
        <Field label="Motif (obligatoire)">
          {(id) => (
            <Textarea
              id={id}
              rows={3}
              value={motif}
              onChange={(e) => setMotif(e.target.value)}
              placeholder={
                secours
                  ? 'ex. Pilote et suppléants injoignables depuis 01:30 ; décision nécessaire avant l’échéance.'
                  : 'ex. Relève de nuit.'
              }
            />
          )}
        </Field>
      </div>
    </Dialog>
  );
}

/* ------------------------------------------------------------------ Supprimer */

/** Suppression définitive (administrateur, MEP jamais lancée ou modèle) : motif et nom saisi pour confirmer. */
export function SuppressionDialog({ open, onOpenChange }: DialogueProps) {
  const { mep } = useMep();
  const toast = useToast();
  const navigate = useNavigate();
  const [motif, setMotif] = useState('');
  const [nom, setNom] = useState('');
  const [erreur, setErreur] = useState('');
  useEffect(() => {
    if (open) {
      setMotif('');
      setNom('');
      setErreur('');
    }
  }, [open]);
  const attendu = nomComplet(mep);
  const etapes = etapesDuPlan(mep, 'Deploiement').length;
  const pieces = mep.piecesJointes.length;
  const ses = (n: number, s: string, p: string) => (n === 0 ? `ses ${p}` : n === 1 ? `son ${s}` : `ses ${n} ${p}`);
  const valide = motif.trim() !== '' && nom.trim() === attendu;
  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      taille="md"
      titre={`Supprimer définitivement ${mep.estModele ? 'le modèle' : 'la MEP'} ?`}
      description={`${mep.estModele ? 'Le modèle' : 'La MEP'}, son journal, ${ses(etapes, 'étape', 'étapes')} et ${ses(pieces, 'pièce jointe', 'pièces jointes')} sont effacés. Seule une ligne reste au journal d’administration.`}
      pied={
        <>
          <Button variante="fantome" onClick={() => onOpenChange(false)}>
            Annuler
          </Button>
          <Button
            variante="danger"
            icone={<Trash2 className="size-4" aria-hidden />}
            disabled={!valide}
            action={async () => {
              setErreur('');
              try {
                await api.supprimerMep(mep.id, { motif: motif.trim(), nom: nom.trim() });
                onOpenChange(false);
                toast.succes(`${mep.estModele ? 'Modèle supprimé' : 'MEP supprimée'} : « ${attendu} ».`);
                void navigate('/');
              } catch (e) {
                setErreur(e instanceof Error ? e.message : 'Suppression impossible.');
              }
            }}
          >
            Supprimer définitivement
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <Alerte ton="danger" icone={AlertTriangle} titre="Cette action est irréversible">
          {mep.estModele
            ? 'Les MEP déjà créées depuis ce modèle ne sont pas touchées.'
            : 'Pour arrêter une MEP en gardant sa trace, utilisez « Annuler la MEP ».'}
        </Alerte>
        <Field label="Motif (obligatoire)">
          {(id) => (
            <Textarea
              id={id}
              rows={2}
              value={motif}
              onChange={(e) => setMotif(e.target.value)}
              placeholder="ex. MEP d’essai créée pendant une formation, jamais lancée."
              autoFocus
            />
          )}
        </Field>
        <Field
          label={
            <>
              Pour confirmer, saisissez le nom {mep.estModele ? 'du modèle' : 'de la MEP'} :{' '}
              <span className="font-mono">{attendu}</span>
            </>
          }
          erreur={erreur || undefined}
        >
          {(id) => <Input id={id} value={nom} autoComplete="off" onChange={(e) => setNom(e.target.value)} />}
        </Field>
      </div>
    </Dialog>
  );
}

/* ------------------------------------------------------------------ Bandeaux */

function Bandeau({
  ton,
  icone,
  titre,
  children,
  action,
}: {
  ton: 'later' | 'wait' | 'warn';
  icone: LucideIcon;
  titre: ReactNode;
  children: ReactNode;
  action?: ReactNode;
}) {
  return (
    <div className="mx-6 mt-4">
      <Alerte ton={ton} icone={icone} titre={titre} role="status" action={action}>
        {children}
      </Alerte>
    </div>
  );
}

/**
 * Bandeaux visibles de tous, sur chaque onglet : MEP annulée (motif, état laissé en production), reportée (motif, date
 * visée) et pilotage de secours en cours. Un administrateur qui ne pilote pas voit aussi, après le Go, un encadré quand ni le
 * pilote ni les suppléants n'ont la MEP ouverte.
 */
export function BandeauxCycleDeVie() {
  const { mep } = useMep();
  const autres = useAutresPresents();
  const [secours, setSecours] = useState(false);
  const a = mep.annulation;
  const r = mep.report;
  const s = mep.pilotageSecours;
  const comptesPilotes = new Set(
    [mep.piloteId, ...mep.suppleantIds]
      .map((id) => mep.participants.find((p) => p.id === id)?.utilisateurId)
      .filter((x): x is string => !!x),
  );
  const personneNeSuit =
    mep.droits.peutChangerPilote &&
    !mep.droits.estPilote &&
    !s &&
    comptesPilotes.size > 0 &&
    !autres.some((p) => comptesPilotes.has(p.utilisateurId));
  return (
    <>
      {mep.phase === 'Annulee' && a && (
        <Bandeau ton="later" icone={Ban} titre={`Annulée${a.par ? ` par ${a.par}` : ''} ${leA(a.le)}`}>
          <p>Motif : {a.motif}</p>
          {a.etatProduction && <p>État laissé en production : {a.etatProduction}</p>}
        </Bandeau>
      )}
      {mep.phase === 'Preparation' && r && (
        <Bandeau
          ton="wait"
          icone={CalendarClock}
          titre={`Reportée${r.par ? ` par ${r.par}` : ''} ${leA(r.le)} · ${r.dateVisee ? `date visée : ${formatJourLong(r.dateVisee)}` : 'sans date visée'}`}
        >
          <p>Motif : {r.motif}</p>
          <p className="text-xs opacity-80">La préparation reste ouverte. Le Go lève le report.</p>
        </Bandeau>
      )}
      {estApresGoEnCours(mep) && s && (
        <Bandeau ton="warn" icone={LifeBuoy} titre={`Pilotage de secours depuis ${heure(s.le)} (${formatDate(s.le)})`}>
          <p>
            {s.par ?? 'Un administrateur'} (administrateur) a{' '}
            {s.par === s.nouveauPilote ? 'pris le pilotage' : `confié le pilotage à ${s.nouveauPilote ?? '—'}`}
            {s.ancienPilote ? ` à la place de ${s.ancienPilote}` : ''}.{suppleantsTexte(mep)}
          </p>
          <p>Motif : {s.motif}</p>
        </Bandeau>
      )}
      {personneNeSuit && (
        <Bandeau
          ton="warn"
          icone={LifeBuoy}
          titre="Ni le pilote ni les suppléants ne suivent la MEP"
          action={
            <Button taille="sm" icone={<LifeBuoy className="size-4" aria-hidden />} onClick={() => setSecours(true)}>
              Pilotage de secours…
            </Button>
          }
        >
          <p>Aucun d’eux n’a la MEP ouverte en ce moment.</p>
          <p className="text-xs opacity-80">Visible des administrateurs seulement.</p>
        </Bandeau>
      )}
      <PilotageDialog open={secours} onOpenChange={setSecours} />
    </>
  );
}

function suppleantsTexte(mep: Mep): string {
  const noms = (estSimplifiee(mep) ? [] : mep.suppleantIds).map((id) => nomParticipant(mep, id)).filter(Boolean);
  if (noms.length === 0) return '';
  if (noms.length === 1) return ` ${noms[0]} reste suppléant.`;
  return ` ${noms.slice(0, -1).join(', ')} et ${noms.at(-1)} restent suppléants.`;
}

/* ------------------------------------------------------------------ Corriger en avant */

/** « Revenir en exécution pour corriger » : depuis la recette, motif obligatoire ; les tests en échec sont à refaire. */
export function BoutonRevenirExecution({ taille = 'sm' }: { taille?: TailleBouton }) {
  const { mep, run } = useMep();
  const [ouvert, setOuvert] = useState(false);
  const [motif, setMotif] = useState('');
  const suivant = prochainCode(mep);
  return (
    <>
      <Button taille={taille} icone={<Wrench className="size-4" aria-hidden />} onClick={() => setOuvert(true)}>
        Revenir en exécution pour corriger
      </Button>
      <Dialog
        open={ouvert}
        onOpenChange={(o) => {
          setOuvert(o);
          if (!o) setMotif('');
        }}
        taille="md"
        titre="Revenir en exécution pour corriger ?"
        description="La recette s’interrompt. Vous pourrez ajouter des étapes correctives à la suite du plan, puis repasser en recette ; les tests en échec seront à refaire."
        pied={
          <>
            <Button variante="fantome" onClick={() => setOuvert(false)}>
              Annuler
            </Button>
            <Button
              variante="primaire"
              icone={<Wrench className="size-4" aria-hidden />}
              disabled={!motif.trim()}
              action={async () => {
                const ok = await run(
                  () => api.changerPhase(mep.id, 'Execution', motif.trim()),
                  'MEP revenue en exécution : ajoutez les étapes correctives.',
                );
                if (ok) {
                  setOuvert(false);
                  setMotif('');
                }
              }}
            >
              Revenir en exécution
            </Button>
          </>
        }
      >
        <div className="space-y-4">
          <Field label="Motif (obligatoire)">
            {(id) => (
              <Textarea
                id={id}
                rows={3}
                value={motif}
                onChange={(e) => setMotif(e.target.value)}
                placeholder="ex. Le rôle gestionnaire n’est pas propagé dans l’annuaire : correction par script."
                autoFocus
              />
            )}
          </Field>
          <Alerte ton="run" icone={Wrench}>
            Les étapes ajoutées prennent les codes suivants ({suivant}…), portent la mention « Ajoutée après le Go » et
            sont signalées dans le rapport de fin.
          </Alerte>
        </div>
      </Dialog>
    </>
  );
}

/** Code de la prochaine étape de déploiement (E0006 après E0005). */
export function prochainCode(mep: Mep): string {
  const max = etapesDuPlan(mep, 'Deploiement').reduce((m, e) => Math.max(m, Number(e.code.slice(1)) || 0), 0);
  return `E${String(max + 1).padStart(4, '0')}`;
}

const VIDE_CORRECTIVE = {
  action: '',
  systemeIds: [] as string[],
  instructions: '',
  resultatAttendu: '',
  duree: '',
  criticite: 'Moderee' as Criticite,
};

/** « Ajouter une étape corrective » (pilote ou suppléant, en exécution) : fiche complète, à la suite du plan. */
export function BoutonEtapeCorrective({ taille = 'sm' }: { taille?: TailleBouton }) {
  const { mep, run } = useMep();
  const [ouvert, setOuvert] = useState(false);
  const [f, setF] = useState(VIDE_CORRECTIVE);
  const suivant = prochainCode(mep);
  const valide = f.action.trim() && f.systemeIds.length > 0 && f.instructions.trim() && f.resultatAttendu.trim();
  return (
    <>
      <Button taille={taille} icone={<Plus className="size-4" aria-hidden />} onClick={() => setOuvert(true)}>
        Ajouter une étape corrective
      </Button>
      <Dialog
        open={ouvert}
        onOpenChange={(o) => {
          setOuvert(o);
          if (!o) setF(VIDE_CORRECTIVE);
        }}
        taille="lg"
        titre={`Étape corrective ${suivant}`}
        description="Elle s’ajoute à la suite du plan (elle attend les dernières étapes), porte la mention « Ajoutée après le Go » et ne se modifie plus ensuite : décrivez-la complètement."
        pied={
          <>
            <Button variante="fantome" onClick={() => setOuvert(false)}>
              Annuler
            </Button>
            <Button
              variante="primaire"
              icone={<Plus className="size-4" aria-hidden />}
              disabled={!valide}
              action={async () => {
                const duree = f.duree.trim() === '' ? null : Number(f.duree);
                const ok = await run(
                  () =>
                    api.ajouterEtapeCorrective(mep.id, {
                      action: f.action.trim(),
                      systemeIds: f.systemeIds,
                      instructions: f.instructions.trim(),
                      resultatAttendu: f.resultatAttendu.trim(),
                      dureeEstimeeMinutes: duree !== null && Number.isFinite(duree) ? duree : null,
                      criticite: f.criticite,
                    }),
                  `${suivant} ajoutée après le Go.`,
                );
                if (ok) {
                  setOuvert(false);
                  setF(VIDE_CORRECTIVE);
                }
              }}
            >
              Ajouter l’étape
            </Button>
          </>
        }
      >
        <div className="space-y-4">
          <Field label="Action (obligatoire)">
            {(id) => (
              <Input
                id={id}
                value={f.action}
                onChange={(e) => setF({ ...f, action: e.target.value })}
                placeholder="ex. Propagation du rôle gestionnaire dans LDAP"
                autoFocus
              />
            )}
          </Field>
          <fieldset>
            <legend className="mb-1.5 text-ui font-medium">Systèmes (au moins un)</legend>
            <div className="flex flex-wrap gap-x-5 gap-y-2">
              {mep.systemes.map((s) => (
                <Checkbox
                  key={s.id}
                  checked={f.systemeIds.includes(s.id)}
                  onChange={(v) =>
                    setF({
                      ...f,
                      systemeIds: v ? [...f.systemeIds, s.id] : f.systemeIds.filter((x) => x !== s.id),
                    })
                  }
                  label={s.nom}
                />
              ))}
            </div>
          </fieldset>
          <Field label="Instructions (obligatoire)">
            {(id) => (
              <Textarea
                id={id}
                rows={4}
                value={f.instructions}
                onChange={(e) => setF({ ...f, instructions: e.target.value })}
              />
            )}
          </Field>
          <Field label="Résultat attendu (obligatoire)">
            {(id) => (
              <Textarea
                id={id}
                rows={2}
                value={f.resultatAttendu}
                onChange={(e) => setF({ ...f, resultatAttendu: e.target.value })}
              />
            )}
          </Field>
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="Durée estimée (minutes)">
              {(id) => (
                <Input
                  id={id}
                  type="number"
                  min={0}
                  value={f.duree}
                  onChange={(e) => setF({ ...f, duree: e.target.value })}
                />
              )}
            </Field>
            <Field label="Criticité">
              {(id) => (
                <Select
                  id={id}
                  value={f.criticite}
                  onChange={(e) => setF({ ...f, criticite: e.target.value as Criticite })}
                >
                  {(Object.keys(LIBELLE_CRITICITE) as Criticite[]).map((c) => (
                    <option key={c} value={c}>
                      {LIBELLE_CRITICITE[c]}
                    </option>
                  ))}
                </Select>
              )}
            </Field>
          </div>
        </div>
      </Dialog>
    </>
  );
}

/* ------------------------------------------------------------------ Clôturer en échec */

/**
 * « Clôturer en échec… » : en retour arrière, ou en recette quand le retour arrière est impossible. Le dialogue demande
 * l'état du service laissé en production (obligatoire), la référence de l'incident (facultative) et le bilan complet.
 */
export function BoutonCloturerEchec({ taille = 'sm' }: { taille?: TailleBouton }) {
  const [ouvert, setOuvert] = useState(false);
  return (
    <>
      <Button
        variante="danger-doux"
        taille={taille}
        icone={<XCircle className="size-4" aria-hidden />}
        onClick={() => setOuvert(true)}
      >
        Clôturer en échec…
      </Button>
      <ClotureEchecDialog open={ouvert} onOpenChange={setOuvert} />
    </>
  );
}

const VIDE_ECHEC = { etatService: '', referenceIncident: '', ceQuiAMarche: '', incidents: '', actions: '' };

export function ClotureEchecDialog({ open, onOpenChange }: DialogueProps) {
  const { mep, run } = useMep();
  const [f, setF] = useState(VIDE_ECHEC);
  useEffect(() => {
    if (open) setF(VIDE_ECHEC);
  }, [open]);
  const repli = mep.phase === 'RetourArriere';
  const restantes = etapesDuPlan(mep, 'RetourArriere').filter((e) => e.etat !== 'Reussie');
  const siEchoue = mep.planRepli.siRepliEchoue.trim();
  const valide = f.etatService.trim() && f.ceQuiAMarche.trim() && f.incidents.trim();
  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      taille="md"
      titre="Clôturer la MEP en échec ?"
      description="Le service n’est pas rétabli comme prévu. La MEP sera clôturée « échec » et passera en lecture seule ; le rapport de fin reste disponible."
      pied={
        <>
          <Button variante="fantome" onClick={() => onOpenChange(false)}>
            Annuler
          </Button>
          <Button
            variante="danger"
            icone={<XCircle className="size-4" aria-hidden />}
            disabled={!valide}
            action={async () => {
              const ok = await run(
                () =>
                  api.cloturerEnEchec(mep.id, {
                    echec: true,
                    etatService: f.etatService.trim(),
                    referenceIncident: f.referenceIncident.trim(),
                    ceQuiAMarche: f.ceQuiAMarche.trim(),
                    incidents: f.incidents.trim(),
                    actions: f.actions.trim(),
                  }),
                'MEP clôturée en échec.',
              );
              if (ok) onOpenChange(false);
            }}
          >
            Clôturer en échec
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        {repli ? (
          restantes.length > 0 && (
            <Alerte ton="danger" icone={XCircle} titre="Retour arrière incomplet">
              <ul>
                {restantes.map((e) => (
                  <li key={e.id}>
                    <span className="font-mono">{e.code}</span> {e.action} : {LIBELLE_ETAT[e.etat].toLowerCase()}
                  </li>
                ))}
              </ul>
            </Alerte>
          )
        ) : (
          <Alerte ton="danger" icone={XCircle} titre="Retour arrière impossible">
            {retourArriereImpossible(mep) ?? ''} Ni la recette ni un retour arrière ne rétablissent le service.
          </Alerte>
        )}
        {repli && siEchoue && (
          <Alerte ton="later" icone={FileText} titre="Si le retour arrière échoue (fiche du plan)">
            {siEchoue}
          </Alerte>
        )}
        <Field label="État du service laissé en production (obligatoire)">
          {(id) => (
            <Textarea
              id={id}
              rows={2}
              value={f.etatService}
              onChange={(e) => setF({ ...f, etatService: e.target.value })}
              placeholder="ex. Version précédente réinstallée sur la nouvelle base : recherche indisponible."
              autoFocus
            />
          )}
        </Field>
        <Field label="Référence de l’incident (facultative)">
          {(id) => (
            <Input
              id={id}
              className="font-mono"
              value={f.referenceIncident}
              onChange={(e) => setF({ ...f, referenceIncident: e.target.value })}
              placeholder="ex. INC-2026-1042"
            />
          )}
        </Field>
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label="Ce qui a marché *">
            {(id) => (
              <Textarea
                id={id}
                rows={3}
                value={f.ceQuiAMarche}
                onChange={(e) => setF({ ...f, ceQuiAMarche: e.target.value })}
              />
            )}
          </Field>
          <Field label="Incidents *">
            {(id) => (
              <Textarea
                id={id}
                rows={3}
                value={f.incidents}
                onChange={(e) => setF({ ...f, incidents: e.target.value })}
              />
            )}
          </Field>
        </div>
        <Field
          label="Actions"
          hint="Bilan obligatoire pour une clôture en échec, même s’il est désactivé pour cette MEP."
        >
          {(id) => (
            <Textarea
              id={id}
              rows={2}
              value={f.actions}
              onChange={(e) => setF({ ...f, actions: e.target.value })}
              placeholder="ex. Tester la restauration de chaque sauvegarde avant le Go."
            />
          )}
        </Field>
      </div>
    </Dialog>
  );
}
