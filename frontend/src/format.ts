import type { Criticite, EtatEtape, IssueMep, Phase, ResultatControle, StatutDisponibilite, StatutResolution } from './types';

export type Ton = 'ok' | 'run' | 'wait' | 'done' | 'danger' | 'warn' | 'later' | 'rollback';

export const LIBELLE_PHASE: Record<Phase, string> = {
  Preparation: 'Préparation',
  Execution: 'Exécution',
  Recette: 'Recette',
  RetourArriere: 'Retour arrière',
  Cloturee: 'Clôturée',
};

export const LIBELLE_ISSUE: Record<IssueMep, string> = {
  Reussie: 'Clôturée : réussie',
  Repliee: 'Clôturée : repliée',
};

export const LIBELLE_ETAT: Record<EtatEtape, string> = {
  AFaire: 'À faire',
  EnCours: 'En cours',
  Reussie: 'Réussie',
  Echec: 'Échec',
};

export const LIBELLE_CRITICITE: Record<Criticite, string> = {
  Faible: 'Faible',
  Moderee: 'Modérée',
  Elevee: 'Élevée',
  Bloquante: 'Bloquante',
};

export const LIBELLE_RESULTAT: Record<ResultatControle, string> = { Conforme: 'Conforme', Echec: 'Échec' };

export const LIBELLE_STATUT_RESOLUTION: Record<StatutResolution, string> = {
  EnCours: 'En cours',
  Reussie: 'Réussie',
  Echouee: 'Échouée',
};

export const LIBELLE_DISPONIBILITE: Record<StatutDisponibilite, string> = {
  PlanInvalide: 'Plan invalide',
  EcheanceARenseigner: 'Échéance à renseigner',
  RetourArriere: 'RETOUR ARRIÈRE',
  FicheACompleter: 'Fiche à compléter',
  InstructionsACompleter: 'Instructions à compléter',
  Attend: 'Attend',
  Terminee: 'Terminée',
  EnCours: 'En cours',
  ArretResolution: 'Arrêt / résolution',
  Disponible: 'Disponible',
  ResolutionEnCours: 'Résolution en cours',
  SiRetourArriere: 'Si retour arrière',
};

/** Ton (couleur sémantique) d'une disponibilité. */
export function tonDisponibilite(s: StatutDisponibilite): Ton {
  switch (s) {
    case 'Disponible': return 'ok';
    case 'EnCours': return 'run';
    case 'Terminee': return 'done';
    case 'ArretResolution': return 'danger';
    case 'ResolutionEnCours': return 'warn';
    case 'RetourArriere': return 'rollback';
    case 'SiRetourArriere': return 'later';
    case 'Attend': return 'wait';
    default: return 'warn';
  }
}

/** Famille d'un type d'événement du journal (préfixe avant le point). */
export const LIBELLE_FAMILLE_EVENEMENT: Record<string, string> = {
  mep: 'MEP',
  sauvegarde: 'Sauvegarde',
  participant: 'Personnes',
  'responsables-techniques': 'Responsables techniques',
  systeme: 'Systèmes',
  livrable: 'Livrables',
  etape: 'Étapes',
  procedure: 'Validation',
  phase: 'Phase',
  repli: 'Retour arrière',
  resolution: 'Résolutions',
  approbation: 'Approbations',
  gonogo: 'Go / no-go',
  recette: 'Recette',
  piece: 'Pièces jointes',
  bilan: 'Bilan',
};

export function familleEvenement(type: string): string {
  const i = type.indexOf('.');
  const famille = i < 0 ? type : type.slice(0, i);
  return famille === 'approbateurs' || famille === 'approbations' ? 'approbation' : famille;
}

export function initiales(nom: string | null | undefined): string {
  const mots = (nom ?? '').trim().split(/\s+/).filter(Boolean);
  if (mots.length === 0) return '?';
  if (mots.length === 1) return mots[0].slice(0, 2).toUpperCase();
  return (mots[0][0] + mots[mots.length - 1][0]).toUpperCase();
}

const dateHeure = new Intl.DateTimeFormat('fr-FR', { dateStyle: 'short', timeStyle: 'short' });
const heureSeule = new Intl.DateTimeFormat('fr-FR', { hour: '2-digit', minute: '2-digit', second: '2-digit' });
const jourLong = new Intl.DateTimeFormat('fr-FR', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' });
const dateSeule = new Intl.DateTimeFormat('fr-FR', { dateStyle: 'long' });
const dateCourte = new Intl.DateTimeFormat('fr-FR', { dateStyle: 'short' });

export function formatDateHeure(iso: string | null | undefined): string {
  if (!iso) return '—';
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? '—' : dateHeure.format(d);
}

export function formatHeureLocale(iso: string | null | undefined): string {
  if (!iso) return '—';
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? '—' : heureSeule.format(d);
}

export function formatJourLong(iso: string): string {
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? '—' : jourLong.format(d);
}

/** Date seule ("2026-10-09" ou ISO complet), sans décalage de fuseau pour le format DateOnly. */
export function formatDate(iso: string | null | undefined, long = false): string {
  if (!iso) return '—';
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(iso);
  const d = m ? new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3])) : new Date(iso);
  if (Number.isNaN(d.getTime())) return '—';
  return (long ? dateSeule : dateCourte).format(d);
}

