import { useMemo, useState, type KeyboardEvent } from 'react';
import { useSearchParams } from 'react-router-dom';
import { api } from '../api';
import { etapesDuPlan } from '../domaine';
import { construireLignes, indexLignes, pairesDuGroupe, type Lien } from '../graphe';
import { useAsync } from '../hooks';
import { useMep } from '../mepContext';
import type { Mep, TypePlan } from '../types';
import { cn } from '../ui/cn';
import { useAstuce } from './graphe/astuce';
import { BarreSystemes } from './graphe/BarreSystemes';
import { GrapheEtapes, groupeDepuisCle } from './graphe/GrapheEtapes';
import { PanneauEdition, saisieDepuis } from './graphe/PanneauEdition';
import { PanneauLiens } from './graphe/PanneauLiens';
import { PanneauResume } from './graphe/PanneauResume';
import { descriptionsSystemes } from './graphe/systemes';

const AIDE = 'Préparation : glissez une fiche pour la déplacer ; tirez le rond du bas d’une fiche vers une autre pour un lien particulier ; cliquez sur un lien pour le sélectionner.';
const estSaisie = (el: EventTarget | null) => el instanceof HTMLElement && /^(INPUT|TEXTAREA|SELECT)$/.test(el.tagName);

/** Classes du panneau de droite (collant sous l'en-tête de la MEP sur grand écran). */
export const CLASSES_PANNEAU = 'min-w-0 xl:sticky xl:top-[calc(var(--entete-h,12rem)+1rem)] xl:max-h-[calc(100vh-var(--entete-h,12rem)-2rem)] xl:overflow-y-auto xl:rounded-xl';

