import { ListFilter, Network, User } from 'lucide-react';
import { useCallback, useMemo, useState } from 'react';
import { api } from '../api';
import { etapeParDefaut, etapesDuPlan, executantEffectif } from '../domaine';
import { useLocalStorageState } from '../hooks';
import { useMep } from '../mepContext';
import type { Etape, TypePlan } from '../types';
import { useReferentiels } from '../referentiels';
import { Apparition } from '../ui/Apparition';
import { cn } from '../ui/cn';
import { MultiSelect } from '../ui/Combobox';
import { EmptyState } from '../ui/EmptyState';
import { EtapePanneau, type ActionsEtape } from './EtapePanneau';
import { GrapheEtapes } from './graphe/GrapheEtapes';
import { useModeEspace } from './graphe/EspaceEtape';
import { AideGraphe, ResumePlan } from './graphe/PanneauResume';
import { descriptionsSystemes } from './graphe/systemes';

/** Vue d'exécution d'un plan : graphe des étapes (états, liens verts une fois les préalables réussis), espace de l'étape ouverte. */
export function ExecutionVue({ plan }: { plan: TypePlan }) {
  const { mep, run } = useMep();
  const etapes = etapesDuPlan(mep, plan);
  const moi = mep.droits.participantId;
  const modeEspace = useModeEspace();

  const [mesSystemes, setMesSystemes] = useLocalStorageState<string[]>(`mep.${mep.id}.mesSystemes`, []);
  const [aMoi, setAMoi] = useLocalStorageState<boolean>(`mep.${mep.id}.aMoi`, false);
  const [choix, setChoix] = useState<string | null | undefined>(undefined);
  // Preuves en cours de saisie par étape : ouvrir une autre étape puis revenir ne les perd pas.
  // (Map stable, modifiée sur place : la saisie ne redessine pas le graphe.)
  const [preuves] = useState(() => new Map<string, string>());
  const { catalogue, resolutions: resolutionsTypes, pret } = useReferentiels();
  const descriptions = useMemo(() => descriptionsSystemes(mep, catalogue), [mep, catalogue]);

  const defaut = useMemo(() => etapeParDefaut(etapes, moi), [etapes, moi]);
  // undefined = pas de choix (l'étape par défaut est seulement mise en avant) ; null = fermé ; un id = espace de l'étape ouvert.
  const ouverte = typeof choix === 'string' ? (etapes.find((e) => e.id === choix) ?? null) : null;
  const selection = ouverte?.id ?? (choix === null ? null : (defaut?.id ?? null));

  const filtreActif = mesSystemes.length > 0 || aMoi;
  const garde = useCallback(
    (e: Etape) =>
      (mesSystemes.length === 0 || e.systemeIds.some((s) => mesSystemes.includes(s))) &&
      (!aMoi || (moi !== null && executantEffectif(e) === moi)),
    [mesSystemes, aMoi, moi],
  );
  const nbMiennes = etapes.filter((e) => moi !== null && executantEffectif(e) === moi && e.etat !== 'Reussie').length;
  const visibles = etapes.filter(garde);

  // Un échec de résolution ne déclenche le retour arrière que s'il est encore possible.
  const repliPossible = mep.retourArriereActive && mep.nonRetourFranchi === null;
  const actions = useMemo<ActionsEtape>(() => {
    const suiteEchec = repliPossible ? ' : retour arrière déclenché' : '';
    // Étape réussie : l'espace se ferme ; en échec, il reste ouvert sur les résolutions.
    const suivante = (ok: boolean, resultat: 'Reussie' | 'Echec', id: string) => {
      if (ok) preuves.delete(id);
      if (ok && resultat === 'Reussie') setChoix(undefined);
      return ok;
    };
    return {
      demarrer: (e) => run(() => api.demarrerEtape(mep.id, e.id, e.revision), `${e.code} démarrée.`),
      terminer: async (e, resultat, preuve, commentaire, resultatConstate) =>
        suivante(
          await run(
            () =>
              api.terminerEtape(
                mep.id,
                e.id,
                e.revision,
                resultat,
                preuve,
                resultat === 'Reussie',
                commentaire,
                resultatConstate,
              ),
            resultat === 'Reussie' ? `${e.code} réussie.` : `${e.code} en échec.`,
          ),
          resultat,
          e.id,
        ),
      commenter: (e, commentaire) =>
        run(() => api.commenterEtape(mep.id, e.id, e.revision, commentaire), 'Commentaire enregistré.'),
      autoriserReprise: (e) => run(() => api.autoriserReprise(mep.id, e.id, e.revision), 'Reprise autorisée.'),
      autoriserNonRetour: (e) =>
        run(
          () => api.autoriserNonRetour(mep.id, e.id, e.revision),
          `Passage du point de non-retour ${e.code} autorisé.`,
        ),
      deposerPiece: (e, f, signaler) => run(() => api.deposerPieceEtape(mep.id, e.id, f), undefined, signaler),
      supprimerPiece: (p, signaler) => run(() => api.supprimerPiece(mep.id, p.id), undefined, signaler),
      prendreLaMain: (e, motif) =>
        run(() => api.prendreLaMain(mep.id, e.id, e.revision, motif), `Vous avez pris la main sur ${e.code}.`),
      appliquerResolution: (e, rid) =>
        run(() => api.appliquerResolution(mep.id, e.id, rid, e.revision), 'Résolution démarrée.'),
      controlerCorrection: (r, ordre, res, obs) =>
        run(
          () => api.controlerCorrection(mep.id, r.id, ordre, res, obs),
          res === 'Conforme' ? `Correction ${ordre} conforme.` : `Correction en échec${suiteEchec}.`,
        ),
      controlerVerification: (r, res, obs) =>
        run(
          () => api.controlerVerification(mep.id, r.id, res, obs),
          res === 'Conforme'
            ? 'Résolution réussie : l’étape peut être redémarrée.'
            : `Vérification en échec${suiteEchec}.`,
        ),
    };
  }, [mep.id, run, preuves, repliPossible]);

  if (etapes.length === 0) {
    return <EmptyState icone={Network} titre="Aucune étape" description="Ce plan ne comporte aucune étape." />;
  }

  return (
    // Onglet affiché en une fois, référentiels compris : voir Apparition.
    <Apparition pret={pret}>
      <div>
        <div className="mb-3 flex flex-wrap items-center gap-3">
          <div className="w-full min-w-56 sm:w-80">
            <MultiSelect
              options={mep.systemes.map((s) => ({
                valeur: s.id,
                libelle: s.nom,
                detail: descriptions.get(s.id) || s.cible,
              }))}
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
              aMoi
                ? 'border-accent bg-accent text-accent-fg'
                : 'border-line-strong bg-surface text-fg hover:bg-surface-2',
            )}
          >
            <User className="size-4" aria-hidden /> À moi
            <span
              className={cn(
                'rounded-full px-1.5 text-[11px] font-bold tnum',
                aMoi ? 'bg-white/25' : 'bg-surface-2 text-fg-muted',
              )}
            >
              {nbMiennes}
            </span>
          </button>
          {filtreActif && (
            <button
              type="button"
              className="inline-flex items-center gap-1.5 text-sm text-fg-subtle underline-offset-2 hover:text-fg hover:underline"
              onClick={() => {
                setMesSystemes([]);
                setAMoi(false);
              }}
            >
              <ListFilter className="size-4" aria-hidden />
              Effacer les filtres ({visibles.length}/{etapes.length})
            </button>
          )}
          <span className="ml-auto flex items-center gap-2">
            <ResumePlan etapes={etapes} />
            <AideGraphe edition={false} />
          </span>
        </div>

        {/* Panneau latéral ouvert : le graphe lui laisse la place à droite. */}
        <div className={ouverte && modeEspace === 'panneau' ? 'min-[1100px]:pr-[608px]' : undefined}>
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
        </div>
        {ouverte && (
          <EtapePanneau
            key={ouverte.id}
            mep={mep}
            etape={ouverte}
            resolutionsTypes={resolutionsTypes}
            actions={actions}
            onSelectionner={setChoix}
            onFermer={() => setChoix(null)}
            preuveSaisie={preuves.get(ouverte.id)}
            onPreuveSaisie={(p) => preuves.set(ouverte.id, p)}
          />
        )}
      </div>
    </Apparition>
  );
}
