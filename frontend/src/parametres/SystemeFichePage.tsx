import {
  ArrowLeft,
  ExternalLink,
  Link2,
  Boxes,
  CheckCircle2,
  CircleOff,
  History,
  Layers,
  Lock,
  OctagonAlert,
  Pencil,
  Plus,
  Server,
  Trash2,
} from 'lucide-react';
import { useEffect, useState, type ReactNode } from 'react';
import { Link, useParams, useSearchParams } from 'react-router-dom';
import { api } from '../api';
import { useAuth } from '../auth';
import { nomDeProduction } from '../environnements';
import { useAsync } from '../hooks';
import type { EnvironnementCatalogue, ObjetCatalogue, SystemeCatalogue } from '../types';
import { Alerte } from '../ui/Alerte';
import { BoutonIcone, Button } from '../ui/Button';
import { Card, CardHeader } from '../ui/Card';
import { Checkbox } from '../ui/Checkbox';
import { cn } from '../ui/cn';
import { ConfirmDialog, Dialog } from '../ui/Dialog';
import { EmptyState } from '../ui/EmptyState';
import { ErreurChargement } from '../ui/ErreurChargement';
import { Field, Input } from '../ui/Input';
import { PastilleEnvironnement } from '../ui/PastilleEnvironnement';
import { StatusPill } from '../ui/StatusPill';
import { Tooltip } from '../ui/Tooltip';
import { useToast } from '../ui/Toast';
import { EnChargement, Squelette, SqueletteTableau } from '../ui/Squelette';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '../ui/Tabs';
import { BadgeNoosphere, LienNoosphere, raisonInactif } from '../admin/liaisons/communs';
import { libelleCycle } from '../admin/liaisons/liaisons';
import { formatDateHeure } from '../format';
import { HistoriqueSysteme } from './HistoriqueSysteme';
import { SystemeDialog } from './SystemeDialog';

interface Donnees {
  systeme: SystemeCatalogue | null;
  objets: ObjetCatalogue[];
}

/**
 * Fiche d'un système du catalogue : ses environnements (proposés dans les MEP, avec la marque « production ») et ses
 * objets connus rangés par catégorie (seuls objets qu'une MEP peut retenir), et l'onglet « Historique des MEP » (#59) :
 * toutes les MEP qui l'ont touché. Modification réservée aux administrateurs.
 */
