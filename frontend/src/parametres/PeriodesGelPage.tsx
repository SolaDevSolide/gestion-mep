// Paramètres › Périodes de gel (#129) : les administrateurs les définissent, tout le monde les voit. Pendant une période de
// gel, le Go d'une MEP concernée est bloqué, sauf dérogation accordée par un approbateur désigné de la MEP.
import { Lock, OctagonAlert, Pencil, Plus, RefreshCw, Save, Snowflake, Trash2 } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { api, ApiError } from '../api';
import { useAuth } from '../auth';
import { ajouterJours, FUSEAU_INSTANCE, jourLocal, LIBELLE_RECURRENCE, plageGel } from '../calendrier';
import { depuisInputDateTimeFuseau, formatDate, formatDateHeureFuseau, versInputDateTimeFuseau } from '../format';
import { useAsync, useNow } from '../hooks';
import { normaliser } from '../recherche';
import { useReferentiel } from '../referentiels';
import type { OccurrenceGel, PageCalendrier, PeriodeGel, PeriodeGelSaisie, RecurrenceGel } from '../types';
import { Alerte } from '../ui/Alerte';
import { Button } from '../ui/Button';
import { Card } from '../ui/Card';
import { Checkbox } from '../ui/Checkbox';
import { MultiSelect } from '../ui/Combobox';
import { ConfirmDialog } from '../ui/Dialog';
import { EmptyState } from '../ui/EmptyState';
import { ErreurChargement } from '../ui/ErreurChargement';
import { Field, Input, Select } from '../ui/Input';
import { Segmented } from '../ui/Segmented';
import { Sheet } from '../ui/Sheet';
import { EnChargement, SqueletteTableau } from '../ui/Squelette';
import { Badge, StatusPill } from '../ui/StatusPill';
import { useToast } from '../ui/Toast';

const JOURS = ['dimanche', 'lundi', 'mardi', 'mercredi', 'jeudi', 'vendredi', 'samedi'];

/** Quand a lieu la période, en clair, dans son fuseau. */
export function decrireQuand(
  p: Pick<PeriodeGel, 'debut' | 'fin' | 'recurrence' | 'finRecurrence' | 'fuseauHoraire'>,
): string {
  const z = p.fuseauHoraire;
  const [dj, dh] = formatDateHeureFuseau(p.debut, z).split(' ');
  const [fj, fh] = formatDateHeureFuseau(p.fin, z).split(' ');
  const jourSem = (iso: string) => JOURS[new Date(`${jourLocal(iso, z)}T00:00:00Z`).getUTCDay()];
  let texte: string;
  switch (p.recurrence) {
    case 'Hebdomadaire':
      texte = `Chaque semaine, du ${jourSem(p.debut)} ${dh} au ${jourSem(p.fin)} ${fh}`;
      break;
    case 'Mensuelle':
      texte = `Le ${Number(dj.slice(0, 2))} de chaque mois ${dh}, jusqu’au ${Number(fj.slice(0, 2))} ${fh}`;
      break;
    case 'FinDeMois':
      texte = `Chaque fin de mois, comme du ${dj.slice(0, 5)} ${dh} au ${fj.slice(0, 5)} ${fh}`;
      break;
    case 'Annuelle':
      texte = `Chaque année, du ${dj.slice(0, 5)} ${dh} au ${fj.slice(0, 5)} ${fh}`;
      break;
    default:
      texte = `${dj} ${dh} → ${fj} ${fh}`;
  }
  return p.recurrence !== 'Aucune' && p.finRecurrence ? `${texte}, jusqu’au ${formatDate(p.finRecurrence)}` : texte;
}

interface Prochaine {
  occurrence: OccurrenceGel;
  meps: number;
}

/** Prochaine occurrence de chaque période (sur un an) et nombre de MEP qu'elle concerne, d'après le calendrier. */
export function prochaines(page: PageCalendrier, maintenant: number): Map<string, Prochaine> {
  const resultat = new Map<string, Prochaine>();
  for (const o of page.gels) {
    if (Date.parse(o.fin) <= maintenant || resultat.has(o.periodeGelId)) continue;
    const meps = page.meps.filter((m) =>
      m.gels.some((g) => g.periodeGelId === o.periodeGelId && Date.parse(g.debut) === Date.parse(o.debut)),
    ).length;
    resultat.set(o.periodeGelId, { occurrence: o, meps });
  }
  return resultat;
}

