// Barre de recherche avancée de la liste des MEP (#59) : un déclencheur par critère (système, environnement, équipe,
// personne et rôle, état, période), une feuille « Filtres » sur téléphone, puis le résumé des critères actifs.
import { Command } from 'cmdk';
import {
  CalendarRange,
  Check,
  ChevronDown,
  CircleDot,
  Layers,
  Link2,
  PlusCircle,
  Server,
  SlidersHorizontal,
  UserRound,
  Users,
  X,
  type LucideIcon,
} from 'lucide-react';
import { forwardRef, useId, useState, type ButtonHTMLAttributes, type ReactNode } from 'react';
import { couleurSysteme } from '../../couleursSysteme';
import { formatDate } from '../../format';
import {
  environnementsProposes,
  ETATS_ARRETES,
  ETATS_CLOTURES,
  ETATS_EN_COURS,
  FILTRE_VIDE,
  LIBELLE_ROLE,
  nbCriteres,
  NOMS_ETATS,
  NOMS_ROLES,
  PREFIXE_LIBRE,
  type FiltreMeps,
  type NomEtat,
  type NomRole,
} from '../../rechercheMeps';
import { metaEtatMep } from '../../statuts';
import type { Equipe, FacettesMeps, SystemeCatalogue } from '../../types';
import { Avatar } from '../../ui/Avatar';
import { Button } from '../../ui/Button';
import { Checkbox } from '../../ui/Checkbox';
import { cn } from '../../ui/cn';
import { Input } from '../../ui/Input';
import { Popover, PopoverContent, PopoverTrigger } from '../../ui/Popover';
import { Sheet } from '../../ui/Sheet';
import { StatusPill } from '../../ui/StatusPill';
import { comptesDe, type ComptesFiltres } from './listeMeps';
import { useToast } from '../../ui/Toast';

/** Personne proposée au filtre : compte principal, nom et identifiants de ses comptes. */
export interface PersonneFiltre {
  id: string;
  nom: string;
  detail: string;
}

export interface DonneesFiltres {
  /** Comptes calculés par le serveur avec les filtres en cours (#96). */
  facettes: FacettesMeps;
  catalogue: SystemeCatalogue[];
  equipes: Equipe[];
  personnes: PersonneFiltre[];
}

interface Ctx extends DonneesFiltres {
  f: FiltreMeps;
  maj: (p: Partial<FiltreMeps>) => void;
  /** Nombre de MEP en choisissant chaque valeur (compteurs des options). */
  comptes: ComptesFiltres;
}

const basculer = <T,>(l: T[], v: T) => (l.includes(v) ? l.filter((x) => x !== v) : [...l, v]);

/**
 * Déclencheur d'un filtre : en pointillé tant qu'il est vide (« ⊕ Système »), plein avec sa valeur une fois choisi
 * (« Système | BDD, API ») et une croix pour l'effacer.
 */
const BoutonFiltre = forwardRef<
  HTMLButtonElement,
  ButtonHTMLAttributes<HTMLButtonElement> & {
    libelle: string;
    valeur: string;
    Icone: LucideIcon;
    onEffacer: () => void;
  }
>(function BoutonFiltre({ libelle, valeur, Icone, onEffacer, className, ...rest }, ref) {
  const actif = valeur !== '';
  return (
    <span
      className={cn(
        'inline-flex h-8 items-center rounded-lg border text-ui transition-colors duration-150',
        actif
          ? 'border-accent/60 bg-accent-soft text-accent-text'
          : 'border-dashed border-line-strong bg-surface text-fg-muted hover:border-fg-subtle hover:text-fg',
        className,
      )}
    >
      <button
        ref={ref}
        type="button"
        className="inline-flex h-full items-center gap-1.5 rounded-lg pr-2 pl-2.5 outline-none focus-visible:ring-2 focus-visible:ring-accent"
        {...rest}
      >
        {actif ? <Icone className="size-3.5" aria-hidden /> : <PlusCircle className="size-3.5" aria-hidden />}
        <span className={cn(actif && 'font-medium')}>{libelle}</span>
        {actif ? (
          <>
            <span className="h-3.5 w-px bg-current opacity-30" aria-hidden />
            <span className="max-w-56 truncate font-semibold">{valeur}</span>
          </>
        ) : (
          <ChevronDown className="size-3.5 opacity-60" aria-hidden />
        )}
      </button>
      {actif && (
        <button
          type="button"
          aria-label={`Effacer le filtre ${libelle}`}
          className="mr-1 inline-flex size-6 items-center justify-center rounded-md hover:bg-accent/15"
          onClick={onEffacer}
        >
          <X className="size-3.5" aria-hidden />
        </button>
      )}
    </span>
  );
});

