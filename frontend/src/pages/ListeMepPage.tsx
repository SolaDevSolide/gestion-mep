import {
  ArrowRight,
  CalendarDays,
  Copy,
  Factory,
  Feather,
  LayoutGrid,
  LayoutTemplate,
  Plus,
  Rocket,
  Rows3,
  Search,
  SearchX,
  TimerReset,
} from 'lucide-react';
import { useMemo, useState, type FormEvent } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { api } from '../api';
import { segmentsResume } from '../domaine';
import { calculerCompteARebours, formatDate } from '../format';
import { useLocalStorageState, useNow } from '../hooks';
import { PuceSystemeMep } from '../mep/SystemeCliquable';
import { personnesDeRecherche } from '../personnes';
import { useReferentiel } from '../referentiels';
import {
  ecrireFiltre,
  estVide,
  FILTRE_VIDE,
  filtreModeles,
  lireFiltre,
  urlExportListe,
  type FiltreMeps,
} from '../rechercheMeps';
import { metaPhase } from '../statuts';
import type { MepResume, TypeMep } from '../types';
import { Alerte } from '../ui/Alerte';
import { ErreurChargement } from '../ui/ErreurChargement';
import { Avatar } from '../ui/Avatar';
import { Button } from '../ui/Button';
import { cn } from '../ui/cn';
import { Dialog } from '../ui/Dialog';
import { EmptyState } from '../ui/EmptyState';
import { Field, Input } from '../ui/Input';
import { ProgressBar } from '../ui/ProgressBar';
import { Badge, StatusPill } from '../ui/StatusPill';
import { BoutonExportExcel } from '../ui/BoutonExportExcel';
import { Segmented } from '../ui/Segmented';
import { DuplicationDialog, type SourceDuplication } from '../mep/DuplicationDialog';
import { BadgeSimplifiee, estSimplifiee } from '../mep/typeMep';
import { useToast } from '../ui/Toast';
import { EnChargement, Squelette } from '../ui/Squelette';
import { OctagonAlert } from 'lucide-react';
import { BarreFiltres } from './liste/BarreFiltres';
import { useListeMeps } from './liste/listeMeps';
import { SqueletteTableauMeps, TableauMeps } from './liste/TableauMeps';

function Chrono({ m, now }: { m: MepResume; now: number }) {
  // Sans plan de retour arrière ou sans échéance : pas de compte à rebours.
  if ((m.phase !== 'Execution' && m.phase !== 'Recette') || !m.retourArriereActive || !m.echeanceRepli) return null;
  const c = calculerCompteARebours(m.echeanceRepli, now, m.phase === 'Recette');
  if (c.niveau === 'aucun') return null;
  return (
    <span
      className={cn(
        'inline-flex items-center gap-1.5 font-mono text-xs tnum',
        c.niveau === 'depasse'
          ? 'font-bold text-danger-solid'
          : c.niveau === 'attention'
            ? 'font-semibold text-warn-text'
            : 'text-fg-muted',
      )}
      title="Temps restant avant l’échéance de retour arrière"
    >
      <TimerReset className="size-3.5" aria-hidden />
      {c.texte}
    </span>
  );
}

const TYPES_MEP: { valeur: TypeMep; libelle: string; Icone: typeof Rocket; description: string }[] = [
  {
    valeur: 'Complete',
    libelle: 'MEP complète',
    Icone: Rocket,
    description:
      'Le plan détaillé : versions, pilote et suppléants choisis, durée, criticité et résolutions par étape.',
  },
  {
    valeur: 'Simplifiee',
    libelle: 'MEP simplifiée',
    Icone: Feather,
    description:
      'Pour une petite intervention : un pilote, les autres personnes sont suppléantes, des étapes sans durée ni criticité.',
  },
];

