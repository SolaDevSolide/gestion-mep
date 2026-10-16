import {
  ArrowLeft,
  ChevronRight,
  CircleDot,
  FolderPlus,
  Inbox,
  PanelRightClose,
  PanelRightOpen,
  Pencil,
  Plus,
  Trash2,
  Undo2,
} from 'lucide-react';
import { useEffect, useState, type PointerEvent as PointerEventReact } from 'react';
import { createPortal } from 'react-dom';
import { couleurPersonne } from '../../ui/Avatar';
import { libelleEditeurs, useAutresPresents, usePresenceElement } from '../../presence';
import { cleIciSection, PresentsSection } from './PresentsGraphe';
import type { Mep } from '../../types';
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
 * Bac des sections : colonne à droite du graphe avec les sections créées mais pas encore placées. On les glisse dans le
 * graphe (ou « Placer »), puis on relie leurs cercles. Tant qu'une section reste au bac, « Vérifier le plan » bloque le Go.
 */
export function BacSections({
  mep,
  sections,
  nbEtapes,
  modifiable,
  onPlacer,
  onOuvrir,
  onNouvelle,
  replieForce = false,
}: {
  mep: Mep;
  /** Replié tant que l'espace d'une étape occupe la droite de l'écran (le graphe garde sa largeur). */
  replieForce?: boolean;
  /** Sections du plan pas encore placées. */
  sections: Section[];
  nbEtapes: (sectionId: string) => number;
  modifiable: boolean;
  onPlacer: (sectionId: string) => void;
  onOuvrir: (sectionId: string) => void;
  onNouvelle: () => void;
}) {
  const [glisse, setGlisse] = useState<{ section: Section; x: number; y: number; dessus: boolean } | null>(null);
  const [replie, setReplie] = useState(lireBacReplie);
  const basculer = () =>
    setReplie((v) => {
      try {
        window.localStorage.setItem('graphe.bacReplie', v ? 'non' : 'oui');
      } catch {
        /* préférence non mémorisée */
      }
      return !v;
    });

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

  if (replie || replieForce)
    return (
      <aside
        aria-label="Bac des sections"
        className="flex w-11 shrink-0 flex-col items-center border-l border-line bg-surface-2/40 py-2"
      >
        <BoutonIcone
          label={
            replieForce
              ? `Sections à placer (${sections.length}) : fermez l’étape ouverte pour afficher le bac`
              : `Afficher les sections à placer (${sections.length})`
          }
          taille="sm"
          disabled={replieForce}
          onClick={basculer}
        >
          <PanelRightOpen className="size-4" aria-hidden />
        </BoutonIcone>
        <span className="mt-2 flex items-center gap-1 text-[11px] font-semibold tracking-[0.06em] text-fg-subtle uppercase [writing-mode:vertical-rl]">
          <Inbox className="size-3.5 rotate-90" aria-hidden />
          Sections à placer ({sections.length})
        </span>
      </aside>
    );

  return (
    // Panneau intérieur du cadre du plan : sa propre hauteur de défilement, le graphe garde la sienne.
    <aside
      aria-label="Bac des sections"
      className="flex w-60 shrink-0 flex-col border-l border-line bg-surface-2/40 max-sm:w-48"
    >
      <div className="flex shrink-0 items-center justify-between gap-2 border-b border-line px-2.5 py-1.5">
        <h3 className="flex items-center gap-1.5 text-[11px] font-semibold tracking-[0.06em] text-fg-subtle uppercase">
          <Inbox className="size-3.5" aria-hidden />
          Sections à placer ({sections.length})
        </h3>
        <BoutonIcone label="Replier le bac des sections" taille="sm" onClick={basculer}>
          <PanelRightClose className="size-4" aria-hidden />
        </BoutonIcone>
      </div>
      <div className="grid min-h-0 flex-1 content-start gap-2 overflow-y-auto p-2.5">
        {modifiable && (
          <Button taille="sm" icone={<FolderPlus className="size-4" aria-hidden />} onClick={onNouvelle}>
            Nouvelle section
          </Button>
        )}
        {sections.length === 0 ? (
          <p className="text-[13px] text-fg-subtle">
            {modifiable ? 'Aucune section en attente. Une nouvelle section arrive ici.' : 'Aucune section en attente.'}
          </p>
        ) : (
          <ul className="grid gap-2">
            {sections.map((s) => (
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
                <p className="truncate text-[14px] font-semibold" title={s.titre}>
                  {s.titre}
                </p>
                <div className="flex flex-wrap gap-1">
                  {systemesDe(mep, s.systemeIds).map((x) => (
                    <PuceSysteme key={x.id} nom={x.nom} className="px-1.5 py-0 text-[11px]" />
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
        {glisse &&
          createPortal(
            <div
              aria-hidden
              className={cn(
                'pointer-events-none fixed z-50 w-56 -translate-x-1/2 -translate-y-1/2 rounded-xl border-2 bg-surface px-3 py-2 text-sm font-semibold shadow-lg',
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
    </aside>
  );
}

function lireBacReplie(): boolean {
  try {
    return window.localStorage.getItem('graphe.bacReplie') === 'oui';
  } catch {
    return false;
  }
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
}: {
  mep: Mep;
  section: Section;
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
  const [titre, setTitre] = useState(section.titre);
  const [description, setDescription] = useState(section.description);
  const [suppr, setSuppr] = useState(false);
  useEffect(() => {
    setTitre(section.titre);
    setDescription(section.description);
  }, [section.titre, section.description]);
  // Les autres voient « X modifie » sur la section dans le plan tant que sa vue d'édition est ouverte ici.
  usePresenceElement(`section:${section.id}`, modifiable);
  // Où l'on est (même en lecture) : les autres voient votre avatar sur la section.
  usePresenceElement(cleIciSection(section.id), true);
  const autres = useAutresPresents();
  const dansSection = autres.filter((p) =>
    p.elements.some((e) => e === `section:${section.id}` || etapeIds.some((id) => e === `etape:${id}`)),
  );
  const enregistrer = (p: Partial<{ titre: string; description: string; systemeIds: string[] }>) =>
    void onModifier({ titre: section.titre, description: section.description, systemeIds: section.systemeIds, ...p });

  const rangee = (sens: SensCercle) => (
    <div className="flex flex-wrap items-center gap-1.5">
      <span className="text-[11px] font-semibold tracking-[0.06em] text-fg-subtle uppercase">
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
        <nav aria-label="Fil d’Ariane" className="flex min-w-0 flex-wrap items-center gap-1 text-[13px] text-fg-subtle">
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
        {dansSection.length > 0 && (
          <span
            role="status"
            className="inline-flex items-center gap-1.5 rounded-full px-2 py-0.5 text-[11px] font-semibold text-white"
            style={{ backgroundColor: couleurPersonne(dansSection[0].nom) }}
          >
            {libelleEditeurs(dansSection)} dans cette section
          </span>
        )}
        <PresentsSection sectionId={section.id} titre={section.titre} etapeIds={etapeIds} className="relative" />
        {modifiable && (
          <div className="ml-auto flex flex-wrap gap-1.5">
            <Button
              taille="sm"
              icone={
                section.placee ? <Undo2 className="size-4" aria-hidden /> : <Inbox className="size-4" aria-hidden />
              }
              onClick={() => onPlacer(!section.placee)}
            >
              {section.placee ? 'Remettre dans le bac' : 'Placer dans le plan'}
            </Button>
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
                value={titre}
                onChange={(e) => setTitre(e.target.value)}
                onBlur={() => titre.trim() && titre.trim() !== section.titre && enregistrer({ titre: titre.trim() })}
                onKeyDown={(e) => e.key === 'Enter' && e.currentTarget.blur()}
                className="rounded-md border border-transparent bg-transparent px-1.5 py-0.5 text-base font-semibold tracking-tight hover:border-line focus:border-accent focus:bg-surface focus:ring-2 focus:ring-accent/30 focus:outline-none"
              />
              <input
                aria-label="Description de la section"
                value={description}
                placeholder="Description (facultative)"
                onChange={(e) => setDescription(e.target.value)}
                onBlur={() =>
                  description.trim() !== section.description && enregistrer({ description: description.trim() })
                }
                onKeyDown={(e) => e.key === 'Enter' && e.currentTarget.blur()}
                className="rounded-md border border-transparent bg-transparent px-1.5 py-0.5 text-[13px] text-fg-muted placeholder:text-fg-subtle hover:border-line focus:border-accent focus:bg-surface focus:ring-2 focus:ring-accent/30 focus:outline-none"
              />
            </>
          ) : (
            <>
              <h2 className="px-1.5 text-base font-semibold tracking-tight">{section.titre}</h2>
              {section.description && <p className="px-1.5 text-[13px] text-fg-muted">{section.description}</p>}
            </>
          )}
          <p className="px-1.5 text-xs text-fg-subtle">
            {section.auteur && <>Créée par {section.auteur} · </>}
            {section.placee ? 'Placée dans le plan' : 'Dans le bac : pas encore placée dans le plan'}
          </p>
        </div>
        <div className="w-full min-w-56 sm:w-72">
          <MultiSelect
            options={optionsSystemes(mep, descriptions)}
            valeurs={section.systemeIds}
            onChange={(ids) => enregistrer({ systemeIds: ids })}
            placeholder="Systèmes de la section : aucun"
            placeholderRecherche="Rechercher un système…"
            vide="Aucun système dans la MEP."
            disabled={!modifiable}
          />
        </div>
      </div>
      <div className="flex flex-wrap items-center gap-x-6 gap-y-1.5">
        {rangee('Entree')}
        {rangee('Sortie')}
      </div>
      <ConfirmDialog
        open={suppr}
        onOpenChange={setSuppr}
        titre={`Supprimer la section « ${section.titre} » ?`}
        description="Ses étapes restent dans le plan, avec leurs liens : seuls le cadre, le titre et les cercles disparaissent."
        libelleConfirmer="Supprimer la section"
        variante="danger"
        onConfirmer={onSupprimer}
      />
    </section>
  );
}
