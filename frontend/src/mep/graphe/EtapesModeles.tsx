import {
  Ban,
  Blocks,
  Check,
  ChevronDown,
  ChevronLeft,
  CopyPlus,
  ExternalLink,
  Hand,
  Info,
  Library,
  TriangleAlert,
  X,
} from 'lucide-react';
import { useEffect, useMemo, useState, type PointerEvent as PointerEventReact } from 'react';
import { useNavigate } from 'react-router-dom';
import { premiereLigne } from '../../blocs';
import {
  analyserRecherche,
  filtrerEtapesModeles,
  libelleType,
  rechercheVide,
  repartitionTypes,
  trierAlpha,
  trierParUsage,
} from '../../bibliotheque';
import { LIBELLE_FIN } from '../../format';
import {
  BadgesModele,
  CHAMPS_ETAPES,
  CompteurUtilisations,
  FiltreType,
  GrilleSystemes,
  LectureRecherche,
  MentionTri,
  RechercheBibliotheque,
  type SystemeBiblio,
  UNITE_ETAPES,
} from '../../parametres/bibliotheque';
import type { Etape, Mep, ModeleEtape, SystemeCatalogue, TypeEtape, TypePlan } from '../../types';
import { Alerte } from '../../ui/Alerte';
import { Button } from '../../ui/Button';
import { cn } from '../../ui/cn';
import { Dialog } from '../../ui/Dialog';
import { Field, Input } from '../../ui/Input';
import { Instructions, Markdown } from '../../ui/Instructions';
import { Menu, MenuContent, MenuItem, MenuLabel, MenuSeparator, MenuTrigger } from '../../ui/Menu';
import { Segmented } from '../../ui/Segmented';
import { CarteEtape } from './CarteEtape';
import { PuceSysteme, systemesDe } from './systemes';

/** Identifiant de l'étape flottante (pas encore dans le plan) pour le glisser-déposer du graphe. */
export const ID_FLOTTANTE = '__etape-modele-flottante__';

/** Systèmes du catalogue présents dans la MEP. */
export const catalogueDeLaMep = (mep: Pick<Mep, 'systemes'>) =>
  mep.systemes.map((s) => s.catalogueId).filter((c): c is string => !!c);

/** Étapes modèles inserables dans ce plan : une sauvegarde n'a pas sa place dans le retour arrière. */
export const inserables = (modeles: ModeleEtape[], plan: TypePlan) =>
  plan === 'Deploiement' ? modeles : modeles.filter((m) => m.type === 'Standard');

/** Systèmes d'un modèle absents de la MEP (noms du catalogue). */
export function systemesAbsents(mep: Pick<Mep, 'systemes'>, m: ModeleEtape, catalogue: SystemeCatalogue[]): string[] {
  const presents = new Set(catalogueDeLaMep(mep));
  return m.systemeCatalogueIds
    .filter((id) => !presents.has(id))
    .map((id) => catalogue.find((c) => c.id === id)?.nom ?? 'Système inconnu');
}

/** Code que prendra la prochaine étape du plan (E0007, R0003), comme côté serveur. */
export function prochainCode(etapes: Pick<Etape, 'code'>[], plan: TypePlan): string {
  const prefixe = plan === 'Deploiement' ? 'E' : 'R';
  const max = etapes.reduce((m, e) => {
    const n = e.code.startsWith(prefixe) ? Number(e.code.slice(1)) : NaN;
    return Number.isInteger(n) && n > m ? n : m;
  }, 0);
  return `${prefixe}${String(max + 1).padStart(4, '0')}`;
}