/** Choix du type d'une nouvelle MEP : deux cartes radio (complète par défaut). */
function ChoixType({ valeur, onChange }: { valeur: TypeMep; onChange: (t: TypeMep) => void }) {
  return (
    <fieldset>
      <legend className="mb-1.5 text-ui font-medium">Type de MEP</legend>
      <div className="grid gap-2 sm:grid-cols-2">
        {TYPES_MEP.map((t) => {
          const actif = valeur === t.valeur;
          return (
            <label
              key={t.valeur}
              className={cn(
                'flex cursor-pointer flex-col gap-1 rounded-lg border-[1.5px] p-3 transition-colors duration-150 has-[:focus-visible]:ring-2 has-[:focus-visible]:ring-accent',
                actif
                  ? 'border-accent bg-accent-soft shadow-[0_0_0_3px_var(--accent-soft)]'
                  : 'border-line-strong hover:border-accent',
              )}
            >
              <span className="flex items-center gap-2 text-sm font-semibold">
                <input
                  type="radio"
                  name="type-mep"
                  value={t.valeur}
                  checked={actif}
                  onChange={() => onChange(t.valeur)}
                  className="size-4 shrink-0 accent-[var(--accent)]"
                />
                <t.Icone className="size-4 text-fg-subtle" aria-hidden />
                {t.libelle}
              </span>
              <span className="pl-6 text-xs leading-snug text-fg-muted">{t.description}</span>
            </label>
          );
        })}
      </div>
      <p className="mt-1.5 text-xs text-fg-subtle">
        Le type se change pendant la préparation : une MEP simplifiée passe en complète sans rien perdre.
      </p>
    </fieldset>
  );
}

function NouvelleMepDialog({
  open,
  onOpenChange,
  modele = false,
}: {
  open: boolean;
  onOpenChange: (o: boolean) => void;
  /** Crée un modèle au lieu d'une MEP. */
  modele?: boolean;
}) {
  const navigate = useNavigate();
  const toast = useToast();
  const [outil, setOutil] = useState('');
  const [objet, setObjet] = useState('');
  const [type, setType] = useState<TypeMep>('Complete');
  const [erreur, setErreur] = useState('');
  const [envoi, setEnvoi] = useState(false);

  async function creer(e: FormEvent) {
    e.preventDefault();
    setErreur('');
    setEnvoi(true);
    try {
      // Un modèle est toujours une MEP complète.
      const mep = await api.creerMep(outil.trim(), objet.trim(), modele, modele ? 'Complete' : type);
      toast.succes(modele ? 'Modèle créé.' : 'MEP créée.');
      onOpenChange(false);
      void navigate(`/meps/${mep.id}`);
    } catch (err) {
      setErreur(err instanceof Error ? err.message : 'Création impossible.');
    } finally {
      setEnvoi(false);
    }
  }

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      titre={modele ? 'Nouveau modèle' : 'Nouvelle mise en production'}
      description={
        modele
          ? 'Un modèle ne s’exécute pas : il sert de base aux prochaines MEP. Le plan se prépare ensuite, section par section.'
          : 'Vous en serez le pilote. Le plan se prépare ensuite, section par section.'
      }
      pied={
        <>
          <Button variante="fantome" onClick={() => onOpenChange(false)}>
            Annuler
          </Button>
          <Button
            type="submit"
            form="form-nouvelle-mep"
            variante="primaire"
            chargement={envoi}
            disabled={!outil.trim()}
          >
            {modele ? 'Créer le modèle' : 'Créer la MEP'}
          </Button>
        </>
      }
    >
      <form id="form-nouvelle-mep" onSubmit={(e) => void creer(e)} className="space-y-4">
        {!modele && <ChoixType valeur={type} onChange={setType} />}
        <Field label="Outil ou application">
          {(id) => (
            <Input
              id={id}
              value={outil}
              onChange={(e) => setOutil(e.target.value)}
              placeholder="ex. Portail clients"
              autoFocus
              required
            />
          )}
        </Field>
        <Field label="Objet de l’intervention">
          {(id) => (
            <Input
              id={id}
              value={objet}
              onChange={(e) => setObjet(e.target.value)}
              placeholder={
                modele
                  ? 'ex. Montée de version standard du portail'
                  : 'ex. Passage en version 4.2 et migration de la base'
              }
            />
          )}
        </Field>
        {erreur && (
          <Alerte ton="danger" icone={OctagonAlert} role="alert">
            {erreur}
          </Alerte>
        )}
      </form>
    </Dialog>
  );
}

