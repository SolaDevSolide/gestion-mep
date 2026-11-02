// Calendrier des MEP et périodes de gel (#129) : calculs de dates et de placement, sans affichage. Les instants viennent
// de l'API en UTC ; le calendrier les place dans le fuseau de l'instance (renvoyé par GET /api/calendrier), comme le serveur
// calcule ses jours « du » et « au ».
import { depuisInputDateTimeFuseau, fuseauValide } from './format';
import type { ConflitCalendrier, ElementCalendrier, OccurrenceGel, Phase, RecurrenceGel } from './types';

export type VueCalendrier = 'mois' | 'semaine' | 'liste';
export const VUES_CALENDRIER: VueCalendrier[] = ['mois', 'semaine', 'liste'];

/** Fuseau de l'instance tant que le serveur n'a pas répondu (même valeur par défaut que le serveur). */
export const FUSEAU_INSTANCE = 'Europe/Paris';

const MOIS = [
  'janvier',
  'février',
  'mars',
  'avril',
  'mai',
  'juin',
  'juillet',
  'août',
  'septembre',
  'octobre',
  'novembre',
  'décembre',
];
export const JOURS_COURTS = ['Lun', 'Mar', 'Mer', 'Jeu', 'Ven', 'Sam', 'Dim'];
const JOURS_LONGS = ['lundi', 'mardi', 'mercredi', 'jeudi', 'vendredi', 'samedi', 'dimanche'];

const p2 = (n: number) => String(n).padStart(2, '0');

/** Jour « AAAA-MM-JJ » d'un objet Date lu en UTC (arithmétique de jours sans fuseau). */
const versJour = (d: Date) => `${d.getUTCFullYear()}-${p2(d.getUTCMonth() + 1)}-${p2(d.getUTCDate())}`;
const lireJour = (j: string) => new Date(`${j}T00:00:00Z`);

export const estJour = (v: string | null | undefined): v is string =>
  !!v && /^\d{4}-\d{2}-\d{2}$/.test(v) && !Number.isNaN(lireJour(v).getTime());

export function ajouterJours(jour: string, n: number): string {
  const d = lireJour(jour);
  d.setUTCDate(d.getUTCDate() + n);
  return versJour(d);
}

/** Même jour du mois décalé de `n` mois (ramené au dernier jour d'un mois plus court). */
export function ajouterMois(jour: string, n: number): string {
  const d = lireJour(jour);
  const cible = new Date(Date.UTC(d.getUTCFullYear(), d.getUTCMonth() + n, 1));
  const dernier = new Date(Date.UTC(cible.getUTCFullYear(), cible.getUTCMonth() + 1, 0)).getUTCDate();
  cible.setUTCDate(Math.min(d.getUTCDate(), dernier));
  return versJour(cible);
}

/** 0 = lundi … 6 = dimanche. */
export const jourSemaine = (jour: string) => (lireJour(jour).getUTCDay() + 6) % 7;
export const debutSemaine = (jour: string) => ajouterJours(jour, -jourSemaine(jour));
export const debutMois = (jour: string) => `${jour.slice(0, 8)}01`;
export const finMois = (jour: string) => ajouterJours(ajouterMois(debutMois(jour), 1), -1);
export const numeroJour = (jour: string) => Number(jour.slice(8, 10));

/** Composantes d'un instant dans un fuseau. */
function composantes(iso: string, fuseau: string) {
  const parts = new Intl.DateTimeFormat('en-GB', {
    timeZone: fuseauValide(fuseau),
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    hourCycle: 'h23',
  }).formatToParts(new Date(iso));
  const v = (t: string) => parts.find((x) => x.type === t)?.value ?? '00';
  return { jour: `${v('year')}-${v('month')}-${v('day')}`, heure: `${v('hour')}:${v('minute')}` };
}

/** Jour local (AAAA-MM-JJ) d'un instant dans le fuseau. */
export const jourLocal = (iso: string, fuseau: string) => composantes(iso, fuseau).jour;
/** Heure locale (HH:MM) d'un instant dans le fuseau. */
export const heureLocale = (iso: string, fuseau: string) => composantes(iso, fuseau).heure;
/** Heures décimales (0 à 24) d'un instant dans le fuseau. */
export function heuresDecimales(iso: string, fuseau: string): number {
  const [h, m] = heureLocale(iso, fuseau).split(':').map(Number);
  return h + m / 60;
}

/** Minuit local de ce jour, en instant (ms). */
export function minuit(jour: string, fuseau: string): number {
  const iso = depuisInputDateTimeFuseau(`${jour}T00:00`, fuseau);
  return iso ? Date.parse(iso) : lireJour(jour).getTime();
}

