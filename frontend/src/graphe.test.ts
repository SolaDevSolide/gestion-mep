import { construireLignes, indexLignes, liensDuPlan } from './graphe';
const n = (id: string, ligne: number, ordre: number, ...dependances: string[]) => ({ id, ligne, ordre, dependances });

describe('construireLignes', () => {
  it('range par ligne puis par position', () => {
    const l = construireLignes([n('C', 1, 0), n('B', 0, 1), n('A', 0, 0), n('D', 3, 0)]);
    expect(l.map((r) => r.map((e) => e.id))).toEqual([['A', 'B'], ['C'], ['D']]);
    expect(indexLignes(l).get('D')).toBe(2);
  });

  it('liste vide', () => {
    expect(construireLignes([])).toEqual([]);
  });
});

describe('liens', () => {
  const etapes = [n('A', 0, 0), n('B', 0, 1), n('C', 1, 0, 'A', 'B', 'Z'), n('D', 2, 0, 'C', 'C')];
  const liens = liensDuPlan(etapes);

  it('liensDuPlan ignore les préalables inconnus et les doublons', () => {
    expect(liens).toEqual([
      { de: 'A', vers: 'C' },
      { de: 'B', vers: 'C' },
      { de: 'C', vers: 'D' },
    ]);
  });
});
