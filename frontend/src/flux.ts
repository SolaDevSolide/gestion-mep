import type { Presence } from './types';

/**
 * Événement `maj` d'un flux : la version (celle de la MEP, ou le nombre de modifications diffusées pour le suivi). Pour le
 * suivi en direct (#96), `meps` : les MEP lancées dont la carte a changé ; `liste` : les listes ont changé, tout relire.
 */
export interface Maj {
  version: number;
  meps?: string[];
  liste?: boolean;
}

function lireMaj(texte: string): Maj | null {
  const brut = JSON.parse(texte) as unknown;
  if (typeof brut !== 'object' || brut === null || !('version' in brut) || typeof brut.version !== 'number')
    return null;
  const meps =
    'meps' in brut && Array.isArray(brut.meps)
      ? brut.meps.filter((m): m is string => typeof m === 'string')
      : undefined;
  const liste = 'liste' in brut && typeof brut.liste === 'boolean' ? brut.liste : undefined;
  return { version: brut.version, meps, liste };
}

/**
 * Flux SSE d'une MEP. Le navigateur se reconnecte seul après une coupure réseau, mais ferme
 * définitivement le flux sur une réponse HTTP en erreur (ex. 502 pendant un déploiement) :
 * on le recrée alors avec un délai croissant (1 s, 2 s, 4 s… plafonné à 30 s).
 * `surReconnexion` est appelé à chaque réouverture, car des mises à jour ont pu passer pendant la coupure.
 * Le flux envoie aussi la présence (`event: presence`, liste des personnes qui ont la MEP ouverte) : `surPresence`.
 * `surOuverture` est appelé à chaque ouverture, la première comprise (pour redéclarer les éléments de l'onglet).
 * Le flux envoie enfin les verrous d'édition des étapes (`event: verrous`) : `surVerrous`, et la fin d'un envoi à une
 * API externe (`event: appels`, sans changement de version) : `surAppels`.
 * `surMaj` reçoit la version et l'événement entier (`Maj` : cartes du suivi à relire).
 * Renvoie la fonction qui ferme le flux.
 */
export function ouvrirFlux(
  url: string,
  surMaj: (version: number, maj: Maj) => void,
  surReconnexion: () => void,
  options: {
    surPresence?: (presences: Presence[]) => void;
    surOuverture?: () => void;
    /** Verrous d'édition des étapes (`event: verrous`, liste complète à chaque changement). */
    surVerrous?: (verrous: unknown) => void;
    /** Envoi à une API externe terminé (`event: appels`, #111) : ligne de journal et échecs à relire. */
    surAppels?: () => void;
  } = {},
): () => void {
  let es: EventSource | null = null;
  let relance: number | undefined;
  let tentatives = 0;
  let dejaOuvert = false;
  let arrete = false;
  const ouvrir = () => {
    const courant = new EventSource(url, { withCredentials: true });
    es = courant;
    courant.addEventListener('open', () => {
      if (dejaOuvert) surReconnexion();
      dejaOuvert = true;
      tentatives = 0;
      options.surOuverture?.();
    });
    courant.addEventListener('presence', (ev) => {
      try {
        const presences = JSON.parse((ev as MessageEvent<string>).data) as unknown;
        if (Array.isArray(presences)) options.surPresence?.(presences as Presence[]);
      } catch {
        /* événement illisible */
      }
    });
    courant.addEventListener('verrous', (ev) => {
      try {
        options.surVerrous?.(JSON.parse((ev as MessageEvent<string>).data));
      } catch {
        /* événement illisible */
      }
    });
    courant.addEventListener('appels', () => options.surAppels?.());
    courant.addEventListener('maj', (ev) => {
      try {
        const maj = lireMaj((ev as MessageEvent<string>).data);
        if (maj) surMaj(maj.version, maj);
      } catch {
        /* événement illisible */
      }
    });
    courant.addEventListener('error', () => {
      if (arrete || courant.readyState !== EventSource.CLOSED) return;
      const delai = Math.min(30_000, 1_000 * 2 ** tentatives);
      tentatives += 1;
      relance = window.setTimeout(ouvrir, delai);
    });
  };
  ouvrir();
  return () => {
    arrete = true;
    window.clearTimeout(relance);
    es?.close();
  };
}
