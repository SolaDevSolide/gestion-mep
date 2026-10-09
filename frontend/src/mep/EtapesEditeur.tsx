import { ArrowDown, ArrowUp, DatabaseBackup, Flag, ListChecks, Pencil, Plus, Save, Trash2, Wrench } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { api } from '../api';
import { etapesDuPlan, nomParticipant } from '../domaine';
import { LIBELLE_CRITICITE } from '../format';
import { useMep } from '../mepContext';
import { metaEtape } from '../statuts';
import type { Criticite, Etape, Mep, ResolutionType, TypeEtape, TypePlan } from '../types';
import { Avatar } from '../ui/Avatar';
import { BoutonIcone, Button } from '../ui/Button';
import { Card } from '../ui/Card';
import { MultiSelect } from '../ui/Combobox';
import { ConfirmDialog } from '../ui/Dialog';
import { EmptyState } from '../ui/EmptyState';
import { Field, Input, Select, Textarea } from '../ui/Input';
import { Sheet } from '../ui/Sheet';
import { Badge, StatusPill } from '../ui/StatusPill';
import { Tooltip } from '../ui/Tooltip';
import { Checkbox } from '../ui/Checkbox';
import { Segmented } from '../ui/Segmented';
import { PiecesJointes } from '../ui/PiecesJointes';
import { BadgesEtape } from './BadgesEtape';
import { ResolutionEditeurDialog } from './ResolutionEditeur';
import type { Ton } from '../format';


/** Pièces jointes d'une étape (hors du fieldset : le dépôt reste possible tant que la MEP n'est pas clôturée). */
export function PiecesEtape({ etapeId, className }: { etapeId: string; className?: string }) {
  const { mep, run } = useMep();
  return (
    <PiecesJointes
      className={className}
      compact
      lectureSeule={mep.phase === 'Cloturee'}
      pilote={mep.droits.estPilote}
      pieces={mep.piecesJointes.filter((p) => p.etapeId === etapeId)}
      onDeposer={(f, signaler) => run(() => api.deposerPieceEtape(mep.id, etapeId, f), undefined, signaler)}
      onSupprimer={(p, signaler) => run(() => api.supprimerPiece(mep.id, p.id), undefined, signaler)}
    />
  );
}

const TON_CRITICITE: Record<Criticite, Ton | undefined> = { Faible: undefined, Moderee: 'wait', Elevee: 'warn', Bloquante: 'danger' };

interface Brouillon {
  action: string;
  systemeId: string;
  responsableId: string;
  criticite: Criticite | '';
  instructions: string;
  resultatAttendu: string;
  dependances: string[];
  resolutionIds: string[];
  type: TypeEtape;
  pointDeNonRetour: boolean;
}

const depuis = (e: Etape): Brouillon => ({
  action: e.action, systemeId: e.systemeId ?? '', responsableId: e.responsableId ?? '', criticite: e.criticite ?? '',
  instructions: e.instructions, resultatAttendu: e.resultatAttendu, dependances: e.dependances, resolutionIds: e.resolutionIds,
  type: e.type, pointDeNonRetour: e.pointDeNonRetour,
});