function Environnements({ p }: { p: PeriodeGel }) {
  if (p.environnements.length === 0 && !p.productionSeulement) return <span className="text-fg-muted">Tous</span>;
  return (
    <span className="flex flex-wrap gap-1">
      {p.productionSeulement && <Badge ton="warn">Production</Badge>}
      {p.environnements.map((e) => (
        <Badge key={e} ton={normaliser(e) === 'production' ? 'warn' : undefined}>
          {e}
        </Badge>
      ))}
    </span>
  );
}

function Systemes({ p }: { p: PeriodeGel }) {
  if (p.systemes.length === 0) return <span className="text-fg-muted">Tous</span>;
  return (
    <span className="flex flex-wrap gap-1">
      {p.systemes.map((s) => (
        <Badge key={s}>{s}</Badge>
      ))}
    </span>
  );
}

function Quand({ p }: { p: PeriodeGel }) {
  return (
    <span className="flex flex-col items-start gap-1">
      <StatusPill
        ton={p.recurrence === 'Aucune' ? 'later' : 'run'}
        icone={p.recurrence === 'Aucune' ? undefined : RefreshCw}
        taille="sm"
      >
        {p.recurrence === 'Aucune' ? 'Ponctuelle' : 'Récurrente'}
      </StatusPill>
      <span className="text-xs text-fg-subtle">{decrireQuand(p)}</span>
    </span>
  );
}

function TexteProchaine({ x, fuseau }: { x: Prochaine | undefined; fuseau: string }) {
  if (!x) return <span className="text-fg-subtle">Aucune dans l’année</span>;
  return (
    <span className="flex flex-col">
      <span className="tnum">{plageGel(x.occurrence, fuseau)}</span>
      <span className="text-xs text-fg-subtle">
        {x.meps === 0 ? 'aucune MEP' : `${x.meps} MEP concernée${x.meps > 1 ? 's' : ''}`}
      </span>
    </span>
  );
}

// ---------- Formulaire ----------

interface Brouillon {
  nom: string;
  motif: string;
  recurrence: RecurrenceGel;
  debut: string;
  fin: string;
  finRecurrence: string;
  tousSystemes: boolean;
  systemes: string[];
  environnements: string[];
  productionSeulement: boolean;
}

function brouillonDe(p: PeriodeGel | null): Brouillon {
  const z = p?.fuseauHoraire ?? FUSEAU_INSTANCE;
  return {
    nom: p?.nom ?? '',
    motif: p?.motif ?? '',
    recurrence: p?.recurrence ?? 'Aucune',
    debut: p ? versInputDateTimeFuseau(p.debut, z) : '',
    fin: p ? versInputDateTimeFuseau(p.fin, z) : '',
    finRecurrence: p?.finRecurrence ?? '',
    tousSystemes: (p?.systemeCatalogueIds.length ?? 0) === 0,
    systemes: p?.systemeCatalogueIds ?? [],
    environnements: p?.environnements ?? [],
    productionSeulement: p?.productionSeulement ?? true,
  };
}

/** Saisie envoyée au serveur ; les heures se lisent dans le fuseau de la période (celui de l'instance pour une nouvelle). */
export function saisieDe(b: Brouillon, fuseau: string, version?: number): PeriodeGelSaisie {
  return {
    nom: b.nom.trim(),
    motif: b.motif.trim(),
    debut: depuisInputDateTimeFuseau(b.debut, fuseau) ?? '',
    fin: depuisInputDateTimeFuseau(b.fin, fuseau) ?? '',
    recurrence: b.recurrence,
    finRecurrence: b.recurrence !== 'Aucune' && b.finRecurrence ? b.finRecurrence : null,
    systemeCatalogueIds: b.tousSystemes ? [] : b.systemes,
    environnements: b.environnements,
    productionSeulement: b.productionSeulement,
    ...(version !== undefined ? { version } : {}),
  };
}

const RECURRENCES: { valeur: Exclude<RecurrenceGel, 'Aucune'>; aide: string }[] = [
  { valeur: 'FinDeMois', aide: 'Même écart avec le dernier jour du mois : la clôture comptable des derniers jours.' },
  { valeur: 'Mensuelle', aide: 'Même jour chaque mois (ramené au dernier jour d’un mois plus court) : la paie du 24.' },
  { valeur: 'Hebdomadaire', aide: 'Même jour de la semaine et mêmes heures.' },
  { valeur: 'Annuelle', aide: 'Mêmes dates chaque année : les fêtes de fin d’année.' },
];

