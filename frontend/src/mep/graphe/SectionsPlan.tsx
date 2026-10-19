import {
  ArrowLeft,
  ChevronDown,
  ChevronRight,
  CircleDot,
  FolderPlus,
  Inbox,
  Info,
  PanelRightClose,
  PanelRightOpen,
  Pencil,
  Plus,
  Trash2,
  Undo2,
} from 'lucide-react';
import { useEffect, useState, type KeyboardEvent, type PointerEvent as PointerEventReact } from 'react';
import { useDraft } from '../../hooks';
import { Popover, PopoverContent, PopoverTrigger } from '../../ui/Popover';
import { createPortal } from 'react-dom';
import { stylePersonne } from '../../ui/Avatar';
import { libelleEditeurs, useAutresPresents, usePresenceElement } from '../../presence';
import { cleIciSection, PresentsSection } from './PresentsGraphe';
import type { Etape, Mep } from '../../types';
import { lireBacReplie, useBacGraphe } from './contexteGraphe';
import { BoutonIcone, Button } from '../../ui/Button';
import { cn } from '../../ui/cn';
import { MultiSelect } from '../../ui/Combobox';
import { ConfirmDialog, Dialog } from '../../ui/Dialog';
import { Field, Input, Textarea } from '../../ui/Input';
import { cerclesTries, type CercleSection, type Section, type SensCercle } from './sections';
import { PuceSysteme, systemesDe } from './systemes';

const optionsSystemes = (mep: Mep, descriptions: Map<string, string>) =>
  mep.systemes.map((s) => ({ valeur: s.id, libelle: s.nom, detail: descriptions.get(s.id) || s.cible }));

/** « Nouvelle section » : titre et systèmes ; la section naît dans le bac et sa vue s'ouvre. */
export function NouvelleSectionDialog({
  ouvert,
  mep,
  descriptions,
  onFermer,
  onCreer,
}: {
  ouvert: boolean;
  mep: Mep;
  descriptions: Map<string, string>;
  onFermer: () => void;
  onCreer: (titre: string, systemeIds: string[]) => Promise<boolean>;
}) {
  const [titre, setTitre] = useState('');
  const [systemes, setSystemes] = useState<string[]>([]);
  const [envoi, setEnvoi] = useState(false);
  useEffect(() => {
    if (ouvert) {
      setTitre('');
      setSystemes([]);
    }
  }, [ouvert]);
  const valider = async () => {
    if (!titre.trim()) return;
    setEnvoi(true);
    const ok = await onCreer(titre.trim(), systemes);
    setEnvoi(false);
    if (ok) onFermer();
  };
  return (
    <Dialog
      open={ouvert}
      onOpenChange={(o) => !o && onFermer()}
      titre="Nouvelle section"
      description="Elle arrive dans le bac, à droite du graphe : vous la remplirez dans sa vue, puis la glisserez dans le plan."
      pied={
        <>
          <Button variante="fantome" onClick={onFermer}>
            Annuler
          </Button>
          <Button variante="primaire" disabled={!titre.trim()} chargement={envoi} onClick={() => void valider()}>
            Créer la section
          </Button>
        </>
      }
    >
      <form
        className="grid gap-3"
        onSubmit={(e) => {
          e.preventDefault();
          void valider();
        }}
      >
        <Field label="Titre">
          {(id) => (
            <Input
              id={id}
              value={titre}
              autoFocus
              onChange={(e) => setTitre(e.target.value)}
              placeholder="ex. Bascule BDD"
            />
          )}
        </Field>
        <Field label="Systèmes touchés" hint="Modifiables ensuite à tout moment.">
          {(id) => (
            <MultiSelect
              id={id}
              options={optionsSystemes(mep, descriptions)}
              valeurs={systemes}
              onChange={setSystemes}
              placeholder="Aucun système"
              placeholderRecherche="Rechercher un système…"
              vide="Aucun système dans la MEP."
            />
          )}
        </Field>
      </form>
    </Dialog>
  );
}

/**
 * Bac du plan : colonne à droite du graphe avec ce qui attend d'y être placé. « Sections à placer » : sections créées mais
 * pas encore placées (on les glisse dans le graphe, ou « Placer », puis on relie leurs cercles). Juste en dessous, « Étapes
 * à placer » : étapes retirées du graphe (glissées sur le bac, ou « Retirer du plan »), sans aucun lien ; on les glisse sur
 * un lien, une carte ou un cercle, comme une étape modèle. Dans la vue d'une section, seulement les étapes de la section.
 * Pendant le glisser d'une carte ou d'une section du graphe, le bac est une zone de dépôt (`depot`). Tant que quelque chose
 * reste au bac, « Vérifier le plan » bloque le Go.
 */