/** Ligne à cocher d'un panneau, avec le nombre de MEP qu'elle donnerait. */
function Option({
  coche,
  onClick,
  compte,
  children,
  statique,
}: {
  coche: boolean;
  onClick?: () => void;
  compte?: number;
  children: ReactNode;
  /** Dans un élément cmdk, qui gère déjà le choix. */
  statique?: boolean;
}) {
  const classes = cn(
    'flex w-full items-center gap-2.5 rounded-lg px-2.5 py-1.5 text-left text-sm text-fg hover:bg-surface-2',
    compte === 0 && !coche && 'opacity-55',
  );
  const contenu = (
    <>
      <span
        className={cn(
          'flex size-4 shrink-0 items-center justify-center rounded-[4px] border',
          coche ? 'border-accent bg-accent text-accent-fg' : 'border-line-strong bg-surface',
        )}
        aria-hidden
      >
        {coche && <Check className="size-3" strokeWidth={3} />}
      </span>
      <span className="flex min-w-0 flex-1 items-center gap-2">{children}</span>
      {compte !== undefined && <span className="text-xs text-fg-subtle tnum">{compte}</span>}
    </>
  );
  if (statique) return <div className={classes}>{contenu}</div>;
  return (
    <button type="button" role="menuitemcheckbox" aria-checked={coche} onClick={onClick} className={classes}>
      {contenu}
    </button>
  );
}

function Titre({ children }: { children: ReactNode }) {
  return <p className="px-2.5 pt-2 pb-1 text-2xs font-semibold tracking-wider text-fg-subtle uppercase">{children}</p>;
}

function Aide({ children }: { children: ReactNode }) {
  return <p className="px-2.5 pt-1.5 pb-1 text-xs text-fg-subtle">{children}</p>;
}

function Puce({ actif, onClick, children }: { actif: boolean; onClick: () => void; children: ReactNode }) {
  return (
    <button
      type="button"
      aria-pressed={actif}
      onClick={onClick}
      className={cn(
        'rounded-full border px-2.5 py-0.5 text-xs transition-colors duration-150',
        actif
          ? 'border-accent bg-accent-soft font-medium text-accent-text'
          : 'border-line-strong text-fg-muted hover:text-fg',
      )}
    >
      {children}
    </button>
  );
}

const ITEM = 'rounded-lg data-[selected=true]:bg-surface-2';
const SAISIE =
  'h-9 w-full border-b border-line bg-transparent px-3 text-sm text-fg outline-none placeholder:text-fg-subtle';
const GROUPE =
  '[&_[cmdk-group-heading]]:px-2.5 [&_[cmdk-group-heading]]:pt-2 [&_[cmdk-group-heading]]:pb-1 [&_[cmdk-group-heading]]:text-2xs [&_[cmdk-group-heading]]:font-semibold [&_[cmdk-group-heading]]:tracking-wider [&_[cmdk-group-heading]]:text-fg-subtle [&_[cmdk-group-heading]]:uppercase';

/* ------------------------------------------------------------------ Panneaux */

