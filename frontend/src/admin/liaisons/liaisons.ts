import type { Ton } from '../../format';
import type {
  ApplicationLiaison,
  CatalogueLiaison,
  EchangeLiaison,
  LiaisonNoosphere,
  SuiviApplicationSaisie,
  SuiviLiaisonSaisie,
} from '../../types';

/** Période de relecture du catalogue par le serveur (TachesLiaisons.Periode). */
export const PERIODE_SYNCHRO_MINUTES = 15;

/** Libellés des capacités accordées par Noosphere (valeurs du contrat). */
export const LIBELLE_CAPACITE: Record<string, string> = {
  'catalog:read': 'Lire le catalogue',
  'notices:send': 'Envoyer les avis de changement',
  'deployments:receive': 'Recevoir les MEP',
};

export const libelleCapacite = (c: string) => LIBELLE_CAPACITE[c] ?? c;

/** Cycle de vie d'une application de Noosphere. */
export const LIBELLE_CYCLE: Record<string, string> = {
  planned: 'Prévue',
  live: 'En service',
  retiring: 'En retrait',
  retired: 'Retirée',
};

export const libelleCycle = (c: string) => LIBELLE_CYCLE[c.toLowerCase()] ?? c;

/** Mention sous une application qui n'est pas en service (« Retirée dans Noosphere »). */
export function mentionCycle(c: string): string | null {
  switch (c.toLowerCase()) {
    case 'retired':
      return 'Retirée dans Noosphere';
    case 'retiring':
      return 'En retrait dans Noosphere';
    case 'planned':
      return 'Prévue dans Noosphere';
    default:
      return null;
  }
}

/**
 * Liaison montrée par l'onglet : la liaison en service (en attente ou active), sinon aucune. Les liaisons coupées restent
 * en historique.
 */
export function liaisonCourante(liaisons: LiaisonNoosphere[]): LiaisonNoosphere | null {
  return liaisons.find((l) => l.etat !== 'Coupee') ?? null;
}

/** Les quatre éléments de l'empreinte (« orbit cedar 0712 lumen »). */
export const motsEmpreinte = (empreinte: string) => empreinte.split(/\s+/).filter(Boolean);

/** État de la confirmation côté Noosphere, d'après son dernier `/status`. */
export function etatPair(l: LiaisonNoosphere): { libelle: string; ton: Ton } {
  if (l.confirmeeParNoosphere === true) return { libelle: 'Confirmée par Noosphere', ton: 'ok' };
  if (l.confirmeeParNoosphere === false) return { libelle: 'En attente de confirmation dans Noosphere', ton: 'warn' };
  return { libelle: 'Pas encore connu', ton: 'wait' };
}

/** Libellé d'un échange du journal, selon l'opération du protocole et le sens. */
export function libelleOperation(e: Pick<EchangeLiaison, 'operation' | 'sens' | 'resultat'>): string {
  if (e.resultat === 'refuse') return 'Requête refusée';
  const entrant = e.sens === 'entrant';
  switch (e.operation) {
    case 'pair':
      return 'Appairage';
    case 'status':
      return entrant ? 'État demandé par Noosphere' : 'État de Noosphere relu';
    case 'catalog':
      return 'Lecture du catalogue';
    case 'notices':
      return 'Avis de changement reçu';
    case 'deployments':
      return 'Envoi des MEP';
    case 'rekey':
      return entrant ? 'Clés de Noosphere renouvelées' : 'Renouvellement des clés';
    case 'unpair':
      return entrant ? 'Coupure par Noosphere' : 'Coupure';
    default:
      return e.operation;
  }
}

/** Résultat d'un échange : libellé et ton de sa pastille. */
export function resultatEchange(r: string): { libelle: string; ton: Ton } {
  switch (r) {
    case 'ok':
      return { libelle: 'Réussi', ton: 'ok' };
    case 'echec':
      return { libelle: 'Échec', ton: 'warn' };
    case 'refuse':
      return { libelle: 'Refusée', ton: 'danger' };
    case 'demo':
      return { libelle: 'Démonstration', ton: 'later' };
    default:
      return { libelle: r, ton: 'wait' };
  }
}

/* ------------------------------------------------------------------ Choix des applications suivies */

/** Saisie du suivi : domaines suivis en entier et applications suivies (toutes, y compris celles des domaines). */
export interface ChoixSuivi {
  domaines: string[];
  applications: string[];
}

export const estRetiree = (a: Pick<ApplicationLiaison, 'cycle'>) => a.cycle.toLowerCase() === 'retired';

/** Choix de départ : ce que le serveur suit déjà. */
export function choixInitial(c: CatalogueLiaison): ChoixSuivi {
  return {
    domaines: c.domaines.filter((d) => d.suivi).map((d) => d.id),
    // Une application mise de côté par la synchronisation (nom proche d'un système local) est déjà choisie : il ne reste
    // qu'à la rapprocher du catalogue.
    applications: c.applications.filter((a) => a.suivie || a.aRapprocher).map((a) => a.id),
  };
}

