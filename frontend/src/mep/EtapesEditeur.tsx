import { useEffect, useMemo, useState, type KeyboardEvent } from 'react';
import { useSearchParams } from 'react-router-dom';
import { api } from '../api';
import { etapesDuPlan } from '../domaine';
import { construireLignes, indexLignes, pairesDuGroupe, type Lien } from '../graphe';
import { useMep } from '../mepContext';
import { useAsync } from '../hooks';
import type { Mep, ModeleEtape, TypePlan } from '../types';
import { useAstuce } from './graphe/astuce';
import { BarreSystemes } from './graphe/BarreSystemes';
import { GrapheEtapes, groupeDepuisCle } from './graphe/GrapheEtapes';
import { PanneauEdition, saisieDepuis } from './graphe/PanneauEdition';
import { useModeEspace } from './graphe/EspaceEtape';
import { useReferentiels } from '../referentiels';
import { Apparition } from '../ui/Apparition';
import { cn } from '../ui/cn';
import { PanneauLiens } from './graphe/PanneauLiens';
import { AideGraphe, ResumePlan } from './graphe/PanneauResume';
import { estSimplifiee } from './typeMep';
import { descriptionsSystemes } from './graphe/systemes';
import {
  apercuDepuisModele,
  BoutonsEtapesModeles,
  ID_FLOTTANTE,
  inserables,
  ParcourirDialog,
  systemesAbsents,
} from './graphe/EtapesModeles';

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
  const afficherAstuce = astuce.afficher;
  const modeEspace = useModeEspace();
  const { catalogue, resolutions, pret } = useReferentiels();
  const descriptions = useMemo(() => descriptionsSystemes(mep, catalogue), [mep, catalogue]);
  // Bibliothèque d'étapes modèles : menu des plus utilisées, fenêtre « Parcourir », mention « Copie de… ».
  const bibliotheque = useAsync(() => api.etapesModeles(), []);
  const modeles = useMemo(() => inserables(bibliotheque.donnees ?? [], plan), [bibliotheque.donnees, plan]);
  const [parcourir, setParcourir] = useState(false);
  // Copie choisie, pas encore posée : elle flotte au-dessus du graphe (rien n'est écrit avant qu'on la pose).
  const [flottante, setFlottante] = useState<ModeleEtape | null>(null);
  const apercu = useMemo(() => (flottante ? apercuDepuisModele(mep, plan, flottante) : null), [flottante, mep, plan]);

  const selection = choix && etapes.some((e) => e.id === choix) ? choix : null;
  const etape = etapes.find((e) => e.id === selection) ?? null;
  const groupe = groupeDepuisCle(etapes, cleGroupe);
  const code = (id: string) => mep.etapes.find((e) => e.id === id)?.code ?? '?';

  // Saisie non enregistrée dans l'espace ouvert : changer d'étape passe d'abord par son choix enregistrer / abandonner.
  const [saisieOuverte, setSaisieOuverte] = useState(false);
  const [demande, setDemande] = useState<string | null | undefined>(undefined);

  function ouvrir(id: string | null) {
    setChoix(id);
    setNouvelle(null);
    setDemande(undefined);
    if (id) setCleGroupe(null);
  }

  function selectionner(id: string | null) {
    if (etape && saisieOuverte && id !== etape.id) setDemande(id);
    else ouvrir(id);
  }

  function choisirModele(m: ModeleEtape) {
    setParcourir(false);
    setFlottante(m);
    setChoix(null);
    setCleGroupe(null);
    astuce.afficher(
      `Copie de « ${m.action} » : glissez-la sur une ligne ou cliquez « Poser ici ». Échap pour annuler.`,
    );
  }

  function annulerFlottante() {
    setFlottante(null);
    astuce.afficher('Insertion annulée.');
  }

  // Échap annule l'insertion (hors fenêtres et champs de saisie).
  useEffect(() => {
    if (!flottante) return;
    function touche(ev: globalThis.KeyboardEvent) {
      if (ev.key !== 'Escape' || ev.defaultPrevented || estSaisie(ev.target)) return;
      if (document.querySelector('[role="dialog"], [role="alertdialog"], [role="menu"]')) return;
      setFlottante(null);
      afficherAstuce('Insertion annulée.');
    }
    document.addEventListener('keydown', touche);
    return () => document.removeEventListener('keydown', touche);
  }, [flottante, afficherAstuce]);

  /** Pose la copie flottante : ligne affichée (null = nouvelle ligne en bas), position (absente = fin de ligne). */
  async function poser(ligne: number | null, position?: number) {
    const m = flottante;
    if (!m) return;
    const avant = new Set(mep.etapes.map((e) => e.id));
    let apres: Mep | undefined;
    const nbLignes = construireLignes(etapes).length;
    const ok = await run(
      async () =>
        (apres = await api.insererEtapeModele(
          mep.id,
          m.id,
          plan,
          ligne === null || ligne >= nbLignes ? undefined : ligne,
          position,
        )),
    );
    if (!ok) return;
    setFlottante(null);
    bibliotheque.recharger();
    const creee = apres?.etapes.find((e) => !avant.has(e.id));
    if (!creee) return;
    setChoix(creee.id);
    setCleGroupe(null);
    const absents = systemesAbsents(mep, m, catalogue);
    astuce.afficher(
      `${creee.code} posée, copie de « ${m.action} ».` +
        (absents.length
          ? ` ${absents.join(', ')} hors de la MEP : ${absents.length > 1 ? 'systèmes non repris' : 'système non repris'}.`
          : ' Reliez-la aux autres étapes.'),
    );
  }

  async function ajouter(ligne: number | null) {
    if (flottante) {
      await poser(ligne);
      return;
    }
    const avant = new Set(mep.etapes.map((e) => e.id));
    let apres: Mep | undefined;
    const ok = await run(
      async () => (apres = await api.ajouterEtape(mep.id, plan, ligne ?? undefined)),
      'Étape ajoutée.',
    );
    const creee = ok && apres ? apres.etapes.find((e) => !avant.has(e.id)) : undefined;
    if (creee) {
      setChoix(creee.id);
      setCleGroupe(null);
      setNouvelle(creee.id);
    }
  }

  async function placer(id: string, ligne: number, position: number, auClavier = false) {
    if (id === ID_FLOTTANTE) {
      await poser(ligne, position);
      return;
    }
    const avant = indexLignes(construireLignes(etapes)).get(id);
    const ok = await run(() => api.placerEtape(mep.id, id, ligne, position), `${code(id)} déplacée.`);
    if (ok) {
      // Au clavier, la fiche garde le focus dans le graphe pour enchaîner les déplacements.
      if (!auClavier) selectionner(id);
      if (avant !== ligne)
        astuce.afficher(`${code(id)} déplacée : liens recréés avec la ligne au-dessus et la ligne en dessous.`);
    }
  }

  async function lier(de: string, vers: string) {
    const cible = mep.etapes.find((e) => e.id === vers);
    if (!cible) return;
    if (cible.dependances.includes(de)) {
      astuce.afficher(`Le lien ${code(de)} → ${code(vers)} existe déjà.`);
      return;
    }
    const message = `Lien créé : ${code(vers)} attendra ${code(de)}.`;
    const ok = await run(
      () => api.modifierEtape(mep.id, vers, { ...saisieDepuis(cible), dependances: [...cible.dependances, de] }),
      message,
    );
    if (ok) astuce.afficher(message);
  }

  async function delier(liens: Lien[]) {
    if (liens.length === 0) return;
    const parCible = new Map<string, string[]>();
    for (const l of liens) parCible.set(l.vers, [...(parCible.get(l.vers) ?? []), l.de]);
    for (const [vers, sources] of parCible) {
      const cible = mep.etapes.find((e) => e.id === vers);
      if (!cible) continue;
      const ok = await run(() =>
        api.modifierEtape(mep.id, vers, {
          ...saisieDepuis(cible),
          dependances: cible.dependances.filter((d) => !sources.includes(d)),
        }),
      );
      if (!ok) return;
    }
    astuce.afficher(
      liens.length > 1
        ? `${liens.length} liens supprimés.`
        : `Lien supprimé : ${code(liens[0].de)} → ${code(liens[0].vers)}.`,
    );
    setCleGroupe(null);
  }

  function clavier(ev: KeyboardEvent<HTMLDivElement>) {
    // Les touches tapées dans l'espace d'une étape (surcouche, rendue en portail) remontent ici : on les ignore.
    if (
      estSaisie(ev.target) ||
      ev.defaultPrevented ||
      (ev.target instanceof Element && ev.target.closest('[role="dialog"], [role="alertdialog"]'))
    )
      return;
    // Échap pendant une insertion : géré par l'écouteur du document.
    if (ev.key === 'Escape' && flottante) return;
    if (ev.key === 'Escape' && cleGroupe) {
      setChoix(null);
      setCleGroupe(null);
    } else if ((ev.key === 'Delete' || ev.key === 'Backspace') && groupe && modifiable) {
      ev.preventDefault();
      void delier(pairesDuGroupe(groupe));
    }
  }

  return (
    // Onglet affiché en une fois, référentiels compris (systèmes impactés, résolutions) : voir Apparition.
    <Apparition pret={pret}>
      {/* Raccourcis délégués (Échap, Suppr) : les touches viennent des fiches et liens focusables du graphe. */}
      {/* eslint-disable-next-line jsx-a11y/no-static-element-interactions */}
      <div className="grid gap-3" onKeyDown={clavier}>
        <BarreSystemes
          modifiable={modifiable}
          catalogue={catalogue}
          descriptions={descriptions}
          onMessage={astuce.afficher}
        />
        <div className="flex min-h-7 flex-wrap items-center gap-x-4 gap-y-1">
          <ResumePlan etapes={etapes} sansDuree={estSimplifiee(mep)} />
          <p aria-live="polite" className="min-w-0 flex-1 text-[13px] font-semibold text-rollback-solid">
            {astuce.flash ? astuce.texte : ''}
          </p>
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
            onGroupeSelection={(c) => {
              if (c && etape && saisieOuverte) {
                setDemande(null);
                return;
              }
              setCleGroupe(c);
              if (c) setChoix(null);
            }}
            onPlacer={(id, l, p, auClavier) => void placer(id, l, p, auClavier)}
            onLier={(de, vers) => void lier(de, vers)}
            onAjouter={(l) => void ajouter(l)}
            onMessage={astuce.afficher}
            flottante={apercu ? { etape: apercu, onAnnuler: annulerFlottante } : null}
            enTete={
              modifiable && (
                <BoutonsEtapesModeles
                  mep={mep}
                  modeles={modeles}
                  catalogue={catalogue}
                  onChoisir={choisirModele}
                  onParcourir={() => setParcourir(true)}
                />
              )
            }
          />
          {groupe && (
            <div className="absolute top-3 right-3 z-20">
              <PanneauLiens
                groupe={groupe}
                etapes={etapes}
                onSupprimer={(l) => void delier(l)}
                onFermer={() => setCleGroupe(null)}
              />
            </div>
          )}
        </div>
        {modifiable && (
          <ParcourirDialog
            ouvert={parcourir}
            mep={mep}
            plan={plan}
            modeles={modeles}
            catalogue={catalogue}
            onFermer={() => setParcourir(false)}
            onInserer={choisirModele}
          />
        )}
        {etape && (
          <PanneauEdition
            key={etape.id}
            mep={mep}
            etape={etape}
            plan={plan}
            etapes={etapes}
            modifiable={modifiable}
            resolutions={resolutions}
            catalogue={catalogue}
            modeles={bibliotheque.donnees}
            onModeleCree={() => bibliotheque.recharger()}
            focusAction={nouvelle === etape.id}
            onLier={(de, vers) => void lier(de, vers)}
            onDelier={(l) => void delier(l)}
            onSelection={ouvrir}
            onDirty={setSaisieOuverte}
            demande={demande}
            onAnnulerDemande={() => setDemande(undefined)}
          />
        )}
      </div>
    </Apparition>
  );
}