export default function SystemeFichePage() {
  const { id = '' } = useParams();
  const { utilisateur } = useAuth();
  const admin = utilisateur?.estAdmin ?? false;
  const toast = useToast();
  const { donnees, erreur, recharger } = useAsync<Donnees>(async () => {
    const [systemes, objets] = await Promise.all([api.catalogueSystemes(), api.objetsCatalogue(id)]);
    return { systeme: systemes.find((s) => s.id === id) ?? null, objets };
  }, [id]);
  // Catégories par défaut : simples suggestions ; en erreur, la fiche reste utilisable et la carte le dit.
  const defaut = useAsync(() => api.categoriesParDefaut(), []);
  const [modifier, setModifier] = useState(false);
  const [params, setParams] = useSearchParams();
  const onglet = params.get('onglet') === 'historique' ? 'historique' : 'fiche';

  /** Exécute une modification puis recharge la fiche ; renvoie true si elle a abouti. */
  async function agir(action: () => Promise<unknown>, succes: string): Promise<boolean> {
    try {
      await action();
      toast.succes(succes);
      recharger();
      return true;
    } catch (e) {
      toast.erreur(e instanceof Error ? e.message : 'Enregistrement impossible.');
      return false;
    }
  }

  const s = donnees?.systeme ?? null;
  return (
    <main className="mx-auto w-full max-w-6xl px-6 py-8">
      <nav aria-label="Fil d’Ariane" className="mb-3">
        <Link
          to="/parametres/systemes"
          className="inline-flex items-center gap-1.5 text-sm font-semibold text-accent-text hover:underline"
        >
          <ArrowLeft className="size-4" aria-hidden />
          Catalogue des systèmes
        </Link>
      </nav>
      {erreur && (
        <Alerte ton="danger" icone={OctagonAlert} role="alert">
          {erreur}
        </Alerte>
      )}
      {donnees && !s && (
        <Alerte ton="danger" icone={OctagonAlert} role="alert">
          Système introuvable dans le catalogue.
        </Alerte>
      )}
      {!donnees && !erreur && (
        <EnChargement libelle="Chargement du système…">
          <Squelette className="h-8 w-64" />
          <Squelette className="mt-2 h-4 w-96 max-w-full" />
          <div className="mt-8 grid gap-6 lg:grid-cols-2">
            <SqueletteTableau lignes={4} colonnes={2} />
            <SqueletteTableau lignes={6} colonnes={3} />
          </div>
        </EnChargement>
      )}
      {s && donnees && (
        <div className="apparition">
          <div className="flex flex-wrap items-end justify-between gap-4">
            <div className="min-w-0">
              <h1 className="flex flex-wrap items-center gap-x-3 gap-y-1.5 text-2xl font-semibold tracking-tight">
                <span className="min-w-0 [overflow-wrap:anywhere]">{s.nom}</span>
                <StatusPill taille="sm" ton={s.actif ? 'ok' : 'wait'} icone={s.actif ? CheckCircle2 : CircleOff}>
                  {s.actif ? 'Actif' : 'Inactif'}
                  {!s.actif && raisonInactif(s.source) && ` · ${raisonInactif(s.source)}`}
                </StatusPill>
                {s.source && <BadgeNoosphere className="text-xs" />}
              </h1>
              <p className="mt-1 text-sm text-fg-subtle">
                {s.description || 'Sans description'}
                {s.cible && <span className="font-mono"> · {s.cible}</span>}
              </p>
              {!admin && (
                <p className="mt-1 inline-flex items-center gap-1 text-sm text-fg-subtle">
                  <Lock className="size-3.5" aria-hidden />
                  Lecture seule : seuls les administrateurs peuvent modifier la fiche.
                </p>
              )}
            </div>
            <div className="flex flex-wrap gap-2">
              {s.source?.url && <BoutonOuvrirNoosphere url={s.source.url} />}
              {admin && (
                <Button icone={<Pencil className="size-4" aria-hidden />} onClick={() => setModifier(true)}>
                  Modifier
                </Button>
              )}
            </div>
          </div>
          <Tabs
            value={onglet}
            onValueChange={(o) => setParams(o === 'fiche' ? {} : { onglet: o }, { replace: true })}
            className="mt-6"
          >
            <TabsList className="border-b border-line" aria-label="Sections du système">
              <TabsTrigger value="fiche">
                <Layers className="size-4" aria-hidden /> Fiche
              </TabsTrigger>
              <TabsTrigger value="historique">
                <History className="size-4" aria-hidden /> Historique des MEP
              </TabsTrigger>
            </TabsList>
            <TabsContent value="fiche" className="mt-6 space-y-6 outline-none">
              {s.source ? (
                <>
                  <Alerte
                    ton="run"
                    icone={Link2}
                    titre="Nom et description viennent de Noosphere"
                    action={
                      s.source.url && (
                        <LienNoosphere
                          url={s.source.url}
                          className="rounded-md border border-line-strong bg-surface px-2.5 py-1 text-ui text-fg shadow-sm hover:bg-surface-2 hover:no-underline"
                        >
                          Ouvrir la fiche dans Noosphere
                        </LienNoosphere>
                      )
                    }
                  >
                    Ils sont en lecture seule ici et suivent la fiche Noosphere à chaque synchronisation.
                    Environnements, cibles et objets restent gérés dans Gestion MEP.
                  </Alerte>
                  <div className="grid grid-cols-[minmax(0,1fr)] items-start gap-6 lg:grid-cols-2">
                    <CarteNoosphere systeme={s} admin={admin} />
                    <CarteEnvironnements systeme={s} admin={admin} agir={agir} />
                  </div>
                </>
              ) : (
                <CarteEnvironnements systeme={s} admin={admin} agir={agir} />
              )}
              <CarteObjets
                systeme={s}
                objets={donnees.objets}
                defaut={defaut.donnees ?? []}
                erreurDefaut={
                  admin &&
                  defaut.echec !== null && (
                    <ErreurChargement
                      variante="ligne"
                      erreur={defaut.echec}
                      titre="Catégories par défaut non chargées"
                      onReessayer={defaut.recharger}
                      enCours={defaut.chargement}
                    />
                  )
                }
                admin={admin}
                agir={agir}
              />
            </TabsContent>
            <TabsContent value="historique" className="mt-6 outline-none">
              <HistoriqueSysteme systeme={s} />
            </TabsContent>
          </Tabs>
          <SystemeDialog existant={s} open={modifier} onOpenChange={setModifier} onFait={recharger} />
        </div>
      )}
    </main>
  );
}

