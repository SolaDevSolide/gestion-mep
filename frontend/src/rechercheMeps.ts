// Recherche avancée de la liste des MEP (#59) : filtre partagé par l'adresse de la page d'accueil, l'API (/api/meps,
// /api/meps/excel) et la ligne de commande (gestion-mep meps lister). Mêmes règles que le serveur (FiltreMeps.cs),
// vérifiées des deux côtés par les mêmes cas (vecteurs-filtre-meps.json). Logique pure, sans affichage.
import { normaliser } from './recherche';
import type { EtatMep, MepResume, RoleMep, SystemeResume } from './types';

/** Préfixe d'un système saisi à la main (hors catalogue) dans le filtre : « libre:GED ». */
export const PREFIXE_LIBRE = 'libre:';

/** Noms des rôles et des états dans l'adresse (et la ligne de commande). */
export const NOMS_ROLES = {
  pilote: 'Pilote',
  suppleant: 'Suppleant',
  responsable: 'ResponsableTechnique',
  executant: 'Executant',
  approbateur: 'Approbateur',
} as const satisfies Record<string, RoleMep>;
export type NomRole = keyof typeof NOMS_ROLES;

export const NOMS_ETATS = {
  preparation: 'Preparation',
  execution: 'Execution',
  recette: 'Recette',
  'retour-arriere': 'RetourArriere',
  reussie: 'Reussie',
  incidents: 'AvecIncidents',
  repliee: 'Repliee',
  reportee: 'Reportee',
  echec: 'Echec',
  annulee: 'Annulee',
} as const satisfies Record<string, EtatMep>;
export type NomEtat = keyof typeof NOMS_ETATS;

export const LIBELLE_ROLE: Record<RoleMep, string> = {
  Pilote: 'Pilote',
  Suppleant: 'Suppléant',
  ResponsableTechnique: 'Responsable technique',
  Executant: 'Exécutant',
  Approbateur: 'Approbateur',
};

/** Libellés des états, comme les exports Excel (FiltreMeps.Libelle). */
export const LIBELLE_ETAT_MEP: Record<EtatMep, string> = {
  Preparation: 'Préparation',
  Execution: 'Exécution',
  Recette: 'Recette',
  RetourArriere: 'Retour arrière',
  Reussie: 'Réussie',
  AvecIncidents: 'Réussie avec incidents',
  Repliee: 'Repliée',
  Reportee: 'Reportée',
  Echec: 'Échec (service dégradé)',
  Annulee: 'Annulée',
};

export const ETATS_EN_COURS: NomEtat[] = ['preparation', 'reportee', 'execution', 'recette', 'retour-arriere'];
export const ETATS_CLOTURES: NomEtat[] = ['reussie', 'incidents', 'repliee', 'echec'];
/** MEP arrêtées sans clôture (#93). */
export const ETATS_ARRETES: NomEtat[] = ['annulee'];
export const ETAT_CLOTURE = (e: EtatMep) =>
  e === 'Reussie' || e === 'AvecIncidents' || e === 'Repliee' || e === 'Echec';

export interface FiltreMeps {
  recherche: string;
  /** Identifiants du catalogue, ou « libre:Nom » pour un système saisi à la main. */
  systemes: string[];
  environnements: string[];
  equipes: string[];
  /** Personne (compte principal) ; vide : toutes. */
  personne: string;
  /** Rôles de la personne (sans effet sans personne). */
  roles: NomRole[];
  etats: NomEtat[];
  /** Adresses existantes : phase et type de MEP. */
  phase: string;
  type: string;
  /** Bornes incluses (AAAA-MM-JJ) sur le jour de la MEP : Go, à défaut création. */
  du: string;
  au: string;
  /** Au moins un échec d'étape survenu (sur les systèmes filtrés s'il y en a). */
  echec: boolean;
}

export const FILTRE_VIDE: FiltreMeps = {
  recherche: '',
  systemes: [],
  environnements: [],
  equipes: [],
  personne: '',
  roles: [],
  etats: [],
  phase: '',
  type: '',
  du: '',
  au: '',
  echec: false,
};

const liste = (v: string | null): string[] => [
  ...new Set(
    (v ?? '')
      .split(',')
      .map((x) => x.trim())
      .filter(Boolean),
  ),
];
const date = (v: string | null) => (/^\d{4}-\d{2}-\d{2}$/.test(v?.trim() ?? '') ? v!.trim() : '');

