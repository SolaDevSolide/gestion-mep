import { ChevronsDownUp, ChevronsUpDown, PencilLine } from 'lucide-react';
import { useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { useSearchParams } from 'react-router-dom';
import { api } from '../api';
import { AuthContext } from '../auth';
import { etapesDuPlan } from '../domaine';
import { useAsync, useLocalStorageState } from '../hooks';
import { useMep } from '../mepContext';
import type { LienRedondant, Mep, ModeleEtape, TypePlan } from '../types';
import { useReferentiels } from '../referentiels';
import { Apparition } from '../ui/Apparition';
import { SqueletteCadreGraphe } from './SqueletteMep';
import { BoutonIcone, Button } from '../ui/Button';
import { cn } from '../ui/cn';
import { apiGraphe, type Deplacement, type PlaceEtape } from './graphe/apiGraphe';
import { useAstuce } from './graphe/astuce';
import { useModeEspace } from './graphe/EspaceEtape';
import {
  apercuDepuisModele,
  BoutonsEtapesModeles,
  ID_FLOTTANTE,
  inserables,
  modelesFrequents,
  ParcourirDialog,
  systemesAbsents,
} from './graphe/EtapesModeles';
import { GrapheLibre, type ActionsGraphe } from './graphe/GrapheLibre';
import { ordreGlobal, reordonner, type LienModele } from './graphe/modeleGraphe';
import { BoutonOptimiserLiens, PanneauOptimisation } from './graphe/OptimisationLiens';
import { PanneauEdition } from './graphe/PanneauEdition';
import { AideGraphe, MenuPlusGraphe, ResumePlan } from './graphe/PanneauResume';
import { EmplacementTiroir, useFenetreBasse, useFenetreEtroite } from './graphe/petitEcran';
import { useRepliMenu } from '../layout/menuLateral';
import { BoutonExportExcel } from '../ui/BoutonExportExcel';
import { BacPlan, CercleDialog, EnTeteVueSection, NouvelleSectionDialog } from './graphe/SectionsPlan';
import { estDepliee, infoSection, sectionsDuPlan, trouverCercle, type CercleSection } from './graphe/sections';
import { descriptionsSystemes } from './graphe/systemes';
import { BandeauVerrou, useVerrouEtape } from './graphe/verrouEtape';
import { estSimplifiee } from './typeMep';

const LIBELLE_PLAN: Record<TypePlan, string> = { Deploiement: 'Plan · Étapes', RetourArriere: 'Plan · Retour arrière' };

/** Sections dépliées : choix de chaque personne, par MEP (les siennes dépliées, celles des autres repliées par défaut). */
export function useSectionsDepliees(mep: Mep, plan: TypePlan) {
  const moi = useContext(AuthContext)?.utilisateur?.id ?? null;
  const [choix, setChoix] = useLocalStorageState<Record<string, boolean>>(`mep.${mep.id}.sections.depliees`, {});
  const sections = sectionsDuPlan(mep, plan);
  const deplies = useMemo(
    () => new Set(sections.filter((s) => estDepliee(s, choix, moi)).map((s) => s.id)),
    [sections, choix, moi],
  );
  const basculer = useCallback(
    (id: string) => setChoix({ ...choix, [id]: !deplies.has(id) }),
    [choix, deplies, setChoix],
  );
  const toutes = (deplier: boolean) =>
    setChoix({ ...choix, ...Object.fromEntries(sections.map((s) => [s.id, deplier])) });
  return { deplies, basculer, toutes };
}

/** Boutons « Tout replier / Tout déplier » des sections (icônes seules en vue compacte). */
export function BoutonsRepli({
  onToutes,
  compact = false,
}: {
  onToutes: (deplier: boolean) => void;
  compact?: boolean;
}) {
  if (compact)
    return (
      <span className="pointer-events-auto flex rounded-lg border border-line bg-surface/95 p-0.5 shadow-sm backdrop-blur">
        <BoutonIcone label="Tout replier" taille="sm" onClick={() => onToutes(false)}>
          <ChevronsDownUp className="size-4" aria-hidden />
        </BoutonIcone>
        <BoutonIcone label="Tout déplier" taille="sm" onClick={() => onToutes(true)}>
          <ChevronsUpDown className="size-4" aria-hidden />
        </BoutonIcone>
      </span>
    );
  return (
    <span className="pointer-events-auto flex rounded-lg border border-line bg-surface/95 p-0.5 shadow-sm backdrop-blur">
      <Button
        taille="sm"
        variante="fantome"
        icone={<ChevronsDownUp className="size-4" aria-hidden />}
        onClick={() => onToutes(false)}
      >
        Tout replier
      </Button>
      <Button
        taille="sm"
        variante="fantome"
        icone={<ChevronsUpDown className="size-4" aria-hidden />}
        onClick={() => onToutes(true)}
      >
        Tout déplier
      </Button>
    </span>
  );
}

/**
 * Éditeur du plan en graphe libre (préparation) : graphe rangé automatiquement (bac des sections dans son cadre, plein écran),
 * espace d'édition de l'étape ouverte. Avec `?section=…`, vue d'édition d'une section : ses étapes et ses cercles.
 * `actions` : boutons ajoutés au bout de la ligne de résumé du plan ; `exporter` : « Exporter en Excel ».
 *
 * Fenêtre de moins de 800 px de haut (vue compacte, hors plein écran) : le résumé passe dans la barre d'outils, l'export et
 * l'aide dans son menu « ⋯ », le bandeau d'une section tient sur une ligne, l'étape ouverte s'affiche en tiroir par-dessus
 * le graphe ; sous 1440 px de large, le bac est replié en rail et le menu latéral se replie tant qu'une étape est ouverte.
 */
export function EtapesEditeur({
  plan,
  actions,
  exporter,
}: {
  plan: TypePlan;
  actions?: ReactNode;
  exporter?: { url: string; title: string };
}) {
  const { mep, run } = useMep();
  const [params, setParams] = useSearchParams();
  const modifiable = mep.droits.peutEditerContenu;
  const etapesPlan = etapesDuPlan(mep, plan);
  const sections = sectionsDuPlan(mep, plan);
  const demandeSection = params.get('section');
  const sectionVue = sections.find((s) => s.id === demandeSection) ?? null;
  const etapes = sectionVue ? etapesPlan.filter((e) => infoSection(e).sectionId === sectionVue.id) : etapesPlan;
  const [choix, setChoix] = useState<string | null>(() => params.get('etape'));
  const [nouvelle, setNouvelle] = useState<string | null>(null);
  const astuce = useAstuce('');
  const modeEspace = useModeEspace();
  const { catalogue, resolutions, pret } = useReferentiels();
  const descriptions = useMemo(() => descriptionsSystemes(mep, catalogue), [mep, catalogue]);
  const { deplies, basculer, toutes } = useSectionsDepliees(mep, plan);
  // Bibliothèque d'étapes modèles : menu des plus utilisées, fenêtre « Parcourir », mention « Copie de… ».
  const bibliotheque = useAsync(() => api.etapesModeles(), []);
  const modeles = useMemo(() => inserables(bibliotheque.donnees ?? [], plan), [bibliotheque.donnees, plan]);
  const [parcourir, setParcourir] = useState(false);
  // « Parcourir la bibliothèque… » ouvert depuis un menu de création : l'étape modèle choisie est posée à cette place.
  const [placeParcourir, setPlaceParcourir] = useState<PlaceEtape | null>(null);
  // Plein écran tenu ici : il reste actif quand on entre dans une section ou qu'on revient au plan.
  const [plein, setPlein] = useState(false);
  const fenetreBasse = useFenetreBasse();
  const fenetreEtroite = useFenetreEtroite();
  const compact = fenetreBasse && !plein;
  const [emplacementTiroir, setEmplacementTiroir] = useState<HTMLElement | null>(null);
  const [nouvelleSection, setNouvelleSection] = useState(false);
  const [cercle, setCercle] = useState<
    (Pick<CercleSection, 'titre' | 'description' | 'sens'> & { id: string | null }) | null
  >(null);
  // Copie choisie, pas encore posée : elle flotte au-dessus du graphe (rien n'est écrit avant qu'on la pose).
  const [flottante, setFlottante] = useState<ModeleEtape | null>(null);
  const apercu = useMemo(() => (flottante ? apercuDepuisModele(mep, plan, flottante) : null), [flottante, mep, plan]);
  // Action de la personne : le rangement suit aussitôt, même gelé.
  const [jeton, setJeton] = useState(0);

  const selection = choix && etapesPlan.some((e) => e.id === choix) ? choix : null;
  const etape = etapesPlan.find((e) => e.id === selection) ?? null;

  // Saisie non enregistrée dans l'espace ouvert : changer d'étape passe d'abord par son choix enregistrer / abandonner.
  const [saisieOuverte, setSaisieOuverte] = useState(false);
  const [demande, setDemande] = useState<string | null | undefined>(undefined);
  // Étape d'une section ouverte depuis le plan : sa fiche se lit, on la modifie dans la vue de la section.
  const sectionEtape = etape ? infoSection(etape).sectionId : null;
  const lectureDansPlan = modifiable && !sectionVue && !!sectionEtape;
  const verrou = useVerrouEtape(mep.id, etape?.id ?? null, modifiable && !lectureDansPlan);
  // Étape ouverte en tiroir par-dessus le graphe (vue compacte), ou en panneau qui lui prend sa droite.
  const tiroirOuvert = compact && !!etape && modeEspace === 'panneau';
  const panneauOuvert = !compact && !!etape && modeEspace === 'panneau';
  useRepliMenu(tiroirOuvert && fenetreEtroite);

  function ouvrir(id: string | null) {
    setChoix(id);
    // L'étape tout juste créée garde le focus sur son action, même ouverte après le choix enregistrer / abandonner.
    setNouvelle((n) => (n === id ? n : null));
    setDemande(undefined);
  }

  /** Ouvre une étape (null : ferme l'espace) ; une saisie non enregistrée dans l'espace ouvert demande d'abord quoi en faire. */
  function selectionner(id: string | null) {
    if (id === ID_FLOTTANTE) return;
    if (etape && saisieOuverte && id !== etape.id) setDemande(id);
    else ouvrir(id);
  }

  const allerSection = (id: string | null, etapeId?: string) =>
    setParams(
      (p) => {
        const q = new URLSearchParams(p);
        if (id) q.set('section', id);
        else q.delete('section');
        if (etapeId) q.set('etape', etapeId);
        else q.delete('etape');
        return q;
      },
      { replace: false },
    );

  // Une seule modification du graphe à la fois : un double clic sur « + », ou deux dépôts rapprochés, partiraient sinon
  // d'un plan périmé (étapes en double, déplacements perdus).
  const enCours = useRef(false);
  async function exclusif(action: () => Promise<boolean | void>) {
    if (enCours.current) return;
    enCours.current = true;
    try {
      if ((await action()) !== false) setJeton((j) => j + 1);
    } finally {
      enCours.current = false;
    }
  }

  function choisirModele(m: ModeleEtape) {
    setParcourir(false);
    setFlottante(m);
    selectionner(null);
    astuce.afficher(
      `Copie de « ${m.action} » : glissez-la sur un lien ou une carte, ou cliquez un « + ». Échap pour annuler.`,
    );
  }

  function annulerFlottante() {
    setFlottante(null);
    astuce.afficher('Insertion annulée.');
  }

  // Échap annule l'insertion (hors fenêtres et champs de saisie).
  const afficherAstuce = astuce.afficher;
  useEffect(() => {
    if (!flottante) return;
    function touche(ev: globalThis.KeyboardEvent) {
      if (ev.key !== 'Escape' || ev.defaultPrevented) return;
      if (ev.target instanceof HTMLElement && /^(INPUT|TEXTAREA|SELECT)$/.test(ev.target.tagName)) return;
      if (document.querySelector('[role="dialog"], [role="alertdialog"], [role="menu"]')) return;
      setFlottante(null);
      afficherAstuce('Insertion annulée.');
    }
    document.addEventListener('keydown', touche);
    return () => document.removeEventListener('keydown', touche);
  }, [flottante, afficherAstuce]);

  // Échap dans la vue d'une section (rien d'autre d'ouvert, pas de saisie en cours) : retour au plan, plein écran gardé.
  const sectionOuverte = sectionVue?.id ?? null;
  useEffect(() => {
    if (!sectionOuverte || flottante) return;
    function touche(ev: globalThis.KeyboardEvent) {
      if (ev.key !== 'Escape' || ev.defaultPrevented) return;
      if (ev.target instanceof HTMLElement && /^(INPUT|TEXTAREA|SELECT)$/.test(ev.target.tagName)) return;
      if (ev.target instanceof HTMLElement && ev.target.isContentEditable) return;
      if (document.querySelector('[role="dialog"], [role="alertdialog"], [role="menu"], [role="listbox"]')) return;
      // Le plein écran ne se ferme pas avec : on revient au plan, toujours en plein écran.
      ev.preventDefault();
      setParams(
        (p) => {
          const q = new URLSearchParams(p);
          q.delete('section');
          q.delete('etape');
          return q;
        },
        { replace: false },
      );
    }
    document.addEventListener('keydown', touche);
    return () => document.removeEventListener('keydown', touche);
  }, [sectionOuverte, flottante, setParams]);

  const nouvelleEtape = (apres: Mep | undefined, avant: Set<string>) => apres?.etapes.find((e) => !avant.has(e.id));

  /** Pose la copie flottante à la place donnée (après une carte, sur un lien, en parallèle, ou à part). */
  async function poser(place: PlaceEtape) {
    return flottante ? poserModele(flottante, place) : false;
  }

  /** Pose une copie de l'étape modèle à la place donnée. */
  async function poserModele(m: ModeleEtape, place: PlaceEtape) {
    const avant = new Set(mep.etapes.map((e) => e.id));
    let apres: Mep | undefined;
    const ok = await run(
      async () => (apres = await apiGraphe.insererModele(mep.id, m.id, plan, place, sectionVue?.id ?? null)),
    );
    if (!ok) return false;
    setFlottante(null);
    bibliotheque.recharger();
    const creee = nouvelleEtape(apres, avant);
    if (!creee) return true;
    selectionner(creee.id);
    const absents = systemesAbsents(mep, m, catalogue);
    astuce.afficher(
      `${creee.code} posée, copie de « ${m.action} ».` +
        (absents.length
          ? ` ${absents.join(', ')} hors de la MEP : ${absents.length > 1 ? 'systèmes non repris' : 'système non repris'}.`
          : ''),
    );
    return true;
  }

  async function ajouter(place: PlaceEtape) {
    if (flottante) return poser(place);
    const avant = new Set(mep.etapes.map((e) => e.id));
    let apres: Mep | undefined;
    const ok = await run(
      async () => (apres = await apiGraphe.creerEtape(mep.id, plan, place, sectionVue?.id ?? null)),
      'Étape ajoutée.',
    );
    const creee = ok ? nouvelleEtape(apres, avant) : undefined;
    if (creee) {
      setNouvelle(creee.id);
      // Saisie en cours dans l'espace ouvert : elle n'est pas perdue, l'espace demande d'abord quoi en faire.
      selectionner(creee.id);
    }
    return ok;
  }

  async function deplacer(id: string, d: Deplacement) {
    const e = mep.etapes.find((x) => x.id === id);
    if (!e) return false;
    const ou =
      'apres' in d
        ? `après ${nom(d.apres)}`
        : 'paralleleA' in d
          ? `en parallèle de ${nom(d.paralleleA)}`
          : `entre ${nom(d.surLien.de)} et ${nom(d.surLien.vers)}`;
    const ok = await run(() => apiGraphe.deplacerEtape(mep.id, id, d), `${e.code} déplacée ${ou}.`);
    if (ok) astuce.afficher(`${e.code} déplacée ${ou} : ses anciens suivants attendent maintenant ses préalables.`);
    return ok;
  }

  /** Endroit d'une création, en clair : « après E0004 », « sur le lien E0001 → E0003 »… */
  const ouPoser = (p: PlaceEtape) =>
    'apres' in p
      ? `après ${nom(p.apres)}`
      : 'paralleleA' in p
        ? `en parallèle de ${nom(p.paralleleA)}`
        : 'entre' in p
          ? `sur le lien ${nom(p.entre.de)} → ${nom(p.entre.vers)}`
          : sectionVue
            ? 'dans la section, sans lien'
            : 'à part, sans lien';

  /** Code d'une étape, ou titre d'un cercle. */
  const nom = (id: string) =>
    mep.etapes.find((e) => e.id === id)?.code ?? `« ${trouverCercle(sections, id)?.cercle.titre ?? '?'} »`;

  async function lier(de: string, vers: string) {
    if (mep.etapes.find((e) => e.id === vers)?.dependances.includes(de)) {
      astuce.afficher(`Le lien ${nom(de)} → ${nom(vers)} existe déjà.`);
      return false;
    }
    const message = `Lien créé : ${nom(vers)} attendra ${nom(de)}.`;
    const ok = await run(() => apiGraphe.lier(mep.id, de, vers), message);
    if (ok) astuce.afficher(message);
    return ok;
  }

  async function delier(liens: { de: string; vers: string }[]) {
    for (const l of liens) {
      const ok = await run(() => apiGraphe.delier(mep.id, l.de, l.vers));
      if (!ok) return false;
    }
    if (liens.length) astuce.afficher(`Lien supprimé : ${nom(liens[0].de)} → ${nom(liens[0].vers)}.`);
    return true;
  }

  async function supprimerLien(l: LienModele) {
    if (l.logique) return delier([l.logique]);
    astuce.afficher('Ce lien passe par le cadre d’une section repliée : dépliez-la pour le retirer.');
    return false;
  }

  async function ordonner(avant: string[], apres: string[]) {
    const ids = reordonner(ordreGlobal(etapesPlan, sections), avant, apres);
    const ok = await run(() => apiGraphe.ordonner(mep.id, plan, ids));
    if (ok) astuce.afficher('Ordre de la rangée changé pour tout le monde ; les liens restent les mêmes.');
    return ok;
  }

  async function placerSection(id: string, placee = true) {
    const s = sections.find((x) => x.id === id);
    return run(
      () => apiGraphe.placerSection(mep.id, id, placee),
      placee
        ? `Section « ${s?.titre} » placée : reliez maintenant ses cercles.`
        : `Section « ${s?.titre} » remise dans le bac : ses cercles ne sont plus reliés au plan.`,
    );
  }

  /** L'étape quitte le graphe pour le bac « Étapes à placer » : ses suivants attendent ses préalables. */
  async function retirerEtape(id: string) {
    const e = mep.etapes.find((x) => x.id === id);
    if (!e) return false;
    const ok = await run(() => apiGraphe.retirerDuPlan(mep.id, id), `${e.code} retirée du plan.`);
    if (ok)
      astuce.afficher(
        `${e.code} attend dans le bac « Étapes à placer » ; ses suivants attendent ses préalables. Glissez-la sur un lien ou une carte pour la replacer.`,
      );
    return ok;
  }

  /** Une étape du bac revient dans le graphe à cette place (ou à part). */
  async function placerEtape(id: string, place: PlaceEtape) {
    const e = mep.etapes.find((x) => x.id === id);
    if (!e) return false;
    const d: Deplacement | Record<string, never> =
      'entre' in place
        ? { surLien: place.entre }
        : 'apres' in place
          ? { apres: place.apres }
          : 'paralleleA' in place
            ? { paralleleA: place.paralleleA }
            : {};
    const message = `${e.code} placée ${ouPoser(place)}.`;
    const ok = await run(() => apiGraphe.placerEtape(mep.id, id, d, sectionVue?.id ?? null), message);
    if (ok) astuce.afficher(message);
    return ok;
  }

  async function creerSection(titre: string, systemeIds: string[]) {
    let creee: string | null = null;
    const ok = await run(async () => {
      const r = await apiGraphe.creerSection(mep.id, plan, titre, systemeIds, false);
      creee = r.sectionId;
      return r.mep;
    }, `Section « ${titre} » créée.`);
    if (ok && creee) allerSection(creee);
    return ok;
  }

  const actionsGraphe: ActionsGraphe = modifiable
    ? {
        ajouter: (p) => void exclusif(() => ajouter(p)),
        deplacer: (id, d) => void exclusif(() => deplacer(id, d)),
        ordonner: (avant, apres) => void exclusif(() => ordonner(avant, apres)),
        lier: (de, vers) => void exclusif(() => lier(de, vers)),
        supprimerLien: (l) => void exclusif(() => supprimerLien(l)),
        ouvrirCercle: sectionVue
          ? (id) => {
              const c = sectionVue.ports.find((x) => x.id === id);
              if (c) setCercle({ ...c });
            }
          : undefined,
        editerSection: (id, etapeId) => allerSection(id, etapeId),
        retirerEtape: (id) => void exclusif(() => retirerEtape(id)),
        retirerSection: (id) => void exclusif(() => placerSection(id, false)),
        placerEtape: (id, place) => void exclusif(() => placerEtape(id, place)),
      }
    : { editerSection: (id, etapeId) => allerSection(id, etapeId) };

  // « Optimiser les liens » : aperçu (liens inutiles en rouge et listés), puis une seule opération au serveur. Dans la vue
  // d'une section, seulement les liens entre ses éléments (étapes et cercles de la section).
  const [optimisation, setOptimisation] = useState<LienRedondant[] | null>(null);
  const [optimisationEnCours, setOptimisationEnCours] = useState(false);
  const portee = sectionVue?.id ?? null;
  // Autre vue (plan ↔ section) : l'aperçu calculé pour l'ancienne ne vaut plus.
  const [porteeAffichee, setPorteeAffichee] = useState(portee);
  if (porteeAffichee !== portee) {
    setPorteeAffichee(portee);
    setOptimisation(null);
  }
  async function calculerOptimisation() {
    if (optimisation) return setOptimisation(null);
    try {
      const liens = await apiGraphe.liensRedondants(mep.id, plan, portee);
      if (liens.length === 0)
        astuce.afficher(
          portee
            ? 'Aucun lien inutile dans cette section : chaque lien compte pour l’ordre d’exécution.'
            : 'Aucun lien inutile : chaque lien compte pour l’ordre d’exécution.',
        );
      else setOptimisation(liens);
    } catch (e) {
      astuce.afficher(e instanceof Error ? e.message : 'Calcul impossible.');
    }
  }
  async function confirmerOptimisation() {
    if (!optimisation) return;
    const n = optimisation.length;
    setOptimisationEnCours(true);
    await exclusif(() =>
      run(
        () => apiGraphe.optimiserLiens(mep.id, plan, optimisation, portee),
        `${n} lien${n > 1 ? 's' : ''} inutile${n > 1 ? 's' : ''} retiré${n > 1 ? 's' : ''}.`,
      ),
    );
    setOptimisationEnCours(false);
    setOptimisation(null);
  }

  const enBac = sections.filter((s) => !s.placee);
  // Étapes du bac : celles du plan, ou de la section dans sa vue.
  const etapesAPlacer = etapes.filter((e) => e.aPlacer);
  const nbEtapes = (sid: string) => etapesPlan.filter((e) => infoSection(e).sectionId === sid).length;

  const astuceTexte = (
    <p aria-live="polite" className="min-w-0 flex-1 truncate text-ui font-semibold text-rollback-solid">
      {astuce.flash ? astuce.texte : ''}
    </p>
  );
  const resume = (
    <ResumePlan etapes={etapes} sections={sectionVue ? undefined : sections} sansDuree={estSimplifiee(mep)} />
  );
  const bandeau = (optimisation || sectionVue || !compact) && (
    <>
      {optimisation && (
        <PanneauOptimisation
          liens={optimisation}
          enCours={optimisationEnCours}
          onConfirmer={() => void confirmerOptimisation()}
          onAnnuler={() => setOptimisation(null)}
        />
      )}
      {sectionVue && (
        <EnTeteVueSection
          mep={mep}
          section={sectionVue}
          libellePlan={LIBELLE_PLAN[plan]}
          descriptions={descriptions}
          modifiable={modifiable}
          etapeIds={etapes.map((e) => e.id)}
          onRetour={() => allerSection(null)}
          onModifier={(s) =>
            run(
              () => apiGraphe.modifierSection(mep.id, sectionVue.id, { revision: sectionVue.revision, ...s }),
              'Section enregistrée.',
            )
          }
          onPlacer={(placee) => void placerSection(sectionVue.id, placee)}
          onSupprimer={async () => {
            const ok = await run(
              () => apiGraphe.supprimerSection(mep.id, sectionVue.id),
              'Section supprimée, ses étapes restent dans le plan.',
            );
            if (ok) allerSection(null);
            return ok;
          }}
          onCercle={setCercle}
          compact={compact}
        />
      )}
      {!compact && (
        <div className="flex min-h-9 flex-wrap items-center gap-x-4 gap-y-1 bg-surface px-3 py-1.5">
          {resume}
          <p aria-live="polite" className="min-w-0 flex-1 text-ui font-semibold text-rollback-solid">
            {astuce.flash ? astuce.texte : ''}
          </p>
          <AideGraphe edition={modifiable} />
          {actions}
          {exporter && <BoutonExportExcel url={exporter.url} title={exporter.title} />}
        </div>
      )}
    </>
  );

  const graphe = (
    <div className={cn('relative min-w-0', panneauOuvert && 'min-[1100px]:pr-[608px]')}>
      <GrapheLibre
        mep={mep}
        plan={plan}
        etapes={sectionVue ? etapes : etapesPlan}
        sections={sections}
        mode="edition"
        modifiable={modifiable}
        descriptions={descriptions}
        selection={selection}
        onSelection={selectionner}
        sectionVue={sectionVue?.id ?? null}
        retraitDroit={panneauOuvert}
        deplies={deplies}
        onBasculer={basculer}
        gel={!!etape && saisieOuverte}
        jeton={jeton}
        actions={actionsGraphe}
        onMessage={astuce.afficher}
        flottante={apercu ? { etape: apercu, onAnnuler: annulerFlottante } : null}
        plein={plein}
        onPlein={setPlein}
        bandeau={bandeau}
        compact={compact}
        resume={
          <>
            {resume}
            {astuceTexte}
          </>
        }
        menu={compact ? <MenuPlusGraphe edition={modifiable} exporter={exporter} /> : undefined}
        tiroir={compact ? { emplacement: setEmplacementTiroir, ouvert: tiroirOuvert } : null}
        creation={
          modifiable
            ? {
                modeles: modelesFrequents(mep, modeles),
                depuisModele: (place, m) => void exclusif(() => poserModele(m, place)),
                parcourir: (place) => {
                  setPlaceParcourir(place);
                  setParcourir(true);
                },
              }
            : undefined
        }
        retraits={optimisation}
        outils={
          <>
            {modifiable && (
              <BoutonOptimiserLiens actif={!!optimisation} dansSection={!!sectionVue} onClick={calculerOptimisation} />
            )}
            {!sectionVue && sections.some((s) => s.placee) && <BoutonsRepli onToutes={toutes} compact={compact} />}
            {compact && actions}
          </>
        }
        lateral={
          (modifiable || enBac.length > 0 || etapesAPlacer.length > 0) && (
            <BacPlan
              mep={mep}
              sections={sectionVue ? null : enBac}
              etapes={etapesAPlacer}
              nomSection={(sid) => sections.find((x) => x.id === sid)?.titre ?? null}
              nbEtapes={nbEtapes}
              modifiable={modifiable}
              onPlacer={(id) => void exclusif(() => placerSection(id))}
              onOuvrir={allerSection}
              onNouvelle={() => setNouvelleSection(true)}
              onPlacerEtape={(id) => void exclusif(() => placerEtape(id, {}))}
              onOuvrirEtape={selectionner}
              replieForce={!!etape && modeEspace === 'panneau'}
              rail={compact && fenetreEtroite}
            />
          )
        }
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
    </div>
  );

  return (
    // Onglet affiché en une fois, référentiels compris (systèmes, résolutions) : voir Apparition.
    <Apparition
      pret={pret}
      libelle="Chargement du plan…"
      squelette={<SqueletteCadreGraphe bac={modifiable || enBac.length > 0 || etapesAPlacer.length > 0} />}
    >
      <div className="grid gap-3">
        {graphe}
        {modifiable && (
          <ParcourirDialog
            ouvert={parcourir}
            mep={mep}
            plan={plan}
            modeles={modeles}
            catalogue={catalogue}
            onFermer={() => {
              setParcourir(false);
              setPlaceParcourir(null);
            }}
            destination={placeParcourir ? ouPoser(placeParcourir) : null}
            onInserer={(m) => {
              const place = placeParcourir;
              if (!place) return choisirModele(m);
              setParcourir(false);
              setPlaceParcourir(null);
              void exclusif(() => poserModele(m, place));
            }}
          />
        )}
        {modifiable && (
          <NouvelleSectionDialog
            ouvert={nouvelleSection}
            mep={mep}
            descriptions={descriptions}
            onFermer={() => setNouvelleSection(false)}
            onCreer={creerSection}
          />
        )}
        {sectionVue && (
          <CercleDialog
            cercle={cercle}
            peutSupprimer={sectionVue.ports.filter((c) => c.sens === cercle?.sens).length > 1}
            onFermer={() => setCercle(null)}
            onEnregistrer={(titre, description) =>
              cercle?.id
                ? run(
                    () => apiGraphe.modifierCercle(mep.id, sectionVue.id, cercle.id as string, titre, description),
                    'Cercle enregistré.',
                  )
                : run(
                    () => apiGraphe.creerCercle(mep.id, sectionVue.id, cercle?.sens ?? 'Entree', titre, description),
                    'Cercle ajouté.',
                  )
            }
            onSupprimer={() =>
              cercle?.id
                ? run(() => apiGraphe.supprimerCercle(mep.id, sectionVue.id, cercle.id as string), 'Cercle supprimé.')
                : Promise.resolve(false)
            }
          />
        )}
        {etape && (
          <EmplacementTiroir.Provider value={compact ? emplacementTiroir : null}>
            <PanneauEdition
              key={etape.id}
              mep={mep}
              etape={etape}
              plan={plan}
              etapes={etapesPlan}
              modifiable={modifiable}
              resolutions={resolutions}
              catalogue={catalogue}
              modeles={bibliotheque.donnees}
              onModeleCree={() => bibliotheque.recharger()}
              focusAction={nouvelle === etape.id}
              onLier={(de, vers) => void exclusif(() => lier(de, vers))}
              onDelier={(l) => void exclusif(() => delier(l))}
              onRetirerDuPlan={
                modifiable && !lectureDansPlan && !etape.aPlacer
                  ? () => void exclusif(() => retirerEtape(etape.id))
                  : undefined
              }
              onSelection={ouvrir}
              onDirty={setSaisieOuverte}
              demande={demande}
              onAnnulerDemande={() => setDemande(undefined)}
              lecture={
                lectureDansPlan && sectionEtape ? (
                  <div className="flex flex-wrap items-center gap-2 rounded-lg border border-accent/40 bg-accent-soft px-3 py-2 text-ui text-accent-text">
                    <span className="min-w-0 flex-1">
                      Étape de la section « {sections.find((x) => x.id === sectionEtape)?.titre} » : on la modifie dans
                      la vue de la section.
                    </span>
                    <Button
                      taille="sm"
                      variante="primaire"
                      icone={<PencilLine className="size-4" aria-hidden />}
                      onClick={() => allerSection(sectionEtape)}
                    >
                      Modifier dans la section
                    </Button>
                  </div>
                ) : undefined
              }
              verrou={{
                lectureSeule: verrou.etat === 'autre' || verrou.etat === 'perdu',
                bandeau: <BandeauVerrou verrou={verrou} />,
              }}
            />
          </EmplacementTiroir.Provider>
        )}
      </div>
    </Apparition>
  );
}