type Agir = (action: () => Promise<unknown>, succes: string) => Promise<boolean>;

/* ------------------------------------------------------------------ Noosphere (#123) */

/** « Ouvrir dans Noosphere » : la fiche de l'application, dans un nouvel onglet (inactif en démonstration). */
function BoutonOuvrirNoosphere({ url }: { url: string }) {
  return (
    <LienNoosphere
      url={url}
      icone={false}
      className="h-9 gap-2 rounded-lg border border-line-strong bg-surface px-3.5 text-sm text-fg shadow-sm transition-colors duration-150 hover:bg-surface-2 hover:no-underline"
    >
      <ExternalLink className="size-4" aria-hidden />
      Ouvrir dans Noosphere
    </LienNoosphere>
  );
}

/** Ce que Noosphere dit du système suivi : domaine, équipe, cycle de vie, identifiant, dernière synchronisation. */
function CarteNoosphere({ systeme: s, admin }: { systeme: SystemeCatalogue; admin: boolean }) {
  const src = s.source;
  if (!src) return null;
  const ligne = (libelle: string, valeur: ReactNode) => (
    <div className="grid gap-0.5 sm:grid-cols-[11rem_minmax(0,1fr)] sm:gap-4">
      <dt className="text-fg-subtle">{libelle}</dt>
      <dd className="min-w-0 [overflow-wrap:anywhere]">{valeur || <span className="text-fg-subtle">—</span>}</dd>
    </div>
  );
  return (
    <Card>
      <CardHeader
        titre="Dans Noosphere"
        description="Lu à chaque synchronisation, en lecture seule."
        action={
          src.url && (
            <LienNoosphere url={src.url} className="text-ui">
              Ouvrir dans Noosphere
            </LienNoosphere>
          )
        }
      />
      <dl className="grid gap-3 p-5 text-sm">
        {ligne('Domaine', src.domaine)}
        {ligne('Équipe', src.equipe)}
        {ligne('Cycle de vie', src.cycle && libelleCycle(src.cycle))}
        {ligne('Identifiant Noosphere', <span className="font-mono">{src.applicationId}</span>)}
        {ligne(
          'Dernière synchronisation',
          <>
            {formatDateHeure(src.derniereSynchro)}
            {admin && (
              <>
                {' · '}
                <Link to="/admin?onglet=liaisons" className="font-semibold text-accent-text hover:underline">
                  Liaison
                </Link>
              </>
            )}
          </>,
        )}
        {src.absente && (
          <p className="text-ui text-warn-text">
            L’application n’est plus dans le catalogue de Noosphere (supprimée ou devenue restreinte) : système
            désactivé.
          </p>
        )}
      </dl>
    </Card>
  );
}

/* ------------------------------------------------------------------ Environnements */

