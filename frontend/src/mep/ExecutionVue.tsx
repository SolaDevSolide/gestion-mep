import { ListFilter, Network, User } from 'lucide-react';
import { useCallback, useMemo, useState, type KeyboardEvent } from 'react';
import { api } from '../api';
import { etapeParDefaut, etapesDuPlan, executantEffectif } from '../domaine';
import { useAsync, useLocalStorageState } from '../hooks';
import { useMep } from '../mepContext';
import type { Etape, TypePlan } from '../types';
import { cn } from '../ui/cn';
import { MultiSelect } from '../ui/Combobox';
import { EmptyState } from '../ui/EmptyState';
import { EtapePanneau, type ActionsEtape } from './EtapePanneau';
import { CLASSES_PANNEAU } from './EtapesEditeur';
import { GrapheEtapes } from './graphe/GrapheEtapes';
import { PanneauResume } from './graphe/PanneauResume';
import { descriptionsSystemes } from './graphe/systemes';

/** Vue d'exécution d'un plan : graphe des étapes (états, liens verts une fois les préalables réussis), panneau de l'étape. */
export function ExecutionVue({ plan }: { plan: TypePlan }) {
  const { mep, run } = useMep();
  const etapes = etapesDuPlan(mep, plan);
  const moi = mep.droits.participantId;

  const [mesSystemes, setMesSystemes] = useLocalStorageState<string[]>(`mep.${mep.id}.mesSystemes`, []);
  const [aMoi, setAMoi] = useLocalStorageState<boolean>(`mep.${mep.id}.aMoi`, false);
  const [choix, setChoix] = useState<string | null | undefined>(undefined);
  const { donnees: resolutionsTypes } = useAsync(() => api.resolutions(), []);
  const catalogue = useAsync(() => api.catalogueSystemes(), []);
  const descriptions = useMemo(() => descriptionsSystemes(mep, catalogue.donnees), [mep, catalogue.donnees]);

  const defaut = useMemo(() => etapeParDefaut(etapes, moi), [etapes, moi]);
  // undefined = pas encore de choix (étape mise en avant par défaut) ; null = panneau fermé.
  const selection = choix === null ? null : (choix && etapes.some((e) => e.id === choix) ? choix : null) ?? defaut?.id ?? null;
  const etape = etapes.find((e) => e.id === selection) ?? null;

  const filtreActif = mesSystemes.length > 0 || aMoi;
  const garde = useCallback(
    (e: Etape) => (mesSystemes.length === 0 || e.systemeIds.some((s) => mesSystemes.includes(s))) && (!aMoi || (moi !== null && executantEffectif(e) === moi)),
    [mesSystemes, aMoi, moi],
  );
  const nbMiennes = etapes.filter((e) => moi !== null && executantEffectif(e) === moi && e.etat !== 'Reussie').length;
  const visibles = etapes.filter(garde);

  const actions = useMemo<ActionsEtape>(() => {
    const suivante = (ok: boolean) => { if (ok) setChoix(undefined); return ok; };
    return {
      demarrer: (e) => run(() => api.demarrerEtape(mep.id, e.id, e.revision), `${e.code} démarrée.`),
      terminer: async (e, resultat, preuve) =>
        suivante(await run(() => api.terminerEtape(mep.id, e.id, e.revision, resultat, preuve, resultat === 'Reussie'), resultat === 'Reussie' ? `${e.code} réussie.` : `${e.code} en échec.`)),
      autoriserReprise: (e) => run(() => api.autoriserReprise(mep.id, e.id, e.revision), 'Reprise autorisée.'),
      autoriserNonRetour: (e) => run(() => api.autoriserNonRetour(mep.id, e.id, e.revision), `Passage du point de non-retour ${e.code} autorisé.`),
      deposerPiece: (e, f, signaler) => run(() => api.deposerPieceEtape(mep.id, e.id, f), undefined, signaler),
      supprimerPiece: (p, signaler) => run(() => api.supprimerPiece(mep.id, p.id), undefined, signaler),
      prendreLaMain: (e, motif) => run(() => api.prendreLaMain(mep.id, e.id, e.revision, motif), `Vous avez pris la main sur ${e.code}.`),
      appliquerResolution: (e, rid) => run(() => api.appliquerResolution(mep.id, e.id, rid, e.revision), 'Résolution démarrée.'),
      controlerCorrection: (r, ordre, res, obs) => run(() => api.controlerCorrection(mep.id, r.id, ordre, res, obs), res === 'Conforme' ? `Correction ${ordre} conforme.` : 'Correction en échec : retour arrière déclenché.'),
      controlerVerification: (r, res, obs) => run(() => api.controlerVerification(mep.id, r.id, res, obs), res === 'Conforme' ? 'Résolution réussie : l’étape peut être redémarrée.' : 'Vérification en échec : retour arrière déclenché.'),
    };
  }, [mep.id, run]);

  if (etapes.length === 0) {
    return <EmptyState icone={Network} titre="Aucune étape" description="Ce plan ne comporte aucune étape." />;
  }

  function clavier(ev: KeyboardEvent<HTMLDivElement>) {
    const t = ev.target;
    if (ev.key === 'Escape' && !ev.defaultPrevented && !(t instanceof HTMLElement && /^(INPUT|TEXTAREA|SELECT)$/.test(t.tagName))) setChoix(null);
  }

  return (
    <div onKeyDown={clavier}>
      <div className="mb-3 flex flex-wrap items-center gap-3">
        <div className="w-full min-w-56 sm:w-80">
          <MultiSelect
            options={mep.systemes.map((s) => ({ valeur: s.id, libelle: s.nom, detail: descriptions.get(s.id) || s.cible }))}
            valeurs={mesSystemes}
            onChange={setMesSystemes}
            placeholder="Mes systèmes : tous"
            placeholderRecherche="Rechercher un système…"
            vide="Aucun système."
          />
        </div>
        <button
          type="button"
          aria-pressed={aMoi}
          onClick={() => setAMoi(!aMoi)}
          className={cn(
            'inline-flex h-9 items-center gap-2 rounded-lg border px-3 text-sm font-medium shadow-sm transition-colors duration-150',
            aMoi ? 'border-accent bg-accent text-accent-fg' : 'border-line-strong bg-surface text-fg hover:bg-surface-2',
          )}
        >
          <User className="size-4" aria-hidden /> À moi
          <span className={cn('rounded-full px-1.5 text-[11px] font-bold tnum', aMoi ? 'bg-white/25' : 'bg-surface-2 text-fg-muted')}>{nbMiennes}</span>
        </button>
        {filtreActif && (
          <button type="button" className="inline-flex items-center gap-1.5 text-sm text-fg-subtle underline-offset-2 hover:text-fg hover:underline" onClick={() => { setMesSystemes([]); setAMoi(false); }}>
            <ListFilter className="size-4" aria-hidden />Effacer les filtres ({visibles.length}/{etapes.length})
          </button>
        )}
        <p className="ml-auto text-[13px] text-fg-subtle">Une étape devient disponible quand tous ses préalables sont réussis.</p>
      </div>

      <div className="grid items-start gap-4 xl:grid-cols-[minmax(0,1fr)_minmax(22rem,26rem)]">
        <GrapheEtapes
          mep={mep}
          plan={plan}
          etapes={etapes}
          mode="execution"
          descriptions={descriptions}
          selection={selection}
          onSelection={setChoix}
          estEstompee={filtreActif ? (e) => !garde(e) : undefined}
        />
        <aside aria-label="Étape sélectionnée" className={CLASSES_PANNEAU}>
          {etape
            ? <EtapePanneau key={etape.id} mep={mep} etape={etape} resolutionsTypes={resolutionsTypes ?? []} actions={actions} onSelectionner={setChoix} />
            : <PanneauResume plan={plan} etapes={etapes} aide={false} />}
        </aside>
      </div>
    </div>
  );
}