/** Plage de jours (incluse) à demander au serveur pour une vue. */
export function plageVue(vue: VueCalendrier, ancre: string): { du: string; au: string } {
  if (vue === 'semaine') {
    const du = debutSemaine(ancre);
    return { du, au: ajouterJours(du, 6) };
  }
  if (vue === 'liste') return { du: debutMois(ancre), au: finMois(ancre) };
  const du = debutSemaine(debutMois(ancre));
  return { du, au: ajouterJours(du, 41) };
}

/** Les 42 jours de la grille du mois (6 semaines, du lundi au dimanche). */
export function grilleMois(ancre: string): string[] {
  const du = debutSemaine(debutMois(ancre));
  return Array.from({ length: 42 }, (_, i) => ajouterJours(du, i));
}

export function joursSemaine(ancre: string): string[] {
  const du = debutSemaine(ancre);
  return Array.from({ length: 7 }, (_, i) => ajouterJours(du, i));
}

/** Période suivante (sens 1) ou précédente (-1) de la vue. */
export const deplacer = (vue: VueCalendrier, ancre: string, sens: 1 | -1): string =>
  vue === 'semaine' ? ajouterJours(ancre, 7 * sens) : ajouterMois(debutMois(ancre), sens);

const nomMois = (jour: string) => MOIS[Number(jour.slice(5, 7)) - 1];
const majuscule = (s: string) => s.charAt(0).toUpperCase() + s.slice(1);

/** « novembre 2026 ». */
export const moisAnnee = (jour: string) => `${nomMois(jour)} ${jour.slice(0, 4)}`;

/** « 1er décembre », « 10 novembre ». */
export const jourMois = (jour: string) => `${numeroJour(jour) === 1 ? '1er' : numeroJour(jour)} ${nomMois(jour)}`;

/** « mardi 10 novembre ». */
export const jourComplet = (jour: string) => `${JOURS_LONGS[jourSemaine(jour)]} ${jourMois(jour)}`;

/** « ven. 27/11 ». */
export const jourAbrege = (jour: string) =>
  `${JOURS_LONGS[jourSemaine(jour)].slice(0, 3)}. ${jour.slice(8, 10)}/${jour.slice(5, 7)}`;

/** Titre de la période affichée. */
export function titreVue(vue: VueCalendrier, ancre: string, aujourdhui: string): string {
  if (vue === 'semaine') {
    const [du, au] = [debutSemaine(ancre), ajouterJours(debutSemaine(ancre), 6)];
    return du.slice(0, 7) === au.slice(0, 7)
      ? `Semaine du ${numeroJour(du)} au ${numeroJour(au)} ${moisAnnee(au)}`
      : `Semaine du ${jourMois(du)} au ${jourMois(au)} ${au.slice(0, 4)}`;
  }
  const titre = majuscule(moisAnnee(ancre));
  return vue === 'liste' && ancre.slice(0, 7) === aujourdhui.slice(0, 7) ? `${titre} · à venir` : titre;
}

/** Jour où placer une MEP dans la vue : le jour de son début, ramené au premier jour affiché s'il est avant. */
export function jourDePlacement(m: Pick<ElementCalendrier, 'debut'>, fuseau: string, premierJour: string): string {
  const j = jourLocal(m.debut, fuseau);
  return j < premierJour ? premierJour : j;
}

/** MEP par jour de placement (dans l'ordre reçu : par début). */
export function mepsParJour<T extends Pick<ElementCalendrier, 'debut'>>(
  meps: T[],
  fuseau: string,
  premierJour: string,
): Map<string, T[]> {
  const parJour = new Map<string, T[]>();
  for (const m of meps) {
    const j = jourDePlacement(m, fuseau, premierJour);
    parJour.set(j, [...(parJour.get(j) ?? []), m]);
  }
  return parJour;
}

/** Occurrences de gel qui touchent ce jour local. */
export function gelsDuJour(gels: OccurrenceGel[], jour: string, fuseau: string): OccurrenceGel[] {
  const debut = minuit(jour, fuseau);
  const fin = minuit(ajouterJours(jour, 1), fuseau);
  return gels.filter((g) => Date.parse(g.debut) < fin && Date.parse(g.fin) > debut);
}

/** Dernier jour (local) touché par une occurrence : la fin est exclue, un gel qui finit à minuit s'arrête la veille. */
export function dernierJourGel(g: Pick<OccurrenceGel, 'fin'>, fuseau: string): string {
  return jourLocal(new Date(Date.parse(g.fin) - 1).toISOString(), fuseau);
}

/** « 26/11 → 30/11 », ou avec les heures quand le gel ne commence ou ne finit pas à minuit. */
export function plageGel(g: Pick<OccurrenceGel, 'debut' | 'fin'>, fuseau: string): string {
  const c = (iso: string) => composantes(iso, fuseau);
  const d = c(g.debut);
  const f = c(g.fin);
  const jj = (j: string) => `${j.slice(8, 10)}/${j.slice(5, 7)}`;
  if (d.heure === '00:00' && f.heure === '00:00') return `${jj(d.jour)} → ${jj(dernierJourGel(g, fuseau))}`;
  return `${jj(d.jour)} ${d.heure} → ${jj(f.jour)} ${f.heure}`;
}