function CarteEnvironnements({ systeme, admin, agir }: { systeme: SystemeCatalogue; admin: boolean; agir: Agir }) {
  const [edition, setEdition] = useState<{ index: number | null } | null>(null);
  const [suppr, setSuppr] = useState<number | null>(null);
  const liste = systeme.environnements;
  const enregistrer = (nouvelle: EnvironnementCatalogue[], message: string) =>
    agir(() => api.environnementsCatalogue(systeme.id, nouvelle), message);

  return (
    <Card className="overflow-hidden">
      <CardHeader
        titre="Environnements"
        description="Proposés dans les MEP : on y choisit le ou les environnements touchés, puis ceux de chaque étape."
        action={
          admin && (
            <Button
              taille="sm"
              icone={<Plus className="size-4" aria-hidden />}
              onClick={() => setEdition({ index: null })}
            >
              Ajouter un environnement
            </Button>
          )
        }
      />
      {liste.length === 0 ? (
        <p className="px-5 py-6 text-sm text-fg-subtle">
          Aucun environnement : les MEP ne pourront pas en choisir pour ce système.
        </p>
      ) : (
        <table className="w-full text-left text-sm">
          <thead className="border-b border-line bg-surface-2/60 text-xs tracking-wide text-fg-subtle uppercase">
            <tr>
              <th scope="col" className="px-5 py-2.5 font-semibold">
                Environnement
              </th>
              <th scope="col" className="px-5 py-2.5 font-semibold">
                Cible exacte
              </th>
              {admin && (
                <th scope="col" className="px-5 py-2.5">
                  <span className="sr-only">Actions</span>
                </th>
              )}
            </tr>
          </thead>
          <tbody className="divide-y divide-line">
            {liste.map((e, i) => (
              <tr key={e.nom}>
                <th scope="row" className="px-5 py-2.5 font-medium">
                  <span className="inline-flex items-center gap-2">
                    <Server className="size-4 text-fg-subtle" aria-hidden />
                    {e.nom}
                    {e.production && <PastilleEnvironnement nom="Production" production />}
                  </span>
                </th>
                <td className="px-5 py-2.5 font-mono text-ui text-fg-muted">{e.cible || '—'}</td>
                {admin && (
                  <td className="px-5 py-2 text-right whitespace-nowrap">
                    <Tooltip content="Modifier">
                      <BoutonIcone label={`Modifier ${e.nom}`} taille="sm" onClick={() => setEdition({ index: i })}>
                        <Pencil className="size-4" aria-hidden />
                      </BoutonIcone>
                    </Tooltip>
                    <Tooltip content="Supprimer">
                      <BoutonIcone label={`Supprimer ${e.nom}`} taille="sm" onClick={() => setSuppr(i)}>
                        <Trash2 className="size-4" aria-hidden />
                      </BoutonIcone>
                    </Tooltip>
                  </td>
                )}
              </tr>
            ))}
          </tbody>
        </table>
      )}
      <EnvironnementDialog
        open={edition !== null}
        onOpenChange={(o) => {
          if (!o) setEdition(null);
        }}
        existant={edition?.index != null ? liste[edition.index] : null}
        onValider={(env) => {
          const i = edition?.index;
          const nouvelle = i == null ? [...liste, env] : liste.map((x, j) => (j === i ? env : x));
          return enregistrer(
            nouvelle,
            i == null ? `Environnement ${env.nom} ajouté.` : `Environnement ${env.nom} modifié.`,
          );
        }}
      />
      <ConfirmDialog
        open={suppr !== null}
        onOpenChange={(o) => {
          if (!o) setSuppr(null);
        }}
        titre={`Supprimer l’environnement ${suppr !== null ? liste[suppr]?.nom : ''} ?`}
        description="Il n’est plus proposé aux MEP ; celles qui l’ont déjà choisi le gardent."
        libelleConfirmer="Supprimer"
        variante="danger"
        onConfirmer={() =>
          suppr !== null
            ? enregistrer(
                liste.filter((_, j) => j !== suppr),
                'Environnement supprimé.',
              )
            : false
        }
      />
    </Card>
  );
}