/** Lit le filtre d'une adresse ; les valeurs inconnues sont ignorées (le serveur, lui, les refuse). */
export function lireFiltre(p: URLSearchParams): FiltreMeps {
  return {
    recherche: p.get('recherche') ?? '',
    systemes: liste(p.get('systeme')),
    environnements: liste(p.get('environnement')),
    equipes: liste(p.get('equipe')),
    personne: p.get('personne')?.trim() ?? '',
    roles: liste(p.get('role'))
      .map((r) => r.toLowerCase())
      .filter((r): r is NomRole => r in NOMS_ROLES),
    etats: liste(p.get('etat'))
      .map((e) => e.toLowerCase())
      .filter((e): e is NomEtat => e in NOMS_ETATS),
    phase: p.get('phase')?.trim() ?? '',
    type: p.get('type')?.trim() ?? '',
    du: date(p.get('du')),
    au: date(p.get('au')),
    echec: ['1', 'oui', 'true'].includes(p.get('echec')?.trim().toLowerCase() ?? ''),
  };
}

/** Écrit le filtre dans des paramètres d'adresse (ceux de <paramref name="base"/> sont gardés). */
export function ecrireFiltre(f: FiltreMeps, base?: URLSearchParams): URLSearchParams {
  const p = new URLSearchParams(base);
  const poser = (cle: string, v: string) => (v ? p.set(cle, v) : p.delete(cle));
  poser('recherche', f.recherche.trim() ? f.recherche : '');
  poser('systeme', f.systemes.join(','));
  poser('environnement', f.environnements.join(','));
  poser('equipe', f.equipes.join(','));
  poser('personne', f.personne);
  poser('role', f.personne ? f.roles.join(',') : '');
  poser('etat', f.etats.join(','));
  poser('phase', f.phase);
  poser('type', f.type);
  poser('du', f.du);
  poser('au', f.au);
  poser('echec', f.echec ? '1' : '');
  return p;
}

/** Nombre de critères actifs (la recherche compte pour un). */
export const nbCriteres = (f: FiltreMeps): number =>
  [
    f.recherche.trim(),
    f.systemes.length,
    f.environnements.length,
    f.equipes.length,
    f.personne,
    f.etats.length,
    f.phase,
    f.type,
    f.du || f.au,
    f.echec,
  ].filter(Boolean).length;

export const estVide = (f: FiltreMeps): boolean => nbCriteres(f) === 0;

/** Le système de la MEP est l'un des systèmes filtrés (tous s'il n'y en a pas). */
export function retient(f: FiltreMeps, s: Pick<SystemeResume, 'catalogueId' | 'nom'>): boolean {
  if (f.systemes.length === 0) return true;
  return f.systemes.some((x) =>
    x.startsWith(PREFIXE_LIBRE)
      ? s.catalogueId === null && normaliser(s.nom.trim()) === normaliser(x.slice(PREFIXE_LIBRE.length).trim())
      : s.catalogueId !== null && s.catalogueId.toLowerCase() === x.toLowerCase(),
  );
}

/** Rôles de la personne filtrée dans la MEP ; null si elle n'y intervient pas (ou sans personne filtrée). */
export function rolesDeLaPersonne(f: FiltreMeps, m: Pick<MepResume, 'intervenants'>): RoleMep[] | null {
  if (!f.personne) return null;
  return m.intervenants.find((i) => i.personneId.toLowerCase() === f.personne.toLowerCase())?.roles ?? null;
}

type MepFiltrable = Pick<
  MepResume,
  | 'outil'
  | 'versionCible'
  | 'objet'
  | 'pilote'
  | 'personnes'
  | 'equipeIds'
  | 'type'
  | 'phase'
  | 'etat'
  | 'jour'
  | 'creeLe'
  | 'echecsSurvenus'
  | 'systemes'
  | 'intervenants'
>;

export function correspond(f: FiltreMeps, m: MepFiltrable): boolean {
  if (f.type && m.type.toLowerCase() !== f.type.toLowerCase()) return false;
  if (f.phase && m.phase.toLowerCase() !== f.phase.replace(/-/g, '').toLowerCase()) return false;
  if (f.etats.length && !f.etats.some((e) => NOMS_ETATS[e] === m.etat)) return false;
  if (f.equipes.length && !m.equipeIds.some((e) => f.equipes.includes(e))) return false;
  const systemes = m.systemes.filter((s) => retient(f, s));
  if (f.systemes.length && systemes.length === 0) return false;
  if (f.environnements.length) {
    const envs = new Set(f.environnements.map((e) => normaliser(e.trim())));
    if (!systemes.some((s) => s.environnements.some((e) => envs.has(normaliser(e.nom.trim()))))) return false;
  }
  if (f.echec && (f.systemes.length ? !systemes.some((s) => s.echecs > 0) : m.echecsSurvenus === 0)) return false;
  if (f.personne) {
    const roles = rolesDeLaPersonne(f, m);
    if (!roles) return false;
    if (f.roles.length && !roles.some((r) => f.roles.some((x) => NOMS_ROLES[x] === r))) return false;
  }
  if (f.du && m.jour < f.du) return false;
  if (f.au && m.jour > f.au) return false;
  const q = normaliser(f.recherche.trim());
  return (
    !q ||
    [m.outil, m.versionCible, m.objet, m.pilote ?? '', ...m.personnes, ...m.systemes.map((s) => s.nom)].some((t) =>
      normaliser(t).includes(q),
    )
  );
}