/** Applications mises de côté par la synchronisation, en attente de « Rattacher » ou « Créer un système ». */
export const aRapprocher = (c: CatalogueLiaison) => c.applications.filter((a) => a.aRapprocher && !a.suivie);

/** Applications d'un domaine. */
export const applicationsDu = (c: CatalogueLiaison, domaineId: string) =>
  c.applications.filter((a) => a.domaineId === domaineId);

/** Coche ou décoche un domaine entier : toutes ses applications (hors retirées, à l'ajout) suivent. */
export function basculerDomaine(
  c: CatalogueLiaison,
  choix: ChoixSuivi,
  domaineId: string,
  suivre: boolean,
): ChoixSuivi {
  const apps = applicationsDu(c, domaineId);
  const ids = new Set(apps.map((a) => a.id));
  const autres = choix.applications.filter((id) => !ids.has(id));
  return suivre
    ? {
        domaines: [...choix.domaines.filter((d) => d !== domaineId), domaineId],
        applications: [...autres, ...apps.filter((a) => !estRetiree(a) || a.suivie).map((a) => a.id)],
      }
    : { domaines: choix.domaines.filter((d) => d !== domaineId), applications: autres };
}

/**
 * Coche ou décoche une application. Décocher une application d'un domaine suivi en entier fait passer ce domaine en
 * choix une par une (ses autres applications restent suivies).
 */
export function basculerApplication(
  c: CatalogueLiaison,
  choix: ChoixSuivi,
  appId: string,
  suivre: boolean,
): ChoixSuivi {
  const app = c.applications.find((a) => a.id === appId);
  const sans = choix.applications.filter((id) => id !== appId);
  if (suivre) return { ...choix, applications: [...sans, appId] };
  return {
    domaines: app?.domaineId ? choix.domaines.filter((d) => d !== app.domaineId) : choix.domaines,
    applications: sans,
  };
}

/** État d'un domaine dans la saisie : suivi en entier, en partie (une par une) ou pas du tout. */
export function etatDomaine(
  c: CatalogueLiaison,
  choix: ChoixSuivi,
  domaineId: string,
): { coche: boolean | 'indetermine'; suivies: number; total: number; entier: boolean } {
  const apps = applicationsDu(c, domaineId);
  const suivies = apps.filter((a) => choix.applications.includes(a.id)).length;
  const entier = choix.domaines.includes(domaineId);
  return {
    coche: entier ? true : suivies > 0 ? 'indetermine' : false,
    suivies,
    total: apps.length,
    entier,
  };
}

/** « Finance (6), Customer (5) et Event bus » : résumé de la saisie. */
export function resumeChoix(c: CatalogueLiaison, choix: ChoixSuivi): string {
  const parties: string[] = [];
  const enDomaine = new Set<string>();
  for (const d of c.domaines) {
    if (!choix.domaines.includes(d.id)) continue;
    const n = applicationsDu(c, d.id).filter((a) => choix.applications.includes(a.id));
    n.forEach((a) => enDomaine.add(a.id));
    parties.push(`${d.nom} (${n.length})`);
  }
  for (const a of c.applications) if (choix.applications.includes(a.id) && !enDomaine.has(a.id)) parties.push(a.nom);
  if (parties.length <= 1) return parties.join('');
  return `${parties.slice(0, -1).join(', ')} et ${parties[parties.length - 1]}`;
}

/** Choix de rapprochement d'une application nouvellement suivie : rattacher à un système, ou en créer un. */
export type Rapprochement = { type: 'rattacher'; systemeId: string; nom: string } | { type: 'creer' };

/**
 * Rapprochements de départ des applications nouvellement suivies : le système du même nom est proposé (à confirmer en
 * validant), un nom seulement proche attend un choix (absent de la table), sans système proche on en crée un.
 */
export function rapprochementsInitiaux(nouvelles: ApplicationLiaison[]): Record<string, Rapprochement> {
  const r: Record<string, Rapprochement> = {};
  for (const a of nouvelles) {
    const meme = a.suggestions.find((s) => s.memeNom);
    if (meme) r[a.id] = { type: 'rattacher', systemeId: meme.id, nom: meme.nom };
    else if (a.suggestions.length === 0) r[a.id] = { type: 'creer' };
  }
  return r;
}

/** Saisie envoyée au serveur : domaines, puis chaque application dont le choix change. */
export function saisieSuivi(
  c: CatalogueLiaison,
  choix: ChoixSuivi,
  rapprochements: Record<string, Rapprochement>,
): SuiviLiaisonSaisie {
  const applications: SuiviApplicationSaisie[] = [];
  for (const a of c.applications) {
    const suivie = choix.applications.includes(a.id);
    if (suivie && !a.suivie) {
      const r = rapprochements[a.id];
      applications.push(
        r?.type === 'rattacher'
          ? { applicationId: a.id, suivre: true, systemeId: r.systemeId }
          : { applicationId: a.id, suivre: true, systemeId: null, creer: true },
      );
    } else if (!suivie && (a.suivie || a.aRapprocher))
      applications.push({ applicationId: a.id, suivre: false, systemeId: null });
  }
  return { domainesSuivis: choix.domaines, applications };
}
