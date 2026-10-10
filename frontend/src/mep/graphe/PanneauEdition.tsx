import { DatabaseBackup, Save, TriangleAlert } from 'lucide-react';
import { useEffect, useMemo, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../../api';
import { nomParticipant } from '../../domaine';
import { LIBELLE_CRITICITE } from '../../format';
import { construireLignes, indexLignes, type Lien } from '../../graphe';
import { useDraft, useReportDirty } from '../../hooks';
import { useMep } from '../../mepContext';
import type { Criticite, Etape, EtapeSaisie, Mep, ResolutionType, SystemeCatalogue, TypeEtape, TypePlan } from '../../types';
import { AvisConflit } from '../../ui/AvisConflit';
import { Button } from '../../ui/Button';
import { Instructions, Markdown } from '../../ui/Instructions';
import { ColonneFiche } from './ColonneFiche';
import { ColonneInstructions } from './ColonneInstructions';
import { ColonneLiens, ItemLien, PiecesEtape } from './ColonneLiens';
import { Bloc, Surcouche } from './EspaceEtape';
import { PuceSysteme, systemesDe } from './systemes';

export { PiecesEtape };

/** Saisie complète d'une étape à partir de son état serveur (pour un PUT qui ne change qu'un champ, ex. les liens). */
export function saisieDepuis(e: Etape): EtapeSaisie {
  return {
    revision: e.revision, action: e.action, systemeIds: e.systemeIds, criticite: e.criticite,
    dureeEstimeeMinutes: e.dureeEstimeeMinutes, instructions: e.instructions, resultatAttendu: e.resultatAttendu,
    dependances: e.dependances, resolutionIds: e.resolutionIds, type: e.type, pointDeNonRetour: e.pointDeNonRetour,
  };
}

/** Brouillon de la fiche d'une étape (champs saisis tels quels, ex. la durée en texte). */
export interface Fiche {
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
    void navigate(`/parametres/resolutions/nouvelle?${q.toString()}`);
  }

  async function supprimer() {
    const ok = await run(() => api.supprimerEtape(mep.id, etape.id), `${etape.code} supprimée.`);
    if (ok) onSelection(null);
    else setSuppr(false);
  }

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

  const gauche = <ColonneFiche mep={mep} plan={plan} fiche={v} onChange={d.update} />;
  const centre = <ColonneInstructions fiche={v} onChange={d.update} />;
  const droite = (
    <ColonneLiens
      etape={etape}
      ligne={r}
      prealables={prealables}
      suivants={suivants}
      ajoutsAvant={ajoutsAvant}
      ajoutsApres={ajoutsApres}
      resolutions={resolutions}
      catalogue={catalogue}
      resolutionIds={v.resolutionIds}
      onResolutions={(ids) => d.update({ resolutionIds: ids })}
      systemesEtape={catalogueIds}
      onCreerResolution={(titre) => void creerResolution(titre)}
      onLier={onLier}
      onDelier={onDelier}
      onSelection={onSelection}
      suppr={suppr}
      onSuppr={setSuppr}
      onSupprimer={() => void supprimer()}
    />
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
