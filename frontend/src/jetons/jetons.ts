import type { JetonApi, PorteeJeton, TypeJeton } from '../types';

/** Jetons d'accès à l'API (#128) : règles d'affichage partagées par « Mon profil » et la console. */

const JOUR = 86_400_000;

/** Libellé court de chaque portée ; la description vient du serveur (`GET /api/jetons/portees`). */
export const LIBELLE_PORTEE: Record<string, string> = {
  'meps:lecture': 'Lire les MEP',
  'meps:ecriture': 'Modifier les MEP',
  execution: 'Exécuter',
  'catalogue:lecture': 'Lire le catalogue',
  'catalogue:ecriture': 'Modifier le catalogue',
  admin: 'Administration',
};

/** Rappel avant expiration : 7 jours pour un jeton personnel, 30 pour un jeton de service (outil à reconfigurer). */
export const RAPPEL_JOURS: Record<TypeJeton, number> = { Personnel: 7, Service: 30 };

/** Jetons révoqués ou expirés : encore montrés dans les listes pendant ce nombre de jours (toujours au journal). */
export const JOURS_VISIBLES_APRES_FIN = 30;

/** Jetons personnels actifs au plus par compte (règle du serveur). */
export const JETONS_ACTIFS_MAX = 20;

export const DUREE_DEFAUT_JOURS = 90;
export const DUREE_MAX_JOURS = 365;

/** Durées proposées à la création ; « date » ouvre un choix de date (1 an au plus). */
export const DUREES: { valeur: string; libelle: string }[] = [
  { valeur: '30', libelle: '30 jours' },
  { valeur: '90', libelle: '90 jours (par défaut)' },
  { valeur: '180', libelle: '180 jours' },
  { valeur: '365', libelle: '1 an' },
  { valeur: 'date', libelle: 'Jusqu’à une date…' },
];

/** Limites de débit proposées pour un jeton de service ('' : réglage global). */
export const DEBITS = [120, 1200, 3000, 6000];

/** Portées qu'un type de jeton peut porter, dans l'ordre du serveur. */
export const porteesPour = (portees: PorteeJeton[], type: TypeJeton) => portees.filter((p) => p.types.includes(type));

/** Jours entiers restants avant l'expiration (arrondis au-dessus : « dans 1 jour » jusqu'au dernier moment). */
export const joursRestants = (expireLe: string, maintenant: Date) =>
  Math.ceil((new Date(expireLe).getTime() - maintenant.getTime()) / JOUR);

export type Echeance =
  | { etat: 'revoque'; le: string }
  | { etat: 'expire'; le: string }
  | { etat: 'bientot'; jours: number }
  | { etat: 'normal'; jours: number };

/** Où en est un jeton : révoqué, expiré, à renouveler bientôt (rappel) ou valable. */
export function echeance(j: JetonApi, maintenant: Date): Echeance {
  if (j.revoqueLe) return { etat: 'revoque', le: j.revoqueLe };
  const jours = joursRestants(j.expireLe, maintenant);
  if (j.etat === 'Expire' || jours <= 0) return { etat: 'expire', le: j.expireLe };
  return jours <= RAPPEL_JOURS[j.type] ? { etat: 'bientot', jours } : { etat: 'normal', jours };
}

/** « Recréer » : proposé quand le jeton arrive à expiration ou a expiré (pas après une révocation, voulue). */
export const aRecreer = (j: JetonApi, maintenant: Date) => {
  const e = echeance(j, maintenant).etat;
  return e === 'bientot' || e === 'expire';
};

/** Montré dans une liste : actif, ou révoqué / expiré depuis moins de 30 jours. */
export function visible(j: JetonApi, maintenant: Date): boolean {
  const fin = j.revoqueLe ?? (j.etat === 'Actif' ? null : j.expireLe);
  return fin === null || maintenant.getTime() - new Date(fin).getTime() <= JOURS_VISIBLES_APRES_FIN * JOUR;
}

/** Actifs d'abord (dans l'ordre du serveur : du plus récent au plus ancien), puis les autres. */
export const trier = (jetons: JetonApi[]) => [
  ...jetons.filter((j) => j.etat === 'Actif'),
  ...jetons.filter((j) => j.etat !== 'Actif'),
];

