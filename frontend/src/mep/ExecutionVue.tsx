import { ListFilter, Network, Rows3, User } from 'lucide-react';
import { useCallback, useMemo, useState } from 'react';
import { api } from '../api';
import { etapeParDefaut, etapesDuPlan, executantEffectif } from '../domaine';
import { useAsync, useLocalStorageState } from '../hooks';
import { useMep } from '../mepContext';
import type { Etape, Mep, TypePlan } from '../types';
import { cn } from '../ui/cn';
import { MultiSelect } from '../ui/Combobox';
import { EmptyState } from '../ui/EmptyState';
import { Segmented } from '../ui/Segmented';
import { EtapePanneau, type ActionsEtape } from './EtapePanneau';
import { GrapheExecution } from './GrapheExecution';
import { ListeExecution } from './ListeExecution';

/** Vue d'exécution d'un plan : arbre (ou liste) à gauche, panneau de l'étape sélectionnée à droite. */
export function ExecutionVue({ plan }: { plan: TypePlan }) {
  const { mep, run } = useMep();
  const etapes = etapesDuPlan(mep, plan);
  const moi = mep.droits.participantId;

  const [vue, setVue] = useLocalStorageState<'graphe' | 'liste'>('mep.vue-execution', 'graphe');
  const [mesSystemes, setMesSystemes] = useLocalStorageState<string[]>(`mep.${mep.id}.mesSystemes`, []);
  const [aMoi, setAMoi] = useLocalStorageState<boolean>(`mep.${mep.id}.aMoi`, false);
  const [choix, setChoix] = useState<string | null>(null);
  const { donnees: resolutionsTypes } = useAsync(() => api.resolutions(), []);

  const defaut = useMemo(() => etapeParDefaut(etapes, moi), [etapes, moi]);
  const selection = (choix && etapes.some((e) => e.id === choix) ? choix : null) ?? defaut?.id ?? null;
  const etape = etapes.find((e) => e.id === selection) ?? null;

  const filtreActif = mesSystemes.length > 0 || aMoi;
  const garde = useCallback(
    (e: Etape) => (mesSystemes.length === 0 || (e.systemeId !== null && mesSystemes.includes(e.systemeId))) && (!aMoi || (moi !== null && executantEffectif(e) === moi)),
    [mesSystemes, aMoi, moi],
  );
  const nbMiennes = etapes.filter((e) => moi !== null && executantEffectif(e) === moi && e.etat !== 'Reussie').length;
  const visibles = etapes.filter(garde);

  const actions = useMemo<ActionsEtape>(() => {
    const suivante = (ok: boolean) => { if (ok) setChoix(null); return ok; };
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
    return <EmptyState icone={Network} titre="Aucune étape" description="Ajoutez des étapes dans l’onglet correspondant pour pouvoir les exécuter." />;
  }

  return (
    <div>
      <div className="mb-4 flex flex-wrap items-center gap-3">
        <div className="w-full min-w-56 sm:w-80">
          <MultiSelect
            options={mep.systemes.map((s) => ({ valeur: s.id, libelle: s.nom, detail: s.cible }))}
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
        <Segmented
          className="ml-auto"
          label="Mode d’affichage"
          valeur={vue}
          onChange={setVue}
          options={[
            { valeur: 'graphe', libelle: <><Network className="size-4" aria-hidden />Graphe</> },
            { valeur: 'liste', libelle: <><Rows3 className="size-4" aria-hidden />Liste</> },
          ]}
        />
      </div>

      <div className="grid items-start gap-5 xl:grid-cols-[minmax(0,1fr)_minmax(22rem,26rem)]">
        <div className="min-w-0">
          {vue === 'graphe' ? (
            <GrapheExecution mep={mep} etapes={etapes} selectionId={selection} onSelect={setChoix} estEstompee={filtreActif ? (e) => !garde(e) : undefined} />
          ) : (
            visibles.length > 0
              ? <ListeExecution mep={mep} etapes={visibles} selectionId={selection} onSelect={setChoix} />
              : <EmptyState icone={ListFilter} titre="Aucune étape ne correspond" description="Modifiez ou effacez les filtres." />
          )}
        </div>
        <div className="min-w-0 xl:[@media(min-height:760px)]:sticky xl:[@media(min-height:760px)]:top-[calc(var(--entete-h,12rem)+1rem)] xl:[@media(min-height:760px)]:max-h-[calc(100vh-var(--entete-h,12rem)-2rem)] xl:[@media(min-height:760px)]:overflow-y-auto xl:rounded-xl">
          {etape && <EtapePanneau key={etape.id} mep={mep as Mep} etape={etape} resolutionsTypes={resolutionsTypes ?? []} actions={actions} onSelectionner={setChoix} />}
        </div>
      </div>
    </div>
  );
}
