import { CircleAlert, DatabaseBackup, Info, Save, Trash2, X } from 'lucide-react';
import { useEffect, useMemo, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../../api';
import { couleurSysteme, styleSysteme } from '../../couleursSysteme';
import { nomParticipant } from '../../domaine';
import { LIBELLE_CRITICITE } from '../../format';
import { construireLignes, indexLignes, type Lien } from '../../graphe';
import { useDraft, useLocalStorageState, useReportDirty } from '../../hooks';
import { useMep } from '../../mepContext';
import type { Criticite, Etape, EtapeSaisie, Mep, ResolutionType, SystemeCatalogue, TypeEtape, TypePlan } from '../../types';
import { Alerte } from '../../ui/Alerte';
import { Button } from '../../ui/Button';
import { Checkbox } from '../../ui/Checkbox';
import { cn } from '../../ui/cn';
import { EditeurBlocs, ZoneAuto } from '../../ui/EditeurBlocs';
import { Field, Input, Select } from '../../ui/Input';
import { Instructions, Markdown } from '../../ui/Instructions';
import { PiecesJointes } from '../../ui/PiecesJointes';
import { Segmented } from '../../ui/Segmented';
import { Section, StructurePanneau, usePanneauLarge, type OngletPanneau } from './PanneauLateral';
import { SelecteurResolutions } from './SelecteurResolutions';
import { PuceSysteme, systemesDe } from './systemes';

/** Saisie complète d'une étape à partir de son état serveur (pour un PUT qui ne change qu'un champ, ex. les liens). */
export function saisieDepuis(e: Etape): EtapeSaisie {
  return {
    revision: e.revision, action: e.action, systemeIds: e.systemeIds, responsableId: e.responsableId, criticite: e.criticite,
    dureeEstimeeMinutes: e.dureeEstimeeMinutes, instructions: e.instructions, resultatAttendu: e.resultatAttendu,
    dependances: e.dependances, resolutionIds: e.resolutionIds, type: e.type, pointDeNonRetour: e.pointDeNonRetour,
  };
}

/** Pièces jointes d'une étape (dépôt possible tant que la MEP n'est pas clôturée). */
export function PiecesEtape({ etapeId, className }: { etapeId: string; className?: string }) {
  const { mep, run } = useMep();
  return (
    <PiecesJointes
      className={className}
      lectureSeule={mep.phase === 'Cloturee'}
      pilote={mep.droits.estPilote}
      pieces={mep.piecesJointes.filter((p) => p.etapeId === etapeId)}
      onDeposer={(f, signaler) => run(() => api.deposerPieceEtape(mep.id, etapeId, f), undefined, signaler)}
      onSupprimer={(p, signaler) => run(() => api.supprimerPiece(mep.id, p.id), undefined, signaler)}
    />
  );
}

interface Fiche {
  action: string;
  type: TypeEtape;
  systemeIds: string[];
  responsableId: string;
  criticite: Criticite | '';
  duree: string;
  instructions: string;
  resultatAttendu: string;
  resolutionIds: string[];
  pointDeNonRetour: boolean;
}

const ficheDepuis = (e: Etape): Fiche => ({
  action: e.action, type: e.type, systemeIds: e.systemeIds, responsableId: e.responsableId ?? '', criticite: e.criticite ?? '',
  duree: e.dureeEstimeeMinutes === null ? '' : String(e.dureeEstimeeMinutes), instructions: e.instructions, resultatAttendu: e.resultatAttendu,
  resolutionIds: e.resolutionIds, pointDeNonRetour: e.pointDeNonRetour,
});

/** Points à compléter, par onglet (pastilles des onglets). */
export function anomaliesFiche(f: Pick<Fiche, 'action' | 'systemeIds' | 'responsableId' | 'criticite' | 'instructions' | 'resultatAttendu'>) {
  const fiche = [
    !f.action.trim() && 'l’action',
    f.systemeIds.length === 0 && 'au moins un système',
    !f.responsableId && 'le responsable',
    !f.criticite && 'la criticité',
  ].filter((x): x is string => !!x);
  const instructions = [!f.instructions.trim() && 'les instructions', !f.resultatAttendu.trim() && 'le résultat attendu'].filter((x): x is string => !!x);
  return { fiche, instructions };
}

interface Props {
  mep: Mep;
  etape: Etape;
  plan: TypePlan;
  etapes: Etape[];
  modifiable: boolean;
  resolutions: ResolutionType[];
  catalogue: SystemeCatalogue[];
  /** Donne le focus au champ Action (étape tout juste créée). */
  focusAction?: boolean;
  onLier: (de: string, vers: string) => void;
  onDelier: (liens: Lien[]) => void;
  onSelection: (id: string | null) => void;
}

/** Ligne d'une liste de liens (préalables / débloque). */
function ItemLien({ etape, onRetirer, libelleRetrait, onOuvrir }: { etape: Etape; onRetirer?: () => void; libelleRetrait: string; onOuvrir: () => void }) {
  return (
    <li className="flex items-center justify-between gap-2 rounded-lg border border-line bg-surface-2/50 px-3 py-2 text-sm">
      <button type="button" className="min-w-0 truncate text-left hover:text-accent-text" onClick={onOuvrir} title="Afficher cette étape">
        <b className="mr-2 font-mono text-xs font-semibold">{etape.code}</b>{etape.action || 'sans libellé'}
      </button>
      {onRetirer && (
        <button type="button" className="shrink-0 rounded p-1 text-fg-subtle hover:bg-danger-bg hover:text-danger-solid" aria-label={libelleRetrait} onClick={onRetirer}>
          <X className="size-4" aria-hidden />
        </button>
      )}
    </li>
  );
}


/** En-tête commun : code, type, point de non-retour, indicateur de saisie. */
function BadgesEntete({ etape, type, pnr, modifie }: { etape: Etape; type: TypeEtape; pnr: boolean; modifie?: boolean }) {
  return (
    <div className="flex flex-wrap items-center gap-2">
      <span className="rounded-md bg-surface-2 px-2 py-0.5 font-mono text-sm font-bold">{etape.code}</span>
      {type === 'Sauvegarde' && <span className="inline-flex items-center gap-1 rounded border px-1.5 text-[11px] font-semibold" style={{ color: 'var(--sys-0)', borderColor: 'var(--sys-0)' }}><DatabaseBackup className="size-3" aria-hidden />Sauvegarde</span>}
      {pnr && <span className="rounded bg-rollback-bg px-1.5 text-[11px] font-bold text-rollback-fg">⛔ Point de non-retour</span>}
      {modifie && <span role="status" className="inline-flex items-center gap-1 text-xs font-medium text-warn-fg"><span className="size-2 rounded-full bg-warn-solid" aria-hidden />Modifications non enregistrées</span>}
    </div>
  );
}

/** Panneau de droite en préparation : en-tête, onglets Fiche / Instructions / Liens / Résolutions / Pièces jointes, pied fixe. */
export function PanneauEdition(props: Props) {
  const { mep, etape, plan, etapes, modifiable, resolutions, catalogue, focusAction, onLier, onDelier, onSelection } = props;
  const { run } = useMep();
  const navigate = useNavigate();
  const { large } = usePanneauLarge();
  const d = useDraft<Fiche>(ficheDepuis(etape));
  useReportDirty(d.dirty);
  const v = d.value;
  const [onglet, setOnglet] = useLocalStorageState<string>('mep.panneau.onglet-edition', 'fiche');
  const [suppr, setSuppr] = useState(false);
  const [envoi, setEnvoi] = useState(false);
  const champAction = useRef<HTMLInputElement>(null);
  useEffect(() => {
    if (focusAction) { champAction.current?.focus(); champAction.current?.select(); }
  }, [focusAction]);

  const lignes = useMemo(() => construireLignes(etapes), [etapes]);
  const ligneDe = useMemo(() => indexLignes(lignes), [lignes]);
  const r = ligneDe.get(etape.id) ?? 0;
  const prealables = etapes.filter((e) => etape.dependances.includes(e.id));
  const suivants = etapes.filter((e) => e.dependances.includes(etape.id));
  const ajoutsAvant = lignes.slice(0, r).flat().filter((e) => !etape.dependances.includes(e.id));
  const ajoutsApres = lignes.slice(r + 1).flat().filter((e) => !e.dependances.includes(etape.id));
  const nbPieces = mep.piecesJointes.filter((p) => p.etapeId === etape.id).length;

  if (!modifiable) return <PanneauLecture mep={mep} etape={etape} prealables={prealables} suivants={suivants} resolutions={resolutions} nbPieces={nbPieces} onSelection={onSelection} />;

  const catalogueIds = systemesDe(mep, v.systemeIds).map((s) => s.catalogueId).filter((c): c is string => !!c);
  const anomalies = anomaliesFiche(v);

  async function enregistrer(): Promise<boolean> {
    const duree = v.duree.trim() === '' ? null : Math.max(0, Math.round(Number(v.duree)));
    const saisie: EtapeSaisie = {
      ...saisieDepuis(etape),
      action: v.action.trim(), systemeIds: v.systemeIds, responsableId: v.responsableId || null, criticite: v.criticite || null,
      dureeEstimeeMinutes: Number.isFinite(duree) ? duree : null, instructions: v.instructions, resultatAttendu: v.resultatAttendu.trim(),
      resolutionIds: v.resolutionIds, type: plan === 'Deploiement' ? v.type : 'Standard', pointDeNonRetour: plan === 'Deploiement' && v.pointDeNonRetour,
    };
    setEnvoi(true);
    const ok = await run(() => api.modifierEtape(mep.id, etape.id, saisie), `${etape.code} enregistrée.`);
    setEnvoi(false);
    if (ok) d.reset();
    return ok;
  }

  async function creerResolution(titre: string) {
    if (d.dirty && !(await enregistrer())) return;
    const q = new URLSearchParams({ mep: mep.id, etape: etape.id });
    if (titre) q.set('titre', titre);
    navigate(`/parametres/resolutions/nouvelle?${q.toString()}`);
  }

  async function supprimer() {
    const ok = await run(() => api.supprimerEtape(mep.id, etape.id), `${etape.code} supprimée.`);
    if (ok) onSelection(null);
    else setSuppr(false);
  }

  const basculerSysteme = (id: string) =>
    d.update({ systemeIds: v.systemeIds.includes(id) ? v.systemeIds.filter((x) => x !== id) : mep.systemes.filter((s) => s.id === id || v.systemeIds.includes(s.id)).map((s) => s.id) });

  const onglets: OngletPanneau[] = [
    {
      valeur: 'fiche', libelle: 'Fiche', alerte: anomalies.fiche.length > 0 && `À compléter : ${anomalies.fiche.join(', ')}`,
      contenu: (
        <>
          {anomalies.fiche.length > 0 && <Alerte ton="warn" icone={CircleAlert}>À compléter : {anomalies.fiche.join(', ')}.</Alerte>}
          <Section titre="Systèmes touchés" description="Plusieurs possibles. Un système manquant ? Ajoutez-le dans la barre « Systèmes impactés ».">
            <div className="flex flex-wrap gap-2">
              {mep.systemes.map((s) => {
                const actif = v.systemeIds.includes(s.id);
                return (
                  <button
                    key={s.id}
                    type="button"
                    aria-pressed={actif}
                    onClick={() => basculerSysteme(s.id)}
                    className={cn('inline-flex items-center gap-1.5 rounded-full border-[1.5px] px-3 py-1 text-[13px] font-semibold transition-colors', !actif && 'border-line-strong text-fg-subtle hover:text-fg')}
                    style={actif ? styleSysteme(s.nom) : undefined}
                  >
                    <i className="size-2 rounded-full" style={{ background: couleurSysteme(s.nom) }} aria-hidden />{s.nom}
                  </button>
                );
              })}
              {mep.systemes.length === 0 && <span className="text-sm text-fg-subtle">Aucun système dans la MEP.</span>}
            </div>
          </Section>
          <Section titre="Responsabilité et durée">
            <div className={cn('grid gap-4', large ? 'sm:grid-cols-3' : 'sm:grid-cols-2')}>
              <Field label="Responsable">
                {(id) => (
                  <Select id={id} value={v.responsableId} onChange={(e) => d.update({ responsableId: e.target.value })}>
                    <option value="">— choisir —</option>
                    {mep.participants.map((p) => <option key={p.id} value={p.id}>{p.nom}</option>)}
                  </Select>
                )}
              </Field>
              <Field label="Criticité">
                {(id) => (
                  <Select id={id} value={v.criticite} onChange={(e) => d.update({ criticite: e.target.value as Criticite | '' })}>
                    <option value="">— choisir —</option>
                    {(Object.keys(LIBELLE_CRITICITE) as Criticite[]).map((c) => <option key={c} value={c}>{LIBELLE_CRITICITE[c]}</option>)}
                  </Select>
                )}
              </Field>
              <Field label="Durée estimée (min)">
                {(id) => <Input id={id} type="number" min={0} inputMode="numeric" value={v.duree} onChange={(e) => d.update({ duree: e.target.value })} />}
              </Field>
            </div>
          </Section>
          {plan === 'Deploiement' && (
            <Section titre="Nature de l’étape">
              <div className="grid gap-4">
                <Field label="Type d’étape" hint="Une étape de sauvegarde réalise la sauvegarde d’un système pendant la MEP.">
                  {() => (
                    <Segmented
                      label="Type d’étape"
                      valeur={v.type}
                      onChange={(t) => d.update({ type: t })}
                      options={[{ valeur: 'Standard', libelle: 'Standard' }, { valeur: 'Sauvegarde', libelle: <><DatabaseBackup className="size-3.5" aria-hidden />Sauvegarde</> }]}
                    />
                  )}
                </Field>
                <div className="rounded-lg border-[1.5px] border-rollback-solid bg-danger-bg/60 px-3.5 py-3 text-danger-fg">
                  <Checkbox
                    label={<span className="font-semibold">Point de non-retour</span>}
                    description={<span className="text-danger-fg">Démarrer cette étape exigera l’accord du pilote. Ensuite, plus de retour arrière : on corrige en avant.</span>}
                    checked={v.pointDeNonRetour}
                    onChange={(c) => d.update({ pointDeNonRetour: c })}
                  />
                </div>
              </div>
            </Section>
          )}
        </>
      ),
    },
    {
      valeur: 'instructions', libelle: 'Instructions', alerte: anomalies.instructions.length > 0 && `À compléter : ${anomalies.instructions.join(', ')}`,
      contenu: (
        <>
          <Section titre="Instructions exactes" description="Un bloc Texte pour expliquer, un bloc Code par commande : l’opérateur la copiera telle quelle.">
            <EditeurBlocs label="Instructions" valeur={v.instructions} onChange={(md) => d.update({ instructions: md })} disposition={large ? 'cote' : 'onglets'} />
          </Section>
          <Section titre="Résultat attendu" description="Ce que l’opérateur doit constater pour déclarer l’étape réussie (markdown).">
            <div className={cn('grid gap-3', large && 'lg:grid-cols-2')}>
              <ZoneAuto
                aria-label="Résultat attendu"
                value={v.resultatAttendu}
                onChange={(e) => d.update({ resultatAttendu: e.target.value })}
                placeholder="ex. `GET /sante` renvoie 200 avec la version 2.4.0."
                className="border border-line-strong bg-surface text-fg placeholder:text-fg-subtle"
              />
              {(large || v.resultatAttendu.trim()) && (
                <div className="rounded-md border border-dashed border-line bg-surface-2/40 px-3 py-2">
                  <p className="mb-0.5 text-[10px] font-semibold tracking-wide text-fg-subtle uppercase">Aperçu</p>
                  <Markdown source={v.resultatAttendu} vide="Rien à afficher." />
                </div>
              )}
            </div>
          </Section>
        </>
      ),
    },
    {
      valeur: 'liens', libelle: 'Liens', compteur: prealables.length + suivants.length,
      contenu: (
        <>
          <p className="text-xs text-fg-subtle">Les liens sont enregistrés immédiatement. Astuce : dans le graphe, tirez le rond du bas d’une fiche vers celle qui doit l’attendre.</p>
          <Section titre={`Préalables : cette étape attend (${prealables.length})`}>
            {prealables.length === 0 ? <p className="text-sm text-fg-subtle">Aucun préalable.</p> : (
              <ul className="grid gap-1.5">
                {prealables.map((p) => (
                  <ItemLien key={p.id} etape={p} onOuvrir={() => onSelection(p.id)} libelleRetrait={`Retirer le préalable ${p.code}`} onRetirer={() => onDelier([{ de: p.id, vers: etape.id }])} />
                ))}
              </ul>
            )}
            {ajoutsAvant.length > 0 && (
              <Select aria-label="Ajouter un préalable" value="" onChange={(e) => { if (e.target.value) onLier(e.target.value, etape.id); }}>
                <option value="">+ Ajouter un préalable…</option>
                {ajoutsAvant.map((x) => <option key={x.id} value={x.id}>{x.code} · {x.action || 'sans libellé'}</option>)}
              </Select>
            )}
            {r > 0 && prealables.length === 0 && <Alerte ton="run" icone={Info}>Aucun préalable : cette étape pourra démarrer dès le Go.</Alerte>}
          </Section>
          <Section titre={`Débloque : ces étapes l’attendent (${suivants.length})`}>
            {suivants.length === 0 ? <p className="text-sm text-fg-subtle">Aucune étape.</p> : (
              <ul className="grid gap-1.5">
                {suivants.map((s) => (
                  <ItemLien key={s.id} etape={s} onOuvrir={() => onSelection(s.id)} libelleRetrait={`Retirer le lien vers ${s.code}`} onRetirer={() => onDelier([{ de: etape.id, vers: s.id }])} />
                ))}
              </ul>
            )}
            {ajoutsApres.length > 0 && (
              <Select aria-label="Ajouter une étape qui attend celle-ci" value="" onChange={(e) => { if (e.target.value) onLier(etape.id, e.target.value); }}>
                <option value="">+ Ajouter une étape qui l’attend…</option>
                {ajoutsApres.map((x) => <option key={x.id} value={x.id}>{x.code} · {x.action || 'sans libellé'}</option>)}
              </Select>
            )}
          </Section>
        </>
      ),
    },
    {
      valeur: 'resolutions', libelle: 'Résolutions', compteur: v.resolutionIds.length,
      contenu: (
        <Section titre="Résolutions types en cas d’échec" description="Proposées à l’opérateur si l’étape échoue. Enregistrées avec la fiche.">
          <SelecteurResolutions
            resolutions={resolutions}
            catalogue={catalogue}
            liees={v.resolutionIds}
            onChange={(ids) => d.update({ resolutionIds: ids })}
            systemesEtape={catalogueIds}
            onCreer={(titre) => void creerResolution(titre)}
          />
        </Section>
      ),
    },
    { valeur: 'pieces', libelle: 'Pièces jointes', compteur: nbPieces, contenu: <PiecesEtape etapeId={etape.id} /> },
  ];

  const pied = suppr ? (
    <div role="alertdialog" aria-label={`Supprimer ${etape.code}`} className="grid gap-2">
      <p className="text-sm"><b>Supprimer {etape.code} ?</b> Les étapes qui l’attendaient seront rattachées à la ligne au-dessus. Action définitive.</p>
      <div className="flex flex-wrap gap-2">
        <Button variante="danger" taille="sm" icone={<Trash2 className="size-4" aria-hidden />} onClick={() => void supprimer()}>Supprimer définitivement</Button>
        <Button variante="fantome" taille="sm" onClick={() => setSuppr(false)} autoFocus>Annuler</Button>
      </div>
    </div>
  ) : (
    <div className="flex flex-wrap items-center gap-2">
      <Button variante="primaire" taille="sm" icone={<Save className="size-4" aria-hidden />} disabled={!d.dirty} chargement={envoi} onClick={() => void enregistrer()}>Enregistrer</Button>
      {d.dirty ? <Button variante="fantome" taille="sm" onClick={d.reset}>Annuler les modifications</Button> : <span className="text-xs text-fg-subtle">Fiche à jour.</span>}
      <Button variante="danger-doux" taille="sm" className="ml-auto" icone={<Trash2 className="size-4" aria-hidden />} onClick={() => setSuppr(true)}>Supprimer l’étape</Button>
    </div>
  );

  return (
    <StructurePanneau
      onFermer={() => onSelection(null)}
      onglet={onglet}
      onOnglet={setOnglet}
      onglets={onglets}
      pied={pied}
      entete={(
        <div className="grid gap-2">
          <BadgesEntete etape={etape} type={v.type} pnr={v.pointDeNonRetour} modifie={d.dirty} />
          <input
            ref={champAction}
            aria-label="Action"
            value={v.action}
            onChange={(e) => d.update({ action: e.target.value })}
            placeholder="Décrivez l’action (ex. Déploiement de l’API 2.4.0)"
            className="-mx-1.5 w-[calc(100%+0.75rem)] rounded-md border border-transparent bg-transparent px-1.5 py-1 text-lg leading-snug font-semibold tracking-tight placeholder:font-normal placeholder:text-fg-subtle hover:border-line focus:border-accent focus:ring-2 focus:ring-accent/30 focus:outline-none"
          />
        </div>
      )}
    />
  );
}

/** Détail d'une étape en lecture seule (plan figé ou sans droit d'édition), même structure en onglets. */
function PanneauLecture({ mep, etape, prealables, suivants, resolutions, nbPieces, onSelection }: {
  mep: Mep; etape: Etape; prealables: Etape[]; suivants: Etape[]; resolutions: ResolutionType[]; nbPieces: number; onSelection: (id: string | null) => void;
}) {
  const [onglet, setOnglet] = useState('fiche');
  const liees = etape.resolutionIds.map((id) => resolutions.find((r) => r.id === id)?.titre ?? 'Résolution inactive');
  const liste = (l: Etape[], vide: string) => (l.length === 0 ? <p className="text-sm text-fg-subtle">{vide}</p> : (
    <ul className="grid gap-1.5">{l.map((p) => <ItemLien key={p.id} etape={p} libelleRetrait="" onOuvrir={() => onSelection(p.id)} />)}</ul>
  ));
  return (
    <StructurePanneau
      onFermer={() => onSelection(null)}
      onglet={onglet}
      onOnglet={setOnglet}
      entete={(
        <div className="grid gap-2">
          <BadgesEntete etape={etape} type={etape.type} pnr={etape.pointDeNonRetour} />
          <h2 className="text-lg leading-snug font-semibold tracking-tight">{etape.action || 'Étape sans libellé'}</h2>
        </div>
      )}
      onglets={[
        {
          valeur: 'fiche', libelle: 'Fiche',
          contenu: (
            <>
              <Section titre="Systèmes touchés"><div className="flex flex-wrap gap-1.5">{systemesDe(mep, etape.systemeIds).map((s) => <PuceSysteme key={s.id} nom={s.nom} />)}</div></Section>
              <Section titre="Responsabilité et durée">
                <p className="text-sm text-fg-muted">
                  Responsable : <b>{nomParticipant(mep, etape.responsableId) ?? 'non désigné'}</b>
                  {etape.criticite && <> · {LIBELLE_CRITICITE[etape.criticite]}</>}
                  {etape.dureeEstimeeMinutes !== null && <> · {etape.dureeEstimeeMinutes} min</>}
                </p>
              </Section>
            </>
          ),
        },
        {
          valeur: 'instructions', libelle: 'Instructions',
          contenu: (
            <>
              <Section titre="Instructions exactes"><Instructions source={etape.instructions} /></Section>
              <Section titre="Résultat attendu"><Markdown source={etape.resultatAttendu} vide="Non renseigné." /></Section>
            </>
          ),
        },
        {
          valeur: 'liens', libelle: 'Liens', compteur: prealables.length + suivants.length,
          contenu: (
            <>
              <Section titre={`Préalables : cette étape attend (${prealables.length})`}>{liste(prealables, 'Aucun préalable.')}</Section>
              <Section titre={`Débloque : ces étapes l’attendent (${suivants.length})`}>{liste(suivants, 'Aucune étape.')}</Section>
            </>
          ),
        },
        {
          valeur: 'resolutions', libelle: 'Résolutions', compteur: liees.length,
          contenu: <Section titre="Résolutions types en cas d’échec">{liees.length ? <ul className="list-disc pl-5 text-sm">{liees.map((t, i) => <li key={i}>{t}</li>)}</ul> : <p className="text-sm text-fg-subtle">Aucune.</p>}</Section>,
        },
        { valeur: 'pieces', libelle: 'Pièces jointes', compteur: nbPieces, contenu: <PiecesEtape etapeId={etape.id} /> },
      ]}
    />
  );
}

