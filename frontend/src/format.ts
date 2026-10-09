import type { Criticite, EtatEtape, Phase, StatutDisponibilite } from './types';

export const LIBELLE_PHASE: Record<Phase, string> = {
  Preparation: 'Préparation',
  Execution: 'Exécution',
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
};

/** Famille de couleur d'une disponibilité (classe CSS dispo-xxx). */
export function tonDisponibilite(s: StatutDisponibilite): 'ok' | 'attend' | 'cours' | 'danger' | 'fini' | 'info' {
  switch (s) {
    case 'Disponible': return 'ok';
    case 'EnCours': return 'cours';
    case 'Terminee': return 'fini';
    case 'ArretResolution':
    case 'RetourArriere': return 'danger';
    case 'Attend': return 'attend';
    default: return 'info';
  }
}

const dateHeure = new Intl.DateTimeFormat('fr-FR', { dateStyle: 'short', timeStyle: 'short' });
const dateSeule = new Intl.DateTimeFormat('fr-FR', { dateStyle: 'long' });
const dateCourte = new Intl.DateTimeFormat('fr-FR', { dateStyle: 'short' });

export function formatDateHeure(iso: string | null | undefined): string {
  if (!iso) return '—';
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? '—' : dateHeure.format(d);
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
