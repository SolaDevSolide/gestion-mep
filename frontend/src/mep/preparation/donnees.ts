import { api } from '../../api';
import { useDonneePartagee, useDonneesPartagees, useReferentiel } from '../../referentiels';
import type { Mep, ObjetCatalogue } from '../../types';
import { PAGE_JOURNAL } from '../journal';

const PREFIXE_OBJETS = 'objets-catalogue:';

/** Fiches au catalogue des systèmes de la MEP (sans doublon, dans un ordre stable). */
export function systemesCatalogue(mep: Pick<Mep, 'systemes'>): string[] {
  return [...new Set(mep.systemes.map((s) => s.catalogueId).filter((x): x is string => !!x))].sort();
}

/** Objets connus de chaque système du catalogue touché par la MEP, par identifiant de fiche. */
export function useObjetsCatalogue(mep: Pick<Mep, 'systemes'>) {
  const par = useDonneesPartagees<ObjetCatalogue[]>(
    systemesCatalogue(mep).map((id) => PREFIXE_OBJETS + id),
    (cle) => api.objetsCatalogue(cle.slice(PREFIXE_OBJETS.length)),
  );
  return new Map([...par].map(([cle, d]) => [cle.slice(PREFIXE_OBJETS.length), d]));
}

/**
 * Derniers événements du journal de la MEP (une page, de la famille `type` s'il y en a une), relus à chaque version
 * (approbations réinitialisées, derniers événements).
 */
export function useJournalMep(mep: Pick<Mep, 'id' | 'version'>, type = '') {
  return useDonneePartagee(
    `journal:${mep.id}:${type}`,
    () => api.journal(mep.id, { type, limite: PAGE_JOURNAL }),
    mep.version,
  );
}

/**
 * Listes dont dépend l'onglet Préparation : comptes, équipes, catalogue, objets de chaque système et décisions du
 * journal (circuit d'approbation). Lues une seule fois pour toutes les sections (elles les relisent par les mêmes clés, sans nouvelle
 * requête). `pret` : chaque liste a répondu, ou échoué (la section concernée montre alors son erreur).
 */
export function usePreparation(mep: Mep) {
  const comptes = useReferentiel('comptes');
  const equipes = useReferentiel('equipes');
  const catalogue = useReferentiel('catalogue');
  const objets = useObjetsCatalogue(mep);
  const journal = useJournalMep(mep, 'approbation');
  const pret =
    comptes.pret && equipes.pret && catalogue.pret && journal.pret && [...objets.values()].every((o) => o.pret);
  return { comptes, equipes, catalogue, objets, journal, pret };
}