function EditeurPeriode({
  periode,
  ouvert,
  onOpenChange,
  prochainesOccurrences,
  onFait,
}: {
  /** null : nouvelle période. */
  periode: PeriodeGel | null;
  ouvert: boolean;
  onOpenChange: (o: boolean) => void;
  prochainesOccurrences: OccurrenceGel[];
  onFait: () => void;
}) {
  const toast = useToast();
  const catalogueLu = useReferentiel('catalogue').donnees;
  const catalogue = useMemo(() => catalogueLu ?? [], [catalogueLu]);
  const [b, setB] = useState<Brouillon>(() => brouillonDe(periode));
  const [erreurs, setErreurs] = useState<string[]>([]);
  const [envoi, setEnvoi] = useState(false);
  const [suppression, setSuppression] = useState(false);
  useEffect(() => {
    if (ouvert) {
      setB(brouillonDe(periode));
      setErreurs([]);
    }
  }, [ouvert, periode]);
  const maj = (p: Partial<Brouillon>) => setB((x) => ({ ...x, ...p }));
  const fuseau = periode?.fuseauHoraire ?? FUSEAU_INSTANCE;
  const environnements = useMemo(() => {
    const vus = new Map<string, { nom: string; production: boolean }>();
    for (const s of catalogue)
      if (b.tousSystemes || b.systemes.includes(s.id))
        for (const e of s.environnements) if (!vus.has(normaliser(e.nom))) vus.set(normaliser(e.nom), e);
    for (const e of b.environnements)
      if (!vus.has(normaliser(e))) vus.set(normaliser(e), { nom: e, production: false });
    return [...vus.values()].sort(
      (x, y) => Number(y.production) - Number(x.production) || x.nom.localeCompare(y.nom, 'fr'),
    );
  }, [catalogue, b.tousSystemes, b.systemes, b.environnements]);
  const modifie = JSON.stringify(b) !== JSON.stringify(brouillonDe(periode));
  const valide = b.nom.trim() && b.motif.trim() && b.debut && b.fin && (b.tousSystemes || b.systemes.length > 0);

  async function enregistrer() {
    setEnvoi(true);
    setErreurs([]);
    try {
      const s = saisieDe(b, fuseau, periode?.version);
      if (periode) await api.modifierPeriodeGel(periode.id, s);
      else await api.creerPeriodeGel(s);
      toast.succes(periode ? 'Période de gel modifiée.' : 'Période de gel créée.');
      onFait();
      onOpenChange(false);
    } catch (e) {
      setErreurs(
        e instanceof ApiError && e.erreurs.length > 0
          ? e.erreurs
          : [e instanceof Error ? e.message : 'Enregistrement impossible.'],
      );
    } finally {
      setEnvoi(false);
    }
  }

  return (
    <>
      <Sheet
        open={ouvert}
        onOpenChange={onOpenChange}
        titre={periode ? `Modifier « ${periode.nom} »` : 'Nouvelle période de gel'}
        description={`Heures dans le fuseau ${fuseau}.`}
        pied={
          <>
            {periode && (
              <Button
                variante="danger-doux"
                className="mr-auto"
                icone={<Trash2 className="size-4" aria-hidden />}
                onClick={() => setSuppression(true)}
              >
                Supprimer
              </Button>
            )}
            <Button variante="fantome" onClick={() => onOpenChange(false)}>
              Annuler
            </Button>
            <Button
              variante="primaire"
              icone={<Save className="size-4" aria-hidden />}
              chargement={envoi}
              disabled={!valide || (periode !== null && !modifie)}
              onClick={() => void enregistrer()}
            >
              Enregistrer
            </Button>
          </>
        }
      >
        <div className="space-y-5">
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="Nom *">
              {(id) => (
                <Input
                  id={id}
                  value={b.nom}
                  onChange={(e) => maj({ nom: e.target.value })}
                  placeholder="ex. Clôture comptable"
                />
              )}
            </Field>
            <Field label="Motif *" hint="Affiché aux pilotes et dans la variable {{gel}}.">
              {(id) => (
                <Input
                  id={id}
                  value={b.motif}
                  onChange={(e) => maj({ motif: e.target.value })}
                  placeholder="ex. Arrêtés de fin de mois"
                />
              )}
            </Field>
          </div>
          <div className="flex flex-col gap-1.5">
            <span className="text-ui font-medium" id="type-gel">
              Type
            </span>
            <Segmented
              label="Type de période"
              valeur={b.recurrence === 'Aucune' ? 'ponctuelle' : 'recurrente'}
              onChange={(v) =>
                maj({
                  recurrence: v === 'ponctuelle' ? 'Aucune' : b.recurrence === 'Aucune' ? 'FinDeMois' : b.recurrence,
                })
              }
              options={[
                { valeur: 'ponctuelle', libelle: 'Ponctuelle' },
                { valeur: 'recurrente', libelle: 'Récurrente' },
              ]}
              className="self-start"
            />
          </div>
          {b.recurrence !== 'Aucune' && (
            <div className="grid gap-4 sm:grid-cols-2">
              <Field label="Répéter" hint={RECURRENCES.find((r) => r.valeur === b.recurrence)?.aide}>
                {(id) => (
                  <Select
                    id={id}
                    value={b.recurrence}
                    onChange={(e) =>
                      maj({ recurrence: RECURRENCES.find((r) => r.valeur === e.target.value)?.valeur ?? b.recurrence })
                    }
                  >
                    {RECURRENCES.map((r) => (
                      <option key={r.valeur} value={r.valeur}>
                        {LIBELLE_RECURRENCE[r.valeur]}
                      </option>
                    ))}
                  </Select>
                )}
              </Field>
              <Field label="Jusqu’au (facultatif)" hint="Dernier jour où une occurrence peut commencer.">
                {(id) => (
                  <Input
                    id={id}
                    type="date"
                    value={b.finRecurrence}
                    onChange={(e) => maj({ finRecurrence: e.target.value })}
                  />
                )}
              </Field>
            </div>
          )}
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label={b.recurrence === 'Aucune' ? 'Début *' : 'Début de la première occurrence *'}>
              {(id) => (
                <Input id={id} type="datetime-local" value={b.debut} onChange={(e) => maj({ debut: e.target.value })} />
              )}
            </Field>
            <Field label="Fin (exclue) *" hint="Un gel jusqu’au 30 inclus finit le 1er à 00:00.">
              {(id) => (
                <Input id={id} type="datetime-local" value={b.fin} onChange={(e) => maj({ fin: e.target.value })} />
              )}
            </Field>
          </div>
          <div className="grid gap-5 sm:grid-cols-2">
            <fieldset className="space-y-2">
              <legend className="mb-1.5 text-ui font-medium">Systèmes</legend>
              <Checkbox
                checked={b.tousSystemes}
                onChange={(v) => maj({ tousSystemes: v })}
                label="Tous les systèmes"
                description="Y compris ceux saisis à la main dans une MEP."
              />
              {!b.tousSystemes && (
                <MultiSelect
                  options={catalogue.map((s) => ({ valeur: s.id, libelle: s.nom }))}
                  valeurs={b.systemes}
                  onChange={(v) => maj({ systemes: v })}
                  placeholder="Choisir des systèmes…"
                  placeholderRecherche="Rechercher un système…"
                />
              )}
            </fieldset>
            <fieldset className="space-y-2">
              <legend className="mb-1.5 text-ui font-medium">Environnements</legend>
              <Checkbox
                checked={b.productionSeulement}
                onChange={(v) => maj({ productionSeulement: v, ...(v ? { environnements: [] } : {}) })}
                label="Tout environnement de production"
                description="Ceux marqués « production » dans le catalogue."
              />
              {environnements.map((e) => (
                <Checkbox
                  key={e.nom}
                  checked={b.environnements.some((x) => normaliser(x) === normaliser(e.nom))}
                  onChange={(v) =>
                    maj({
                      productionSeulement: false,
                      environnements: v
                        ? [...b.environnements, e.nom]
                        : b.environnements.filter((x) => normaliser(x) !== normaliser(e.nom)),
                    })
                  }
                  label={e.nom}
                  description={e.production ? 'Marqué « production »' : undefined}
                />
              ))}
              <p className="text-xs text-fg-subtle">Rien de coché : tous les environnements.</p>
            </fieldset>
          </div>
          <Alerte ton="wait" icone={Lock} titre="Approbateurs des dérogations">
            Les approbateurs désignés de chaque MEP. Jamais le demandeur lui-même, ni le pilote, un suppléant ou un
            responsable technique de la MEP.
          </Alerte>
          {periode && (
            <Alerte ton="run" icone={Snowflake} titre="Prochaines occurrences">
              {prochainesOccurrences.length === 0
                ? 'Aucune dans l’année qui vient.'
                : prochainesOccurrences
                    .slice(0, 4)
                    .map((o) => plageGel(o, fuseau))
                    .join(' · ')}
              {modifie && ' (avant vos modifications)'}
            </Alerte>
          )}
          {erreurs.length > 0 && (
            <Alerte ton="danger" icone={OctagonAlert} role="alert" titre="Enregistrement refusé">
              {erreurs.join('\n')}
            </Alerte>
          )}
        </div>
      </Sheet>
      <ConfirmDialog
        open={suppression}
        onOpenChange={setSuppression}
        titre={`Supprimer « ${periode?.nom ?? ''} » ?`}
        description="Les dérogations déjà accordées restent au journal des MEP. Le Go n’est plus bloqué par cette période."
        libelleConfirmer="Supprimer"
        variante="danger"
        onConfirmer={async () => {
          if (!periode) return false;
          try {
            await api.supprimerPeriodeGel(periode.id);
            toast.succes('Période de gel supprimée.');
            onFait();
            onOpenChange(false);
            return true;
          } catch (e) {
            toast.erreur(e instanceof Error ? e.message : 'Suppression impossible.');
            return false;
          }
        }}
      />
    </>
  );
}