function EnvironnementDialog({
  open,
  onOpenChange,
  existant,
  onValider,
}: {
  open: boolean;
  onOpenChange: (o: boolean) => void;
  existant: EnvironnementCatalogue | null;
  onValider: (e: EnvironnementCatalogue) => Promise<boolean>;
}) {
  const [v, setV] = useState<EnvironnementCatalogue>({ nom: '', cible: '', production: false });
  const [productionTouchee, setProductionTouchee] = useState(false);
  const [envoi, setEnvoi] = useState(false);
  useEffect(() => {
    if (!open) return;
    setV(existant ?? { nom: '', cible: '', production: false });
    setProductionTouchee(!!existant);
  }, [open, existant]);

  async function valider() {
    setEnvoi(true);
    const ok = await onValider({ nom: v.nom.trim(), cible: v.cible.trim(), production: v.production });
    setEnvoi(false);
    if (ok) onOpenChange(false);
  }

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      titre={existant ? 'Modifier l’environnement' : 'Ajouter un environnement'}
      pied={
        <>
          <Button variante="fantome" onClick={() => onOpenChange(false)}>
            Annuler
          </Button>
          <Button variante="primaire" chargement={envoi} disabled={!v.nom.trim()} onClick={() => void valider()}>
            Enregistrer
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <Field label="Nom *">
          {(id) => (
            <Input
              id={id}
              value={v.nom}
              autoFocus
              placeholder="ex. Préproduction"
              onChange={(e) =>
                setV({
                  ...v,
                  nom: e.target.value,
                  // Tant qu'on n'a pas touché la case, la production se déduit du nom.
                  production: productionTouchee ? v.production : nomDeProduction(e.target.value),
                })
              }
            />
          )}
        </Field>
        <Field label="Cible exacte" hint="Serveur, base, URL ou chemin de cet environnement.">
          {(id) => (
            <Input
              id={id}
              className="font-mono"
              value={v.cible}
              onChange={(e) => setV({ ...v, cible: e.target.value })}
            />
          )}
        </Field>
        <Checkbox
          label="Environnement de production"
          description="Une MEP qui le touche est signalée « Production » dans la liste des MEP."
          checked={v.production}
          onChange={(production) => {
            setProductionTouchee(true);
            setV({ ...v, production });
          }}
        />
      </div>
    </Dialog>
  );
}

/* ------------------------------------------------------------------ Catégories et objets */

const OBJET_VIDE = { type: '', nom: '', libelle: '', precision: '' };

