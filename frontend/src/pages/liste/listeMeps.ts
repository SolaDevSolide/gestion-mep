import { useCallback, useEffect, useRef, useState } from 'react';
import { api } from '../../api';
import { normaliser } from '../../recherche';
import { NOMS_ETATS, parametresListe, PREFIXE_LIBRE, type FiltreMeps, type NomEtat } from '../../rechercheMeps';
import type { FacettesMeps, MepResume } from '../../types';

/** Délai avant d'envoyer la recherche au serveur, le temps de finir de taper. */
export const DELAI_RECHERCHE = 250;

export interface ListeMeps {
  cle: string;
  meps: MepResume[];
  /** MEP qui correspondent aux filtres (toutes pages comprises). */
  total: number;
  /** Toutes les MEP (ou tous les modèles), sans filtre. */
  ensemble: number;
  suivant: string | null;
  facettes: FacettesMeps;
}

export interface ListeMepsLue {
  /** Dernière liste reçue ; null jusqu'à la première réponse. */
  liste: ListeMeps | null;
  /** false tant que la liste affichée est celle de filtres précédents (la nouvelle arrive). */
  aJour: boolean;
  erreur: string;
  /** Erreur reçue telle quelle (null sans erreur), pour `ErreurChargement`. */
  echec: unknown;
  /** Première page des filtres en cours de lecture (premier chargement, nouveaux filtres ou « Réessayer »). */
  chargement: boolean;
  /** Relit la première page des filtres en cours (« Réessayer »). */
  recharger: () => void;
  chargementSuite: boolean;
  afficherPlus: () => void;
}

/**
 * Liste des MEP paginée par le serveur (#96) : première page et comptes de la barre de filtres demandés ensemble (l'écran
 * n'affiche jamais une page sans ses comptes), puis « Afficher plus » ajoute la page suivante. La recherche part après une
 * courte pause de frappe ; la liste précédente reste affichée jusqu'à la réponse.
 */
export function useListeMeps(f: FiltreMeps, modele: boolean): ListeMepsLue {
  const [recherche, setRecherche] = useState(f.recherche);
  useEffect(() => {
    const t = window.setTimeout(() => setRecherche(f.recherche), DELAI_RECHERCHE);
    return () => window.clearTimeout(t);
  }, [f.recherche]);
  const parametres = parametresListe({ ...f, recherche }, modele);
  const cle = parametres.toString();

  const [liste, setListe] = useState<ListeMeps | null>(null);
  const [erreur, setErreur] = useState<{ cle: string; echec: unknown } | null>(null);
  const [chargementSuite, setChargementSuite] = useState(false);
  // « Réessayer » : un nouvel essai relit la première page ; `essaiRecu` est le dernier essai qui a répondu (ou échoué).
  const [essai, setEssai] = useState(0);
  const demande = `${essai}\n${cle}`;
  const [essaiRecu, setEssaiRecu] = useState<string | null>(null);
  const listeRef = useRef<ListeMeps | null>(null);
  const cleRef = useRef(cle);

  useEffect(() => {
    cleRef.current = cle;
    let annule = false;
    const p = new URLSearchParams(cle);
    Promise.all([api.pageMeps(p), api.facettesMeps(p)])
      .then(([page, facettes]) => {
        if (annule) return;
        const l: ListeMeps = {
          cle,
          meps: page.meps,
          total: page.total,
          ensemble: page.ensemble,
          suivant: page.suivant,
          facettes,
        };
        listeRef.current = l;
        setListe(l);
        setErreur(null);
        setEssaiRecu(`${essai}\n${cle}`);
      })
      .catch((e: unknown) => {
        if (annule) return;
        setErreur({ cle, echec: e ?? new Error('Chargement impossible.') });
        setEssaiRecu(`${essai}\n${cle}`);
      });
    return () => {
      annule = true;
    };
  }, [cle, essai]);

  const afficherPlus = useCallback(() => {
    const l = listeRef.current;
    if (!l?.suivant || l.cle !== cleRef.current || chargementSuite) return;
    setChargementSuite(true);
    api
      .pageMeps(new URLSearchParams(l.cle), l.suivant)
      .then((page) => {
        if (listeRef.current !== l) return;
        const vus = new Set(l.meps.map((m) => m.id));
        const suite: ListeMeps = {
          ...l,
          meps: [...l.meps, ...page.meps.filter((m) => !vus.has(m.id))],
          total: page.total,
          ensemble: page.ensemble,
          suivant: page.suivant,
        };
        listeRef.current = suite;
        setListe(suite);
      })
      .catch((e: unknown) => {
        if (listeRef.current === l) setErreur({ cle: l.cle, echec: e ?? new Error('Chargement impossible.') });
      })
      .finally(() => setChargementSuite(false));
  }, [chargementSuite]);

  const echec = erreur?.cle === cle ? erreur.echec : null;
  return {
    liste,
    aJour: liste?.cle === cle,
    erreur: echec === null ? '' : echec instanceof Error ? echec.message : 'Chargement impossible.',
    echec,
    chargement: essaiRecu !== demande,
    recharger: () => setEssai((n) => n + 1),
    chargementSuite,
    afficherPlus,
  };
}

/** Nombre de MEP en choisissant cette valeur d'un critère (à la place de celles choisies), d'après les comptes du serveur. */
export interface ComptesFiltres {
  systeme: (cle: string) => number;
  environnement: (nom: string) => number;
  equipe: (id: string) => number;
  personne: (id: string) => number;
  etat: (e: NomEtat) => number;
}

/** Clé d'un système dans les comptes : identifiant du catalogue en minuscules, ou « libre: » et le nom normalisé. */
export const cleSysteme = (cle: string): string =>
  cle.startsWith(PREFIXE_LIBRE)
    ? PREFIXE_LIBRE + normaliser(cle.slice(PREFIXE_LIBRE.length).trim())
    : cle.toLowerCase();

export function comptesDe(facettes: FacettesMeps): ComptesFiltres {
  const lu = (d: Record<string, number>, cle: string) => d[cle] ?? 0;
  return {
    systeme: (cle) => lu(facettes.systemes, cleSysteme(cle)),
    environnement: (nom) => lu(facettes.environnements, normaliser(nom.trim())),
    equipe: (id) => lu(facettes.equipes, id.toLowerCase()),
    personne: (id) => lu(facettes.personnes, id.toLowerCase()),
    etat: (e) => lu(facettes.etats, NOMS_ETATS[e]),
  };
}