/** Éditeur du plan en graphe (préparation) : systèmes impactés, graphe des étapes, panneau de droite. */
export function EtapesEditeur({ plan }: { plan: TypePlan }) {
  const { mep, run } = useMep();
  const [params] = useSearchParams();
  const modifiable = mep.droits.peutEditerContenu;
  const etapes = etapesDuPlan(mep, plan);
  const [choix, setChoix] = useState<string | null>(() => params.get('etape'));
  const [cleGroupe, setCleGroupe] = useState<string | null>(null);
  const [nouvelle, setNouvelle] = useState<string | null>(null);
  const astuce = useAstuce(modifiable ? AIDE : 'Lecture seule : cliquez sur une étape pour afficher son détail.');
  const catalogue = useAsync(() => api.catalogueSystemes(), []);
  const resolutions = useAsync(() => api.resolutions(), []);
  const descriptions = useMemo(() => descriptionsSystemes(mep, catalogue.donnees), [mep, catalogue.donnees]);

  const selection = choix && etapes.some((e) => e.id === choix) ? choix : null;
  const etape = etapes.find((e) => e.id === selection) ?? null;
  const groupe = groupeDepuisCle(etapes, cleGroupe);
  const code = (id: string) => mep.etapes.find((e) => e.id === id)?.code ?? '?';

  function selectionner(id: string | null) {
    setChoix(id);
    setNouvelle(null);
    if (id) setCleGroupe(null);
  }

  async function ajouter(ligne: number | null) {
    const avant = new Set(mep.etapes.map((e) => e.id));
    let apres: Mep | undefined;
    const ok = await run(async () => (apres = await api.ajouterEtape(mep.id, plan, ligne ?? undefined)), 'Étape ajoutée.');
    const creee = ok && apres ? apres.etapes.find((e) => !avant.has(e.id)) : undefined;
    if (creee) {
      setChoix(creee.id);
      setCleGroupe(null);
      setNouvelle(creee.id);
    }
  }

  async function placer(id: string, ligne: number, position: number) {
    const avant = indexLignes(construireLignes(etapes)).get(id);
    const ok = await run(() => api.placerEtape(mep.id, id, ligne, position), `${code(id)} déplacée.`);
    if (ok) {
      selectionner(id);
      if (avant !== ligne) astuce.afficher(`${code(id)} déplacée : liens recréés avec la ligne au-dessus et la ligne en dessous.`);
    }
  }

  async function lier(de: string, vers: string) {
    const cible = mep.etapes.find((e) => e.id === vers);
    if (!cible) return;
    if (cible.dependances.includes(de)) { astuce.afficher(`Le lien ${code(de)} → ${code(vers)} existe déjà.`); return; }
    const message = `Lien créé : ${code(vers)} attendra ${code(de)}.`;
    const ok = await run(() => api.modifierEtape(mep.id, vers, { ...saisieDepuis(cible), dependances: [...cible.dependances, de] }), message);
    if (ok) astuce.afficher(message);
  }

  async function delier(liens: Lien[]) {
    if (liens.length === 0) return;
    const parCible = new Map<string, string[]>();
    for (const l of liens) parCible.set(l.vers, [...(parCible.get(l.vers) ?? []), l.de]);
    for (const [vers, sources] of parCible) {
      const cible = mep.etapes.find((e) => e.id === vers);
      if (!cible) continue;
      const ok = await run(() => api.modifierEtape(mep.id, vers, { ...saisieDepuis(cible), dependances: cible.dependances.filter((d) => !sources.includes(d)) }));
      if (!ok) return;
    }
    astuce.afficher(liens.length > 1 ? `${liens.length} liens supprimés.` : `Lien supprimé : ${code(liens[0].de)} → ${code(liens[0].vers)}.`);
    setCleGroupe(null);
  }

  function clavier(ev: KeyboardEvent<HTMLDivElement>) {
    if (estSaisie(ev.target) || ev.defaultPrevented) return;
    if (ev.key === 'Escape' && (selection || cleGroupe)) {
      setChoix(null);
      setCleGroupe(null);
    } else if ((ev.key === 'Delete' || ev.key === 'Backspace') && groupe && modifiable) {
      ev.preventDefault();
      void delier(pairesDuGroupe(groupe));
    }
  }

  return (
    <div className="grid gap-3" onKeyDown={clavier}>
      <BarreSystemes modifiable={modifiable} catalogue={catalogue.donnees ?? []} descriptions={descriptions} onMessage={astuce.afficher} />
      <p aria-live="polite" className={cn('min-h-[1.4em] text-[13px]', astuce.flash ? 'font-semibold text-rollback-solid' : 'text-fg-subtle')}>{astuce.texte}</p>
      <div className="grid items-start gap-4 xl:grid-cols-[minmax(0,1fr)_380px]">
        <GrapheEtapes
          mep={mep}
          plan={plan}
          etapes={etapes}
          mode="edition"
          modifiable={modifiable}
          descriptions={descriptions}
          selection={selection}
          onSelection={selectionner}
          groupeSelection={groupe?.cle ?? null}
          onGroupeSelection={(c) => { setCleGroupe(c); if (c) setChoix(null); }}
          onPlacer={(id, l, p) => void placer(id, l, p)}
          onLier={(de, vers) => void lier(de, vers)}
          onAjouter={(l) => void ajouter(l)}
          onMessage={astuce.afficher}
        />
        <aside aria-label="Détail de la sélection" className={CLASSES_PANNEAU}>
          {groupe ? (
            <PanneauLiens groupe={groupe} etapes={etapes} onSupprimer={(l) => void delier(l)} onFermer={() => setCleGroupe(null)} />
          ) : etape ? (
            <PanneauEdition
              key={etape.id}
              mep={mep}
              etape={etape}
              plan={plan}
              etapes={etapes}
              modifiable={modifiable}
              resolutions={resolutions.donnees ?? []}
              focusAction={nouvelle === etape.id}
              onLier={(de, vers) => void lier(de, vers)}
              onDelier={(l) => void delier(l)}
              onSelection={selectionner}
            />
          ) : (
            <PanneauResume plan={plan} etapes={etapes} aide={modifiable} />
          )}
        </aside>
      </div>
    </div>
  );
}
