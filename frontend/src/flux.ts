/**
 * Flux SSE d'une MEP. Le navigateur se reconnecte seul après une coupure réseau, mais ferme
 * définitivement le flux sur une réponse HTTP en erreur (ex. 502 pendant un déploiement) :
 * on le recrée alors avec un délai croissant (1 s, 2 s, 4 s… plafonné à 30 s).
 * `surReconnexion` est appelé à chaque réouverture, car des mises à jour ont pu passer pendant la coupure.
 * Renvoie la fonction qui ferme le flux.
 */
export function ouvrirFlux(url: string, surMaj: (version: number) => void, surReconnexion: () => void): () => void {
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
    });
    courant.addEventListener('maj', (ev) => {
      try {
        const { version } = JSON.parse((ev as MessageEvent<string>).data) as { version: number };
        surMaj(version);
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