/** Horaire d'une MEP : « 21:00 → 23:30 », « journée entière », « depuis 20:00 » (lancée, sans fin). */
export function horaire(
  m: Pick<ElementCalendrier, 'debut' | 'fin' | 'journeeEntiere' | 'enCours'>,
  fuseau: string,
): string {
  if (m.journeeEntiere) return 'journée entière';
  const debut = heureLocale(m.debut, fuseau);
  if (!m.fin) return m.enCours ? `depuis ${debut}` : debut;
  return `${debut} → ${heureLocale(m.fin, fuseau)}`;
}

/**
 * Colonnes des MEP d'un même jour (vue semaine) : deux MEP qui se chevauchent sont côte à côte. `colonne` : la sienne ;
 * `colonnes` : le nombre de colonnes de son groupe de chevauchements.
 */
export function colonnes<T extends Pick<ElementCalendrier, 'id' | 'debut' | 'fin' | 'enCours'>>(
  meps: T[],
  maintenant: number,
): Map<string, { colonne: number; colonnes: number }> {
  const fin = (m: T) =>
    Math.max(
      m.fin ? Date.parse(m.fin) : m.enCours ? maintenant : Date.parse(m.debut) + 30 * 60_000,
      Date.parse(m.debut) + 30 * 60_000,
    );
  const tries = [...meps].sort((a, b) => Date.parse(a.debut) - Date.parse(b.debut));
  const resultat = new Map<string, { colonne: number; colonnes: number }>();
  let groupe: T[] = [];
  let finGroupe = -Infinity;
  let finsColonnes: number[] = [];
  const clore = () => {
    const n = finsColonnes.length;
    for (const m of groupe) resultat.set(m.id, { colonne: resultat.get(m.id)!.colonne, colonnes: n });
    groupe = [];
    finsColonnes = [];
  };
  for (const m of tries) {
    const debut = Date.parse(m.debut);
    if (debut >= finGroupe) clore();
    let c = finsColonnes.findIndex((f) => f <= debut);
    if (c < 0) c = finsColonnes.push(0) - 1;
    finsColonnes[c] = fin(m);
    resultat.set(m.id, { colonne: c, colonnes: 0 });
    groupe.push(m);
    finGroupe = Math.max(finGroupe, fin(m));
  }
  clore();
  return resultat;
}

export const LIBELLE_RECURRENCE: Record<RecurrenceGel, string> = {
  Aucune: 'Ponctuelle',
  Hebdomadaire: 'Chaque semaine',
  Mensuelle: 'Chaque mois, même jour',
  FinDeMois: 'Chaque fin de mois',
  Annuelle: 'Chaque année',
};

/** Phrase du gel d'une MEP : en préparation, ce qu'il en est du Go. */
export function texteGelMep(g: Pick<OccurrenceGel, 'nom' | 'derogation'>, phase: Phase): string {
  if (phase !== 'Preparation') return g.nom;
  switch (g.derogation) {
    case 'Accordee':
      return `${g.nom} · dérogation accordée`;
    case 'Demandee':
      return `${g.nom} · dérogation demandée`;
    case 'Refusee':
      return `${g.nom} · dérogation refusée, Go bloqué`;
    default:
      return `${g.nom} · Go bloqué sans dérogation`;
  }
}

/** « Conflit sur WEB avec Portail adhérents 5.2.1 (21:30 → 23:00) ». */
export function texteConflit(c: ConflitCalendrier, fuseau: string): string {
  const quand = horaireConflit(c, fuseau);
  const titre = c.versionCible.trim() ? `${c.outil} ${c.versionCible}` : c.outil;
  return `Conflit sur ${c.systemes.join(', ')} avec ${titre} (${quand})`;
}

/** Horaire de l'autre MEP d'un conflit : « 21:30 → 23:00 », ou « dès 21:30 » sans fin connue. */
export const horaireConflit = (c: Pick<ConflitCalendrier, 'debut' | 'fin'>, fuseau: string): string =>
  c.fin ? `${heureLocale(c.debut, fuseau)} → ${heureLocale(c.fin, fuseau)}` : `dès ${heureLocale(c.debut, fuseau)}`;

/** Fin d'un gel en clair : « libre à partir du mardi 1er décembre », ou « fin du gel : mercredi 2 décembre à 08:00 ». */
export function finDuGel(g: Pick<OccurrenceGel, 'fin'>, fuseau: string): string {
  const jour = jourComplet(jourLocal(g.fin, fuseau));
  const heure = heureLocale(g.fin, fuseau);
  return heure === '00:00' ? `Libre à partir du ${jour}` : `Fin du gel : ${jour} à ${heure}`;
}
