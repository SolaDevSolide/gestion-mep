// Connexions aux API externes (#111) : libellés et mises en forme partagés par l'onglet, l'éditeur et la MEP.
import type { Ton } from '../../format';
import type {
  AppelResume,
  AuthentificationApi,
  EtatConnexion,
  FormatCorps,
  MethodeHttp,
  ModeleConnexion,
  OrigineAppel,
} from '../../types';

export const LIBELLE_ETAT_CONNEXION: Record<EtatConnexion, string> = {
  Active: 'Active',
  Desactivee: 'Désactivée',
  EnErreur: 'En erreur',
  JamaisAppelee: 'Jamais appelée',
};

export const TON_ETAT_CONNEXION: Record<EtatConnexion, Ton> = {
  Active: 'ok',
  Desactivee: 'later',
  EnErreur: 'danger',
  JamaisAppelee: 'wait',
};

export const LIBELLE_MODELE: Record<ModeleConnexion, string> = {
  Teams: 'Microsoft Teams',
  Slack: 'Slack',
  WebhookJson: 'Webhook JSON',
  Tickets: 'Outil de tickets',
  Libre: 'Requête libre',
};

export const METHODES: MethodeHttp[] = ['Post', 'Put', 'Patch', 'Get'];
export const methode = (m: MethodeHttp) => m.toUpperCase();

export const LIBELLE_AUTHENTIFICATION: Record<AuthentificationApi, string> = {
  Aucune: 'Aucune',
  Jeton: 'Jeton',
  Basic: 'Identifiant et mot de passe',
  CleEntete: 'Clé dans un en-tête',
  OAuth2: 'OAuth2',
};

/** Nom du secret de chaque authentification (champ et messages). */
export const NOM_SECRET: Record<AuthentificationApi, string> = {
  Aucune: '',
  Jeton: 'Jeton',
  Basic: 'Mot de passe',
  CleEntete: 'Valeur de la clé',
  OAuth2: 'Secret client',
};

export const LIBELLE_FORMAT: Record<FormatCorps, string> = {
  Json: 'JSON',
  Formulaire: 'Formulaire',
  Texte: 'Texte',
  Aucun: 'Aucun',
};

export const LIBELLE_ORIGINE: Record<OrigineAppel, string> = {
  Automatique: 'Automatique',
  Manuel: 'Envoi depuis la MEP',
  Essai: 'Essai',
  Renvoi: 'Renvoi',
};

const heure = new Intl.DateTimeFormat('fr-FR', { hour: '2-digit', minute: '2-digit' });
const heureSecondes = new Intl.DateTimeFormat('fr-FR', { hour: '2-digit', minute: '2-digit', second: '2-digit' });
const jourCourt = new Intl.DateTimeFormat('fr-FR', { day: '2-digit', month: '2-digit' });

/** « aujourd'hui 09:16 », sinon « 27/10 16:02 ». */
export function quandCourt(iso: string | null | undefined, maintenant = new Date()): string {
  if (!iso) return '—';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return '—';
  const memeJour = d.toDateString() === maintenant.toDateString();
  return memeJour ? `aujourd’hui ${heure.format(d)}` : `${jourCourt.format(d)} ${heure.format(d)}`;
}

/** « 09:16:24 » aujourd'hui, sinon « 27/10 16:02 ». */
export function quandListe(iso: string, maintenant = new Date()): string {
  const d = new Date(iso);
  return d.toDateString() === maintenant.toDateString()
    ? heureSecondes.format(d)
    : `${jourCourt.format(d)} ${heure.format(d)}`;
}

export const heureCourte = (iso: string | null | undefined) => (iso ? heure.format(new Date(iso)) : '—');

/** « 412 ms », « 10,0 s ». */
export function duree(ms: number): string {
  if (ms < 1000) return `${ms} ms`;
  return `${(ms / 1000).toLocaleString('fr-FR', { minimumFractionDigits: 1, maximumFractionDigits: 1 })} s`;
}

/** Résultat d'un appel : libellé et ton (« Réussi », « Échec, 3 tentatives », « Nouvelle tentative à 09:42 »). */
export function resultatAppel(a: AppelResume): { libelle: string; ton: Ton } {
  switch (a.etat) {
    case 'Reussi':
      return {
        libelle: a.origine === 'Essai' ? `Essai${a.demandePar ? ` par ${a.demandePar}` : ''}` : 'Réussi',
        ton: 'ok',
      };
    case 'Echec':
      return {
        libelle:
          a.origine === 'Essai'
            ? `Essai en échec${a.demandePar ? ` (${a.demandePar})` : ''}`
            : a.tentatives > 1
              ? `Échec, ${a.tentatives} tentatives`
              : 'Échec',
        ton: 'danger',
      };
    case 'EnCours':
      return { libelle: 'En cours', ton: 'run' };
    default:
      return {
        libelle:
          a.prochaineTentative && a.tentatives > 0
            ? `Nouvelle tentative à ${heureCourte(a.prochaineTentative)}`
            : 'En attente',
        ton: 'wait',
      };
  }
}

/** Code de réponse, ou un tiret quand l'outil n'a pas répondu. */
export const codeAppel = (a: Pick<AppelResume, 'code'>) => (a.code === null ? '—' : String(a.code));

/** « MEP · événement » d'un appel (« Essai » pour un essai sans MEP). */
export function contexteAppel(a: AppelResume): string {
  const evenement = a.origine === 'Essai' ? 'Essai' : a.libelleEvenement;
  return a.mepNom ? `${a.mepNom} · ${evenement}` : evenement;
}