/** Aperçu de la copie d'un modèle, pour l'étape flottante : rien n'est écrit tant qu'elle n'est pas posée. */
export function apercuDepuisModele(mep: Mep, plan: TypePlan, m: ModeleEtape): Etape {
  const systemeIds = mep.systemes.filter((s) => s.catalogueId && m.systemeCatalogueIds.includes(s.catalogueId));
  return {
    id: ID_FLOTTANTE,
    plan,
    rang: 0,
    aPlacer: false,
    code: prochainCode(
      mep.etapes.filter((e) => e.plan === plan),
      plan,
    ),
    ligne: -1,
    ordre: 0,
    action: m.action,
    systemeIds: systemeIds.map((s) => s.id),
    responsableId: null,
    criticite: m.criticite ?? null,
    dureeEstimeeMinutes: m.dureeEstimeeMinutes ?? null,
    etat: 'AFaire',
    instructions: m.instructions,
    resultatAttendu: m.resultatAttendu,
    preuve: '',
    commentaire: '',
    debutReel: null,
    finReelle: null,
    revision: 0,
    executantId: null,
    dependances: [],
    dependancesDerivees: [],
    sectionId: null,
    portsAttendus: [],
    resolutionIds: m.resolutionIds,
    type: m.type,
    pointDeNonRetour: false,
    nonRetourAutorise: null,
    fin: m.fin,
    resultatConstate: '',
    sauvegardeEmplacement: m.sauvegardeEmplacement,
    sauvegardeMethodeRecuperation: m.sauvegardeMethodeRecuperation,
    disponibilite: { statut: 'Disponible', libelle: 'Disponible' },
    modeleEtapeId: m.id,
    // Comme côté serveur : l'environnement unique d'un système est repris, sinon il reste à choisir.
    environnements: systemeIds
      .filter((s) => s.environnements.length === 1)
      .map((s) => ({ systemeId: s.id, nom: s.environnements[0].nom })),
  };
}

/** Les 5 étapes modèles les plus utilisées pour les systèmes de la MEP (menu « Étape modèle », menus de création). */
export function modelesFrequents(mep: Pick<Mep, 'systemes'>, modeles: ModeleEtape[]): ModeleEtape[] {
  const parmi = catalogueDeLaMep(mep);
  return trierParUsage(
    modeles.filter((m) => m.systemeCatalogueIds.some((s) => parmi.includes(s))),
    (m) => m.creeLe,
  ).slice(0, 5);
}

const STYLE_BOUTON =
  'inline-flex h-8 items-center gap-1.5 rounded-md px-2.5 text-[13px] font-semibold text-fg-muted hover:bg-surface-2 hover:text-fg data-[state=open]:bg-accent-soft data-[state=open]:text-accent-text';

/**
 * Deux boutons accolés en haut à gauche du graphe : « Étape modèle » (les 5 plus utilisées pour les systèmes de la
 * MEP) et « Parcourir la bibliothèque » (fenêtre de choix).
 */
export function BoutonsEtapesModeles({
  mep,
  modeles,
  catalogue,
  onChoisir,
  onParcourir,
}: {
  mep: Mep;
  modeles: ModeleEtape[];
  catalogue: SystemeCatalogue[];
  onChoisir: (m: ModeleEtape) => void;
  onParcourir: () => void;
}) {
  const navigate = useNavigate();
  const parmi = catalogueDeLaMep(mep);
  const frequentes = modelesFrequents(mep, modeles);
  const nom = (id: string) => catalogue.find((c) => c.id === id)?.nom ?? '';
  return (
    <div
      data-controles
      role="toolbar"
      aria-label="Étapes modèles"
      className="absolute top-3 left-3 z-20 flex items-center gap-0.5 rounded-lg border border-line bg-surface/95 p-0.5 shadow-md backdrop-blur"
    >
      <Menu modal={false}>
        <MenuTrigger asChild>
          <button type="button" className={STYLE_BOUTON}>
            <Blocks className="size-4" aria-hidden />
            Étape modèle
            <ChevronDown className="size-3.5" aria-hidden />
          </button>
        </MenuTrigger>
        <MenuContent align="start" className="w-[26rem] max-w-[calc(100vw-2rem)]">
          <MenuLabel>Les plus utilisées · systèmes de la MEP</MenuLabel>
          {frequentes.length === 0 && (
            <p className="px-2 py-1.5 text-[13px] text-fg-subtle">
              {parmi.length === 0
                ? 'Ajoutez à la MEP un système du catalogue pour voir ses étapes modèles.'
                : 'Aucune étape modèle pour les systèmes de la MEP.'}
            </p>
          )}
          {frequentes.map((m) => (
            <MenuItem key={m.id} onSelect={() => onChoisir(m)}>
              <CopyPlus className="size-4 shrink-0 text-fg-subtle" aria-hidden />
              <span className="min-w-0 flex-1 truncate">{m.action}</span>
              <span className="shrink-0 text-xs text-fg-subtle">{m.systemeCatalogueIds.map(nom).join(', ')}</span>
              <CompteurUtilisations n={m.utilisations} />
            </MenuItem>
          ))}
          <MenuSeparator />
          <MenuItem onSelect={onParcourir}>
            <Library className="size-4 text-fg-subtle" aria-hidden />
            Parcourir la bibliothèque…
          </MenuItem>
          <MenuItem onSelect={() => void navigate('/parametres/etapes')}>
            <ExternalLink className="size-4 text-fg-subtle" aria-hidden />
            Gérer les étapes modèles
          </MenuItem>
        </MenuContent>
      </Menu>
      <span className="h-5 w-px bg-line" aria-hidden />
      <button type="button" className={STYLE_BOUTON} onClick={onParcourir}>
        <Library className="size-4" aria-hidden />
        Parcourir la bibliothèque
      </button>
    </div>
  );
}