export const dansJours = (jours: number) => (jours <= 1 ? 'dans 1 jour' : `dans ${jours} jours`);

const heure = new Intl.DateTimeFormat('fr-FR', { hour: '2-digit', minute: '2-digit' });
const dateCourte = new Intl.DateTimeFormat('fr-FR', { dateStyle: 'short' });

/** Dernière utilisation, à 5 minutes près : « il y a 4 min », « il y a 2 h », « hier », « 17/10/2026 », « jamais ». */
export function quandUtilise(iso: string | null | undefined, maintenant: Date): string {
  if (!iso) return 'jamais';
  const d = new Date(iso);
  const minutes = Math.floor((maintenant.getTime() - d.getTime()) / 60_000);
  if (minutes < 1) return 'à l’instant';
  if (minutes < 60) return `il y a ${minutes} min`;
  if (minutes < 12 * 60) return `il y a ${Math.floor(minutes / 60)} h`;
  if (d.toDateString() === maintenant.toDateString()) return `aujourd’hui ${heure.format(d)}`;
  const hier = new Date(maintenant);
  hier.setDate(hier.getDate() - 1);
  if (d.toDateString() === hier.toDateString()) return 'hier';
  return dateCourte.format(d);
}

/** Date au format des champs `<input type="date">` (jour local). */
export function versChampDate(d: Date): string {
  const z = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${z(d.getMonth() + 1)}-${z(d.getDate())}`;
}

/** Durée en jours jusqu'à une date choisie (fin de journée locale), ou null si elle est passée ou trop lointaine. */
export function joursJusquA(champ: string, maintenant: Date): number | null {
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(champ);
  if (!m) return null;
  const fin = new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3]), 23, 59, 59);
  const jours = Math.floor((fin.getTime() - maintenant.getTime()) / JOUR);
  return jours >= 1 && jours <= DUREE_MAX_JOURS ? jours : null;
}

/** Date d'expiration d'un jeton créé maintenant pour `jours` jours. */
export const expirationPour = (jours: number, maintenant: Date) => new Date(maintenant.getTime() + jours * JOUR);

/**
 * Login du compte technique créé avec un jeton de service, comme le serveur le calcule : `svc-` suivi du nom en minuscules,
 * sans accents, mots séparés par des tirets (40 caractères au plus). Le serveur ajoute -2, -3… s'il est déjà pris.
 */
export function loginCompteTechnique(nom: string): string {
  const slug = nom
    .normalize('NFD')
    .replace(/\p{Mn}/gu, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, 40)
    .replace(/-+$/, '');
  return `svc-${slug || 'jeton'}`;
}

/** Exemple d'appel montré à la création, avec l'adresse de cette instance. */
export function exempleCurl(origine: string, portees: string[]): string {
  if (portees.includes('meps:lecture') || portees.includes('meps:ecriture'))
    return `# Les MEP en préparation\ncurl -H "Authorization: Bearer $GMEP_JETON" \\\n  "${origine}/api/meps?modele=false&phase=Preparation"`;
  if (portees.includes('catalogue:lecture') || portees.includes('catalogue:ecriture'))
    return `# Les systèmes du catalogue\ncurl -H "Authorization: Bearer $GMEP_JETON" \\\n  "${origine}/api/catalogue/systemes"`;
  return `# Au nom de qui agit ce jeton\ncurl -H "Authorization: Bearer $GMEP_JETON" \\\n  "${origine}/api/auth/moi"`;
}

/** Comptes techniques connus par leurs jetons de service (pour « Utiliser un compte technique existant »). */
export function comptesTechniques(jetons: JetonApi[]): { id: string; login: string; nom: string }[] {
  const vus = new Map<string, { id: string; login: string; nom: string }>();
  for (const j of jetons)
    if (j.type === 'Service' && !vus.has(j.compteId))
      vus.set(j.compteId, { id: j.compteId, login: j.compteLogin, nom: j.compteNom });
  return [...vus.values()].sort((a, b) => a.login.localeCompare(b.login, 'fr'));
}
