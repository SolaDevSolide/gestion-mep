import {
  classer,
  concerne,
  correspond,
  memeSysteme,
  normaliser,
  score,
  surligner,
  termes,
  type DocumentRecherche,
} from './recherche';

describe('normaliser et termes', () => {
  it('ignore la casse et les accents', () => {
    expect(normaliser('Échec de PURGE — Œuvre')).toBe('echec de purge — oeuvre');
    expect(termes('  Purge   CDN, purge ')).toEqual(['purge', 'cdn']);
    expect(termes('')).toEqual([]);
  });
});

describe('correspond', () => {
  const doc: DocumentRecherche = {
    titre: 'WEB : purge du cache CDN refusée',
    textes: ['La publication échoue avec 403', 'Portail public'],
  };

  it('tous les mots doivent être trouvés, dans n’importe quel champ', () => {
    expect(correspond(doc, termes('cdn 403'))).toBe(true);
    expect(correspond(doc, termes('refusee'))).toBe(true);
    expect(correspond(doc, termes('PORTAIL echoue'))).toBe(true);
    expect(correspond(doc, termes('cdn api'))).toBe(false);
  });

  it('recherche vide : tout correspond', () => {
    expect(correspond(doc, [])).toBe(true);
  });
});

describe('classer', () => {
  type R = { id: string; titre: string; symptomes: string; meme: boolean };
  const r: R[] = [
    { id: 'a', titre: 'BDD : droits retirés', symptomes: 'Connexion refusée au service', meme: false },
    { id: 'b', titre: 'API : service arrêté', symptomes: '503', meme: false },
    { id: 'c', titre: 'Annuaire : service LDAP muet', symptomes: 'timeout', meme: true },
    { id: 'd', titre: 'WEB : cache', symptomes: 'rien', meme: false },
  ];
  const vers = (x: R): DocumentRecherche => ({ titre: x.titre, textes: [x.symptomes], prioritaire: x.meme });

  it('même système d’abord, puis correspondance dans le titre, puis le reste', () => {
    expect(classer(r, 'service', vers).map((x) => x.id)).toEqual(['c', 'b', 'a']);
  });

  it('sans recherche : prioritaires puis ordre alphabétique', () => {
    expect(classer(r, '', vers).map((x) => x.id)).toEqual(['c', 'b', 'a', 'd']);
  });

  it('score : bonus si tous les termes sont dans le titre', () => {
    expect(score({ titre: 'Purge CDN', textes: [] }, ['purge', 'cdn'])).toBeGreaterThan(
      score({ titre: 'Purge', textes: ['cdn'] }, ['purge', 'cdn']),
    );
  });
});

describe('surligner', () => {
  it('marque les occurrences sans tenir compte des accents ni de la casse', () => {
    expect(surligner('Purge refusée (purge)', ['purge', 'refusee'])).toEqual([
      { texte: 'Purge', trouve: true },
      { texte: ' ', trouve: false },
      { texte: 'refusée', trouve: true },
      { texte: ' (', trouve: false },
      { texte: 'purge', trouve: true },
      { texte: ')', trouve: false },
    ]);
  });

  it('sans terme : un seul segment', () => {
    expect(surligner('abc', [])).toEqual([{ texte: 'abc', trouve: false }]);
    expect(surligner('', ['a'])).toEqual([]);
  });
});

describe('résolutions multi-systèmes', () => {
  it('concerne : aucun système = tous ; sinon au moins un en commun', () => {
    expect(concerne([], ['api'])).toBe(true);
    expect(concerne(['api', 'bdd'], ['bdd'])).toBe(true);
    expect(concerne(['web'], ['api', 'bdd'])).toBe(false);
  });
  it('même système : intersection non vide', () => {
    expect(memeSysteme(['api', 'bdd'], ['bdd', 'web'])).toBe(true);
    expect(memeSysteme([], ['bdd'])).toBe(false);
  });
});