function PanneauSysteme({ f, maj, catalogue, facettes, comptes }: Ctx) {
  const libres = facettes.libres;
  const choisir = (cle: string) => maj({ systemes: basculer(f.systemes, cle), environnements: [] });
  return (
    <div className="w-full sm:w-72">
      <Command label="Systèmes">
        <Command.Input placeholder="Système…" className={SAISIE} />
        <Command.List className="max-h-64 overflow-y-auto p-1">
          <Command.Empty className="px-3 py-4 text-center text-sm text-fg-subtle">Aucun système.</Command.Empty>
          <Command.Group heading="Catalogue" className={GROUPE}>
            {catalogue.map((s) => (
              <Command.Item
                key={s.id}
                value={`${s.nom} ${s.description}`}
                onSelect={() => choisir(s.id)}
                className={ITEM}
              >
                <Option statique coche={f.systemes.includes(s.id)} compte={comptes.systeme(s.id)}>
                  <i
                    className="size-2 shrink-0 rounded-full"
                    style={{ background: couleurSysteme(s.nom) }}
                    aria-hidden
                  />
                  <span className="font-medium">{s.nom}</span>
                  <span className="truncate text-xs text-fg-subtle">{s.description}</span>
                </Option>
              </Command.Item>
            ))}
          </Command.Group>
          {libres.length > 0 && (
            <Command.Group heading="Hors catalogue" className={GROUPE}>
              {libres.map((nom) => {
                const cle = `${PREFIXE_LIBRE}${nom}`;
                return (
                  <Command.Item
                    key={cle}
                    value={`hors catalogue ${nom}`}
                    onSelect={() => choisir(cle)}
                    className={ITEM}
                  >
                    <Option statique coche={f.systemes.includes(cle)} compte={comptes.systeme(cle)}>
                      <i className="size-2 shrink-0 rounded-full border border-dashed border-fg-subtle" aria-hidden />
                      <span className="font-medium">{nom}</span>
                      <span className="text-xs text-fg-subtle">saisi à la main</span>
                    </Option>
                  </Command.Item>
                );
              })}
            </Command.Group>
          )}
        </Command.List>
      </Command>
      <div className="border-t border-line px-3 py-2.5">
        <Checkbox
          checked={f.echec}
          onChange={(v) => maj({ echec: v })}
          label={f.systemes.length ? 'Avec un échec sur ces systèmes' : 'Avec au moins un échec d’étape'}
          description="Une étape a échoué pendant la MEP, même reprise ensuite."
        />
      </div>
    </div>
  );
}

function PanneauEnvironnement({ f, maj, catalogue, facettes, comptes }: Ctx) {
  const envs = environnementsProposes(f, [], catalogue, facettes.environnementsProposes);
  const noms = f.systemes.map((x) => catalogue.find((s) => s.id === x)?.nom ?? x.replace(PREFIXE_LIBRE, '')).join(', ');
  return (
    <div className="w-full p-1 sm:w-64">
      <Titre>{f.systemes.length ? `Environnements de ${noms}` : 'Tous les systèmes'}</Titre>
      {envs.map((e) => (
        <Option
          key={e.nom}
          coche={f.environnements.includes(e.nom)}
          onClick={() => maj({ environnements: basculer(f.environnements, e.nom) })}
          compte={comptes.environnement(e.nom)}
        >
          {e.nom}
          {e.production && (
            <span className="rounded border border-warn-bd bg-warn-bg px-1 text-2xs text-warn-fg">Production</span>
          )}
        </Option>
      ))}
      {envs.length === 0 && <Aide>Aucun environnement.</Aide>}
      {!f.systemes.length && <Aide>Choisissez un système pour ne voir que ses environnements.</Aide>}
    </div>
  );
}

function PanneauEquipe({ f, maj, equipes, comptes }: Ctx) {
  return (
    <div className="w-full p-1 sm:w-64">
      {equipes.map((e) => (
        <Option
          key={e.id}
          coche={f.equipes.includes(e.id)}
          onClick={() => maj({ equipes: basculer(f.equipes, e.id) })}
          compte={comptes.equipe(e.id)}
        >
          {e.nom}
        </Option>
      ))}
      {equipes.length === 0 && <Aide>Aucune équipe : la console d’administration les crée.</Aide>}
      <Aide>MEP dont au moins une personne fait partie de l’équipe.</Aide>
    </div>
  );
}

