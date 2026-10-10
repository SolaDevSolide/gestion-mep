import { DatabaseBackup, Info, Plus, Save, Trash2, X } from 'lucide-react';
import { useEffect, useMemo, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../../api';
import { nomParticipant } from '../../domaine';
import { couleurSysteme, styleSysteme } from '../../couleursSysteme';
import { LIBELLE_CRITICITE } from '../../format';
import { construireLignes, indexLignes, type Lien } from '../../graphe';
import { useDraft, useReportDirty } from '../../hooks';
import { useMep } from '../../mepContext';
import type { Criticite, Etape, EtapeSaisie, Mep, ResolutionType, TypeEtape, TypePlan } from '../../types';
import { Alerte } from '../../ui/Alerte';
import { Button } from '../../ui/Button';
import { Card } from '../../ui/Card';
import { Checkbox } from '../../ui/Checkbox';
import { ConfirmDialog } from '../../ui/Dialog';
import { EditeurBlocs } from '../../ui/EditeurBlocs';
import { Field, Input, Select, Textarea } from '../../ui/Input';
import { Instructions, Markdown } from '../../ui/Instructions';
import { PiecesJointes } from '../../ui/PiecesJointes';
import { Segmented } from '../../ui/Segmented';
import { cn } from '../../ui/cn';
import { TitrePanneau } from './PanneauResume';
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

interface Props {
  mep: Mep;
  etape: Etape;
  plan: TypePlan;
  etapes: Etape[];
  modifiable: boolean;
  resolutions: ResolutionType[];
  /** Donne le focus au champ Action (étape tout juste créée). */
  focusAction?: boolean;
  onLier: (de: string, vers: string) => void;
  onDelier: (liens: Lien[]) => void;
  onSelection: (id: string | null) => void;
}

/** Ligne d'une liste de liens (préalables / débloque). */
function ItemLien({ etape, onRetirer, libelleRetrait, onOuvrir }: { etape: Etape; onRetirer?: () => void; libelleRetrait: string; onOuvrir: () => void }) {
  return (
    <li className="flex items-center justify-between gap-2 rounded-md bg-surface-2 px-2 py-1 text-[13px]">
      <button type="button" className="min-w-0 truncate text-left hover:text-accent-text" onClick={onOuvrir} title="Afficher cette étape">
        <b className="mr-1.5 font-mono text-xs font-medium">{etape.code}</b>{etape.action || 'sans libellé'}
      </button>
      {onRetirer && (
        <button type="button" className="shrink-0 rounded px-1 text-fg-subtle hover:text-danger-solid" aria-label={libelleRetrait} onClick={onRetirer}>
          <X className="size-4" aria-hidden />
        </button>
      )}
    </li>
  );
}

/** Panneau de droite en préparation : fiche de l'étape, liens, résolutions types, pièces jointes, suppression. */
export function PanneauEdition({ mep, etape, plan, etapes, modifiable, resolutions, focusAction, onLier, onDelier, onSelection }: Props) {
  const { run } = useMep();
  const navigate = useNavigate();
  const d = useDraft<Fiche>(ficheDepuis(etape));
  useReportDirty(d.dirty);
  const v = d.value;
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

  const catalogueIds = new Set(systemesDe(mep, v.systemeIds).map((s) => s.catalogueId).filter((c): c is string => !!c));
  const resDispo = resolutions
    .filter((x) => x.actif || v.resolutionIds.includes(x.id))
    .map((x) => ({ r: x, meme: x.systemeCatalogueId !== null && catalogueIds.has(x.systemeCatalogueId) }))
    .sort((a, b) => Number(b.meme) - Number(a.meme) || a.r.titre.localeCompare(b.r.titre, 'fr'));

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

  async function creerResolution() {
    if (d.dirty && !(await enregistrer())) return;
    navigate(`/parametres/resolutions/nouvelle?mep=${encodeURIComponent(mep.id)}&etape=${encodeURIComponent(etape.id)}`);
  }

  if (!modifiable) return <PanneauLecture mep={mep} etape={etape} prealables={prealables} suivants={suivants} resolutions={resolutions} onSelection={onSelection} />;

  const basculerSysteme = (id: string) =>
    d.update({ systemeIds: v.systemeIds.includes(id) ? v.systemeIds.filter((x) => x !== id) : mep.systemes.filter((s) => s.id === id || v.systemeIds.includes(s.id)).map((s) => s.id) });

  return (
    <Card className="grid">
      <div className="grid gap-4 p-4">
        <div className="flex items-center justify-between gap-2">
          <span className="font-mono text-[15px] font-medium">{etape.code}</span>
          {plan === 'Deploiement' && (
            <Segmented
              label="Type d’étape"
              valeur={v.type}
              onChange={(t) => d.update({ type: t })}
              options={[{ valeur: 'Standard', libelle: 'Standard' }, { valeur: 'Sauvegarde', libelle: <><DatabaseBackup className="size-3.5" aria-hidden />Sauvegarde</> }]}
            />
          )}
        </div>

        <Field label="Action">{(id) => <Input id={id} ref={champAction} value={v.action} onChange={(e) => d.update({ action: e.target.value })} placeholder="ex. Déploiement de l’API 2.4.0" />}</Field>

        <section className="grid gap-1.5" aria-labelledby={`sys-${etape.id}`}>
          <TitrePanneau id={`sys-${etape.id}`}>Systèmes touchés (plusieurs possibles)</TitrePanneau>
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
            {mep.systemes.length === 0 && <span className="text-[13px] text-fg-subtle">Aucun système dans la MEP.</span>}
          </div>
          <p className="text-xs text-fg-subtle">Un système manquant ? Ajoutez-le à la MEP dans la barre « Systèmes impactés ».</p>
        </section>

        <div className="grid grid-cols-2 gap-3">
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
        </div>
        <Field label="Durée estimée (min)">
          {(id) => <Input id={id} type="number" min={0} inputMode="numeric" className="w-32" value={v.duree} onChange={(e) => d.update({ duree: e.target.value })} />}
        </Field>

        <section className="grid gap-1.5">
          <TitrePanneau>Instructions exactes</TitrePanneau>
          <EditeurBlocs label="Instructions" valeur={v.instructions} onChange={(md) => d.update({ instructions: md })} />
        </section>

        <Field label="Résultat attendu (markdown)">
          {(id) => (
            <div className="grid gap-1.5">
              <Textarea id={id} rows={2} value={v.resultatAttendu} onChange={(e) => d.update({ resultatAttendu: e.target.value })} placeholder="ex. `GET /sante` renvoie 200 avec la version 2.4.0." />
              {v.resultatAttendu.trim() && (
                <div className="rounded-md border border-dashed border-line px-2 py-1.5">
                  <p className="mb-0.5 text-[10px] font-semibold tracking-wide text-fg-subtle uppercase">Aperçu</p>
                  <Markdown source={v.resultatAttendu} />
                </div>
              )}
            </div>
          )}
        </Field>

        <section className="grid gap-1.5">
          <TitrePanneau>Préalables (attend)</TitrePanneau>
          {prealables.length === 0 ? <p className="text-[13px] text-fg-subtle">aucun</p> : (
            <ul className="grid gap-1">
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
        </section>

        <section className="grid gap-1.5">
          <TitrePanneau>Débloque</TitrePanneau>
          {suivants.length === 0 ? <p className="text-[13px] text-fg-subtle">aucune</p> : (
            <ul className="grid gap-1">
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
          <p className="text-xs text-fg-subtle">Les liens sont enregistrés immédiatement.</p>
        </section>

        <section className="grid gap-1.5">
          <TitrePanneau>Résolutions types en cas d’échec</TitrePanneau>
          {resDispo.length === 0 && <p className="text-[13px] text-fg-subtle">Aucune résolution type dans le catalogue.</p>}
          <div className="grid gap-1.5">
            {resDispo.map(({ r: x, meme }) => (
              <Checkbox
                key={x.id}
                checked={v.resolutionIds.includes(x.id)}
                onChange={(c) => d.update({ resolutionIds: c ? [...v.resolutionIds, x.id] : v.resolutionIds.filter((y) => y !== x.id) })}
                label={<>{x.titre}{meme && <span className="ml-1.5 rounded border border-accent px-1 text-[10.5px] font-semibold text-accent-text">même système</span>}{!x.actif && <span className="ml-1.5 text-xs text-fg-subtle">(inactive)</span>}</>}
              />
            ))}
          </div>
          <div><Button taille="sm" icone={<Plus className="size-4" aria-hidden />} onClick={() => void creerResolution()}>Créer une nouvelle fiche de résolution</Button></div>
        </section>

        <PiecesEtape etapeId={etape.id} />

        {plan === 'Deploiement' && (
          <div className="rounded-lg border-[1.5px] border-rollback-solid bg-danger-bg/60 px-3 py-2.5 text-danger-fg">
            <Checkbox
              label={<span className="font-semibold">Point de non-retour</span>}
              description={<span className="text-danger-fg">Démarrer cette étape exigera l’accord du pilote. Ensuite, plus de retour arrière : on corrige en avant.</span>}
              checked={v.pointDeNonRetour}
              onChange={(c) => d.update({ pointDeNonRetour: c })}
            />
          </div>
        )}

        <div className="flex flex-wrap gap-2">
          <Button variante="danger-doux" taille="sm" icone={<Trash2 className="size-4" aria-hidden />} onClick={() => setSuppr(true)}>Supprimer l’étape</Button>
          <Button variante="fantome" taille="sm" onClick={() => onSelection(null)}>Fermer</Button>
        </div>
      </div>

      <div className="sticky bottom-0 flex flex-wrap items-center gap-2 rounded-b-xl border-t border-line bg-surface/95 px-4 py-2.5 backdrop-blur">
        <Button variante="primaire" taille="sm" icone={<Save className="size-4" aria-hidden />} disabled={!d.dirty} chargement={envoi} onClick={() => void enregistrer()}>Enregistrer</Button>
        {d.dirty ? <Button variante="fantome" taille="sm" onClick={d.reset}>Annuler les modifications</Button> : <span className="text-xs text-fg-subtle">Fiche à jour.</span>}
      </div>

      <ConfirmDialog
        open={suppr}
        onOpenChange={setSuppr}
        titre={`Supprimer ${etape.code} ?`}
        description={`« ${etape.action || 'Étape sans libellé'} » sera retirée du plan. Les étapes qui l’attendaient seront rattachées à la ligne au-dessus.`}
        libelleConfirmer="Supprimer l’étape"
        variante="danger"
        onConfirmer={async () => {
          const ok = await run(() => api.supprimerEtape(mep.id, etape.id), `${etape.code} supprimée.`);
          if (ok) onSelection(null);
          return ok;
        }}
      />
    </Card>
  );
}

/** Détail d'une étape en lecture seule (plan figé ou sans droit d'édition). */
function PanneauLecture({ mep, etape, prealables, suivants, resolutions, onSelection }: {
  mep: Mep; etape: Etape; prealables: Etape[]; suivants: Etape[]; resolutions: ResolutionType[]; onSelection: (id: string | null) => void;
}) {
  const liees = etape.resolutionIds.map((id) => resolutions.find((r) => r.id === id)?.titre ?? 'Résolution inactive');
  return (
    <Card className="grid gap-4 p-4">
      <div className="flex flex-wrap items-center gap-2">
        <span className="font-mono text-[15px] font-medium">{etape.code}</span>
        {etape.type === 'Sauvegarde' && <span className="rounded border px-1 text-[11px] font-semibold" style={{ color: 'var(--sys-0)', borderColor: 'var(--sys-0)' }}>Sauvegarde</span>}
        {etape.pointDeNonRetour && <span className="rounded bg-rollback-bg px-1.5 text-[11px] font-bold text-rollback-fg">⛔ Point de non-retour</span>}
      </div>
      <h3 className="text-base leading-snug font-semibold">{etape.action || 'Étape sans libellé'}</h3>
      <div className="flex flex-wrap gap-1.5">{systemesDe(mep, etape.systemeIds).map((s) => <PuceSysteme key={s.id} nom={s.nom} />)}</div>
      <p className="text-[13px] text-fg-muted">
        Responsable : <b>{nomParticipant(mep, etape.responsableId) ?? 'non désigné'}</b>
        {etape.criticite && <> · {LIBELLE_CRITICITE[etape.criticite]}</>}
        {etape.dureeEstimeeMinutes !== null && <> · {etape.dureeEstimeeMinutes} min</>}
      </p>
      <section className="grid gap-1.5"><TitrePanneau>Instructions exactes</TitrePanneau><Instructions source={etape.instructions} /></section>
      <section className="grid gap-1.5"><TitrePanneau>Résultat attendu</TitrePanneau><Markdown source={etape.resultatAttendu} vide="Non renseigné." /></section>
      <section className="grid gap-1.5">
        <TitrePanneau>Préalables (attend)</TitrePanneau>
        {prealables.length === 0 ? <p className="text-[13px] text-fg-subtle">aucun</p> : <ul className="grid gap-1">{prealables.map((p) => <ItemLien key={p.id} etape={p} libelleRetrait="" onOuvrir={() => onSelection(p.id)} />)}</ul>}
      </section>
      <section className="grid gap-1.5">
        <TitrePanneau>Débloque</TitrePanneau>
        {suivants.length === 0 ? <p className="text-[13px] text-fg-subtle">aucune</p> : <ul className="grid gap-1">{suivants.map((s) => <ItemLien key={s.id} etape={s} libelleRetrait="" onOuvrir={() => onSelection(s.id)} />)}</ul>}
      </section>
      {liees.length > 0 && (
        <section className="grid gap-1.5"><TitrePanneau>Résolutions types</TitrePanneau><ul className="list-disc pl-5 text-[13px]">{liees.map((t, i) => <li key={i}>{t}</li>)}</ul></section>
      )}
      <PiecesEtape etapeId={etape.id} />
      <div><Button variante="fantome" taille="sm" onClick={() => onSelection(null)}>Fermer</Button></div>
    </Card>
  );
}
