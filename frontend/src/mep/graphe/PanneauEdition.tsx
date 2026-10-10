import { DatabaseBackup, Save, Trash2, TriangleAlert, X } from 'lucide-react';
import { useEffect, useMemo, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../../api';
import { couleurSysteme, styleSysteme } from '../../couleursSysteme';
import { nomParticipant } from '../../domaine';
import { LIBELLE_CRITICITE } from '../../format';
import { construireLignes, indexLignes, type Lien } from '../../graphe';
import { useDraft, useReportDirty } from '../../hooks';
import { useMep } from '../../mepContext';
import type { Criticite, Etape, EtapeSaisie, Mep, ResolutionType, SystemeCatalogue, TypeEtape, TypePlan } from '../../types';
import { AvisConflit } from '../../ui/AvisConflit';
import { Button } from '../../ui/Button';
import { Checkbox } from '../../ui/Checkbox';
import { cn } from '../../ui/cn';
import { EditeurBlocs, ZoneAuto } from '../../ui/EditeurBlocs';
import { Field, Input, Select } from '../../ui/Input';
import { Instructions, Markdown } from '../../ui/Instructions';
import { PiecesJointes } from '../../ui/PiecesJointes';
import { Segmented } from '../../ui/Segmented';
import { Bloc, Surcouche } from './EspaceEtape';
import { SelecteurResolutions } from './SelecteurResolutions';
import { PuceSysteme, systemesDe } from './systemes';

/** Saisie complète d'une étape à partir de son état serveur (pour un PUT qui ne change qu'un champ, ex. les liens). */
export function saisieDepuis(e: Etape): EtapeSaisie {
  return {
    revision: e.revision, action: e.action, systemeIds: e.systemeIds, criticite: e.criticite,
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
      compact
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
  criticite: Criticite | '';
  duree: string;
  instructions: string;
  resultatAttendu: string;
  resolutionIds: string[];
  pointDeNonRetour: boolean;
}

const ficheDepuis = (e: Etape): Fiche => ({
  action: e.action, type: e.type, systemeIds: e.systemeIds, criticite: e.criticite ?? '',
  duree: e.dureeEstimeeMinutes === null ? '' : String(e.dureeEstimeeMinutes), instructions: e.instructions, resultatAttendu: e.resultatAttendu,
  resolutionIds: e.resolutionIds, pointDeNonRetour: e.pointDeNonRetour,
});

/** Points à compléter (la durée estimée est facultative). */
export function anomaliesFiche(f: Pick<Fiche, 'action' | 'systemeIds' | 'criticite' | 'instructions' | 'resultatAttendu'>): string[] {
  return [
    !f.action.trim() && 'l’action',
    f.systemeIds.length === 0 && 'au moins un système',
    !f.criticite && 'la criticité',
    !f.instructions.trim() && 'les instructions',
    !f.resultatAttendu.trim() && 'le résultat attendu',
  ].filter((x): x is string => !!x);
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

/** Ligne compacte d'une liste de liens (préalables / débloque). */
function ItemLien({ etape, onRetirer, libelleRetrait, onOuvrir }: { etape: Etape; onRetirer?: () => void; libelleRetrait: string; onOuvrir: () => void }) {
  return (
    <li className="inline-flex max-w-full items-center gap-0.5 rounded-full border border-line bg-surface-2/60 py-0.5 pr-0.5 pl-2 text-[13px]">
      <button type="button" className="min-w-0 truncate text-left hover:text-accent-text" onClick={onOuvrir} title={`${etape.code} · ${etape.action || 'sans libellé'}`} aria-label={`${etape.code} ${etape.action || 'sans libellé'}`}>
        <b className="font-mono text-xs font-semibold">{etape.code}</b>
      </button>
      {onRetirer && (
        <button type="button" className="shrink-0 rounded p-0.5 text-fg-subtle hover:bg-danger-bg hover:text-danger-solid" aria-label={libelleRetrait} onClick={onRetirer}>
          <X className="size-3.5" aria-hidden />
        </button>
      )}
    </li>
  );
}

/** Badges de la barre du haut : code, type, point de non-retour. */
function Badges({ etape, type, pnr }: { etape: Etape; type: TypeEtape; pnr: boolean }) {
  return (
    <span className="flex shrink-0 flex-wrap items-center gap-1.5">
      <span className="rounded-md bg-surface-2 px-2 py-0.5 font-mono text-sm font-bold">{etape.code}</span>
      {type === 'Sauvegarde' && <span className="inline-flex items-center gap-1 rounded border px-1.5 text-[11px] font-semibold" style={{ color: 'var(--sys-0)', borderColor: 'var(--sys-0)' }}><DatabaseBackup className="size-3" aria-hidden />Sauvegarde</span>}
      {pnr && <span className="rounded bg-rollback-bg px-1.5 text-[11px] font-bold text-rollback-fg">⛔ Point de non-retour</span>}
    </span>
  );
}

/** Espace d'édition d'une étape (préparation) : barre du haut, colonnes Fiche / Instructions / Liens, résolutions et pièces. */
export function PanneauEdition(props: Props) {
  const { mep, etape, plan, etapes, modifiable, resolutions, catalogue, focusAction, onLier, onDelier, onSelection } = props;
  const { run } = useMep();
  const navigate = useNavigate();
  const d = useDraft<Fiche>(ficheDepuis(etape));
  useReportDirty(d.dirty);
  const v = d.value;
  const [suppr, setSuppr] = useState(false);
  const [garde, setGarde] = useState(false);
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

  if (!modifiable) return <EspaceLecture mep={mep} etape={etape} prealables={prealables} suivants={suivants} resolutions={resolutions} onSelection={onSelection} />;

  const catalogueIds = systemesDe(mep, v.systemeIds).map((s) => s.catalogueId).filter((c): c is string => !!c);
  const anomalies = anomaliesFiche(v);

  async function enregistrer(): Promise<boolean> {
    if (d.conflit) return false;
    const duree = v.duree.trim() === '' ? null : Math.max(0, Math.round(Number(v.duree)));
    const saisie: EtapeSaisie = {
      ...saisieDepuis(etape),
      action: v.action.trim(), systemeIds: v.systemeIds, criticite: v.criticite || null,
      dureeEstimeeMinutes: duree !== null && Number.isFinite(duree) ? duree : null, instructions: v.instructions, resultatAttendu: v.resultatAttendu.trim(),
      resolutionIds: v.resolutionIds, type: plan === 'Deploiement' ? v.type : 'Standard', pointDeNonRetour: plan === 'Deploiement' && v.pointDeNonRetour,
    };
    setEnvoi(true);
    const ok = await run(() => api.modifierEtape(mep.id, etape.id, saisie), `${etape.code} enregistrée.`);
    setEnvoi(false);
    if (ok) d.reset();
    return ok;
  }

  const fermer = () => { if (d.dirty) setGarde(true); else onSelection(null); };

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

  const barre = (
    <div className="grid gap-2">
      <div className="flex flex-wrap items-center gap-x-3 gap-y-2">
        <Badges etape={etape} type={v.type} pnr={v.pointDeNonRetour} />
        <input
          ref={champAction}
          aria-label="Action"
          value={v.action}
          onChange={(e) => d.update({ action: e.target.value })}
          placeholder="Action (ex. Déploiement de l’API 2.4.0)"
          className="min-w-64 flex-1 rounded-md border border-transparent bg-transparent px-1.5 py-1 text-lg leading-snug font-semibold tracking-tight placeholder:font-normal placeholder:text-fg-subtle hover:border-line focus:border-accent focus:ring-2 focus:ring-accent/30 focus:outline-none"
        />
        <span className="flex shrink-0 items-center gap-2">
          {d.dirty && <span role="status" className="inline-flex items-center gap-1 text-xs font-medium text-warn-fg"><span className="size-2 rounded-full bg-warn-solid" aria-hidden />Non enregistré</span>}
          {d.dirty && <Button variante="fantome" taille="sm" onClick={() => { d.reset(); setGarde(false); }}>Annuler</Button>}
          <Button variante="primaire" taille="sm" icone={<Save className="size-4" aria-hidden />} disabled={!d.dirty || d.conflit} chargement={envoi} onClick={() => void enregistrer()}>Enregistrer</Button>
        </span>
      </div>
      {anomalies.length > 0 && <p className="flex items-center gap-1.5 text-xs text-warn-fg"><TriangleAlert className="size-3.5" aria-hidden />À compléter : {anomalies.join(', ')}.</p>}
      <AvisConflit brouillon={d} />
      {garde && (
        <div role="alertdialog" aria-label="Modifications non enregistrées" className="flex flex-wrap items-center gap-2 rounded-lg border border-warn-bd bg-warn-bg px-3 py-2 text-sm text-warn-fg">
          <span className="font-medium">Modifications non enregistrées.</span>
          <Button variante="primaire" taille="sm" onClick={() => void enregistrer().then((ok) => { if (ok) onSelection(null); })}>Enregistrer et fermer</Button>
          <Button taille="sm" onClick={() => onSelection(null)}>Fermer sans enregistrer</Button>
          <Button variante="fantome" taille="sm" onClick={() => setGarde(false)} autoFocus>Continuer</Button>
        </div>
      )}
    </div>
  );

  const gauche = (
    <>
      <Bloc titre="Systèmes">
        <div className="flex flex-wrap gap-1.5">
          {mep.systemes.map((s) => {
            const actif = v.systemeIds.includes(s.id);
            return (
              <button
                key={s.id}
                type="button"
                aria-pressed={actif}
                onClick={() => basculerSysteme(s.id)}
                className={cn('inline-flex items-center gap-1.5 rounded-full border-[1.5px] px-2.5 py-0.5 text-[13px] font-semibold transition-colors', !actif && 'border-line-strong text-fg-subtle hover:text-fg')}
                style={actif ? styleSysteme(s.nom) : undefined}
              >
                <i className="size-2 rounded-full" style={{ background: couleurSysteme(s.nom) }} aria-hidden />{s.nom}
              </button>
            );
          })}
          {mep.systemes.length === 0 && <span className="text-sm text-fg-subtle">Aucun système dans la MEP.</span>}
        </div>
      </Bloc>
      <div className="grid grid-cols-2 gap-3">
        <Field label="Criticité">
          {(id) => (
            <Select id={id} value={v.criticite} onChange={(e) => d.update({ criticite: e.target.value as Criticite | '' })}>
              <option value="">—</option>
              {(Object.keys(LIBELLE_CRITICITE) as Criticite[]).map((c) => <option key={c} value={c}>{LIBELLE_CRITICITE[c]}</option>)}
            </Select>
          )}
        </Field>
        <Field label="Durée (min)">
          {(id) => <Input id={id} type="number" min={0} inputMode="numeric" placeholder="facultatif" value={v.duree} onChange={(e) => d.update({ duree: e.target.value })} />}
        </Field>
      </div>
      {plan === 'Deploiement' && (
        <>
          <Bloc titre="Type">
            <Segmented
              label="Type d’étape"
              valeur={v.type}
              onChange={(t) => d.update({ type: t })}
              options={[{ valeur: 'Standard', libelle: 'Standard' }, { valeur: 'Sauvegarde', libelle: <><DatabaseBackup className="size-3.5" aria-hidden />Sauvegarde</> }]}
            />
          </Bloc>
          <div className="rounded-lg border-[1.5px] border-rollback-solid bg-danger-bg/60 px-3 py-2 text-danger-fg" title="Démarrer cette étape exigera l’accord du pilote ; ensuite, plus de retour arrière.">
            <Checkbox label={<span className="font-semibold">Point de non-retour</span>} checked={v.pointDeNonRetour} onChange={(c) => d.update({ pointDeNonRetour: c })} />
          </div>
        </>
      )}
    </>
  );

  const centre = (
    <>
      <Bloc titre="Instructions exactes">
        <EditeurBlocs label="Instructions" valeur={v.instructions} onChange={(md) => d.update({ instructions: md })} />
      </Bloc>
      <Bloc titre="Résultat attendu">
        <ZoneAuto
          aria-label="Résultat attendu"
          value={v.resultatAttendu}
          onChange={(e) => d.update({ resultatAttendu: e.target.value })}
          placeholder="ex. `GET /sante` renvoie 200 avec la version 2.4.0."
          className="border border-line-strong bg-surface text-fg placeholder:text-fg-subtle"
        />
        {v.resultatAttendu.trim() && <div className="rounded-md border border-dashed border-line bg-surface px-3 py-2"><Markdown source={v.resultatAttendu} /></div>}
      </Bloc>
    </>
  );

  const droite = (
    <>
      <Bloc titre={`Attend (${prealables.length})`}>
        {prealables.length > 0 && (
          <ul className="flex flex-wrap gap-1">
            {prealables.map((p) => (
              <ItemLien key={p.id} etape={p} onOuvrir={() => onSelection(p.id)} libelleRetrait={`Retirer le préalable ${p.code}`} onRetirer={() => onDelier([{ de: p.id, vers: etape.id }])} />
            ))}
          </ul>
        )}
        {r > 0 && prealables.length === 0 && <p className="text-xs text-fg-subtle">Aucun : démarrable dès le Go.</p>}
        {ajoutsAvant.length > 0 && (
          <Select aria-label="Ajouter un préalable" className="h-8 text-[13px]" value="" onChange={(e) => { if (e.target.value) onLier(e.target.value, etape.id); }}>
            <option value="">+ Préalable…</option>
            {ajoutsAvant.map((x) => <option key={x.id} value={x.id}>{x.code} · {x.action || 'sans libellé'}</option>)}
          </Select>
        )}
      </Bloc>
      <Bloc titre={`Débloque (${suivants.length})`}>
        {suivants.length > 0 && (
          <ul className="flex flex-wrap gap-1">
            {suivants.map((s) => (
              <ItemLien key={s.id} etape={s} onOuvrir={() => onSelection(s.id)} libelleRetrait={`Retirer le lien vers ${s.code}`} onRetirer={() => onDelier([{ de: etape.id, vers: s.id }])} />
            ))}
          </ul>
        )}
        {ajoutsApres.length > 0 && (
          <Select aria-label="Ajouter une étape qui attend celle-ci" className="h-8 text-[13px]" value="" onChange={(e) => { if (e.target.value) onLier(etape.id, e.target.value); }}>
            <option value="">+ Étape qui l’attend…</option>
            {ajoutsApres.map((x) => <option key={x.id} value={x.id}>{x.code} · {x.action || 'sans libellé'}</option>)}
          </Select>
        )}
      </Bloc>
      <Bloc titre={`Résolutions en cas d’échec (${v.resolutionIds.length})`}>
        <SelecteurResolutions
          resolutions={resolutions}
          catalogue={catalogue}
          liees={v.resolutionIds}
          onChange={(ids) => d.update({ resolutionIds: ids })}
          systemesEtape={catalogueIds}
          onCreer={(titre) => void creerResolution(titre)}
        />
      </Bloc>
      <PiecesEtape etapeId={etape.id} />
      <div className="border-t border-line pt-3">
        {suppr ? (
          <div role="alertdialog" aria-label={`Supprimer ${etape.code}`} className="grid gap-2 rounded-lg border border-danger-bd bg-danger-bg/60 p-2.5">
            <p className="text-sm font-medium text-danger-fg">Supprimer {etape.code} définitivement ?</p>
            <div className="flex flex-wrap gap-2">
              <Button variante="danger" taille="sm" icone={<Trash2 className="size-4" aria-hidden />} onClick={() => void supprimer()}>Supprimer définitivement</Button>
              <Button variante="fantome" taille="sm" onClick={() => setSuppr(false)} autoFocus>Annuler</Button>
            </div>
          </div>
        ) : (
          <Button variante="danger-doux" taille="sm" icone={<Trash2 className="size-4" aria-hidden />} onClick={() => setSuppr(true)}>Supprimer l’étape</Button>
        )}
      </div>
    </>
  );

  return <Surcouche etiquette={`Étape ${etape.code}`} onDemandeFermeture={fermer} barre={barre} gauche={gauche} centre={centre} droite={droite} />;
}

/** Espace d'une étape en lecture seule (plan figé ou sans droit d'édition). */
function EspaceLecture({ mep, etape, prealables, suivants, resolutions, onSelection }: {
  mep: Mep; etape: Etape; prealables: Etape[]; suivants: Etape[]; resolutions: ResolutionType[]; onSelection: (id: string | null) => void;
}) {
  const liees = etape.resolutionIds.map((id) => resolutions.find((r) => r.id === id)?.titre ?? 'Résolution inactive');
  const liste = (l: Etape[]) => (l.length === 0 ? <p className="text-[13px] text-fg-subtle">Aucune.</p> : (
    <ul className="grid gap-1">{l.map((p) => <ItemLien key={p.id} etape={p} libelleRetrait="" onOuvrir={() => onSelection(p.id)} />)}</ul>
  ));
  const executant = nomParticipant(mep, etape.executantId);
  return (
    <Surcouche
      etiquette={`Étape ${etape.code}`}
      onDemandeFermeture={() => onSelection(null)}
      barre={(
        <div className="flex flex-wrap items-center gap-3">
          <Badges etape={etape} type={etape.type} pnr={etape.pointDeNonRetour} />
          <h2 className="text-lg leading-snug font-semibold tracking-tight">{etape.action || 'Étape sans libellé'}</h2>
        </div>
      )}
      gauche={(
        <>
          <Bloc titre="Systèmes"><div className="flex flex-wrap gap-1.5">{systemesDe(mep, etape.systemeIds).map((s) => <PuceSysteme key={s.id} nom={s.nom} />)}</div></Bloc>
          <p className="text-sm text-fg-muted">
            {etape.criticite ? LIBELLE_CRITICITE[etape.criticite] : 'Criticité non précisée'}
            {etape.dureeEstimeeMinutes !== null && <> · {etape.dureeEstimeeMinutes} min</>}
            {executant && <><br />Exécutée par <b>{executant}</b></>}
          </p>
        </>
      )}
      centre={(
        <>
          <Bloc titre="Instructions exactes"><Instructions source={etape.instructions} /></Bloc>
          <Bloc titre="Résultat attendu"><Markdown source={etape.resultatAttendu} vide="Non renseigné." /></Bloc>
        </>
      )}
      droite={(
        <>
          <Bloc titre={`Attend (${prealables.length})`}>{liste(prealables)}</Bloc>
          <Bloc titre={`Débloque (${suivants.length})`}>{liste(suivants)}</Bloc>
          <Bloc titre={`Résolutions (${liees.length})`}>{liees.length ? <ul className="list-disc pl-5 text-[13px]">{liees.map((t, i) => <li key={i}>{t}</li>)}</ul> : <p className="text-[13px] text-fg-subtle">Aucune.</p>}</Bloc>
          <PiecesEtape etapeId={etape.id} />
        </>
      )}
    />
  );
}