function EtapeSheet({ etape, plan, editable, onClose }: { etape: Etape | null; plan: TypePlan; editable: boolean; onClose: () => void }) {
  const { mep, run } = useMep();
  const [b, setB] = useState<Brouillon | null>(() => (etape ? depuis(etape) : null));
  const [resolutions, setResolutions] = useState<ResolutionType[]>([]);
  const [toutVoir, setToutVoir] = useState(false);
  const [creation, setCreation] = useState(false);
  const [envoi, setEnvoi] = useState(false);

  const id = etape?.id;
  useEffect(() => {
    setB(etape ? depuis(etape) : null);
    setToutVoir(false);
    // On ne réinitialise que si l'on change d'étape : la saisie en cours survit aux rechargements.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id]);
  useEffect(() => {
    if (id) api.resolutions().then(setResolutions).catch(() => setResolutions([]));
  }, [id]);

  const systeme = mep.systemes.find((s) => s.id === b?.systemeId);
  const catalogueId = systeme?.catalogueId ?? null;

  const optionsDeps = useMemo(
    () => etapesDuPlan(mep, plan).filter((e) => e.id !== id).map((e) => ({ valeur: e.id, libelle: `${e.code} — ${e.action || 'sans libellé'}` })),
    [mep, plan, id],
  );
  const optionsRes = useMemo(() => {
    const noms = new Map(mep.systemes.filter((s) => s.catalogueId).map((s) => [s.catalogueId!, s.nom]));
    const visibles = resolutions.filter((r) => toutVoir || !catalogueId || r.systemeCatalogueId === catalogueId || r.systemeCatalogueId === null || b?.resolutionIds.includes(r.id));
    return visibles.map((r) => ({ valeur: r.id, libelle: r.titre, detail: r.systemeCatalogueId ? noms.get(r.systemeCatalogueId) ?? 'Autre système' : 'Tous les systèmes' }));
  }, [resolutions, toutVoir, catalogueId, mep.systemes, b?.resolutionIds]);

  if (!etape || !b) return <Sheet open={false} onOpenChange={onClose} titre="Étape" />;
  const maj = (patch: Partial<Brouillon>) => setB({ ...b, ...patch });
  const courante = mep.etapes.find((e) => e.id === etape.id) ?? etape;

  async function enregistrer() {
    if (!b) return;
    setEnvoi(true);
    const ok = await run(
      () => api.modifierEtape(mep.id, courante.id, {
        revision: courante.revision, action: b.action.trim(), systemeId: b.systemeId || null, responsableId: b.responsableId || null,
        criticite: b.criticite || null, instructions: b.instructions, resultatAttendu: b.resultatAttendu.trim(),
        dependances: b.dependances, resolutionIds: b.resolutionIds,
        type: plan === 'Deploiement' ? b.type : 'Standard', pointDeNonRetour: plan === 'Deploiement' && b.pointDeNonRetour,
      }),
      `${courante.code} enregistrée.`,
    );
    setEnvoi(false);
    if (ok) onClose();
  }

  return (
    <>
      <Sheet
        open
        onOpenChange={(o) => { if (!o) onClose(); }}
        titre={<span className="flex items-center gap-2"><span className="font-mono">{courante.code}</span>{editable ? 'Modifier l’étape' : 'Détail de l’étape'}</span>}
        description={editable ? 'Les modifications sont enregistrées sur le serveur à la validation.' : 'Lecture seule : le plan est figé.'}
        pied={editable ? (
          <>
            <Button variante="fantome" onClick={onClose}>Annuler</Button>
            <Button variante="primaire" icone={<Save className="size-4" aria-hidden />} chargement={envoi} onClick={() => void enregistrer()}>Enregistrer</Button>
          </>
        ) : <Button onClick={onClose}>Fermer</Button>}
      >
        <fieldset disabled={!editable} className="space-y-5">
          <Field label="Action">{(i) => <Input id={i} value={b.action} onChange={(e) => maj({ action: e.target.value })} placeholder="ex. Arrêter le service API" />}</Field>
          {plan === 'Deploiement' && (
            <div className="grid gap-4 rounded-lg border border-line bg-surface-2/40 p-3.5 sm:grid-cols-2">
              <Field label="Type d’étape" hint="Une étape de sauvegarde réalise la sauvegarde pendant la MEP.">
                {() => (
                  <Segmented
                    label="Type d’étape"
                    valeur={b.type}
                    onChange={(v) => maj({ type: v })}
                    options={[
                      { valeur: 'Standard', libelle: 'Standard' },
                      { valeur: 'Sauvegarde', libelle: <><DatabaseBackup className="size-4" aria-hidden />Sauvegarde</> },
                    ]}
                  />
                )}
              </Field>
              <div className="pt-0.5">
                <Checkbox
                  label={<span className="flex items-center gap-1.5 font-medium"><Flag className="size-3.5 text-warn-solid" aria-hidden />Point de non-retour</span>}
                  description="Après cette étape, le retour arrière n’est plus possible. Le pilote doit autoriser le passage."
                  checked={b.pointDeNonRetour}
                  onChange={(c) => maj({ pointDeNonRetour: c })}
                />
              </div>
            </div>
          )}
          <div className="grid gap-4 sm:grid-cols-3">
            <Field label="Système">
              {(i) => (
                <Select id={i} value={b.systemeId} onChange={(e) => maj({ systemeId: e.target.value })}>
                  <option value="">— choisir —</option>
                  {mep.systemes.map((s) => <option key={s.id} value={s.id}>{s.nom}</option>)}
                </Select>
              )}
            </Field>
            <Field label="Responsable">
              {(i) => (
                <Select id={i} value={b.responsableId} onChange={(e) => maj({ responsableId: e.target.value })}>
                  <option value="">— choisir —</option>
                  {mep.participants.map((p) => <option key={p.id} value={p.id}>{p.nom}</option>)}
                </Select>
              )}
            </Field>
            <Field label="Criticité">
              {(i) => (
                <Select id={i} value={b.criticite} onChange={(e) => maj({ criticite: e.target.value as Criticite | '' })}>
                  <option value="">— choisir —</option>
                  {(Object.keys(LIBELLE_CRITICITE) as Criticite[]).map((c) => <option key={c} value={c}>{LIBELLE_CRITICITE[c]}</option>)}
                </Select>
              )}
            </Field>
          </div>
          <Field label="Instructions exactes" hint="Les commandes sont copiables telles quelles pendant l’exécution : une action par ligne, sans commentaire parasite.">
            {(i) => <Textarea id={i} mono rows={7} value={b.instructions} onChange={(e) => maj({ instructions: e.target.value })} placeholder="systemctl stop api-portail" />}
          </Field>
          <Field label="Résultat attendu">{(i) => <Textarea id={i} rows={2} value={b.resultatAttendu} onChange={(e) => maj({ resultatAttendu: e.target.value })} placeholder="ex. Le service est arrêté (statut « inactive »)." />}</Field>
          <Field label="Dépendances" hint="L’étape ne devient disponible que lorsque toutes ses dépendances sont réussies.">
            {(i) => <MultiSelect id={i} options={optionsDeps} valeurs={b.dependances} onChange={(v) => maj({ dependances: v })} placeholder="Aucune dépendance" placeholderRecherche="Rechercher une étape…" vide="Aucune autre étape." disabled={!editable} libelleInconnu="Étape supprimée" />}
          </Field>
          <div className="space-y-2 rounded-lg border border-line bg-surface-2/40 p-3.5">
            <Field label={<span className="flex items-center gap-1.5"><Wrench className="size-3.5" aria-hidden />Résolutions en cas d’échec</span>} hint="Proposées à l’opérateur si cette étape échoue.">
              {(i) => (
                <MultiSelect
                  id={i}
                  options={optionsRes}
                  valeurs={b.resolutionIds}
                  onChange={(v) => maj({ resolutionIds: v })}
                  placeholder="Aucune résolution liée"
                  placeholderRecherche="Rechercher une résolution…"
                  vide="Aucune résolution disponible."
                  disabled={!editable}
                  libelleInconnu="Résolution inactive"
                />
              )}
            </Field>
            {editable && (
              <div className="flex flex-wrap items-center justify-between gap-3 pt-1">
                {catalogueId ? (
                  <Checkbox label={`Voir toutes les résolutions (filtre : ${systeme?.nom})`} checked={toutVoir} onChange={setToutVoir} />
                ) : <span className="text-xs text-fg-subtle">Aucun système du catalogue : toutes les résolutions sont proposées.</span>}
                <Button taille="sm" icone={<Plus className="size-4" aria-hidden />} onClick={() => setCreation(true)}>Créer une résolution</Button>
              </div>
            )}
          </div>
        </fieldset>
        <PiecesEtape etapeId={courante.id} className="mt-5" />
      </Sheet>
      <ResolutionEditeurDialog
        open={creation}
        onOpenChange={setCreation}
        systemeParDefaut={catalogueId}
        onEnregistree={(r) => {
          setResolutions((l) => [...l.filter((x) => x.id !== r.id), r]);
          setB((cur) => (cur ? { ...cur, resolutionIds: [...cur.resolutionIds, r.id] } : cur));
        }}
      />
    </>
  );
}

/** Éditeur du plan (déploiement ou retour arrière) : liste ordonnée + panneau latéral d'édition. */
export function EtapesEditeur({ plan }: { plan: TypePlan }) {
  const { mep, run } = useMep();
  const etapes = etapesDuPlan(mep, plan);
  const editable = mep.droits.peutEditerContenu;
  const [ouverte, setOuverte] = useState<string | null>(null);
  const [suppr, setSuppr] = useState<Etape | null>(null);

  async function ajouter() {
    const avant = new Set(mep.etapes.map((e) => e.id));
    let apres: Mep | undefined;
    const ok = await run(async () => { apres = await api.ajouterEtape(mep.id, plan); return apres; }, 'Étape ajoutée.');
    if (ok && apres) {
      const nouvelle = apres.etapes.find((e) => !avant.has(e.id));
      if (nouvelle) setOuverte(nouvelle.id);
    }
  }

  const courante = ouverte ? mep.etapes.find((e) => e.id === ouverte) ?? null : null;
  const libelleType = plan === 'RetourArriere' ? 'étape de retour arrière' : 'étape';
  const libelleNombre = plan === 'RetourArriere' ? `${etapes.length} étape${etapes.length > 1 ? 's' : ''} de retour arrière` : `${etapes.length} étape${etapes.length > 1 ? 's' : ''}`;

  return (
    <div>
      <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
        <p className="text-sm text-fg-muted tnum">{libelleNombre}</p>
        {editable && <Button variante="primaire" icone={<Plus className="size-4" aria-hidden />} onClick={() => void ajouter()}>Ajouter une {libelleType}</Button>}
      </div>

      {etapes.length === 0 ? (
        <Card><EmptyState icone={ListChecks} titre={`Aucune ${libelleType}`} description={plan === 'RetourArriere' ? 'Décrivez comment revenir à l’état d’avant si l’intervention échoue.' : 'Décrivez le déroulé de l’intervention, étape par étape.'} action={editable && <Button variante="primaire" icone={<Plus className="size-4" aria-hidden />} onClick={() => void ajouter()}>Ajouter une {libelleType}</Button>} /></Card>
      ) : (
        <ol className="space-y-2.5">
          {etapes.map((e, i) => {
            const meta = metaEtape(e);
            const resp = nomParticipant(mep, e.responsableId);
            const sys = mep.systemes.find((s) => s.id === e.systemeId);
            return (
              <li key={e.id}>
                <Card className="transition-shadow duration-150 hover:shadow-md">
                  <div className="flex flex-wrap items-start gap-x-4 gap-y-3 p-4">
                    <button type="button" onClick={() => setOuverte(e.id)} className="flex min-w-0 flex-1 basis-72 items-start gap-3 rounded-lg text-left" aria-label={`Ouvrir ${e.code}`}>
                      <span className="mt-0.5 rounded-md bg-surface-2 px-2 py-1 font-mono text-xs font-bold">{e.code}</span>
                      <span className="min-w-0">
                        <span className="block font-medium">{e.action || <em className="text-fg-subtle not-italic">Action à décrire</em>}</span>
                        <span className="mt-1 flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-fg-subtle">
                          <span>{sys?.nom ?? 'Système à choisir'}</span>
                          {e.criticite && <Badge ton={TON_CRITICITE[e.criticite]}>{LIBELLE_CRITICITE[e.criticite]}</Badge>}
                          <BadgesEtape etape={e} />
                        </span>
                      </span>
                    </button>

                    <div className="flex items-center gap-2 text-sm text-fg-muted">
                      {resp ? <><Avatar nom={resp} taille="sm" />{resp}</> : <span className="text-fg-subtle">Sans responsable</span>}
                    </div>

                    <div className="flex min-w-24 flex-wrap items-center gap-1.5">
                      {e.dependances.length === 0 ? <span className="text-xs text-fg-subtle">Sans préalable</span> : (
                        <>
                          <span className="text-xs text-fg-subtle">Après</span>
                          {e.dependances.map((d) => {
                            const dep = mep.etapes.find((x) => x.id === d);
                            return (
                              <button key={d} type="button" onClick={() => dep && setOuverte(dep.id)} className="rounded-md border border-line bg-surface-2 px-1.5 py-0.5 font-mono text-[11px] font-semibold text-fg-muted transition-colors hover:border-accent hover:text-accent-text" title={dep?.action}>
                                {dep?.code ?? '?'}
                              </button>
                            );
                          })}
                        </>
                      )}
                    </div>

                    {e.resolutionIds.length > 0 && (
                      <Tooltip content={`${e.resolutionIds.length} résolution(s) liée(s)`}>
                        <span className="inline-flex items-center gap-1 text-xs font-medium text-warn-fg" aria-label={`${e.resolutionIds.length} résolution(s) liée(s)`}>
                          <Wrench className="size-4 text-warn-solid" aria-hidden />{e.resolutionIds.length}
                        </span>
                      </Tooltip>
                    )}

                    {!editable && <StatusPill ton={meta.ton} icone={meta.Icone} anime={meta.anime} taille="sm">{meta.libelle}</StatusPill>}

                    <div className="ml-auto flex items-center gap-0.5">
                      {editable && (
                        <>
                          <BoutonIcone label={`Monter ${e.code}`} taille="sm" disabled={i === 0} onClick={() => void run(() => api.deplacerEtape(mep.id, e.id, 'haut'))}><ArrowUp className="size-4" aria-hidden /></BoutonIcone>
                          <BoutonIcone label={`Descendre ${e.code}`} taille="sm" disabled={i === etapes.length - 1} onClick={() => void run(() => api.deplacerEtape(mep.id, e.id, 'bas'))}><ArrowDown className="size-4" aria-hidden /></BoutonIcone>
                        </>
                      )}
                      <BoutonIcone label={`${editable ? 'Modifier' : 'Voir'} ${e.code}`} taille="sm" onClick={() => setOuverte(e.id)}><Pencil className="size-4" aria-hidden /></BoutonIcone>
                      {editable && <BoutonIcone label={`Supprimer ${e.code}`} taille="sm" onClick={() => setSuppr(e)}><Trash2 className="size-4" aria-hidden /></BoutonIcone>}
                    </div>
                  </div>
                </Card>
              </li>
            );
          })}
        </ol>
      )}

      <EtapeSheet key={courante?.id ?? 'aucune'} etape={courante} plan={plan} editable={editable} onClose={() => setOuverte(null)} />
      <ConfirmDialog
        open={suppr !== null}
        onOpenChange={(o) => { if (!o) setSuppr(null); }}
        titre={`Supprimer ${suppr?.code ?? ''} ?`}
        description={suppr ? `« ${suppr.action || 'Étape sans libellé'} » sera retirée du plan. Impossible si une autre étape en dépend.` : undefined}
        libelleConfirmer="Supprimer l’étape"
        variante="danger"
        onConfirmer={() => suppr ? run(() => api.supprimerEtape(mep.id, suppr.id), `${suppr.code} supprimée.`) : false}
      />
    </div>
  );
}
