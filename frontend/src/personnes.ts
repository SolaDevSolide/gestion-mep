// Personnes et équipes : des comptes reliés (`comptePrincipalId`) forment une seule personne, nommée d'après son compte
// principal ; les équipes d'une personne sont celles de tous ses comptes. Logique pure, sans affichage.
import { normaliser } from './recherche';
import type { Utilisateur } from './types';

type Compte = Pick<Utilisateur, 'id' | 'login' | 'nomAffiche' | 'role' | 'comptePrincipalId' | 'equipeIds'>;

/** Identifiant de la personne d'un compte : son compte principal, ou lui-même. */
export const personneDe = (c: Pick<Utilisateur, 'id' | 'comptePrincipalId'>): string => c.comptePrincipalId ?? c.id;

/** Comptes regroupés par personne : le compte principal d'abord, puis ses comptes reliés. */
export interface GroupeComptes<T extends Compte = Compte> {
  principal: T;
  relies: T[];
}

/**
 * Regroupe les comptes par personne, dans l'ordre des comptes principaux reçus. Un compte relié dont le principal est
 * absent de la liste forme son propre groupe.
 */
export function grouperParPersonne<T extends Compte>(comptes: T[]): GroupeComptes<T>[] {
  const ids = new Set(comptes.map((c) => c.id));
  const groupes = new Map<string, GroupeComptes<T>>();
  for (const c of comptes) {
    if (c.comptePrincipalId && ids.has(c.comptePrincipalId)) continue;
    groupes.set(c.id, { principal: c, relies: [] });
  }
  for (const c of comptes) {
    if (c.comptePrincipalId && ids.has(c.comptePrincipalId)) groupes.get(c.comptePrincipalId)?.relies.push(c);
  }
  return [...groupes.values()];
}

/** Tous les comptes d'un groupe, le principal en premier. */
export const comptesDuGroupe = <T extends Compte>(g: GroupeComptes<T>): T[] => [g.principal, ...g.relies];

/** Équipes de la personne d'un compte : celles de tous ses comptes reliés présents dans la liste. */
export function equipesDeLaPersonne(compte: Compte, comptes: Compte[]): Set<string> {
  const personne = personneDe(compte);
  return new Set(comptes.filter((c) => personneDe(c) === personne).flatMap((c) => c.equipeIds));
}

/**
 * Comptes dont la personne fait partie de l'équipe (par l'un de ses comptes reliés). `equipe` vide : tous les comptes.
 */
export function filtrerParEquipe<T extends Compte>(comptes: T[], equipe: string): T[] {
  if (!equipe) return comptes;
  const personnes = new Set(comptes.filter((c) => c.equipeIds.includes(equipe)).map(personneDe));
  return comptes.filter((c) => personnes.has(personneDe(c)));
}

/** Un groupe correspond à la recherche si l'un de ses comptes correspond (nom, identifiant ou rôle). */
export function groupeCorrespond(g: GroupeComptes, recherche: string): boolean {
  const q = normaliser(recherche.trim());
  return !q || comptesDuGroupe(g).some((c) => normaliser(`${c.nomAffiche} ${c.login} ${c.role}`).includes(q));
}

/** Option d'un compte dans un sélecteur : nom de la personne, puis identifiant et état du compte. */
export function optionCompte(u: Pick<Utilisateur, 'id' | 'login' | 'nomPersonne' | 'actif' | 'comptePrincipalId'>): {
  valeur: string;
  libelle: string;
  detail: string;
} {
  const etat = [u.comptePrincipalId ? 'compte relié' : '', u.actif ? '' : 'désactivé'].filter(Boolean);
  return { valeur: u.id, libelle: u.nomPersonne, detail: [u.login, ...etat].join(' · ') };
}

/**
 * Personnes proposées par la recherche des MEP (#59) : une par compte principal, nommée d'après lui, avec les identifiants
 * de tous ses comptes (pour la retrouver par l'un d'eux). Triées par nom.
 */
export function personnesDeRecherche(
  comptes: Pick<
    Utilisateur,
    'id' | 'login' | 'nomPersonne' | 'nomAffiche' | 'role' | 'comptePrincipalId' | 'equipeIds'
  >[],
): { id: string; nom: string; detail: string }[] {
  return grouperParPersonne(comptes)
    .map((g) => ({
      id: g.principal.id,
      nom: g.principal.nomPersonne || g.principal.nomAffiche,
      detail: comptesDuGroupe(g)
        .map((c) => c.login)
        .join(', '),
    }))
    .sort((a, b) => a.nom.localeCompare(b.nom, 'fr'));
}
