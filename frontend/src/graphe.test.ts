import { calculerGraphe, DIMENSIONS_PAR_DEFAUT, placerGraphe } from './graphe';

const n = (id: string, ...dependances: string[]) => ({ id, dependances });

describe('calculerGraphe', () => {
  it('place une chaîne sur des niveaux successifs', () => {
    const g = calculerGraphe([n('A'), n('B', 'A'), n('C', 'B')]);
    expect(g.niveaux).toEqual({ A: 0, B: 1, C: 2 });
    expect(g.colonnes).toEqual([['A'], ['B'], ['C']]);
    expect(g.aCycle).toBe(false);
  });

  it('empile les étapes parallèles dans la même colonne', () => {
    const g = calculerGraphe([n('A'), n('B', 'A'), n('C', 'A'), n('D', 'B', 'C')]);
    expect(g.colonnes).toEqual([['A'], ['B', 'C'], ['D']]);
    expect(g.liens).toHaveLength(4);
  });

  it('le niveau est le plus long chemin depuis une étape sans préalable', () => {
    const g = calculerGraphe([n('A'), n('B', 'A'), n('C', 'B'), n('D', 'A', 'C')]);
    expect(g.niveaux.D).toBe(3);
  });

  it('plusieurs racines sont toutes au niveau 0', () => {
    const g = calculerGraphe([n('A'), n('B'), n('C', 'A', 'B')]);
    expect(g.colonnes[0]).toEqual(['A', 'B']);
    expect(g.niveaux.C).toBe(1);
  });

  it('ignore les cycles sans planter', () => {
    const g = calculerGraphe([n('A', 'C'), n('B', 'A'), n('C', 'B')]);
    expect(g.aCycle).toBe(true);
    expect(g.liensIgnores).toHaveLength(1);
    expect(Object.keys(g.niveaux).sort()).toEqual(['A', 'B', 'C']);
    expect(g.colonnes.flat().sort()).toEqual(['A', 'B', 'C']);
  });

  it('ignore auto-dépendances et dépendances inconnues', () => {
    const g = calculerGraphe([n('A', 'A', 'Z'), n('B', 'A')]);
    expect(g.niveaux).toEqual({ A: 0, B: 1 });
    expect(g.liens).toEqual([{ de: 'A', vers: 'B' }]);
  });

  it('gère une liste vide', () => {
    const g = calculerGraphe([]);
    expect(g.colonnes).toEqual([]);
    expect(placerGraphe(g)).toEqual({ noeuds: [], liens: [], largeur: 0, hauteur: 0 });
  });

  it('réduit les croisements en suivant la position des préalables', () => {
    const g = calculerGraphe([n('A'), n('B'), n('C', 'B'), n('D', 'A')]);
    expect(g.colonnes[1]).toEqual(['D', 'C']);
  });
});

describe('placerGraphe', () => {
  it('calcule positions, dimensions et courbes', () => {
    const d = DIMENSIONS_PAR_DEFAUT;
    const p = placerGraphe(calculerGraphe([n('A'), n('B', 'A'), n('C', 'A')]));
    const b = p.noeuds.find((x) => x.id === 'B')!;
    const c = p.noeuds.find((x) => x.id === 'C')!;
    expect(b.x).toBe(c.x);
    expect(c.y - b.y).toBe(d.hauteurNoeud + d.espaceY);
    expect(b.x).toBe(d.marge + d.largeurNoeud + d.espaceX);
    expect(p.largeur).toBe(d.marge * 2 + 2 * d.largeurNoeud + d.espaceX);
    expect(p.liens).toHaveLength(2);
    expect(p.liens[0].chemin).toMatch(/^M [\d.]+ [\d.]+ C /);
  });
});
