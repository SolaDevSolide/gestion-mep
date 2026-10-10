import { useMemo, useState, type KeyboardEvent } from 'react';
import { useSearchParams } from 'react-router-dom';
import { api } from '../api';
import { etapesDuPlan } from '../domaine';
import { construireLignes, indexLignes, pairesDuGroupe, type Lien } from '../graphe';
import { useAsync } from '../hooks';
import { useMep } from '../mepContext';
import type { Mep, TypePlan } from '../types';
import { useAstuce } from './graphe/astuce';
import { BarreSystemes } from './graphe/BarreSystemes';
import { GrapheEtapes, groupeDepuisCle } from './graphe/GrapheEtapes';
import { PanneauEdition, saisieDepuis } from './graphe/PanneauEdition';
import { useModeEspace } from './graphe/EspaceEtape';
import { cn } from '../ui/cn';
import { PanneauLiens } from './graphe/PanneauLiens';
import { AideGraphe, ResumePlan } from './graphe/PanneauResume';
import { descriptionsSystemes } from './graphe/systemes';

const estSaisie = (el: EventTarget | null) => el instanceof HTMLElement && /^(INPUT|TEXTAREA|SELECT)$/.test(el.tagName);


/** Éditeur du plan en graphe (préparation) : systèmes impactés, graphe des étapes, espace d'édition de l'étape ouverte. */
export function EtapesEditeur({ plan }: { plan: TypePlan }) {
  const { mep, run } = useMep();
  const [params] = useSearchParams();
  const modifiable = mep.droits.peutEditerContenu;
  const etapes = etapesDuPlan(mep, plan);
  const [choix, setChoix] = useState<string | null>(() => params.get('etape'));
  const [cleGroupe, setCleGroupe] = useState<string | null>(null);
  const [nouvelle, setNouvelle] = useState<string | null>(null);
  const astuce = useAstuce('');
  const modeEspace = useModeEspace();
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
    // Les touches tapées dans l'espace d'une étape (surcouche, rendue en portail) remontent ici : on les ignore.
    if (estSaisie(ev.target) || ev.defaultPrevented || (ev.target instanceof Element && ev.target.closest('[role="dialog"], [role="alertdialog"]'))) return;
    if (ev.key === 'Escape' && cleGroupe) {
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
      <div className="flex min-h-7 flex-wrap items-center gap-x-4 gap-y-1">
        <ResumePlan etapes={etapes} />
        <p aria-live="polite" className="min-w-0 flex-1 text-[13px] font-semibold text-rollback-solid">{astuce.flash ? astuce.texte : ''}</p>
        <AideGraphe edition={modifiable} />
      </div>
      <div className={cn('relative', etape && modeEspace === 'panneau' && 'min-[1100px]:pr-[608px]')}>
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
        {groupe && (
          <div className="absolute top-3 right-3 z-20">
            <PanneauLiens groupe={groupe} etapes={etapes} onSupprimer={(l) => void delier(l)} onFermer={() => setCleGroupe(null)} />
          </div>
        )}
      </div>
      {etape && (
        <PanneauEdition
          key={etape.id}
          mep={mep}
          etape={etape}
          plan={plan}
          etapes={etapes}
          modifiable={modifiable}
          resolutions={resolutions.donnees ?? []}
          catalogue={catalogue.donnees ?? []}
          focusAction={nouvelle === etape.id}
          onLier={(de, vers) => void lier(de, vers)}
          onDelier={(l) => void delier(l)}
          onSelection={selectionner}
        />
      )}
    </div>
  );
}
