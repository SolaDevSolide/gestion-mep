import { useEffect, useSyncExternalStore } from 'react';
import { api } from './api';
import type { CompteAnnuaire, Equipe, ResolutionType, SystemeCatalogue } from './types';

/**
 * Données partagées entre écrans (référentiels, objets d'un système, journal d'une MEP) : une seule requête par clé à la
 * fois, quel que soit le nombre de composants qui la demandent, et la dernière réponse gardée en mémoire (un onglet
 * rouvert s'affiche aussitôt, puis se met à jour). Une erreur n'efface pas une réponse déjà reçue ; sans réponse, elle
 * est exposée telle quelle (`ErreurChargement`), jamais remplacée par une liste vide.
 */
interface Entree {
  donnees: unknown;
  /** Une réponse a été reçue. */
  recue: boolean;
  erreur: unknown;
  enCours: boolean;
  /** Relecture demandée pendant une lecture en cours (la donnée a changé entre-temps) : relancée à sa fin. */
  relancer: boolean;
  /** Dernière valeur de `relire` (version de la MEP) pour laquelle la donnée a été relue. */
  marque?: unknown;
  /** Heure de la dernière réponse ou de la dernière erreur (`Date.now()`). */
  recueLe: number;
}

/** Une réponse plus récente que ce délai sert telle quelle à un nouveau composant (sections montées après l'onglet). */
const FRAICHEUR_MS = 10_000;

const entrees = new Map<string, Entree>();
const abonnes = new Map<string, number>();
const ecouteurs = new Set<() => void>();
let version = 0;

function notifier() {
  version++;
  for (const f of ecouteurs) f();
}

/**
 * Lance la lecture de `cle`. Une lecture déjà en cours sert à tous ; `forcer` (donnée changée, « Réessayer ») en
 * relance une autre à sa fin. `charger` reçoit la dernière réponse gardée (null avant la première) : une donnée lue par
 * morceaux (le journal d'une MEP) n'en demande que la suite.
 */
function demander<T>(cle: string, charger: (precedent: T | null) => Promise<T>, forcer = false) {
  let e = entrees.get(cle);
  if (!e) {
    e = { donnees: null, recue: false, erreur: null, enCours: false, relancer: false, recueLe: 0 };
    entrees.set(cle, e);
  }
  if (e.enCours) {
    if (forcer) e.relancer = true;
    return;
  }
  const courante = e;
  courante.enCours = true;
  courante.relancer = false;
  notifier();
  let promesse: Promise<unknown>;
  try {
    promesse = charger(courante.recue ? (courante.donnees as T) : null);
  } catch (err) {
    promesse = Promise.reject(err);
  }
  const terminer = () => {
    courante.enCours = false;
    notifier();
    if (courante.relancer && entrees.get(cle) === courante) demander(cle, charger);
  };
  promesse.then(
    (d) => {
      courante.donnees = d;
      courante.recue = true;
      courante.recueLe = Date.now();
      courante.erreur = null;
      terminer();
    },
    (err: unknown) => {
      courante.erreur = err ?? new Error('Chargement impossible.');
      courante.recueLe = Date.now();
      terminer();
    },
  );
}

function abonner(f: () => void) {
  ecouteurs.add(f);
  return () => {
    ecouteurs.delete(f);
  };
}
const lireVersion = () => version;

/**
 * Pour les tests (appelée après chaque test par test/setup.ts) : repart d'un état sans données partagées, sans prévenir
 * les composants, déjà démontés. Une lecture encore en cours d'un test fini ne remplit plus rien.
 */
export function oublierReferentiels() {
  entrees.clear();
  abonnes.clear();
}

/** État d'une donnée partagée vu par un composant. */
export interface DonneePartagee<T> {
  donnees: T | null;
  /** Erreur de chargement, seulement s'il n'y a aucune réponse à afficher. */
  erreur: unknown;
  /** Réponse ou erreur reçue : de quoi afficher la donnée, ou son erreur. */
  pret: boolean;
  /** Lecture en cours (premier chargement, mise à jour ou « Réessayer »). */
  enCours: boolean;
  recharger: () => void;
}

function etat<T>(cle: string, charger: (precedent: T | null) => Promise<T>): DonneePartagee<T> {
  const e = entrees.get(cle);
  const recue = e?.recue ?? false;
  return {
    donnees: recue ? (e?.donnees as T) : null,
    erreur: e && !recue ? e.erreur : null,
    pret: !!e && (recue || e.erreur !== null),
    enCours: e?.enCours ?? false,
    recharger: () => demander(cle, charger, true),
  };
}