/**
 * Critères qu'on peut retirer quand rien ne correspond : le nombre de MEP qu'on retrouverait sans chacun (`cle` : son
 * compte dans `sans` des comptes du serveur).
 */
const RETRAITS: { cle: string; libelle: string; effacer: Partial<FiltreMeps> }[] = [
  { cle: 'recherche', libelle: 'la recherche', effacer: { recherche: '' } },
  { cle: 'systeme', libelle: 'le système', effacer: { systemes: [], environnements: [], echec: false } },
  { cle: 'environnement', libelle: 'l’environnement', effacer: { environnements: [] } },
  { cle: 'equipe', libelle: 'l’équipe', effacer: { equipes: [] } },
  { cle: 'personne', libelle: 'la personne', effacer: { personne: '', roles: [] } },
  { cle: 'etat', libelle: 'l’état', effacer: { etats: [], phase: '', type: '' } },
  { cle: 'periode', libelle: 'la période', effacer: { du: '', au: '' } },
];

/** Place d'une page de cartes qui arrive (« Afficher plus ») ou de la première. */
function SqueletteCartesMeps({ cartes }: { cartes: number }) {
  return Array.from({ length: cartes }, (_, i) => (
    <div key={i} className="rounded-xl border border-line bg-surface p-5 shadow-sm">
      <div className="flex items-start justify-between gap-6">
        <div className="grid flex-1 gap-2">
          <div className="flex items-center gap-3">
            <Squelette className="h-6 w-56 max-w-full" />
            <Squelette className="h-5 w-24 rounded-full" />
          </div>
          <Squelette className="h-4 w-96 max-w-full" />
        </div>
        <Squelette className="hidden h-5 w-48 sm:block" />
      </div>
      <div className="mt-4 flex items-center gap-6 sm:pr-32">
        <Squelette className="h-2 flex-1 rounded-full" />
        <Squelette className="h-3.5 w-32" />
      </div>
    </div>
  ));
}

type Affichage = 'cartes' | 'tableau';