/** Avertissement : systèmes du modèle absents de la MEP (on ne les ajoute pas d'office). */
export function AvisSystemesAbsents({ absents, className }: { absents: string[]; className?: string }) {
  if (absents.length === 0) return null;
  return (
    <Alerte ton="warn" icone={TriangleAlert} className={className}>
      {absents.length > 1 ? 'Les systèmes' : 'Le système'} {absents.join(', ')} de ce modèle{' '}
      {absents.length > 1 ? 'ne sont pas' : 'n’est pas'} dans la MEP : la copie ne {absents.length > 1 ? 'les' : 'le'}{' '}
      reprend pas. Ajoutez-{absents.length > 1 ? 'les' : 'le'} aux systèmes de la MEP (Préparation › Systèmes) si
      besoin.
    </Alerte>
  );
}

/**
 * Fenêtre « Parcourir la bibliothèque » : même modèle que la page (choix du système, puis la bibliothèque du système),
 * limitée par défaut aux systèmes de la MEP. L'étape choisie arrive flottante dans le graphe, ou, ouverte depuis un menu
 * de création (`destination`), est posée tout de suite à l'endroit choisi.
 */
export function ParcourirDialog({
  ouvert,
  mep,
  plan,
  modeles,
  catalogue,
  onFermer,
  onInserer,
  destination,
}: {
  ouvert: boolean;
  mep: Mep;
  plan: TypePlan;
  modeles: ModeleEtape[];
  catalogue: SystemeCatalogue[];
  onFermer: () => void;
  onInserer: (m: ModeleEtape) => void;
  /** Endroit où la copie sera posée (« après E0004 », « sur le lien E0001 → E0003 »…), quand il est déjà choisi. */
  destination?: string | null;
}) {
  const [recherche, setRecherche] = useState('');
  const [portee, setPortee] = useState<'mep' | 'tous'>('mep');
  const [systeme, setSysteme] = useState<string | null>(null);
  const [type, setType] = useState<TypeEtape | ''>('');
  const [choix, setChoix] = useState<string | null>(null);
  useEffect(() => {
    if (!ouvert) return;
    setRecherche('');
    setSysteme(null);
    setChoix(null);
    setType('');
    setPortee('mep');
  }, [ouvert]);

  const permis = useMemo(() => inserables(modeles, plan), [modeles, plan]);
  const parmiMep = catalogueDeLaMep(mep);
  const nomDe = (id: string) => catalogue.find((c) => c.id === id)?.nom ?? '';
  const proposes: SystemeBiblio[] = catalogue
    .filter((c) => c.actif || permis.some((m) => m.systemeCatalogueIds.includes(c.id)))
    .map((c) => ({ id: c.id, nom: c.nom, description: c.description, actif: c.actif }));
  const visibles = portee === 'mep' ? proposes.filter((s) => parmiMep.includes(s.id)) : proposes;
  const analyse = useMemo(
    () =>
      analyserRecherche(
        recherche,
        catalogue.map((c) => c.nom),
      ),
    [recherche, catalogue],
  );
  const liste = filtrerEtapesModeles(permis, nomDe, analyse, {
    systemeId: systeme,
    type,
    parmi: portee === 'mep' ? parmiMep : undefined,
  });
  const choixSysteme = !systeme && rechercheVide(analyse);
  const m = liste.find((x) => x.id === choix) ?? null;
  const duSysteme = (id: string) =>
    permis.filter((x) => x.systemeCatalogueIds.includes(id) && (!type || x.type === type));
  const absents = m ? systemesAbsents(mep, m, catalogue) : [];

  return (
    <Dialog
      open={ouvert}
      onOpenChange={(o) => {
        if (!o) onFermer();
      }}
      titre="Parcourir la bibliothèque d’étapes"
      description="L’étape choisie est copiée dans le plan : la modifier ensuite ne change pas le modèle."
      taille="xl"
      pied={
        <>
          <p className="mr-auto flex items-center gap-1.5 text-[13px] text-fg-muted">
            <Hand className="size-4 text-fg-subtle" aria-hidden />
            {destination
              ? `La copie est posée ${destination}.`
              : 'L’étape arrive flottante dans le graphe : vous la posez ensuite à la main.'}
          </p>
          <Button variante="fantome" onClick={onFermer}>
            Annuler
          </Button>
          <Button
            variante="primaire"
            disabled={!m || choixSysteme}
            icone={<CopyPlus className="size-4" aria-hidden />}
            onClick={() => m && onInserer(m)}
          >
            Insérer une copie
          </Button>
        </>
      }
    >
      <div className="flex flex-wrap items-center gap-3">
        <RechercheBibliotheque
          valeur={recherche}
          onChange={setRecherche}
          placeholder={
            systeme ? `Rechercher dans ${nomDe(systeme)} (nom, type)…` : 'Rechercher, ex. « BDD sauvegarde »…'
          }
        />
        {plan === 'Deploiement' && <FiltreType valeur={type} onChange={setType} className="w-40" />}
        <Segmented
          label="Systèmes proposés"
          valeur={portee}
          onChange={(v) => {
            setPortee(v);
            if (v === 'mep' && systeme && !parmiMep.includes(systeme)) setSysteme(null);
          }}
          options={[
            {
              valeur: 'mep',
              libelle: `Systèmes de la MEP (${proposes.filter((s) => parmiMep.includes(s.id)).length})`,
            },
            { valeur: 'tous', libelle: `Tous (${proposes.length})` },
          ]}
        />
      </div>
      {plan === 'RetourArriere' && (
        <p className="mt-2 text-xs text-fg-subtle">Retour arrière : seules les étapes normales sont proposées.</p>
      )}

      {choixSysteme ? (
        <div className="mt-4">
          <p className="mb-2 text-xs font-semibold tracking-wider text-fg-subtle uppercase">
            Choisissez un système
            {portee === 'mep' && (
              <span className="font-normal tracking-normal normal-case">
                {' '}
                · {visibles.length} sur {proposes.length}, filtrés sur les systèmes de la MEP
              </span>
            )}
          </p>
          {visibles.length === 0 ? (
            <p className="py-6 text-center text-sm text-fg-subtle">
              Aucun système du catalogue dans la MEP.{' '}
              <button
                type="button"
                className="font-medium text-accent-text hover:underline"
                onClick={() => setPortee('tous')}
              >
                Voir tous les systèmes
              </button>
            </p>
          ) : (
            <GrilleSystemes
              systemes={visibles}
              compter={(s) => duSysteme(s.id).length}
              detail={(s) => repartitionTypes(duSysteme(s.id))}
              unite={['étape', 'étapes']}
              onChoisir={(s) => {
                setSysteme(s.id);
                setChoix(null);
              }}
            />
          )}
          {portee === 'mep' && proposes.length > visibles.length && (
            <p className="mt-3 text-[13px] text-fg-subtle">
              Autres systèmes du catalogue :{' '}
              {trierAlpha(proposes.filter((s) => !parmiMep.includes(s.id)))
                .map((s) => s.nom)
                .join(', ')}{' '}
              ·{' '}
              <button
                type="button"
                className="font-medium text-accent-text hover:underline"
                onClick={() => setPortee('tous')}
              >
                les afficher
              </button>
            </p>
          )}
        </div>
      ) : (
        <>
          <div className="mt-4 flex flex-wrap items-center justify-between gap-2">
            <div className="flex flex-wrap items-center gap-2">
              <button
                type="button"
                onClick={() => {
                  setSysteme(null);
                  setRecherche('');
                  setChoix(null);
                }}
                className="inline-flex items-center gap-1 text-sm font-medium text-fg-muted hover:text-fg"
              >
                <ChevronLeft className="size-4" aria-hidden />
                Systèmes
              </button>
              {systeme && <PuceSysteme nom={nomDe(systeme)} className="py-0 text-xs" />}
              {!rechercheVide(analyse) && (
                <LectureRecherche analyse={analyse} nb={liste.length} unite={UNITE_ETAPES} champs={CHAMPS_ETAPES} />
              )}
            </div>
            <MentionTri />
          </div>
          <div className="mt-3 grid gap-4 md:grid-cols-[minmax(0,1fr)_minmax(0,1fr)]">
            <ul
              role="listbox"
              aria-label="Étapes modèles"
              className="grid max-h-[400px] min-w-0 grid-cols-1 content-start gap-1.5 overflow-y-auto pr-1"
            >
              {liste.map((x) => (
                <li key={x.id} role="option" aria-selected={choix === x.id} className="min-w-0">
                  <button
                    type="button"
                    onClick={() => setChoix(x.id)}
                    onDoubleClick={() => onInserer(x)}
                    className={cn(
                      'grid w-full min-w-0 grid-cols-[minmax(0,1fr)] gap-1 rounded-lg border px-3 py-2 text-left transition-colors',
                      choix === x.id
                        ? 'border-accent bg-accent-soft shadow-[0_0_0_2px_var(--accent-soft)]'
                        : 'border-line hover:border-line-strong hover:bg-surface-2',
                    )}
                  >
                    <span className="flex items-center gap-2 text-sm font-semibold">
                      <Blocks className="size-4 shrink-0 text-accent" aria-hidden />
                      <span className="min-w-0 flex-1 truncate">{x.action}</span>
                      <CompteurUtilisations n={x.utilisations} />
                    </span>
                    <span className="flex items-center gap-1.5 text-xs text-fg-muted">
                      {(!systeme || x.systemeCatalogueIds.length > 1) &&
                        x.systemeCatalogueIds.map((s) => (
                          <PuceSysteme key={s} nom={nomDe(s)} className="px-1.5 py-0 text-[11px]" />
                        ))}
                      <span className="min-w-0 flex-1 truncate">
                        {libelleType(x.type)}
                        {premiereLigne(x.instructions) && ` · ${premiereLigne(x.instructions)}`}
                      </span>
                    </span>
                  </button>
                </li>
              ))}
              {liste.length === 0 && (
                <li className="py-10 text-center text-sm text-fg-subtle">Aucune étape modèle ne correspond.</li>
              )}
            </ul>
            <div className="min-w-0 rounded-xl border border-line bg-surface-2/50 p-4">
              {m ? (
                <div className="grid gap-3 text-sm">
                  <div>
                    <p className="text-xs font-semibold tracking-wider text-fg-subtle uppercase">Aperçu</p>
                    <h3 className="mt-1 text-base font-semibold">{m.action}</h3>
                    <div className="mt-1.5 flex flex-wrap gap-1.5">
                      <BadgesModele m={m} />
                    </div>
                  </div>
                  <div>
                    <p className="font-medium">Instructions exactes</p>
                    <div className="mt-1">
                      <Instructions source={m.instructions} />
                    </div>
                  </div>
                  <div>
                    <p className="font-medium">Résultat attendu · {LIBELLE_FIN[m.fin]}</p>
                    <Markdown className="mt-1 text-fg-muted" source={m.resultatAttendu} vide="Non renseigné." />
                  </div>
                  <AvisSystemesAbsents absents={absents} />
                  <Alerte ton="later" icone={Info}>
                    À compléter après l’insertion : les liens (attend / débloque) et, si besoin, le point de non-retour
                    (un modèle n’en a jamais).
                  </Alerte>
                </div>
              ) : (
                <p className="py-16 text-center text-sm text-fg-subtle">
                  Choisissez une étape modèle pour la voir ici.
                </p>
              )}
            </div>
          </div>
        </>
      )}
    </Dialog>
  );
}