function PanneauPersonne({ f, maj, personnes, comptes }: Ctx) {
  return (
    <div className="w-full sm:w-80">
      <Command label="Personnes">
        <Command.Input placeholder="Nom ou identifiant…" className={SAISIE} />
        <Command.List className="max-h-52 overflow-y-auto p-1">
          <Command.Empty className="px-3 py-4 text-center text-sm text-fg-subtle">Personne.</Command.Empty>
          {personnes.map((p) => {
            const coche = f.personne === p.id;
            return (
              <Command.Item
                key={p.id}
                value={`${p.nom} ${p.detail}`}
                onSelect={() => maj({ personne: coche ? '' : p.id })}
                className={cn(ITEM, 'flex cursor-pointer items-center gap-2.5 px-2.5 py-1.5 text-sm')}
              >
                <Avatar nom={p.nom} taille="sm" />
                <span className="min-w-0 flex-1">
                  <span className="block font-medium">{p.nom}</span>
                  <span className="block truncate text-xs text-fg-subtle">{p.detail}</span>
                </span>
                <span className="text-xs text-fg-subtle tnum">{comptes.personne(p.id)}</span>
                <Check className={cn('size-4 text-accent-text', !coche && 'invisible')} aria-hidden />
              </Command.Item>
            );
          })}
        </Command.List>
      </Command>
      <div className="border-t border-line px-3 py-2.5">
        <p className="mb-1.5 text-xs font-medium text-fg-muted">Dans le rôle de</p>
        <div className="flex flex-wrap gap-1.5">
          <Puce actif={f.roles.length === 0} onClick={() => maj({ roles: [] })}>
            Tous les rôles
          </Puce>
          {(Object.keys(NOMS_ROLES) as NomRole[]).map((r) => (
            <Puce key={r} actif={f.roles.includes(r)} onClick={() => maj({ roles: basculer(f.roles, r) })}>
              {LIBELLE_ROLE[NOMS_ROLES[r]]}
            </Puce>
          ))}
        </div>
        <p className="mt-2 text-xs text-fg-subtle">
          {f.personne ? 'Comptes reliés compris.' : 'Choisissez d’abord une personne.'} Sans rôle : simple participante.
        </p>
      </div>
    </div>
  );
}

function PanneauEtat({ f, maj, comptes }: Ctx) {
  const ligne = (e: NomEtat) => {
    const meta = metaEtatMep(NOMS_ETATS[e]);
    return (
      <Option
        key={e}
        coche={f.etats.includes(e)}
        onClick={() => maj({ etats: basculer(f.etats, e) })}
        compte={comptes.etat(e)}
      >
        <StatusPill ton={meta.ton} icone={meta.Icone} taille="sm">
          {meta.libelle}
        </StatusPill>
      </Option>
    );
  };
  return (
    <div className="w-full p-1 sm:w-72">
      <Titre>En cours</Titre>
      {ETATS_EN_COURS.map(ligne)}
      <Titre>Clôturées</Titre>
      {ETATS_CLOTURES.map(ligne)}
      <Titre>Arrêtées</Titre>
      {ETATS_ARRETES.map(ligne)}
      <Aide>
        « Réussie avec incidents » : clôturée réussie, mais au moins une étape a échoué en route. « Reportée » : en
        préparation, avec un report.
      </Aide>
    </div>
  );
}