export default function ListeMepPage() {
  const [vue, setVue] = useState<'meps' | 'modeles'>('meps');
  // Listes des filtres : partagées avec les autres écrans ; une liste en erreur n'empêche pas de filtrer sur les autres.
  const equipesLues = useReferentiel('equipes');
  const catalogueLu = useReferentiel('catalogue');
  const comptesLus = useReferentiel('comptes');
  const equipes = equipesLues.donnees;
  const catalogue = catalogueLu.donnees;
  const comptes = comptesLus.donnees;
  const listesFiltres = [equipesLues, catalogueLu, comptesLus];
  const filtresEnErreur = listesFiltres.filter((l) => l.erreur != null);
  // Filtres dans l'adresse : un lien (ou un favori) rouvre la même recherche.
  const [parametres, setParametres] = useSearchParams();
  const f = useMemo(() => lireFiltre(parametres), [parametres]);
  const maj = (p: Partial<FiltreMeps>) =>
    setParametres((avant) => ecrireFiltre({ ...lireFiltre(avant), ...p }, avant), { replace: true });
  // Cartes sans filtre, tableau dès qu'un filtre est actif ; un choix fait à la main est retenu.
  const [choixAffichage, setChoixAffichage] = useLocalStorageState<Affichage | null>('liste-meps.affichage', null);
  const [creation, setCreation] = useState(false);
  const [duplication, setDuplication] = useState<SourceDuplication | null>(null);
  const [depuisModele, setDepuisModele] = useState(false);
  const modele = vue === 'modeles';
  const now = useNow(1000);

  const filtreApplique = modele ? filtreModeles(f) : f;
  // Liste paginée par le serveur (#96), avec les comptes de la barre de filtres.
  const {
    liste,
    aJour,
    erreur,
    echec,
    chargement: lectureMeps,
    recharger,
    chargementSuite,
    afficherPlus,
  } = useListeMeps(f, modele);
  const rechargement = liste !== null && !aJour;
  const meps = liste?.meps ?? [];
  const personnes = useMemo(() => personnesDeRecherche(comptes ?? []), [comptes]);
  const affichage: Affichage = modele ? 'cartes' : (choixAffichage ?? (estVide(f) ? 'cartes' : 'tableau'));
  const retraits = RETRAITS.map((r) => ({ ...r, nombre: liste?.facettes.sans[r.cle] ?? 0 })).filter(
    (r) => r.nombre > 0,
  );
  const suite = liste !== null && aJour && liste.suivant !== null;

  return (
    <main className="mx-auto w-full max-w-6xl px-4 py-8 sm:px-6">
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Mises en production</h1>
          <p className="mt-1 text-sm text-fg-subtle">Préparez, exécutez et suivez vos interventions.</p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          {/* Clés distinctes : sans elles, React réutilise le même bouton d'une vue à l'autre et sa transition anime
              le changement de couleur (primaire ↔ secondaire). */}
          {modele ? (
            <Button
              key="nouveau-modele"
              variante="primaire"
              icone={<Plus className="size-4" aria-hidden />}
              onClick={() => setCreation(true)}
            >
              Nouveau modèle
            </Button>
          ) : (
            <>
              <Button
                key="depuis-modele"
                icone={<LayoutTemplate className="size-4" aria-hidden />}
                onClick={() => setDepuisModele(true)}
              >
                Nouvelle MEP depuis un modèle
              </Button>
              <Button
                key="nouvelle-mep"
                variante="primaire"
                icone={<Plus className="size-4" aria-hidden />}
                onClick={() => setCreation(true)}
              >
                Nouvelle MEP
              </Button>
            </>
          )}
        </div>
      </div>

      <div className="mt-6 flex flex-wrap items-center gap-3">
        <div className="relative min-w-0 flex-1 basis-60 sm:max-w-sm">
          <Search
            className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-fg-subtle"
            aria-hidden
          />
          <Input
            aria-label="Rechercher une MEP"
            placeholder="Rechercher (outil, version, personne, système…)"
            className="pl-9"
            value={f.recherche}
            onChange={(e) => maj({ recherche: e.target.value })}
          />
        </div>
        <Segmented
          label="Afficher les MEP ou les modèles"
          valeur={vue}
          onChange={setVue}
          options={[
            {
              valeur: 'meps',
              libelle: (
                <>
                  <Rocket className="size-4" aria-hidden />
                  MEP
                </>
              ),
            },
            {
              valeur: 'modeles',
              libelle: (
                <>
                  <LayoutTemplate className="size-4" aria-hidden />
                  Modèles
                </>
              ),
            },
          ]}
        />
        <div className="flex items-center gap-2 sm:ml-auto">
          {!modele && (
            <Segmented
              label="Présentation des résultats"
              valeur={affichage}
              onChange={setChoixAffichage}
              options={[
                {
                  valeur: 'cartes',
                  titre: 'Cartes',
                  libelle: (
                    <>
                      <LayoutGrid className="size-4" aria-hidden />
                      <span className="sr-only sm:not-sr-only">Cartes</span>
                    </>
                  ),
                },
                {
                  valeur: 'tableau',
                  titre: 'Tableau',
                  libelle: (
                    <>
                      <Rows3 className="size-4" aria-hidden />
                      <span className="sr-only sm:not-sr-only">Tableau</span>
                    </>
                  ),
                },
              ]}
            />
          )}
          <BoutonExportExcel
            url={urlExportListe(f, modele)}
            disabled={liste === null || liste.total === 0}
            title={
              modele
                ? 'Les modèles affichés, recherche et équipe comprises.'
                : 'Les MEP affichées, filtres compris, avec un onglet des échecs d’étape.'
            }
          />
        </div>
      </div>
      {/* Critères : chargés avec la page (catalogue, équipes, personnes), leur place est tenue en attendant (pas quand la
          liste elle-même est en erreur : rien n'arrivera avant « Réessayer »). */}
      {!listesFiltres.every((l) => l.pret) || liste === null ? (
        echec === null && (
          <div className="mt-3 flex gap-2" aria-hidden>
            {(modele ? ['w-24'] : ['w-24', 'w-32', 'w-24', 'w-28', 'w-20', 'w-24']).map((w, i) => (
              <Squelette key={i} className={cn('h-8 rounded-lg', w, i > 0 && 'max-sm:hidden')} />
            ))}
          </div>
        )
      ) : (
        <BarreFiltres
          f={filtreApplique}
          maj={maj}
          modeles={modele}
          resultats={liste.total}
          donnees={{ facettes: liste.facettes, catalogue: catalogue ?? [], equipes: equipes ?? [], personnes }}
        />
      )}
      {filtresEnErreur.length > 0 && (
        <ErreurChargement
          variante="ligne"
          className="mt-3"
          erreur={filtresEnErreur[0].erreur}
          titre="Listes des filtres non chargées (systèmes, équipes ou personnes)"
          onReessayer={() => filtresEnErreur.forEach((l) => l.recharger())}
          enCours={filtresEnErreur.some((l) => l.enCours)}
        />
      )}

      {echec !== null && (
        <ErreurChargement
          className="mt-6"
          erreur={echec}
          titre={modele ? 'Impossible de charger les modèles' : 'Impossible de charger les mises en production'}
          onReessayer={recharger}
          enCours={lectureMeps}
        />
      )}

      {liste === null && !erreur && affichage === 'tableau' && <SqueletteTableauMeps />}
      {liste === null && !erreur && affichage === 'cartes' && (
        <EnChargement libelle="Chargement des mises en production…" className="mt-6 space-y-3">
          <SqueletteCartesMeps cartes={4} />
        </EnChargement>
      )}

      {liste !== null && liste.ensemble === 0 && (
        <div className="mt-6 rounded-xl border border-dashed border-line-strong bg-surface">
          <EmptyState
            icone={Rocket}
            titre={modele ? 'Aucun modèle pour l’instant' : 'Aucune mise en production pour l’instant'}
            description={
              modele
                ? 'Créez un modèle, ou dupliquez une MEP en cochant « En faire un modèle » : il servira de base à vos prochaines interventions.'
                : 'Créez votre première MEP pour préparer le plan, désigner l’équipe et suivre l’exécution en direct.'
            }
            action={
              <Button
                variante="primaire"
                icone={<Plus className="size-4" aria-hidden />}
                onClick={() => setCreation(true)}
              >
                {modele ? 'Nouveau modèle' : 'Nouvelle MEP'}
              </Button>
            }
          />
        </div>
      )}

      {liste !== null && liste.ensemble > 0 && liste.total === 0 && (
        <div className="mt-6 rounded-xl border border-dashed border-line-strong bg-surface">
          <EmptyState
            icone={SearchX}
            titre="Aucun résultat"
            description={
              retraits.length > 0 ? (
                <>
                  Aucune {modele ? 'modèle' : 'MEP'} ne correspond à tous ces critères. Sans l’un d’eux :
                  <span className="mt-3 flex flex-wrap justify-center gap-2">
                    {retraits.map((r) => (
                      <Button key={r.libelle} taille="sm" onClick={() => maj(r.effacer)}>
                        Sans {r.libelle} ({r.nombre})
                      </Button>
                    ))}
                  </span>
                </>
              ) : (
                `Aucun${modele ? '' : 'e'} ${modele ? 'modèle' : 'MEP'} ne correspond à ces critères.`
              )
            }
            action={<Button onClick={() => maj({ ...FILTRE_VIDE })}>Effacer les filtres</Button>}
          />
        </div>
      )}

      {affichage === 'tableau' && meps.length > 0 && (
        <div className={cn('transition-opacity duration-200', 'apparition', rechargement && 'opacity-50')}>
          <TableauMeps
            meps={meps}
            f={f}
            nomPersonne={personnes.find((p) => p.id === f.personne)?.nom}
            chargementSuite={chargementSuite}
          />
        </div>
      )}

      {/* Rechargement (MEP ↔ modèles) : la liste précédente reste, estompée, jusqu'à la réponse. */}
      {affichage === 'cartes' && (
        <ul
          aria-busy={rechargement || chargementSuite || undefined}
          className={cn(
            'mt-6 space-y-3 transition-opacity duration-200',
            liste !== null && 'apparition',
            rechargement && 'opacity-50',
          )}
        >
          {meps.map((m) => {
            const meta = metaPhase(m.phase, m.etat, m.reporteeAu);
            const annulee = m.phase === 'Annulee';
            return (
              <li
                key={m.id}
                className="group relative rounded-xl border border-line bg-surface p-4 shadow-sm transition-[border-color,box-shadow] duration-150 hover:border-line-strong hover:shadow-md sm:p-5"
              >
                <div className="flex flex-wrap items-start justify-between gap-x-6 gap-y-3">
                  <div className={cn('min-w-0 flex-1', annulee && 'text-fg-subtle')}>
                    <div className="flex flex-wrap items-center gap-x-3 gap-y-1.5">
                      <h2 className="flex min-w-0 items-center gap-2 text-base font-semibold tracking-tight">
                        {/* Toute la carte ouvre la MEP ; puces et bouton restent cliquables au-dessus. */}
                        <Link
                          to={`/meps/${m.id}`}
                          className="truncate outline-none after:absolute after:inset-0 after:rounded-xl focus-visible:after:ring-2 focus-visible:after:ring-accent"
                        >
                          {m.outil || 'Sans nom'}
                        </Link>
                        {!estSimplifiee(m) && m.versionCible.trim() && (
                          <>
                            <ArrowRight className="size-4 shrink-0 text-fg-subtle" aria-hidden />
                            <span className="font-mono text-md font-semibold">{m.versionCible}</span>
                          </>
                        )}
                      </h2>
                      {!m.estModele && (
                        <StatusPill ton={meta.ton} icone={meta.Icone} anime={meta.anime}>
                          {meta.libelle}
                        </StatusPill>
                      )}
                      {m.estModele && (
                        <Badge ton="later">
                          <LayoutTemplate className="size-3" aria-hidden />
                          Modèle
                        </Badge>
                      )}
                      {m.toucheProduction && (
                        <Badge ton="warn">
                          <Factory className="size-3" aria-hidden />
                          Production
                          <span className="sr-only"> : la MEP touche un environnement de production</span>
                        </Badge>
                      )}
                      {estSimplifiee(m) && <BadgeSimplifiee className="text-2xs" />}
                    </div>
                    {m.objet && (
                      <p className={cn('mt-1 line-clamp-1 text-sm', annulee ? 'text-fg-subtle' : 'text-fg-muted')}>
                        {m.objet}
                      </p>
                    )}
                  </div>
                  <div className="flex flex-wrap items-center gap-x-5 gap-y-1 text-sm text-fg-muted">
                    <span className="inline-flex items-center gap-1.5">
                      <CalendarDays className="size-4 text-fg-subtle" aria-hidden />
                      {m.estModele
                        ? m.creeLe
                          ? `Créé le ${formatDate(m.creeLe)}`
                          : 'Modèle'
                        : annulee && m.annuleeLe
                          ? `Annulée le ${formatDate(m.annuleeLe)}`
                          : m.dateMep
                            ? `Démarrée le ${formatDate(m.dateMep)}`
                            : m.creeLe
                              ? `Créée le ${formatDate(m.creeLe)}`
                              : 'Pas encore démarrée'}
                    </span>
                    {!m.estModele && (
                      <span className="inline-flex items-center gap-2">
                        {m.pilote ? <Avatar nom={m.pilote} taille="sm" /> : null}
                        <span>{m.pilote ?? 'Pas de pilote'}</span>
                      </span>
                    )}
                  </div>
                </div>
                {!m.estModele && m.systemes.length > 0 && (
                  <div className="relative z-10 mt-3 flex w-fit max-w-full flex-wrap gap-1" aria-label="Systèmes">
                    {m.systemes.map((s, i) => (
                      <PuceSystemeMep key={`${s.nom}-${i}`} s={s} />
                    ))}
                  </div>
                )}
                {m.estModele ? (
                  <p className="mt-4 text-xs text-fg-subtle tnum sm:pr-32">
                    {m.nbEtapes} étape{m.nbEtapes > 1 ? 's' : ''} de déploiement
                  </p>
                ) : annulee ? (
                  /* Une MEP annulée ne progresse plus : ni barre ni compte à rebours. */
                  <p className="mt-4 text-xs text-fg-subtle tnum sm:pr-32">
                    {m.nbReussies}/{m.nbEtapes} étapes réussies avant l’annulation
                  </p>
                ) : (
                  <div className="mt-4 flex flex-wrap items-center gap-x-6 gap-y-2 sm:pr-32">
                    <ProgressBar
                      className="min-w-40 flex-1"
                      segments={segmentsResume(m.nbEtapes, m.nbReussies, m.nbEchecs)}
                    />
                    <span className="text-xs text-fg-subtle tnum">
                      {m.nbReussies}/{m.nbEtapes} étapes réussies
                      {m.nbEchecs > 0 && (
                        <span className="font-semibold text-danger-solid"> · {m.nbEchecs} en échec</span>
                      )}
                    </span>
                    <Chrono m={m} now={now} />
                  </div>
                )}
                <div className="relative z-10 mt-3 flex justify-end sm:absolute sm:right-4 sm:bottom-4 sm:mt-0">
                  <Button
                    taille="sm"
                    icone={<Copy className="size-3.5" aria-hidden />}
                    aria-label={`Dupliquer ${m.outil || 'la MEP'}`}
                    onClick={() => setDuplication({ id: m.id, outil: m.outil, estModele: m.estModele })}
                  >
                    Dupliquer
                  </Button>
                </div>
              </li>
            );
          })}
          {chargementSuite && (
            <li aria-hidden className="grid gap-3">
              <SqueletteCartesMeps cartes={2} />
            </li>
          )}
        </ul>
      )}
      {/* Page suivante (#96) : ses lignes de chargement prennent la place du bouton jusqu'à la réponse. */}
      {chargementSuite && (
        <p role="status" className="sr-only">
          Chargement des mises en production suivantes…
        </p>
      )}
      {suite && !chargementSuite && (
        <div className="mt-6 flex justify-center">
          <Button onClick={afficherPlus}>Afficher plus</Button>
        </div>
      )}
      <NouvelleMepDialog open={creation} onOpenChange={setCreation} modele={modele} />
      <DuplicationDialog
        open={duplication !== null}
        onOpenChange={(o) => {
          if (!o) setDuplication(null);
        }}
        source={duplication}
      />
      <DuplicationDialog open={depuisModele} onOpenChange={setDepuisModele} depuisModele />
    </main>
  );
}