/**
 * Étape insérée, pas encore posée : elle flotte au-dessus du graphe jusqu'à ce qu'on la glisse sur une ligne ou qu'on
 * clique « Poser ici ». Échap (ou la croix) annule l'insertion.
 */
export function EtapeFlottante({
  mep,
  etape,
  descriptions,
  glissee,
  onAppuyer,
  onAnnuler,
}: {
  mep: Mep;
  etape: Etape;
  descriptions: Map<string, string>;
  glissee: boolean;
  onAppuyer: (ev: PointerEventReact<HTMLDivElement>) => void;
  onAnnuler: () => void;
}) {
  return (
    <div
      role="group"
      aria-label={`${etape.code}, copie de l’étape modèle « ${etape.action} », à placer`}
      className="pointer-events-none absolute top-[58px] left-4 z-30 grid gap-1.5"
    >
      <div className="pointer-events-auto flex w-fit items-center gap-1.5 rounded-full border border-accent bg-accent-soft py-0.5 pr-1 pl-2.5 text-xs font-semibold text-accent-text shadow-sm">
        <Hand className="size-3.5" aria-hidden />À placer
        <span className="font-normal text-fg-muted">· Échap pour annuler</span>
        <button
          type="button"
          onClick={onAnnuler}
          aria-label="Annuler l’insertion"
          title="Annuler l’insertion"
          className="rounded-full p-0.5 hover:bg-surface"
        >
          <X className="size-3.5" aria-hidden />
        </button>
      </div>
      {/* Poignée du glisser : la souris pose l'étape ; au clavier, les boutons « Poser ici » font de même. */}
      {/* eslint-disable-next-line jsx-a11y/no-static-element-interactions */}
      <div
        data-noeud={ID_FLOTTANTE}
        // Hors du graphe (qui interdit la sélection) : sans cela, glisser la copie sélectionnait le texte de la page.
        onPointerDown={(ev) => {
          ev.preventDefault();
          onAppuyer(ev);
        }}
        className={cn(
          'pointer-events-auto ml-3 w-fit -rotate-2 cursor-grab touch-none rounded-lg shadow-[0_18px_40px_-12px_rgba(0,0,0,0.35)] ring-2 ring-accent ring-offset-2 ring-offset-surface',
          glissee && 'opacity-30',
        )}
      >
        <div data-carte={ID_FLOTTANTE}>
          <CarteEtape mep={mep} etape={etape} mode="edition" descriptions={descriptions} />
        </div>
      </div>
    </div>
  );
}