export function BacPlan({
  mep,
  sections,
  etapes,
  nomSection,
  nbEtapes,
  modifiable,
  onPlacer,
  onOuvrir,
  onNouvelle,
  onPlacerEtape,
  onOuvrirEtape,
  replieForce = false,
  rail = false,
}: {
  mep: Mep;
  /** Replié tant que l'espace d'une étape occupe la droite de l'écran (le graphe garde sa largeur). */
  replieForce?: boolean;
  /**
   * Vue compacte sur un écran étroit : replié en rail de 30 px (le graphe garde sa largeur) ; un clic sur le rail l'ouvre
   * le temps de s'en servir, sans changer la préférence mémorisée.
   */
  rail?: boolean;
  /** Sections du plan pas encore placées ; null dans la vue d'une section (seulement ses étapes). */
  sections: Section[] | null;
  /** Étapes du bac (retirées du graphe). */
  etapes: Etape[];
  /** Titre de la section d'une étape (plan : « dans Bascule BDD »). */
  nomSection: (sectionId: string) => string | null;
  nbEtapes: (sectionId: string) => number;
  modifiable: boolean;
  onPlacer: (sectionId: string) => void;
  onOuvrir: (sectionId: string) => void;
  onNouvelle: () => void;
  /** « Placer à part » (clavier) : l'étape revient dans le graphe, sans lien (reliée aux cercles dans une section). */
  onPlacerEtape: (etapeId: string) => void;
  onOuvrirEtape: (etapeId: string) => void;
}) {
  // Dépôt et glisser fournis par le graphe qui contient le bac.
  const bac = useBacGraphe();
  const [glisse, setGlisse] = useState<{ section: Section; x: number; y: number; dessus: boolean } | null>(null);
  const [replie, setReplie] = useState(lireBacReplie);
  const [railOuvert, setRailOuvert] = useState(false);
  const basculer = () =>
    setReplie((v) => {
      try {
        window.localStorage.setItem('graphe.bacReplie', v ? 'non' : 'oui');
      } catch {
        /* préférence non mémorisée */
      }
      return !v;
    });
  const listeSections = sections ?? [];
  const total = listeSections.length + etapes.length;
  const libelle = sections ? `À placer (${total})` : `Étapes à placer (${etapes.length})`;
  const enRail = rail && !railOuvert;

  const appuyer = (ev: PointerEventReact<HTMLElement>, s: Section) => {
    if (!modifiable || ev.button !== 0) return;
    if (ev.target instanceof Element && ev.target.closest('button')) return;
    ev.preventDefault();
    const surGraphe = (x: number, y: number) =>
      document.elementsFromPoint(x, y).some((el) => !!el.closest('[data-graphe-libre]'));
    const depart = { x: ev.clientX, y: ev.clientY };
    let bouge = false;
    const bouger = (e: PointerEvent) => {
      if (!bouge && Math.hypot(e.clientX - depart.x, e.clientY - depart.y) < 4) return;
      bouge = true;
      setGlisse({ section: s, x: e.clientX, y: e.clientY, dessus: surGraphe(e.clientX, e.clientY) });
    };
    const lacher = (e: PointerEvent) => {
      window.removeEventListener('pointermove', bouger);
      window.removeEventListener('pointerup', lacher);
      setGlisse(null);
      if (bouge && surGraphe(e.clientX, e.clientY)) onPlacer(s.id);
    };
    window.addEventListener('pointermove', bouger);
    window.addEventListener('pointerup', lacher);
  };
  const appuyerEtape = (ev: PointerEventReact<HTMLElement>, e: Etape) => {
    if (!modifiable || !bac.glisserEtape) return;
    if (ev.target instanceof Element && ev.target.closest('button')) return;
    ev.preventDefault();
    bac.glisserEtape(ev, e);
  };

  // Zone de dépôt pendant le glisser d'une carte ou d'une section du graphe : contour, puis fond quand on la vise.
  const zone = bac.depot !== 'aucun' && (
    <div
      aria-hidden
      className={cn(
        'pointer-events-none absolute inset-1 z-10 grid place-content-center rounded-xl border-2 border-dashed p-3 text-center text-ui font-semibold transition-colors duration-150',
        bac.depot === 'vise'
          ? 'border-accent bg-accent-soft/95 text-accent-text'
          : 'border-accent/70 bg-surface/80 text-fg-muted',
      )}
    >
      <Inbox className="mx-auto mb-1 size-5" aria-hidden />
      {bac.depot === 'vise' ? 'Lâcher : retirer du plan' : 'Glisser ici pour retirer du plan'}
    </div>
  );

  if (rail && (enRail || replieForce))
    return (
      <aside
        aria-label="Bac des éléments à placer"
        data-bac-plan
        className={cn(
          'relative flex w-[30px] shrink-0 flex-col border-l border-line bg-surface-2/40',
          bac.depot === 'vise' && 'bg-accent-soft',
        )}
      >
        <button
          type="button"
          disabled={replieForce}
          onClick={() => {
            if (replie) basculer();
            setRailOuvert(true);
          }}
          aria-label={
            replieForce
              ? `${libelle} : fermez l’étape ouverte pour afficher le bac`
              : `Afficher le bac : ${libelle.toLowerCase()}`
          }
          title={replieForce ? undefined : 'Afficher le bac'}
          className="flex flex-1 flex-col items-center gap-2 py-2 text-fg-subtle enabled:hover:bg-surface-2 enabled:hover:text-fg"
        >
          <PanelRightOpen className="size-4 shrink-0" aria-hidden />
          <span className="flex items-center gap-1 text-2xs font-semibold tracking-[0.06em] uppercase [writing-mode:vertical-rl]">
            <Inbox className="size-3.5 rotate-90" aria-hidden />
            {`À placer (${total})`}
          </span>
        </button>
        {bac.depot !== 'aucun' && (
          <span
            aria-hidden
            className={cn(
              'pointer-events-none absolute inset-0.5 rounded-lg border-2 border-dashed',
              bac.depot === 'vise' ? 'border-accent' : 'border-accent/60',
            )}
          />
        )}
      </aside>
    );

  if ((replie && !rail) || replieForce)
    return (
      <aside
        aria-label="Bac des éléments à placer"
        data-bac-plan
        className={cn(
          'relative flex w-11 shrink-0 flex-col items-center border-l border-line bg-surface-2/40 py-2',
          bac.depot === 'vise' && 'bg-accent-soft',
        )}
      >
        <BoutonIcone
          label={
            replieForce
              ? `${libelle} : fermez l’étape ouverte pour afficher le bac`
              : `Afficher le bac : ${libelle.toLowerCase()}`
          }
          taille="sm"
          disabled={replieForce}
          onClick={basculer}
        >
          <PanelRightOpen className="size-4" aria-hidden />
        </BoutonIcone>
        <span className="mt-2 flex items-center gap-1 text-2xs font-semibold tracking-[0.06em] text-fg-subtle uppercase [writing-mode:vertical-rl]">
          <Inbox className="size-3.5 rotate-90" aria-hidden />
          {libelle}
        </span>
        {bac.depot !== 'aucun' && (
          <span
            aria-hidden
            className={cn(
              'pointer-events-none absolute inset-0.5 rounded-lg border-2 border-dashed',
              bac.depot === 'vise' ? 'border-accent' : 'border-accent/60',
            )}
          />
        )}
      </aside>
    );

  const titreListe = 'flex items-center gap-1.5 text-2xs font-semibold tracking-[0.06em] text-fg-subtle uppercase';
  return (
    // Panneau intérieur du cadre du plan : sa propre hauteur de défilement, le graphe garde la sienne.
    <aside
      aria-label="Bac des éléments à placer"
      data-bac-plan
      className="relative flex w-60 shrink-0 flex-col border-l border-line bg-surface-2/40 max-sm:w-48"
    >
      <div className="flex shrink-0 items-center justify-between gap-2 border-b border-line px-2.5 py-1.5">
        <h3 className={titreListe}>
          <Inbox className="size-3.5" aria-hidden />
          {sections ? `Sections à placer (${listeSections.length})` : `Étapes à placer (${etapes.length})`}
        </h3>
        <BoutonIcone label="Replier le bac" taille="sm" onClick={rail ? () => setRailOuvert(false) : basculer}>
          <PanelRightClose className="size-4" aria-hidden />
        </BoutonIcone>
      </div>
      <div className="grid min-h-0 flex-1 content-start gap-2 overflow-y-auto p-2.5">
        {sections && (
          <>
            {modifiable && (
              <Button taille="sm" icone={<FolderPlus className="size-4" aria-hidden />} onClick={onNouvelle}>
                Nouvelle section
              </Button>
            )}
            {listeSections.length === 0 ? (
              <p className="text-ui text-fg-subtle">
                {modifiable
                  ? 'Aucune section en attente. Une nouvelle section arrive ici.'
                  : 'Aucune section en attente.'}
              </p>
            ) : (
              <ul aria-label="Sections à placer" className="grid gap-2">
                {listeSections.map((s) => (
                  // eslint-disable-next-line jsx-a11y/no-noninteractive-element-interactions -- glisser à la souris ; « Placer » au clavier
                  <li
                    key={s.id}
                    onPointerDown={(e) => appuyer(e, s)}
                    className={cn(
                      'grid gap-1.5 rounded-xl border-2 border-dashed border-accent/60 bg-surface p-2.5 shadow-sm',
                      modifiable && 'cursor-grab touch-none',
                      glisse?.section.id === s.id && 'opacity-40',
                    )}
                  >
                    <p className="truncate text-sm font-semibold" title={s.titre}>
                      {s.titre}
                    </p>
                    <div className="flex flex-wrap gap-1">
                      {systemesDe(mep, s.systemeIds).map((x) => (
                        <PuceSysteme key={x.id} nom={x.nom} className="px-1.5 py-0 text-2xs" />
                      ))}
                    </div>
                    <p className="text-xs text-fg-muted">
                      {nbEtapes(s.id)} étape{nbEtapes(s.id) > 1 ? 's' : ''}
                      {s.auteur && <> · par {s.auteur}</>}
                    </p>
                    <div className="flex flex-wrap gap-1">
                      {modifiable && (
                        <Button taille="sm" variante="primaire" onClick={() => onPlacer(s.id)}>
                          Placer dans le graphe
                        </Button>
                      )}
                      <Button
                        taille="sm"
                        variante="fantome"
                        icone={<Pencil className="size-3.5" aria-hidden />}
                        onClick={() => onOuvrir(s.id)}
                        aria-label={`Ouvrir la section ${s.titre}`}
                      >
                        Ouvrir
                      </Button>
                    </div>
                  </li>
                ))}
              </ul>
            )}
            <h3 className={cn(titreListe, 'mt-2 border-t border-line pt-3')}>
              <Inbox className="size-3.5" aria-hidden />
              Étapes à placer ({etapes.length})
            </h3>
          </>
        )}
        {etapes.length === 0 ? (
          <p className="text-ui text-fg-subtle">
            {modifiable
              ? 'Aucune étape en attente. Glissez ici une carte du graphe pour la retirer du plan.'
              : 'Aucune étape en attente.'}
          </p>
        ) : (
          <ul aria-label="Étapes à placer" className="grid gap-2">
            {etapes.map((e) => {
              const section = sections && e.sectionId ? nomSection(e.sectionId) : null;
              return (
                // eslint-disable-next-line jsx-a11y/no-noninteractive-element-interactions -- glisser à la souris ; « Placer à part » au clavier
                <li
                  key={e.id}
                  onPointerDown={(ev) => appuyerEtape(ev, e)}
                  className={cn(
                    'grid gap-1.5 rounded-xl border-2 border-dashed border-line-strong bg-surface p-2.5 shadow-sm',
                    modifiable && bac.glisserEtape && 'cursor-grab touch-none',
                  )}
                >
                  <p className="flex min-w-0 items-baseline gap-1.5">
                    <span className="shrink-0 text-xs font-semibold text-fg-muted tnum">{e.code}</span>
                    <span className="truncate text-sm font-semibold" title={e.action}>
                      {e.action || 'Étape sans action'}
                    </span>
                  </p>
                  {(e.systemeIds.length > 0 || section) && (
                    <div className="flex flex-wrap items-center gap-1">
                      {systemesDe(mep, e.systemeIds).map((x) => (
                        <PuceSysteme key={x.id} nom={x.nom} className="px-1.5 py-0 text-2xs" />
                      ))}
                      {section && <span className="text-xs text-fg-muted">dans « {section} »</span>}
                    </div>
                  )}
                  <div className="flex flex-wrap gap-1">
                    {modifiable && (
                      <Button
                        taille="sm"
                        onClick={() => onPlacerEtape(e.id)}
                        aria-label={`Placer ${e.code} à part dans le graphe`}
                        title="Revient dans le graphe sans lien (dans une section : reliée à ses cercles) ; ou la glisser sur un lien, une carte, un cercle"
                      >
                        Placer à part
                      </Button>
                    )}
                    <Button
                      taille="sm"
                      variante="fantome"
                      icone={<Pencil className="size-3.5" aria-hidden />}
                      onClick={() => onOuvrirEtape(e.id)}
                      aria-label={`Ouvrir ${e.code}`}
                    >
                      Ouvrir
                    </Button>
                  </div>
                </li>
              );
            })}
          </ul>
        )}
        {glisse &&
          createPortal(
            <div
              aria-hidden
              className={cn(
                'pointer-events-none fixed z-panneau w-56 -translate-x-1/2 -translate-y-1/2 rounded-xl border-2 bg-surface px-3 py-2 text-sm font-semibold shadow-lg',
                glisse.dessus ? 'border-accent' : 'border-dashed border-line-strong opacity-80',
              )}
              style={{ left: glisse.x, top: glisse.y }}
            >
              {glisse.section.titre}
              <span className="block text-xs font-normal text-fg-muted">
                {glisse.dessus ? 'Lâcher pour placer dans le graphe' : 'Glisser sur le graphe'}
              </span>
            </div>,
            document.body,
          )}
      </div>
      {zone}
    </aside>
  );
}