export function formatHeure(t: string | null | undefined): string {
  return t ? t.slice(0, 5).replace(':', ' h ') : '—';
}

/** Valeur pour <input type="datetime-local"> en heure locale. */
export function versInputDateTime(iso: string | null | undefined): string {
  if (!iso) return '';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return '';
  const p = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}T${p(d.getHours())}:${p(d.getMinutes())}`;
}

export function depuisInputDateTime(v: string): string | null {
  if (!v) return null;
  const d = new Date(v);
  return Number.isNaN(d.getTime()) ? null : d.toISOString();
}

export type NiveauCompteARebours = 'aucun' | 'normal' | 'attention' | 'depasse';

export interface CompteARebours {
  niveau: NiveauCompteARebours;
  texte: string;
  msRestants: number | null;
}

/** Compte à rebours jusqu'à l'échéance : orange sous 1 h, rouge une fois dépassée. */
export function calculerCompteARebours(echeance: string | null | undefined, maintenant: number): CompteARebours {
  if (!echeance) return { niveau: 'aucun', texte: 'Échéance non renseignée', msRestants: null };
  const cible = new Date(echeance).getTime();
  if (Number.isNaN(cible)) return { niveau: 'aucun', texte: 'Échéance non renseignée', msRestants: null };
  const reste = cible - maintenant;
  if (reste <= 0) {
    return { niveau: 'depasse', texte: 'RETOUR ARRIÈRE OBLIGATOIRE', msRestants: reste };
  }
  const niveau: NiveauCompteARebours = reste < 3_600_000 ? 'attention' : 'normal';
  return { niveau, texte: formatDuree(reste), msRestants: reste };
}

export function formatDuree(ms: number): string {
  const total = Math.max(0, Math.floor(ms / 1000));
  const j = Math.floor(total / 86400);
  const h = Math.floor((total % 86400) / 3600);
  const m = Math.floor((total % 3600) / 60);
  const s = total % 60;
  const p = (n: number) => String(n).padStart(2, '0');
  const hms = `${p(h)} h ${p(m)} min ${p(s)} s`;
  return j > 0 ? `${j} j ${hms}` : hms;
}

/** Détecte des instructions qui ressemblent à des commandes (police à chasse fixe). */
export function ressembleCommande(texte: string): boolean {
  if (!texte.trim()) return false;
  const debut = /^\s*(\$|#!|sudo\s|cd\s|git\s|docker\s|kubectl\s|curl\s|wget\s|npm\s|dotnet\s|systemctl\s|service\s|psql\s|sqlcmd\s|mysql\s|ssh\s|scp\s|cp\s|mv\s|rm\s|chmod\s|chown\s|ls\s|cat\s|echo\s|export\s|\.\/|\/[\w.-]+\/|[A-Za-z]:\\|SELECT\s|UPDATE\s|INSERT\s|DELETE\s|ALTER\s|EXEC\s)/im;
  return debut.test(texte) || /\s--?[a-z][\w-]+/i.test(texte) && /\|\s*\w|&&|;\s*$/m.test(texte);
}

/** Libellés de rôles d'affichage pour un identifiant manquant. */
export function nomOuTiret(nom: string | null | undefined): string {
  return nom && nom.trim() ? nom : '—';
}

/** Libellé d'une durée en minutes : « 90 min » → « 1 h 30 ». */
export function formatMinutes(min: number | null | undefined): string {
  if (min === null || min === undefined) return '—';
  if (min < 60) return `${min} min`;
  const h = Math.floor(min / 60);
  const m = min % 60;
  return m === 0 ? `${h} h` : `${h} h ${String(m).padStart(2, '0')}`;
}

export const ENVIRONNEMENTS = ['Recette', 'Préproduction', 'Production'] as const;

/** Taille d'un fichier lisible (octets, Ko, Mo). */
export function formatTaille(octets: number): string {
  if (octets < 1024) return `${octets} o`;
  const nombre = new Intl.NumberFormat('fr-FR', { maximumFractionDigits: 1 });
  if (octets < 1024 * 1024) return `${nombre.format(octets / 1024)} Ko`;
  return `${nombre.format(octets / 1024 / 1024)} Mo`;
}
