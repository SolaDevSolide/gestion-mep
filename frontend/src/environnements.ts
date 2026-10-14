import type { EnvironnementEtape, Etape, Mep, Systeme } from './types';

/** Environnements proposés pour un système hors catalogue (saisie libre possible). */
export const ENVIRONNEMENTS = ['Développement', 'Recette', 'Préproduction', 'Production'] as const;

/** Environnement de production d'après son nom (« Production », « prod », « PROD-2 ») : valeur proposée par défaut. */
export const nomDeProduction = (nom: string): boolean => nom.trim().toLowerCase().startsWith('prod');

/** Nom court d'un environnement sur les cartes et l'en-tête : Préproduction → Préprod, Production → Prod. */
export function abregerEnvironnement(nom: string): string {
  const n = nom.trim();
  const cle = n.toLowerCase();
  if (cle === 'production') return 'Prod';
  if (cle === 'préproduction' || cle === 'preproduction') return 'Préprod';
  if (cle === 'développement' || cle === 'developpement') return 'Dév';
  return n;
}

export const memeEnvironnement = (a: string, b: string) => a.trim().toLowerCase() === b.trim().toLowerCase();

/** Vrai si l'un des systèmes de la MEP touche un environnement de production. */
export const toucheProduction = (mep: Pick<Mep, 'systemes'>): boolean =>
  mep.systemes.some((s) => s.environnements.some((e) => e.production));

/** Environnement d'une étape, avec sa marque « production » et s'il est encore choisi pour son système dans la MEP. */
export interface EnvironnementAffiche {
  nom: string;
  production: boolean;
  /** Retiré du système dans Préparation › Systèmes alors que l'étape le garde (affiché barré, erreur du plan). */
  retire: boolean;
}

/** Environnements d'une étape pour l'un de ses systèmes, dans l'ordre du système (les retirés à la fin). */
export function environnementsEtape(
  etape: Pick<Etape, 'environnements'>,
  systeme: Pick<Systeme, 'id' | 'environnements'>,
): EnvironnementAffiche[] {
  const liste = (etape.environnements ?? []).filter((e) => e.systemeId === systeme.id);
  const rang = (e: EnvironnementEtape) => {
    const i = systeme.environnements.findIndex((x) => memeEnvironnement(x.nom, e.nom));
    return i < 0 ? Number.MAX_SAFE_INTEGER : i;
  };
  return [...liste]
    .sort((a, b) => rang(a) - rang(b))
    .map((e) => {
      const choisi = systeme.environnements.find((x) => memeEnvironnement(x.nom, e.nom));
      return { nom: e.nom, production: choisi?.production ?? nomDeProduction(e.nom), retire: !choisi };
    });
}

/**
 * Environnements proposés par défaut quand une étape reçoit un système : son environnement s'il n'en a qu'un, sinon
 * aucun (on choisit).
 */
export function environnementsParDefaut(systeme: Pick<Systeme, 'id' | 'environnements'>): EnvironnementEtape[] {
  return systeme.environnements.length === 1 ? [{ systemeId: systeme.id, nom: systeme.environnements[0].nom }] : [];
}

/** Coche ou décoche un environnement d'un système dans la liste de l'étape. */
export function basculerEnvironnement(
  liste: EnvironnementEtape[],
  systemeId: string,
  nom: string,
): EnvironnementEtape[] {
  const present = liste.some((e) => e.systemeId === systemeId && memeEnvironnement(e.nom, nom));
  return present
    ? liste.filter((e) => !(e.systemeId === systemeId && memeEnvironnement(e.nom, nom)))
    : [...liste, { systemeId, nom }];
}