export default function PeriodesGelPage() {
  const { utilisateur } = useAuth();
  const admin = utilisateur?.estAdmin ?? false;
  const maintenant = useNow(60_000);
  const periodes = useAsync(() => api.periodesGel(), []);
  // Calendrier de l'année qui vient : prochaines occurrences et MEP concernées.
  const annee = useAsync(() => {
    const du = jourLocal(new Date().toISOString(), FUSEAU_INSTANCE);
    return api.calendrier(new URLSearchParams({ du, au: ajouterJours(du, 365), limite: '500' }));
  }, [periodes.donnees]);
  const [edition, setEdition] = useState<{ periode: PeriodeGel | null } | null>(null);
  const suivantes = useMemo(
    () => (annee.donnees ? prochaines(annee.donnees, maintenant) : new Map()),
    [annee.donnees, maintenant],
  );
  const fuseau = annee.donnees?.fuseauHoraire ?? FUSEAU_INSTANCE;
  const liste = periodes.donnees ?? [];
  const pret = periodes.donnees !== null && (annee.donnees !== null || annee.echec !== null);

  return (
    <main className="mx-auto w-full max-w-6xl px-4 py-8 sm:px-6">
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div className="max-w-3xl">
          <h1 className="text-2xl font-semibold tracking-tight">Périodes de gel</h1>
          <p className="mt-1 text-sm text-fg-subtle">
            Pendant une période de gel, le Go d’une MEP concernée est bloqué, sauf dérogation accordée par un
            approbateur. Les administrateurs les définissent ; tout le monde les voit.{' '}
            {!admin && (
              <span className="inline-flex items-center gap-1">
                <Lock className="size-3.5" aria-hidden />
                Lecture seule.
              </span>
            )}
          </p>
        </div>
        {admin && (
          <Button
            variante="primaire"
            icone={<Plus className="size-4" aria-hidden />}
            onClick={() => setEdition({ periode: null })}
          >
            Nouvelle période
          </Button>
        )}
      </div>

      {periodes.echec !== null && (
        <ErreurChargement
          className="mt-6"
          erreur={periodes.echec}
          titre="Impossible de charger les périodes de gel"
          onReessayer={periodes.recharger}
          enCours={periodes.chargement}
        />
      )}
      {annee.echec !== null && periodes.donnees !== null && (
        <ErreurChargement
          variante="ligne"
          className="mt-4"
          erreur={annee.echec}
          titre="Prochaines occurrences non chargées"
          onReessayer={annee.recharger}
          enCours={annee.chargement}
        />
      )}

      <Card className="mt-6 overflow-hidden">
        {!pret && periodes.echec === null ? (
          <EnChargement libelle="Chargement des périodes de gel…">
            <SqueletteTableau lignes={3} colonnes={5} className="rounded-none border-0" />
          </EnChargement>
        ) : pret && liste.length === 0 ? (
          <EmptyState
            icone={Snowflake}
            titre="Aucune période de gel"
            description="Ajoutez par exemple la clôture comptable de fin de mois : les MEP prévues pendant ces jours seront signalées, et leur Go demandera une dérogation."
            action={
              admin ? (
                <Button
                  variante="primaire"
                  icone={<Plus className="size-4" aria-hidden />}
                  onClick={() => setEdition({ periode: null })}
                >
                  Nouvelle période
                </Button>
              ) : undefined
            }
          />
        ) : (
          pret && (
            <>
              {/* Écran large : tableau. */}
              <table className="apparition hidden w-full text-sm md:table">
                <thead className="border-b border-line bg-surface-2/60 text-left text-xs text-fg-subtle">
                  <tr>
                    <th className="px-4 py-2.5 font-medium">Période</th>
                    <th className="px-4 py-2.5 font-medium">Quand</th>
                    <th className="px-4 py-2.5 font-medium">Systèmes</th>
                    <th className="px-4 py-2.5 font-medium">Environnements</th>
                    <th className="px-4 py-2.5 font-medium">Prochaine</th>
                    {admin && (
                      <th className="px-4 py-2.5">
                        <span className="sr-only">Actions</span>
                      </th>
                    )}
                  </tr>
                </thead>
                <tbody className="divide-y divide-line">
                  {liste.map((p) => (
                    <tr key={p.id} className="align-top transition-colors duration-150 hover:bg-surface-2/50">
                      <td className="px-4 py-3">
                        <span className="font-semibold">{p.nom}</span>
                        <span className="block text-xs text-fg-subtle">{p.motif}</span>
                      </td>
                      <td className="px-4 py-3">
                        <Quand p={p} />
                      </td>
                      <td className="px-4 py-3">
                        <Systemes p={p} />
                      </td>
                      <td className="px-4 py-3">
                        <Environnements p={p} />
                      </td>
                      <td className="px-4 py-3">
                        <TexteProchaine x={suivantes.get(p.id)} fuseau={fuseau} />
                      </td>
                      {admin && (
                        <td className="px-4 py-3 text-right">
                          <Button
                            taille="sm"
                            icone={<Pencil className="size-3.5" aria-hidden />}
                            aria-label={`Modifier ${p.nom}`}
                            onClick={() => setEdition({ periode: p })}
                          >
                            Modifier
                          </Button>
                        </td>
                      )}
                    </tr>
                  ))}
                </tbody>
              </table>
              {/* Téléphone : une fiche par période. */}
              <ul className="apparition divide-y divide-line md:hidden">
                {liste.map((p) => (
                  <li key={p.id} className="space-y-2.5 px-4 py-3.5">
                    <div className="flex items-start justify-between gap-3">
                      <div className="min-w-0">
                        <p className="font-semibold">{p.nom}</p>
                        <p className="text-xs text-fg-subtle">{p.motif}</p>
                      </div>
                      {admin && (
                        <Button
                          taille="sm"
                          icone={<Pencil className="size-3.5" aria-hidden />}
                          aria-label={`Modifier ${p.nom}`}
                          onClick={() => setEdition({ periode: p })}
                        >
                          Modifier
                        </Button>
                      )}
                    </div>
                    <dl className="grid grid-cols-[7.5rem_minmax(0,1fr)] gap-x-3 gap-y-2 text-sm">
                      <dt className="text-xs text-fg-subtle">Quand</dt>
                      <dd>
                        <Quand p={p} />
                      </dd>
                      <dt className="text-xs text-fg-subtle">Systèmes</dt>
                      <dd>
                        <Systemes p={p} />
                      </dd>
                      <dt className="text-xs text-fg-subtle">Environnements</dt>
                      <dd>
                        <Environnements p={p} />
                      </dd>
                      <dt className="text-xs text-fg-subtle">Prochaine</dt>
                      <dd>
                        <TexteProchaine x={suivantes.get(p.id)} fuseau={fuseau} />
                      </dd>
                    </dl>
                  </li>
                ))}
              </ul>
            </>
          )
        )}
      </Card>

      {admin && (
        <EditeurPeriode
          periode={edition?.periode ?? null}
          ouvert={edition !== null}
          onOpenChange={(o) => {
            if (!o) setEdition(null);
          }}
          prochainesOccurrences={(annee.donnees?.gels ?? []).filter(
            (o) => o.periodeGelId === edition?.periode?.id && Date.parse(o.fin) > maintenant,
          )}
          onFait={periodes.recharger}
        />
      )}
    </main>
  );
}