function useAbonnement<T>(cles: string[], charger: (cle: string, precedent: T | null) => Promise<T>) {
  useSyncExternalStore(abonner, lireVersion, lireVersion);
  const liste = cles.join('\n');
  useEffect(() => {
    const actives = liste ? liste.split('\n') : [];
    for (const c of actives) {
      abonnes.set(c, (abonnes.get(c) ?? 0) + 1);
      // Un montage relit la donnée (mise à jour d'un onglet rouvert), sauf réponse ou erreur toute récente (une erreur
      // se relance par « Réessayer », pas en cachette par la section montée juste après) ; une lecture en cours sert à
      // tous.
      const e = entrees.get(c);
      const lue = !!e && (e.recue || e.erreur !== null);
      if (!lue || Date.now() - e.recueLe > FRAICHEUR_MS) demander<T>(c, (p) => charger(c, p));
    }
    return () => {
      for (const c of actives) {
        const n = (abonnes.get(c) ?? 1) - 1;
        abonnes.set(c, n);
        // Plus personne ne l'affiche : une erreur sans réponse est oubliée, le prochain montage recharge sans la montrer.
        const e = entrees.get(c);
        if (n <= 0 && e && !e.recue && !e.enCours) entrees.delete(c);
      }
    };
    // `charger` ne dépend que de la clé.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [liste]);
}

/**
 * Donnée partagée identifiée par `cle` (null : rien à charger, `pret` vrai). `charger` ne doit dépendre que de la clé ; il
 * reçoit la dernière réponse gardée (null avant la première). `relire` : valeur qui, en changeant, relit la donnée (la
 * version d'une MEP pour son journal).
 */
export function useDonneePartagee<T>(
  cle: string | null,
  charger: (precedent: T | null) => Promise<T>,
  relire?: unknown,
): DonneePartagee<T> {
  useAbonnement<T>(cle ? [cle] : [], (_c, p) => charger(p));
  // Relecture quand `relire` change, une fois pour tous les composants (pas au premier montage : l'abonnement vient de
  // lancer la lecture).
  useEffect(() => {
    if (!cle || relire === undefined) return;
    const e = entrees.get(cle);
    if (!e || Object.is(e.marque, relire)) return;
    const premiere = e.marque === undefined;
    e.marque = relire;
    if (!premiere && e.recue) demander(cle, charger, true);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [cle, relire]);
  if (!cle) return { donnees: null, erreur: null, pret: true, enCours: false, recharger: () => undefined };
  return etat(cle, charger);
}

/** Plusieurs données partagées de même nature (les objets de chaque système d'une MEP). */
export function useDonneesPartagees<T>(
  cles: string[],
  charger: (cle: string) => Promise<T>,
): Map<string, DonneePartagee<T>> {
  useAbonnement(cles, charger);
  return new Map(cles.map((c) => [c, etat(c, () => charger(c))]));
}

// ---------- Référentiels communs

interface TypesReferentiels {
  catalogue: SystemeCatalogue[];
  resolutions: ResolutionType[];
  comptes: CompteAnnuaire[];
  equipes: Equipe[];
}
export type CleReferentiel = keyof TypesReferentiels;

const CHARGEURS: { [K in CleReferentiel]: () => Promise<TypesReferentiels[K]> } = {
  catalogue: () => api.catalogueSystemes(),
  resolutions: () => api.resolutions(),
  comptes: () => api.utilisateurs(),
  equipes: () => api.equipes(),
};

/**
 * Un référentiel commun à toutes les MEP : catalogue des systèmes, résolutions types, comptes (annuaire) ou équipes.
 * `actif` faux : rien n'est chargé (palette de commandes fermée).
 */
export function useReferentiel<K extends CleReferentiel>(cle: K, actif = true): DonneePartagee<TypesReferentiels[K]> {
  return useDonneePartagee(actif ? `referentiel:${cle}` : null, CHARGEURS[cle]);
}

/** Catalogue des systèmes et résolutions types, communs à toutes les MEP. */
export interface Referentiels {
  catalogue: SystemeCatalogue[];
  resolutions: ResolutionType[];
}

/**
 * Référentiels des onglets du plan (Étapes, Retour arrière, Exécution, vue d'ensemble). `pret` passe à vrai quand les
 * deux ont répondu, ou tout de suite si on les a déjà en mémoire ; une erreur laisse afficher l'onglet sans eux.
 */
export function useReferentiels(): Referentiels & { pret: boolean } {
  const catalogue = useReferentiel('catalogue');
  const resolutions = useReferentiel('resolutions');
  return {
    catalogue: catalogue.donnees ?? [],
    resolutions: resolutions.donnees ?? [],
    pret: catalogue.pret && resolutions.pret,
  };
}