const iso = (d: Date) =>
  `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;

function PanneauPeriode({ f, maj }: Ctx) {
  const [auj] = useState(() => new Date());
  const id = useId();
  const ilYa = (mois: number, jours = 0) =>
    iso(new Date(auj.getFullYear(), auj.getMonth() - mois, auj.getDate() - jours));
  const raccourcis: [string, string][] = [
    ['30 derniers jours', ilYa(0, 30)],
    ['3 derniers mois', ilYa(3)],
    ['12 derniers mois', ilYa(12)],
    [`Année ${auj.getFullYear()}`, `${auj.getFullYear()}-01-01`],
  ];
  return (
    <div className="w-full p-3 sm:w-72">
      <div className="flex flex-wrap gap-1.5">
        {raccourcis.map(([l, du]) => (
          <Puce key={l} actif={f.du === du && !f.au} onClick={() => maj({ du, au: '' })}>
            {l}
          </Puce>
        ))}
      </div>
      <div className="mt-3 grid grid-cols-2 gap-2">
        <label htmlFor={`${id}-du`} className="text-xs text-fg-muted">
          Du
          <Input
            id={`${id}-du`}
            type="date"
            value={f.du}
            max={f.au || undefined}
            onChange={(e) => maj({ du: e.target.value })}
            className="mt-1 h-8"
          />
        </label>
        <label htmlFor={`${id}-au`} className="text-xs text-fg-muted">
          Au
          <Input
            id={`${id}-au`}
            type="date"
            value={f.au}
            min={f.du || undefined}
            onChange={(e) => maj({ au: e.target.value })}
            className="mt-1 h-8"
          />
        </label>
      </div>
      <p className="mt-2 text-xs text-fg-subtle">Jour du Go ; à défaut, de la création.</p>
    </div>
  );
}

/* ------------------------------------------------------------------ Barre */

interface Critere {
  cle: string;
  libelle: string;
  Icone: LucideIcon;
  valeur: string;
  effacer: Partial<FiltreMeps>;
  Panneau: (c: Ctx) => ReactNode;
}

function criteres(c: Ctx, modeles: boolean): Critere[] {
  const { f, catalogue, equipes, personnes } = c;
  const nomSysteme = (x: string) =>
    x.startsWith(PREFIXE_LIBRE) ? x.slice(PREFIXE_LIBRE.length) : (catalogue.find((s) => s.id === x)?.nom ?? '?');
  const personne = personnes.find((p) => p.id === f.personne);
  const periode =
    f.du && f.au
      ? `${formatDate(f.du)} → ${formatDate(f.au)}`
      : f.du
        ? `depuis le ${formatDate(f.du)}`
        : f.au
          ? `jusqu’au ${formatDate(f.au)}`
          : '';
  const equipe: Critere = {
    cle: 'equipe',
    libelle: 'Équipe',
    Icone: Users,
    valeur: f.equipes.map((e) => equipes.find((x) => x.id === e)?.nom ?? '?').join(', '),
    effacer: { equipes: [] },
    Panneau: PanneauEquipe,
  };
  if (modeles) return [equipe];
  return [
    {
      cle: 'systeme',
      libelle: 'Système',
      Icone: Layers,
      valeur: [f.systemes.map(nomSysteme).join(', '), f.echec ? 'avec échec' : ''].filter(Boolean).join(' · '),
      effacer: { systemes: [], environnements: [], echec: false },
      Panneau: PanneauSysteme,
    },
    {
      cle: 'environnement',
      libelle: 'Environnement',
      Icone: Server,
      valeur: f.environnements.join(', '),
      effacer: { environnements: [] },
      Panneau: PanneauEnvironnement,
    },
    equipe,
    {
      cle: 'personne',
      libelle: 'Personne',
      Icone: UserRound,
      valeur: f.personne
        ? (personne?.nom ?? 'Personne inconnue') +
          (f.roles.length ? ` (${f.roles.map((r) => LIBELLE_ROLE[NOMS_ROLES[r]].toLowerCase()).join(', ')})` : '')
        : '',
      effacer: { personne: '', roles: [] },
      Panneau: PanneauPersonne,
    },
    {
      cle: 'etat',
      libelle: 'État',
      Icone: CircleDot,
      valeur: f.etats.map((e) => metaEtatMep(NOMS_ETATS[e]).court).join(', '),
      effacer: { etats: [], phase: '' },
      Panneau: PanneauEtat,
    },
    {
      cle: 'periode',
      libelle: 'Période',
      Icone: CalendarRange,
      valeur: periode,
      effacer: { du: '', au: '' },
      Panneau: PanneauPeriode,
    },
  ];
}

export function BarreFiltres({
  f,
  maj,
  donnees,
  modeles,
  resultats,
}: {
  f: FiltreMeps;
  maj: (p: Partial<FiltreMeps>) => void;
  donnees: DonneesFiltres;
  /** Vue des modèles : seule l'équipe s'ajoute à la recherche. */
  modeles: boolean;
  resultats: number;
}) {
  const toast = useToast();
  const [ouvert, setOuvert] = useState<string | null>(null);
  const [feuille, setFeuille] = useState(false);
  const ctx: Ctx = {
    ...donnees,
    f,
    maj,
    comptes: comptesDe(donnees.facettes),
  };
  const liste = criteres(ctx, modeles);
  const actifs = liste.filter((c) => c.valeur);
  const n = nbCriteres(f) - (f.recherche.trim() ? 1 : 0);
  const toutEffacer = () => maj({ ...FILTRE_VIDE });
  return (
    <>
      {/* Écran large : un déclencheur par critère. */}
      <div className="mt-3 hidden flex-wrap items-center gap-2 sm:flex">
        {liste.map((c) => (
          <Popover key={c.cle} open={ouvert === c.cle} onOpenChange={(o) => setOuvert(o ? c.cle : null)}>
            <PopoverTrigger asChild>
              <BoutonFiltre libelle={c.libelle} Icone={c.Icone} valeur={c.valeur} onEffacer={() => maj(c.effacer)} />
            </PopoverTrigger>
            <PopoverContent className="p-0" align="start">
              <c.Panneau {...ctx} />
            </PopoverContent>
          </Popover>
        ))}
      </div>
      {/* Téléphone : un bouton « Filtres » et une feuille avec tous les critères. */}
      <div className="mt-3 flex items-center gap-2 sm:hidden">
        <Button
          taille="sm"
          icone={<SlidersHorizontal className="size-4" aria-hidden />}
          onClick={() => setFeuille(true)}
        >
          Filtres
          {n > 0 && <span className="ml-1 rounded-full bg-accent px-1.5 text-2xs text-accent-fg tnum">{n}</span>}
        </Button>
      </div>
      <Sheet
        open={feuille}
        onOpenChange={setFeuille}
        titre="Filtres"
        description={`${resultats} ${modeles ? 'modèle' : 'MEP'}${resultats > 1 && modeles ? 's' : ''} sur ${donnees.facettes.ensemble}`}
        pied={
          <>
            <Button variante="fantome" onClick={toutEffacer}>
              Tout effacer
            </Button>
            <Button variante="primaire" onClick={() => setFeuille(false)}>
              Voir les résultats ({resultats})
            </Button>
          </>
        }
      >
        <div className="-mx-3 divide-y divide-line">
          {liste.map((c) => (
            <section key={c.cle} className="py-2">
              <h3 className="flex items-center gap-2 px-3 pb-1 text-sm font-semibold">
                <c.Icone className="size-4 text-fg-subtle" aria-hidden />
                {c.libelle}
              </h3>
              <c.Panneau {...ctx} />
            </section>
          ))}
        </div>
      </Sheet>
      {/* Résumé : nombre de résultats, critères actifs (puces sur téléphone), tout effacer, lien à partager. */}
      {nbCriteres(f) > 0 && (
        <div className="mt-3 flex flex-wrap items-center gap-x-3 gap-y-2 text-sm" aria-live="polite">
          <span className="font-semibold tnum">
            {resultats} {modeles ? `modèle${resultats > 1 ? 's' : ''}` : 'MEP'}{' '}
            <span className="font-normal text-fg-subtle">sur {donnees.facettes.ensemble}</span>
          </span>
          {actifs.length > 0 && (
            <span className="flex flex-wrap gap-1.5 sm:hidden">
              {actifs.map((c) => (
                <span
                  key={c.cle}
                  className="inline-flex items-center gap-1 rounded-full border border-accent/50 bg-accent-soft py-0.5 pr-1 pl-2 text-xs text-accent-text"
                >
                  {c.libelle} : <strong className="max-w-36 truncate">{c.valeur}</strong>
                  <button
                    type="button"
                    aria-label={`Effacer le filtre ${c.libelle}`}
                    onClick={() => maj(c.effacer)}
                    className="rounded-full p-0.5 hover:bg-accent/15"
                  >
                    <X className="size-3" aria-hidden />
                  </button>
                </span>
              ))}
            </span>
          )}
          <button type="button" className="font-medium text-accent-text hover:underline" onClick={toutEffacer}>
            Tout effacer
          </button>
          <button
            type="button"
            className="inline-flex items-center gap-1 text-fg-muted hover:text-fg"
            onClick={() => {
              void navigator.clipboard
                ?.writeText(window.location.href)
                .then(() => toast.succes('Lien de la recherche copié : il rouvre la liste avec ces filtres.'))
                .catch(() => toast.erreur('Copie impossible : copiez l’adresse de la page.'));
            }}
          >
            <Link2 className="size-3.5" aria-hidden />
            Copier le lien
          </button>
        </div>
      )}
    </>
  );
}