/** Par jour de MEP décroissant, puis création décroissante (comme le serveur). */
export const comparer = (a: Pick<MepResume, 'jour' | 'creeLe'>, b: Pick<MepResume, 'jour' | 'creeLe'>): number =>
  b.jour.localeCompare(a.jour) || Date.parse(b.creeLe) - Date.parse(a.creeLe);

export function filtrerMeps<T extends MepFiltrable>(meps: T[], f: FiltreMeps): T[] {
  return meps.filter((m) => correspond(f, m)).sort(comparer);
}

/** Filtre des modèles : seuls la recherche et l'équipe s'appliquent. */
export const filtreModeles = (f: FiltreMeps): FiltreMeps => ({
  ...FILTRE_VIDE,
  recherche: f.recherche,
  equipes: f.equipes,
});

/** Paramètres de la liste paginée et de ses comptes (`/api/meps/page`, `/api/meps/facettes`). */
export function parametresListe(f: FiltreMeps, modele: boolean): URLSearchParams {
  const p = ecrireFiltre(modele ? filtreModeles(f) : f);
  p.set('modele', String(modele));
  return p;
}

/** Adresse de l'export Excel de la liste : mêmes paramètres que la page. */
export function urlExportListe(f: FiltreMeps, modele: boolean): string {
  const p = ecrireFiltre(modele ? filtreModeles(f) : f);
  if (modele) p.set('modele', 'true');
  const q = p.toString();
  return `/api/meps/excel${q ? `?${q}` : ''}`;
}

/** Adresse de la liste filtrée sur un système (puce de système, historique). */
export const lienListeSysteme = (systeme: string, autres: Partial<FiltreMeps> = {}): string =>
  `/?${ecrireFiltre({ ...FILTRE_VIDE, ...autres, systemes: [systeme] }).toString()}`;

/**
 * Environnements proposés au filtre : ceux des systèmes filtrés (de tous les systèmes sinon), production à la fin.
 * `proposes` : ceux que le serveur a relevés dans les MEP (`/api/meps/facettes`), quand la liste n'est pas toute chargée.
 */
export function environnementsProposes(
  f: FiltreMeps,
  meps: Pick<MepResume, 'systemes'>[],
  catalogue: { id: string; environnements: { nom: string; production: boolean }[] }[],
  proposes: { nom: string; production: boolean }[] = [],
): { nom: string; production: boolean }[] {
  const vus = new Map<string, { nom: string; production: boolean }>();
  const ajouter = (e: { nom: string; production: boolean }) => {
    const cle = normaliser(e.nom.trim());
    const deja = vus.get(cle);
    vus.set(cle, { nom: deja?.nom ?? e.nom, production: (deja?.production ?? false) || e.production });
  };
  for (const c of catalogue) if (retient(f, { catalogueId: c.id, nom: '' })) c.environnements.forEach(ajouter);
  for (const m of meps) for (const s of m.systemes) if (retient(f, s)) s.environnements.forEach(ajouter);
  proposes.forEach(ajouter);
  return [...vus.values()].sort((a, b) => Number(a.production) - Number(b.production));
}

/** Systèmes saisis à la main dans les MEP (hors catalogue), par nom, sans doublon. */
export function systemesHorsCatalogue(meps: Pick<MepResume, 'systemes'>[]): string[] {
  const vus = new Map<string, string>();
  for (const m of meps)
    for (const s of m.systemes) if (s.catalogueId === null) vus.set(normaliser(s.nom.trim()), s.nom.trim());
  return [...vus.values()].sort((a, b) => a.localeCompare(b, 'fr'));
}

/** Systèmes du filtre en premier, puis les autres (ordre de la MEP). */
export const systemesTries = <S extends Pick<SystemeResume, 'catalogueId' | 'nom'>>(
  f: FiltreMeps,
  systemes: S[],
): S[] => (f.systemes.length ? [...systemes].sort((a, b) => Number(retient(f, b)) - Number(retient(f, a))) : systemes);
