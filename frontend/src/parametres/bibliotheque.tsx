import { ChevronLeft, ChevronRight, DatabaseBackup, Search, Timer, TrendingUp, Wrench } from 'lucide-react';
import type { ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { libelleType, nombre, TYPES_ETAPE, trierAlpha, type AnalyseRecherche } from '../bibliotheque';
import { couleurSysteme } from '../couleursSysteme';
import { LIBELLE_CRITICITE } from '../format';
import type { ModeleEtape, TypeEtape } from '../types';
import { Card } from '../ui/Card';
import { cn } from '../ui/cn';
import { Input, Select } from '../ui/Input';
import { EnChargement, Squelette, SqueletteLignes } from '../ui/Squelette';
import { Badge } from '../ui/StatusPill';

/**
 * Socle commun des bibliothèques (étapes modèles, recettes, résolutions types) : choix du système, en-tête de la
 * bibliothèque d'un système, recherche à plusieurs mots, filtre par type d'étape, compteur d'utilisations.
 */

/** Système proposé au choix : nom, description et état du catalogue. */
export interface SystemeBiblio {
  id: string;
  nom: string;
  description?: string;
  actif?: boolean;
}

/** Pastille du système (trois premières lettres sur sa couleur). */
export function PastilleSysteme({ nom, className }: { nom: string; className?: string }) {
  return (
    <span
      className={cn('grid size-10 shrink-0 place-content-center rounded-lg text-[13px] font-bold', className)}
      style={{ background: `color-mix(in srgb, ${couleurSysteme(nom)} 14%, transparent)`, color: couleurSysteme(nom) }}
      aria-hidden
    >
      {nom.slice(0, 3)}
    </span>
  );
}

/** Champ de recherche des bibliothèques. */
export function RechercheBibliotheque({
  valeur,
  onChange,
  placeholder,
  className,
}: {
  valeur: string;
  onChange: (v: string) => void;
  placeholder: string;
  className?: string;
}) {
  return (
    <div className={cn('relative min-w-60 flex-1', className)}>
      <Search
        className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-fg-subtle"
        aria-hidden
      />
      <Input
        type="search"
        aria-label="Rechercher dans la bibliothèque"
        placeholder={placeholder}
        className="pl-9"
        value={valeur}
        onChange={(e) => onChange(e.target.value)}
      />
    </div>
  );
}

/** Aide sous la recherche : comment se combinent les mots. */
export function AideRecherche({ champs }: { champs: string }) {
  return (
    <p className="mt-2 text-xs text-fg-subtle">
      Plusieurs mots : un nom de système limite à ce système, les autres mots doivent figurer dans {champs}.
    </p>
  );
}

/** Ce que la recherche a compris : systèmes reconnus et mots cherchés. */
export function LectureRecherche({
  analyse,
  nb,
  unite,
  champs,
}: {
  analyse: AnalyseRecherche;
  nb: number;
  unite: readonly [string, string];
  champs: string;
}) {
  return (
    <p role="status" className="flex flex-wrap items-center gap-1.5 text-sm text-fg-muted">
      <span className="font-semibold text-fg tnum">{nombre(nb, unite)}</span>
      {(analyse.systemes.length > 0 || analyse.mots.length > 0) && <span aria-hidden>·</span>}
      {analyse.systemes.map((s) => (
        <span
          key={s}
          className="inline-flex items-center gap-1 rounded-full border border-line bg-surface px-2 py-0.5 text-xs"
        >
          <i className="size-2 rounded-full" style={{ background: couleurSysteme(s) }} aria-hidden />
          système <b className="font-semibold text-fg">{s}</b>
        </span>
      ))}
      {analyse.systemes.length > 0 && analyse.mots.length > 0 && <span>et</span>}
      {analyse.mots.map((m) => (
        <span key={m} className="rounded-full border border-line bg-surface px-2 py-0.5 text-xs">
          « <b className="font-semibold text-fg">{m}</b> » dans {champs}
        </span>
      ))}
    </p>
  );
}

/** Filtre par type d'étape, rempli depuis la liste des types (prête pour de nouveaux types). */
export function FiltreType({
  valeur,
  onChange,
  className,
}: {
  valeur: TypeEtape | '';
  onChange: (v: TypeEtape | '') => void;
  className?: string;
}) {
  return (
    <div className={cn('w-44', className)}>
      <Select
        aria-label="Filtrer par type d’étape"
        value={valeur}
        onChange={(e) => onChange(e.target.value as TypeEtape | '')}
      >
        <option value="">Tous les types</option>
        {TYPES_ETAPE.map((t) => (
          <option key={t.valeur} value={t.valeur}>
            {t.libelle}
          </option>
        ))}
      </Select>
    </div>
  );
}

/** Grille de choix du système : ordre alphabétique, nombre d'éléments, détail facultatif (lien ou bouton). */
export function GrilleSystemes({
  systemes,
  compter,
  detail,
  unite,
  lien,
  onChoisir,
  colonnes = 3,
}: {
  systemes: SystemeBiblio[];
  compter: (s: SystemeBiblio) => number;
  detail?: (s: SystemeBiblio) => ReactNode;
  unite: readonly [string, string];
  lien?: (s: SystemeBiblio) => string;
  onChoisir?: (s: SystemeBiblio) => void;
  colonnes?: 2 | 3;
}) {
  return (
    <ul className={cn('grid gap-3 sm:grid-cols-2', colonnes === 3 && 'lg:grid-cols-3')}>
      {trierAlpha(systemes).map((s) => {
        const n = compter(s);
        const contenu = (
          <Card
            className={cn(
              'group flex h-full items-center gap-4 p-4 text-left transition-[box-shadow,border-color] duration-150 hover:border-line-strong hover:shadow-md',
              n === 0 && 'bg-surface-2/40',
            )}
          >
            <PastilleSysteme nom={s.nom} />
            <span className="min-w-0 flex-1">
              <span className="block font-semibold">
                {s.nom}
                {s.actif === false && <span className="ml-1.5 text-xs font-normal text-fg-subtle">(désactivé)</span>}
              </span>
              {s.description && <span className="block truncate text-xs text-fg-subtle">{s.description}</span>}
              {detail && <span className="mt-1 block truncate text-xs text-fg-muted">{detail(s)}</span>}
            </span>
            <span className="shrink-0 text-right">
              <span className={cn('block text-xl leading-6 font-semibold tnum', n === 0 && 'text-fg-subtle')}>{n}</span>
              <span className="block text-[11px] text-fg-subtle">{n > 1 ? unite[1] : unite[0]}</span>
            </span>
            <ChevronRight className="size-4 shrink-0 text-fg-subtle group-hover:text-fg" aria-hidden />
          </Card>
        );
        const etiquette = `${s.nom} : ${nombre(n, unite)}`;
        return (
          <li key={s.id}>
            {lien ? (
              <Link
                to={lien(s)}
                aria-label={etiquette}
                className="block h-full rounded-xl focus-visible:outline-2 focus-visible:outline-accent"
              >
                {contenu}
              </Link>
            ) : (
              <button
                type="button"
                aria-label={etiquette}
                onClick={() => onChoisir?.(s)}
                className="block h-full w-full rounded-xl focus-visible:outline-2 focus-visible:outline-accent"
              >
                {contenu}
              </button>
            )}
          </li>
        );
      })}
    </ul>
  );
}

/** En-tête de la bibliothèque d'un système : retour au choix du système, nom, description et action. */
export function EnTeteSysteme({
  bibliotheque,
  retour,
  systeme,
  description,
  action,
}: {
  bibliotheque: string;
  retour: string;
  systeme: string;
  description?: ReactNode;
  action?: ReactNode;
}) {
  return (
    <div>
      <Link to={retour} className="inline-flex items-center gap-1 text-sm font-medium text-fg-muted hover:text-fg">
        <ChevronLeft className="size-4" aria-hidden />
        {bibliotheque} · tous les systèmes
      </Link>
      <div className="mt-2 flex flex-wrap items-end justify-between gap-4">
        <div className="flex items-center gap-3">
          <PastilleSysteme nom={systeme} />
          <div>
            <h1 className="text-2xl font-semibold tracking-tight">
              {bibliotheque} · {systeme}
            </h1>
            {description && <p className="text-sm text-fg-muted">{description}</p>}
          </div>
        </div>
        {action}
      </div>
    </div>
  );
}

/** Compteur d'utilisations, visible sur chaque élément (premier critère du tri). */
export function CompteurUtilisations({ n, className }: { n: number; className?: string }) {
  return (
    <span
      className={cn(
        'inline-flex shrink-0 items-center gap-1 rounded-md border px-1.5 py-0.5 text-[11.5px] font-semibold tnum',
        n > 0 ? 'border-accent/40 bg-accent-soft text-accent-text' : 'border-line bg-surface-2 text-fg-subtle',
        className,
      )}
      title={`Utilisé dans ${n} MEP`}
    >
      <TrendingUp className="size-3" aria-hidden />
      {n} MEP
    </span>
  );
}

/** Rappel du tri par défaut. */
export function MentionTri({ className }: { className?: string }) {
  return (
    <span className={cn('text-xs text-fg-subtle', className)}>Tri : nombre d’utilisations, puis ordre de création</span>
  );
}

export const UNITE_ETAPES = ['étape modèle', 'étapes modèles'] as const;
export const CHAMPS_ETAPES = 'le nom ou le type';

/** Badges d'une étape modèle : type, criticité, durée, résolutions (page et fenêtre d'insertion). */
export function BadgesModele({ m }: { m: ModeleEtape }) {
  return (
    <>
      {m.type === 'Sauvegarde' ? (
        <Badge ton="done">
          <DatabaseBackup className="size-3" aria-hidden />
          {libelleType(m.type)}
        </Badge>
      ) : (
        <Badge>{libelleType(m.type)}</Badge>
      )}
      {m.criticite && <Badge>{LIBELLE_CRITICITE[m.criticite]}</Badge>}
      {m.dureeEstimeeMinutes != null && (
        <Badge>
          <Timer className="size-3" aria-hidden />
          {m.dureeEstimeeMinutes} min
        </Badge>
      )}
      {m.resolutionIds.length > 0 && (
        <Badge>
          <Wrench className="size-3" aria-hidden />
          {nombre(m.resolutionIds.length, ['résolution', 'résolutions'])}
        </Badge>
      )}
    </>
  );
}

/**
 * Bibliothèque en chargement : la grille des systèmes (accueil) ou la liste des éléments, aux dimensions du contenu à
 * venir.
 */
export function SqueletteBibliotheque({ accueil }: { accueil: boolean }) {
  return (
    <EnChargement libelle="Chargement de la bibliothèque…">
      {accueil ? (
        <>
          <Squelette className="mt-8 mb-3 h-3 w-72" />
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
            {Array.from({ length: 6 }, (_, i) => (
              <Card key={i} className="flex items-center gap-4 p-4">
                <Squelette className="size-10 rounded-lg" />
                <SqueletteLignes lignes={3} className="flex-1" />
                <Squelette className="h-6 w-6" />
              </Card>
            ))}
          </div>
        </>
      ) : (
        <div className="mt-4 grid gap-3 lg:grid-cols-2">
          {Array.from({ length: 6 }, (_, i) => (
            <Card key={i} className="grid h-40 content-start gap-3 p-4">
              <Squelette className="h-5 w-3/5" />
              <Squelette className="h-4 w-1/3" />
              <SqueletteLignes lignes={2} />
            </Card>
          ))}
        </div>
      )}
    </EnChargement>
  );
}