/** Titre et description d'un cercle (« Validation », « Sauvegarde »…) ; suppression. */
export function CercleDialog({
  cercle,
  peutSupprimer,
  onFermer,
  onEnregistrer,
  onSupprimer,
}: {
  cercle: (Pick<CercleSection, 'titre' | 'description' | 'sens'> & { id: string | null }) | null;
  peutSupprimer: boolean;
  onFermer: () => void;
  onEnregistrer: (titre: string, description: string) => Promise<boolean>;
  onSupprimer: () => Promise<boolean>;
}) {
  const [titre, setTitre] = useState('');
  const [description, setDescription] = useState('');
  useEffect(() => {
    setTitre(cercle?.titre ?? '');
    setDescription(cercle?.description ?? '');
  }, [cercle]);
  const entree = cercle?.sens === 'Entree';
  const valider = async () => {
    if (titre.trim() && (await onEnregistrer(titre.trim(), description.trim()))) onFermer();
  };
  return (
    <Dialog
      open={!!cercle}
      onOpenChange={(o) => !o && onFermer()}
      titre={
        cercle?.id
          ? `${entree ? 'Entrée' : 'Sortie'} « ${cercle.titre} »`
          : entree
            ? 'Nouvelle entrée'
            : 'Nouvelle sortie'
      }
      description={
        entree
          ? 'Ce qui arrive à la section : les étapes reliées à cette entrée l’attendent.'
          : 'Ce que la section donne : les étapes extérieures reliées à cette sortie attendent les étapes de la section qui y mènent.'
      }
      pied={
        <>
          {cercle?.id && peutSupprimer && (
            <Button
              variante="danger-doux"
              className="mr-auto"
              icone={<Trash2 className="size-4" aria-hidden />}
              action={() => onSupprimer().then((ok) => ok && onFermer())}
            >
              Supprimer
            </Button>
          )}
          <Button variante="fantome" onClick={onFermer}>
            Annuler
          </Button>
          <Button variante="primaire" disabled={!titre.trim()} action={valider}>
            Enregistrer
          </Button>
        </>
      }
    >
      <form
        className="grid gap-3"
        onSubmit={(e) => {
          e.preventDefault();
          void valider();
        }}
      >
        <Field label="Titre">
          {(id) => <Input id={id} value={titre} autoFocus onChange={(e) => setTitre(e.target.value)} />}
        </Field>
        <Field label="Explication (facultative)">
          {(id) => <Textarea id={id} rows={2} value={description} onChange={(e) => setDescription(e.target.value)} />}
        </Field>
      </form>
    </Dialog>
  );
}