function CarteObjets({
  systeme,
  objets,
  defaut,
  erreurDefaut,
  admin,
  agir,
}: {
  systeme: SystemeCatalogue;
  objets: ObjetCatalogue[];
  defaut: string[];
  /** Catégories par défaut non chargées (suggestions absentes). */
  erreurDefaut?: React.ReactNode;
  admin: boolean;
  agir: Agir;
}) {
  const [active, setActive] = useState<string | null>(null);
  const [ajoutCategorie, setAjoutCategorie] = useState(false);
  const [renommage, setRenommage] = useState<string | null>(null);
  const [retrait, setRetrait] = useState<string | null>(null);
  const [objet, setObjet] = useState<{ existant: ObjetCatalogue | null } | null>(null);
  const [supprObjet, setSupprObjet] = useState<ObjetCatalogue | null>(null);
  const categories = systeme.categories;
  const courante = active && categories.includes(active) ? active : (categories[0] ?? null);
  const liste = objets.filter((o) => o.categorie === courante).sort((a, b) => a.nom.localeCompare(b.nom, 'fr'));
  const autresDefaut = defaut.filter((d) => !categories.some((c) => c.toLowerCase() === d.toLowerCase()));

  const definirCategories = (nouvelles: { nom: string; ancien?: string }[], message: string) =>
    agir(() => api.categoriesCatalogue(systeme.id, nouvelles), message);

  return (
    <Card className="overflow-hidden">
      <CardHeader
        titre="Objets du système"
        description="Catégories d’objets qui s’appliquent à ce système et éléments connus : ils sont proposés dans les objets impactés des MEP."
        action={
          admin && (
            <Button taille="sm" icone={<Plus className="size-4" aria-hidden />} onClick={() => setAjoutCategorie(true)}>
              Ajouter une catégorie
            </Button>
          )
        }
      />
      {erreurDefaut && <div className="px-5 pt-4">{erreurDefaut}</div>}
      {categories.length === 0 ? (
        <EmptyState
          icone={Boxes}
          titre="Aucune catégorie"
          description={
            admin
              ? 'Ajoutez les catégories qui s’appliquent à ce système (tables, packages, ordonnancement…), puis leurs éléments.'
              : 'Un administrateur peut y ajouter des catégories et leurs éléments.'
          }
        />
      ) : (
        <div className="grid md:grid-cols-[15rem_minmax(0,1fr)]">
          <nav aria-label="Catégories" className="border-b border-line p-2 md:border-r md:border-b-0">
            <ul className="space-y-0.5">
              {categories.map((c) => {
                const n = objets.filter((o) => o.categorie === c).length;
                const actif = c === courante;
                return (
                  <li key={c}>
                    <button
                      type="button"
                      aria-current={actif ? 'true' : undefined}
                      onClick={() => setActive(c)}
                      className={cn(
                        'flex w-full items-center justify-between gap-2 rounded-lg px-2.5 py-2 text-left text-sm transition-colors duration-150',
                        actif
                          ? 'bg-accent-soft font-semibold text-accent-text'
                          : 'text-fg-muted hover:bg-surface-2 hover:text-fg',
                      )}
                    >
                      <span className="min-w-0 truncate">{c}</span>
                      <span className="text-xs tnum">{n}</span>
                    </button>
                  </li>
                );
              })}
            </ul>
            {autresDefaut.length > 0 && (
              <p className="mt-2 border-t border-line px-2.5 pt-2 text-xs text-fg-subtle">
                {autresDefaut.length} autre{autresDefaut.length > 1 ? 's' : ''} catégorie
                {autresDefaut.length > 1 ? 's' : ''} par défaut disponible{autresDefaut.length > 1 ? 's' : ''}.
              </p>
            )}
          </nav>
          {courante && (
            <section aria-label={`Catégorie ${courante}`} className="min-w-0">
              <div className="flex flex-wrap items-center gap-2 border-b border-line px-5 py-3">
                <h3 className="min-w-0 flex-1 text-sm">
                  <span className="font-semibold">{courante}</span>
                  <span className="text-fg-subtle">
                    {' '}
                    · {liste.length} élément{liste.length > 1 ? 's' : ''} connu{liste.length > 1 ? 's' : ''}
                  </span>
                </h3>
                {admin && (
                  <>
                    <Button
                      taille="sm"
                      variante="fantome"
                      icone={<Plus className="size-4" aria-hidden />}
                      onClick={() => setObjet({ existant: null })}
                    >
                      Ajouter un élément
                    </Button>
                    <Tooltip content="Renommer la catégorie">
                      <BoutonIcone label={`Renommer ${courante}`} taille="sm" onClick={() => setRenommage(courante)}>
                        <Pencil className="size-4" aria-hidden />
                      </BoutonIcone>
                    </Tooltip>
                    <Tooltip content="Retirer la catégorie">
                      <BoutonIcone
                        label={`Retirer la catégorie ${courante}`}
                        taille="sm"
                        onClick={() => setRetrait(courante)}
                      >
                        <Trash2 className="size-4" aria-hidden />
                      </BoutonIcone>
                    </Tooltip>
                  </>
                )}
              </div>
              {liste.length === 0 ? (
                <p className="px-5 py-6 text-sm text-fg-subtle">Aucun élément dans cette catégorie.</p>
              ) : (
                <div className="overflow-x-auto">
                  <table className="w-full text-left text-sm">
                    <thead className="border-b border-line bg-surface-2/60 text-xs tracking-wide text-fg-subtle uppercase">
                      <tr>
                        <th scope="col" className="px-5 py-2.5 font-semibold">
                          Type
                        </th>
                        <th scope="col" className="px-5 py-2.5 font-semibold">
                          Nom
                        </th>
                        <th scope="col" className="px-5 py-2.5 font-semibold">
                          Libellé
                        </th>
                        <th scope="col" className="px-5 py-2.5 font-semibold">
                          Précision
                        </th>
                        {admin && (
                          <th scope="col" className="px-5 py-2.5">
                            <span className="sr-only">Actions</span>
                          </th>
                        )}
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-line">
                      {liste.map((o) => (
                        <tr key={o.id}>
                          <td className="px-5 py-2.5 text-fg-muted">{o.type || '—'}</td>
                          <th scope="row" className="px-5 py-2.5 font-mono text-ui font-normal">
                            {o.nom}
                          </th>
                          <td className="px-5 py-2.5">{o.libelle}</td>
                          <td className="px-5 py-2.5 text-fg-muted">{o.precision}</td>
                          {admin && (
                            <td className="px-5 py-2 text-right whitespace-nowrap">
                              <Tooltip content="Modifier">
                                <BoutonIcone
                                  label={`Modifier ${o.nom}`}
                                  taille="sm"
                                  onClick={() => setObjet({ existant: o })}
                                >
                                  <Pencil className="size-4" aria-hidden />
                                </BoutonIcone>
                              </Tooltip>
                              <Tooltip content="Supprimer">
                                <BoutonIcone label={`Supprimer ${o.nom}`} taille="sm" onClick={() => setSupprObjet(o)}>
                                  <Trash2 className="size-4" aria-hidden />
                                </BoutonIcone>
                              </Tooltip>
                            </td>
                          )}
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </section>
          )}
        </div>
      )}

      <CategorieDialog
        open={ajoutCategorie}
        onOpenChange={setAjoutCategorie}
        titre="Ajouter une catégorie"
        suggestions={autresDefaut}
        initial=""
        onValider={async (nom) => {
          const ok = await definirCategories(
            [...categories.map((c) => ({ nom: c })), { nom }],
            `Catégorie ${nom} ajoutée.`,
          );
          if (ok) setActive(nom);
          return ok;
        }}
      />
      <CategorieDialog
        open={renommage !== null}
        onOpenChange={(o) => {
          if (!o) setRenommage(null);
        }}
        titre={`Renommer la catégorie ${renommage ?? ''}`}
        suggestions={[]}
        initial={renommage ?? ''}
        onValider={async (nom) => {
          const ok = await definirCategories(
            categories.map((c) => (c === renommage ? { nom, ancien: c } : { nom: c })),
            `Catégorie renommée ${nom}.`,
          );
          if (ok) setActive(nom);
          return ok;
        }}
      />
      <ConfirmDialog
        open={retrait !== null}
        onOpenChange={(o) => {
          if (!o) setRetrait(null);
        }}
        titre={`Retirer la catégorie ${retrait ?? ''} ?`}
        description="Ses éléments sont retirés de la fiche. Les MEP qui les ont déjà retenus les gardent."
        libelleConfirmer="Retirer"
        variante="danger"
        onConfirmer={() =>
          retrait
            ? definirCategories(
                categories.filter((c) => c !== retrait).map((c) => ({ nom: c })),
                `Catégorie ${retrait} retirée.`,
              )
            : false
        }
      />
      <ObjetDialog
        open={objet !== null}
        onOpenChange={(o) => {
          if (!o) setObjet(null);
        }}
        categorie={courante ?? ''}
        existant={objet?.existant ?? null}
        onValider={(v) => {
          const saisie = { categorie: courante ?? '', ...v };
          const ex = objet?.existant;
          return agir(
            () =>
              ex
                ? api.modifierObjetCatalogue(systeme.id, ex.id, saisie)
                : api.ajouterObjetCatalogue(systeme.id, saisie),
            ex ? `${v.nom} modifié.` : `${v.nom} ajouté à la fiche.`,
          );
        }}
      />
      <ConfirmDialog
        open={supprObjet !== null}
        onOpenChange={(o) => {
          if (!o) setSupprObjet(null);
        }}
        titre={`Supprimer ${supprObjet?.nom ?? ''} de la fiche ?`}
        description="Il n’est plus proposé aux MEP ; celles qui l’ont déjà retenu le gardent."
        libelleConfirmer="Supprimer"
        variante="danger"
        onConfirmer={() =>
          supprObjet
            ? agir(
                () => api.supprimerObjetCatalogue(systeme.id, supprObjet.id),
                `${supprObjet.nom} supprimé de la fiche.`,
              )
            : false
        }
      />
    </Card>
  );
}

/** Nom d'une catégorie : saisie libre ou choix parmi les catégories par défaut pas encore utilisées. */
function CategorieDialog({
  open,
  onOpenChange,
  titre,
  suggestions,
  initial,
  onValider,
}: {
  open: boolean;
  onOpenChange: (o: boolean) => void;
  titre: string;
  suggestions: string[];
  initial: string;
  onValider: (nom: string) => Promise<boolean>;
}) {
  const [nom, setNom] = useState('');
  const [envoi, setEnvoi] = useState(false);
  useEffect(() => {
    if (open) setNom(initial);
  }, [open, initial]);
  async function valider(n = nom) {
    if (!n.trim()) return;
    setEnvoi(true);
    const ok = await onValider(n.trim());
    setEnvoi(false);
    if (ok) onOpenChange(false);
  }
  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      titre={titre}
      pied={
        <>
          <Button variante="fantome" onClick={() => onOpenChange(false)}>
            Annuler
          </Button>
          <Button variante="primaire" chargement={envoi} disabled={!nom.trim()} onClick={() => void valider()}>
            Enregistrer
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <Field label="Nom de la catégorie">
          {(id) => (
            <Input
              id={id}
              value={nom}
              autoFocus
              onChange={(e) => setNom(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter') void valider();
              }}
            />
          )}
        </Field>
        {suggestions.length > 0 && (
          <div>
            <p className="mb-2 text-xs text-fg-subtle">Catégories par défaut pas encore utilisées :</p>
            <div className="flex flex-wrap gap-1.5" role="group" aria-label="Catégories par défaut">
              {suggestions.map((c) => (
                <button
                  key={c}
                  type="button"
                  onClick={() => setNom(c)}
                  aria-pressed={nom === c}
                  className={cn(
                    'rounded-full border px-2.5 py-0.5 text-xs font-medium transition-colors duration-150',
                    nom === c
                      ? 'border-accent bg-accent-soft text-accent-text'
                      : 'border-line bg-surface-2 text-fg-muted hover:border-line-strong hover:text-fg',
                  )}
                >
                  {c}
                </button>
              ))}
            </div>
          </div>
        )}
      </div>
    </Dialog>
  );
}

function ObjetDialog({
  open,
  onOpenChange,
  categorie,
  existant,
  onValider,
}: {
  open: boolean;
  onOpenChange: (o: boolean) => void;
  categorie: string;
  existant: ObjetCatalogue | null;
  onValider: (v: typeof OBJET_VIDE) => Promise<boolean>;
}) {
  const [v, setV] = useState(OBJET_VIDE);
  const [envoi, setEnvoi] = useState(false);
  useEffect(() => {
    if (!open) return;
    setV(
      existant
        ? { type: existant.type, nom: existant.nom, libelle: existant.libelle, precision: existant.precision }
        : OBJET_VIDE,
    );
  }, [open, existant]);
  async function valider() {
    setEnvoi(true);
    const ok = await onValider({
      type: v.type.trim(),
      nom: v.nom.trim(),
      libelle: v.libelle.trim(),
      precision: v.precision.trim(),
    });
    setEnvoi(false);
    if (ok) onOpenChange(false);
  }
  const champ = (
    cle: keyof typeof OBJET_VIDE,
    label: ReactNode,
    props: { placeholder?: string; mono?: boolean } = {},
  ) => (
    <Field label={label}>
      {(id) => (
        <Input
          id={id}
          value={v[cle]}
          autoFocus={cle === 'nom'}
          placeholder={props.placeholder}
          className={props.mono ? 'font-mono' : undefined}
          onChange={(e) => setV({ ...v, [cle]: e.target.value })}
        />
      )}
    </Field>
  );
  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      titre={existant ? `Modifier ${existant.nom}` : `Ajouter un élément à ${categorie}`}
      pied={
        <>
          <Button variante="fantome" onClick={() => onOpenChange(false)}>
            Annuler
          </Button>
          <Button variante="primaire" chargement={envoi} disabled={!v.nom.trim()} onClick={() => void valider()}>
            Enregistrer
          </Button>
        </>
      }
    >
      <div className="grid gap-4 sm:grid-cols-2">
        {champ('nom', 'Nom *', { mono: true, placeholder: 'ex. clients' })}
        {champ('type', 'Type', { placeholder: 'ex. Table' })}
        {champ('libelle', 'Libellé', { placeholder: 'ex. Fiches clients' })}
        {champ('precision', 'Précision', { placeholder: 'ex. schéma public' })}
      </div>
    </Dialog>
  );
}
