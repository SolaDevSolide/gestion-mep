import { useEffect, useState } from 'react';
import { api } from './api';
import type { ResolutionType, SystemeCatalogue } from './types';

/** Catalogue des systèmes et résolutions types, communs à toutes les MEP. */
export interface Referentiels {
  catalogue: SystemeCatalogue[];
  resolutions: ResolutionType[];
}

// Dernière version chargée, gardée d'un onglet à l'autre : un onglet rouvert s'affiche aussitôt, puis se met à jour.
let enMemoire: Referentiels | null = null;

/** Pour les tests : repart d'un état sans référentiels chargés. */
export function oublierReferentiels() {
  enMemoire = null;
}

/**
 * Référentiels des onglets du plan (Étapes, Retour arrière, Exécution). `pret` passe à vrai quand les deux sont
 * arrivés, ou dès la première réponse si on les a déjà en mémoire ; une erreur laisse afficher l'onglet sans eux.
 */
export function useReferentiels(): Referentiels & { pret: boolean } {
  const [valeur, setValeur] = useState<Referentiels | null>(enMemoire);
  useEffect(() => {
    let annule = false;
    void Promise.allSettled([api.catalogueSystemes(), api.resolutions()]).then(([c, r]) => {
      const suivante: Referentiels = {
        catalogue: c.status === 'fulfilled' ? c.value : (enMemoire?.catalogue ?? []),
        resolutions: r.status === 'fulfilled' ? r.value : (enMemoire?.resolutions ?? []),
      };
      if (c.status === 'fulfilled' && r.status === 'fulfilled') enMemoire = suivante;
      if (!annule) setValeur(suivante);
    });
    return () => {
      annule = true;
    };
  }, []);
  return { catalogue: valeur?.catalogue ?? [], resolutions: valeur?.resolutions ?? [], pret: valeur !== null };
}