const AUCUNE: string[] = [];

/**
 * Bandeau de la vue d'une section, posé dans le cadre du graphe (il reste visible en plein écran) : « Retour au plan »
 * (Échap), fil d'Ariane, qui d'autre y travaille, titre et description, systèmes, auteur, cercles d'entrée et de sortie
 * (« + » pour en ajouter, clic pour les nommer), bac et suppression.
 */
export function EnTeteVueSection({
  mep,
  section,
  etapeIds = AUCUNE,
  libellePlan,
  descriptions,
  modifiable,
  onRetour,
  onModifier,
  onPlacer,
  onSupprimer,
  onCercle,
  compact = false,
}: {
  mep: Mep;
  section: Section;
  /** Vue compacte : une seule rangée de 44 px ; description, systèmes, cercles, auteur et actions dans « Détails ». */
  compact?: boolean;
  /** Étapes de la section : qui en modifie une travaille dans la section. */
  etapeIds?: string[];
  libellePlan: string;
  descriptions: Map<string, string>;
  modifiable: boolean;
  onRetour: () => void;
  onModifier: (s: { titre: string; description: string; systemeIds: string[] }) => Promise<boolean>;
  onPlacer: (placee: boolean) => void;
  onSupprimer: () => Promise<boolean>;
  onCercle: (c: (Pick<CercleSection, 'titre' | 'description' | 'sens'> & { id: string | null }) | null) => void;
}) {
  // Saisie gardée (brouillon) même si la section change entre-temps (modification d'un collègue reçue pendant la saisie) ;
  // elle reprend la valeur de la section une fois enregistrée ou abandonnée (Échap).
  const titre = useDraft(section.titre);
  const description = useDraft(section.description);
  const [suppr, setSuppr] = useState(false);
  const [details, setDetails] = useState(false);
  // Les autres voient « X modifie » sur la section dans le plan tant que sa vue d'édition est ouverte ici.
  usePresenceElement(`section:${section.id}`, modifiable);
  // Où l'on est (même en lecture) : les autres voient votre avatar sur la section.
  usePresenceElement(cleIciSection(section.id), true);
  const autres = useAutresPresents();
  const dansSection = autres.filter((p) =>
    p.elements.some((e) => e === `section:${section.id}` || etapeIds.some((id) => e === `etape:${id}`)),
  );
  const enregistrer = (p: Partial<{ titre: string; description: string; systemeIds: string[] }>) =>
    onModifier({ titre: section.titre, description: section.description, systemeIds: section.systemeIds, ...p });
  const validerTitre = async () => {
    const t = titre.value.trim();
    if (!t || t === section.titre) titre.reset();
    else if (await enregistrer({ titre: t })) titre.reset();
  };
  const validerDescription = async () => {
    const d = description.value.trim();
    if (d === section.description) description.reset();
    else if (await enregistrer({ description: d })) description.reset();
  };
  /** Entrée valide (le champ perd le focus, ce qui enregistre), Échap abandonne la saisie (le champ garde le focus). */
  const touches = (brouillon: { reset: () => void }) => (e: KeyboardEvent<HTMLInputElement | HTMLTextAreaElement>) => {
    if (e.key === 'Enter' && !(e.currentTarget instanceof HTMLTextAreaElement && e.shiftKey)) {
      e.preventDefault();
      e.currentTarget.blur();
    } else if (e.key === 'Escape') brouillon.reset();
  };
  const nbEntrees = section.ports.filter((c) => c.sens === 'Entree').length;
  const nbSorties = section.ports.filter((c) => c.sens === 'Sortie').length;
  const presents = (
    <>
      {dansSection.length > 0 && (
        <span
          role="status"
          className="inline-flex shrink-0 items-center gap-1.5 rounded-full px-2 py-0.5 text-2xs font-semibold"
          style={stylePersonne(dansSection[0].nom)}
        >
          {libelleEditeurs(dansSection)} dans cette section
        </span>
      )}
      <PresentsSection sectionId={section.id} titre={section.titre} etapeIds={etapeIds} className="relative" />
    </>
  );
  const etat = section.placee ? 'Placée dans le plan' : 'Dans le bac : pas encore placée dans le plan';
  const systemes = (
    <MultiSelect
      options={optionsSystemes(mep, descriptions)}
      valeurs={section.systemeIds}
      onChange={(ids) => void enregistrer({ systemeIds: ids })}
      placeholder="Systèmes de la section : aucun"
      placeholderRecherche="Rechercher un système…"
      vide="Aucun système dans la MEP."
      disabled={!modifiable}
    />
  );
  const boutonPlacer = (
    <Button
      taille="sm"
      icone={section.placee ? <Undo2 className="size-4" aria-hidden /> : <Inbox className="size-4" aria-hidden />}
      onClick={() => {
        setDetails(false);
        onPlacer(!section.placee);
      }}
    >
      {section.placee ? 'Remettre dans le bac' : 'Placer dans le plan'}
    </Button>
  );
  const confirmation = (
    <ConfirmDialog
      open={suppr}
      onOpenChange={setSuppr}
      titre={`Supprimer la section « ${section.titre} » ?`}
      description="Ses étapes restent dans le plan, avec leurs liens : seuls le cadre, le titre et les cercles disparaissent."
      libelleConfirmer="Supprimer la section"
      variante="danger"
      onConfirmer={onSupprimer}
    />
  );

  const rangee = (sens: SensCercle) => (
    <div className="flex flex-wrap items-center gap-1.5">
      <span className="text-2xs font-semibold tracking-[0.06em] text-fg-subtle uppercase">
        {sens === 'Entree' ? 'Entrées' : 'Sorties'}
      </span>
      {cerclesTries(section, sens).map((c) => (
        <button
          key={c.id}
          type="button"
          disabled={!modifiable}
          onClick={() => onCercle({ ...c })}
          title={c.description || undefined}
          className="inline-flex items-center gap-1.5 rounded-full border border-accent bg-accent-soft px-2 py-0.5 text-xs font-semibold text-accent-text enabled:hover:bg-accent enabled:hover:text-accent-fg"
        >
          <CircleDot className="size-3.5" aria-hidden />
          {c.titre}
          <span className="font-normal opacity-75">({c.etapes.length})</span>
        </button>
      ))}
      {modifiable && (
        <button
          type="button"
          onClick={() => onCercle({ id: null, sens, titre: '', description: '' })}
          aria-label={sens === 'Entree' ? 'Ajouter une entrée' : 'Ajouter une sortie'}
          title={sens === 'Entree' ? 'Ajouter une entrée' : 'Ajouter une sortie'}
          className="grid size-6 place-content-center rounded-full border-[1.5px] border-dashed border-accent text-accent-text hover:bg-accent hover:text-accent-fg"
        >
          <Plus className="size-3.5" aria-hidden />
        </button>
      )}
    </div>
  );

  if (compact)
    return (
      <section
        aria-label={`Section « ${section.titre} »`}
        className="flex h-11 items-center gap-2 overflow-hidden bg-accent-soft/40 px-2"
      >
        <Button
          taille="sm"
          variante="primaire"
          icone={<ArrowLeft className="size-4" aria-hidden />}
          onClick={onRetour}
          aria-label="Retour au plan"
          title="Retour au plan (Échap)"
          aria-keyshortcuts="Escape"
        >
          Plan
        </Button>
        <span className="shrink-0 text-ui text-fg-subtle">Section</span>
        {modifiable ? (
          <input
            aria-label="Titre de la section"
            value={titre.value}
            onChange={(e) => titre.setValeur(e.target.value)}
            onBlur={() => void validerTitre()}
            onKeyDown={touches(titre)}
            className="max-w-72 min-w-24 shrink rounded-md border border-dashed border-line-strong bg-transparent px-1.5 py-0.5 text-base font-semibold tracking-tight [field-sizing:content] hover:border-line focus:border-accent focus:bg-surface focus:ring-2 focus:ring-accent/30 focus:outline-none"
          />
        ) : (
          <h2 className="min-w-0 truncate px-1.5 text-base font-semibold tracking-tight">{section.titre}</h2>
        )}
        <span className="flex min-w-0 shrink items-center gap-1 overflow-hidden">
          {systemesDe(mep, section.systemeIds).map((x) => (
            <PuceSysteme key={x.id} nom={x.nom} className="px-1.5 py-0 text-2xs" />
          ))}
        </span>
        <button
          type="button"
          onClick={() => setDetails(true)}
          className="inline-flex shrink-0 items-center gap-1.5 rounded-full border border-accent bg-accent-soft px-2 py-0.5 text-xs font-semibold text-accent-text hover:bg-accent hover:text-accent-fg"
          title="Entrées de la section : voir le détail"
        >
          <CircleDot className="size-3.5" aria-hidden />
          {nbEntrees} entrée{nbEntrees > 1 ? 's' : ''}
        </button>
        <button
          type="button"
          onClick={() => setDetails(true)}
          className="inline-flex shrink-0 items-center gap-1.5 rounded-full border border-accent bg-accent-soft px-2 py-0.5 text-xs font-semibold text-accent-text hover:bg-accent hover:text-accent-fg"
          title="Sorties de la section : voir le détail"
        >
          <CircleDot className="size-3.5" aria-hidden />
          {nbSorties} sortie{nbSorties > 1 ? 's' : ''}
        </button>
        <span className="min-w-0 truncate text-xs text-fg-subtle" title={etat}>
          {section.placee ? 'Placée dans le plan' : 'Dans le bac'}
        </span>
        {presents}
        <Popover open={details} onOpenChange={setDetails}>
          <PopoverTrigger asChild>
            <Button
              taille="sm"
              className="ml-auto shrink-0"
              icone={<Info className="size-4" aria-hidden />}
              aria-label="Détails de la section"
            >
              Détails
              <ChevronDown className="size-3.5" aria-hidden />
            </Button>
          </PopoverTrigger>
          <PopoverContent align="end" className="grid w-[26rem] max-w-[calc(100vw-2rem)] gap-3 p-3">
            <div className="grid gap-1">
              <span className="text-ui font-semibold">Description</span>
              {modifiable ? (
                <Textarea
                  aria-label="Description de la section"
                  rows={2}
                  value={description.value}
                  placeholder="Description (facultative)"
                  onChange={(e) => description.setValeur(e.target.value)}
                  onBlur={() => void validerDescription()}
                  onKeyDown={touches(description)}
                />
              ) : (
                <p className="text-ui text-fg-muted">{section.description || 'Aucune description.'}</p>
              )}
            </div>
            <div className="grid gap-1">
              <span className="text-ui font-semibold">Systèmes de la section</span>
              {systemes}
            </div>
            {rangee('Entree')}
            {rangee('Sortie')}
            <p className="border-t border-line pt-2 text-xs text-fg-subtle">
              {section.auteur && <>Créée par {section.auteur} · </>}
              {etat}
            </p>
            {modifiable && (
              <div className="flex flex-wrap justify-end gap-1.5">
                {boutonPlacer}
                <Button
                  taille="sm"
                  variante="danger-doux"
                  icone={<Trash2 className="size-4" aria-hidden />}
                  onClick={() => {
                    setDetails(false);
                    setSuppr(true);
                  }}
                >
                  Supprimer
                </Button>
              </div>
            )}
          </PopoverContent>
        </Popover>
        {confirmation}
      </section>
    );

  return (
    <section aria-label={`Section « ${section.titre} »`} className="grid gap-2 bg-accent-soft/40 px-3 py-2">
      <div className="flex flex-wrap items-center gap-x-3 gap-y-1.5">
        <Button
          taille="sm"
          variante="primaire"
          icone={<ArrowLeft className="size-4" aria-hidden />}
          onClick={onRetour}
          title="Retour au plan (Échap)"
          aria-keyshortcuts="Escape"
        >
          Retour au plan
        </Button>
        <nav aria-label="Fil d’Ariane" className="flex min-w-0 flex-wrap items-center gap-1 text-ui text-fg-subtle">
          <span>{mep.outil}</span>
          <ChevronRight className="size-3.5" aria-hidden />
          <button type="button" onClick={onRetour} className="underline-offset-2 hover:text-fg hover:underline">
            {libellePlan}
          </button>
          <ChevronRight className="size-3.5" aria-hidden />
          <span aria-current="page" className="font-semibold text-fg">
            Section {section.titre}
          </span>
        </nav>
        {presents}
        {modifiable && (
          <div className="ml-auto flex flex-wrap gap-1.5">
            {boutonPlacer}
            <Button
              taille="sm"
              variante="danger-doux"
              icone={<Trash2 className="size-4" aria-hidden />}
              onClick={() => setSuppr(true)}
            >
              Supprimer la section
            </Button>
          </div>
        )}
      </div>
      <div className="flex flex-wrap items-start gap-x-3 gap-y-1.5">
        <div className="grid min-w-64 flex-1 gap-0.5">
          {modifiable ? (
            <>
              <input
                aria-label="Titre de la section"
                value={titre.value}
                onChange={(e) => titre.setValeur(e.target.value)}
                onBlur={() => void validerTitre()}
                onKeyDown={touches(titre)}
                className="rounded-md border border-transparent bg-transparent px-1.5 py-0.5 text-base font-semibold tracking-tight hover:border-line focus:border-accent focus:bg-surface focus:ring-2 focus:ring-accent/30 focus:outline-none"
              />
              <input
                aria-label="Description de la section"
                value={description.value}
                placeholder="Description (facultative)"
                onChange={(e) => description.setValeur(e.target.value)}
                onBlur={() => void validerDescription()}
                onKeyDown={touches(description)}
                className="rounded-md border border-transparent bg-transparent px-1.5 py-0.5 text-ui text-fg-muted placeholder:text-fg-subtle hover:border-line focus:border-accent focus:bg-surface focus:ring-2 focus:ring-accent/30 focus:outline-none"
              />
            </>
          ) : (
            <>
              <h2 className="px-1.5 text-base font-semibold tracking-tight">{section.titre}</h2>
              {section.description && <p className="px-1.5 text-ui text-fg-muted">{section.description}</p>}
            </>
          )}
          <p className="px-1.5 text-xs text-fg-subtle">
            {section.auteur && <>Créée par {section.auteur} · </>}
            {etat}
          </p>
        </div>
        <div className="w-full min-w-56 sm:w-72">{systemes}</div>
      </div>
      <div className="flex flex-wrap items-center gap-x-6 gap-y-1.5">
        {rangee('Entree')}
        {rangee('Sortie')}
      </div>
      {confirmation}
    </section>
  );
}