/** Confirmation « Transformer en étape modèle » depuis l'espace d'une étape : nom, systèmes, ce qui est repris. */
export function TransformerDialog({
  ouvert,
  onFermer,
  mep,
  etape,
  catalogue,
  onCreer,
}: {
  ouvert: boolean;
  onFermer: () => void;
  mep: Mep;
  etape: Etape;
  catalogue: SystemeCatalogue[];
  onCreer: (nom: string) => Promise<boolean>;
}) {
  const [nom, setNom] = useState(etape.action);
  const [envoi, setEnvoi] = useState(false);
  useEffect(() => {
    if (ouvert) setNom(etape.action);
  }, [ouvert, etape.action]);
  const systemes = systemesDe(mep, etape.systemeIds);
  const auCatalogue = systemes.filter((s) => s.catalogueId && catalogue.some((c) => c.id === s.catalogueId));
  const horsCatalogue = systemes.filter((s) => !auCatalogue.includes(s));
  const repris = [
    'Action, systèmes, criticité, durée estimée',
    etape.type === 'Sauvegarde' ? 'Type Sauvegarde, emplacement et récupération' : 'Type d’étape',
    'Instructions exactes et résultat attendu',
    `Fin de l’étape (${LIBELLE_FIN[etape.fin]})`,
    `Résolutions en cas d’échec (${etape.resolutionIds.length})`,
  ];
  const nonRepris = [
    'Point de non-retour',
    'Liens avec les autres étapes',
    'Environnements (propres à chaque MEP)',
    'Exécutant, résultats et pièces jointes',
  ];

  async function creer() {
    setEnvoi(true);
    const ok = await onCreer(nom.trim());
    setEnvoi(false);
    if (ok) onFermer();
  }

  return (
    <Dialog
      open={ouvert}
      onOpenChange={(o) => {
        if (!o) onFermer();
      }}
      titre={`Transformer ${etape.code} en étape modèle ?`}
      description="Une copie de l’étape rejoint la bibliothèque d’étapes. L’étape de cette MEP ne change pas."
      taille="md"
      pied={
        <>
          <Button variante="fantome" onClick={onFermer}>
            Annuler
          </Button>
          <Button
            variante="primaire"
            icone={<Blocks className="size-4" aria-hidden />}
            disabled={!nom.trim() || auCatalogue.length === 0}
            chargement={envoi}
            onClick={() => void creer()}
          >
            Créer l’étape modèle
          </Button>
        </>
      }
    >
      <div className="grid gap-4">
        <Field label="Nom de l’étape modèle *">
          {(id) => <Input id={id} value={nom} onChange={(e) => setNom(e.target.value)} />}
        </Field>
        <div className="flex flex-wrap items-center gap-1.5 text-sm">
          <span className="text-fg-muted">Systèmes :</span>
          {auCatalogue.map((s) => (
            <PuceSysteme key={s.id} nom={s.nom} />
          ))}
          {auCatalogue.length === 0 && <span className="text-fg-subtle">aucun système du catalogue</span>}
        </div>
        <div className="grid gap-3 sm:grid-cols-2">
          <div>
            <p className="text-xs font-semibold tracking-wider text-fg-subtle uppercase">Repris dans le modèle</p>
            <ul className="mt-1.5 grid gap-1 text-[13px]">
              {repris.map((r) => (
                <li key={r} className="flex items-start gap-1.5">
                  <Check className="mt-0.5 size-3.5 shrink-0 text-ok-solid" aria-hidden />
                  {r}
                </li>
              ))}
            </ul>
          </div>
          <div>
            <p className="text-xs font-semibold tracking-wider text-fg-subtle uppercase">Non repris</p>
            <ul className="mt-1.5 grid gap-1 text-[13px] text-fg-muted">
              {nonRepris.map((r) => (
                <li key={r} className="flex items-start gap-1.5">
                  <X className="mt-0.5 size-3.5 shrink-0 text-fg-subtle" aria-hidden />
                  {r}
                </li>
              ))}
            </ul>
          </div>
        </div>
        {auCatalogue.length === 0 && (
          <Alerte ton="danger" icone={TriangleAlert}>
            Une étape modèle est rangée par système du catalogue : choisissez pour {etape.code} au moins un système de
            la MEP issu du catalogue.
          </Alerte>
        )}
        {auCatalogue.length > 0 && horsCatalogue.length > 0 && (
          <Alerte ton="warn" icone={TriangleAlert}>
            {horsCatalogue.map((s) => s.nom).join(', ')} hors catalogue : non repris dans le modèle.
          </Alerte>
        )}
        {etape.pointDeNonRetour && (
          <Alerte ton="warn" icone={Ban}>
            {etape.code} est un point de non-retour dans cette MEP. Une étape modèle n’en a jamais : il restera à le
            cocher dans chaque MEP où on l’insère.
          </Alerte>
        )}
      </div>
    </Dialog>
  );
}

/** Mention dans l'espace d'une étape insérée depuis la bibliothèque (rien sur la fiche du graphe). */
export function MentionCopie({ modeleId, modeles }: { modeleId: string; modeles: ModeleEtape[] | null }) {
  if (modeles === null) return null;
  const modele = modeles.find((m) => m.id === modeleId);
  return (
    <p className="flex items-center gap-1.5 text-xs text-fg-muted">
      <Blocks className="size-3.5 shrink-0 text-accent" aria-hidden />
      {modele ? (
        <>Copie de l’étape modèle « {modele.action} » : la modifier ici ne change pas le modèle.</>
      ) : (
        <>Copie d’une étape modèle retirée depuis de la bibliothèque.</>
      )}
    </p>
  );
}
