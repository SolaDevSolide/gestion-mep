import {
  cheminCoude,
  construireLignes,
  groupesDeLiens,
  indexLignes,
  lienPossible,
  liensDuPlan,
  pairesDuGroupe,
  tracerJonctions,
  type Ancrage,
} from './graphe';

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
  const lignes = indexLignes(construireLignes(etapes));

  it('liensDuPlan ignore les préalables inconnus et les doublons', () => {
    expect(liens).toEqual([
      { de: 'A', vers: 'C' },
      { de: 'B', vers: 'C' },
      { de: 'C', vers: 'D' },
    ]);
  });

  it('lienPossible : un lien descend toujours et n’est pas dupliqué', () => {
    expect(lienPossible('A', 'D', lignes, liens)).toBe('ok');
    expect(lienPossible('D', 'A', lignes, liens)).toBe('remonte');
    expect(lienPossible('A', 'B', lignes, liens)).toBe('remonte');
    expect(lienPossible('A', 'A', lignes, liens)).toBe('remonte');
    expect(lienPossible('A', 'C', lignes, liens)).toBe('existe');
  });
});

describe('groupesDeLiens (jonctions)', () => {
  it('regroupe les sources qui attendent le même ensemble de cibles', () => {
    const g = groupesDeLiens([
      { de: 'E1', vers: 'E4' },
      { de: 'E1', vers: 'E5' },
      { de: 'E2', vers: 'E5' },
      { de: 'E2', vers: 'E4' },
      { de: 'E3', vers: 'E6' },
    ]);
    expect(g).toEqual([
      { cle: 'E1|E2>E4|E5', sources: ['E1', 'E2'], cibles: ['E4', 'E5'] },
      { cle: 'E3>E6', sources: ['E3'], cibles: ['E6'] },
    ]);
    expect(pairesDuGroupe(g[0])).toHaveLength(4);
  });

  it('des ensembles de cibles différents donnent des groupes différents', () => {
    const g = groupesDeLiens([
      { de: 'A', vers: 'C' },
      { de: 'B', vers: 'C' },
      { de: 'B', vers: 'D' },
    ]);
    expect(g.map((x) => x.cle)).toEqual(['A>C', 'B>C|D']);
  });
});

describe('tracerJonctions', () => {
  const a = (x: number, haut: number, bas = haut + 80): Ancrage => ({ x, haut, bas });

  it('lien simple : un seul chemin fléché, sans barre ni point', () => {
    const [t] = tracerJonctions([{ cle: 'A>B', sources: ['A'], cibles: ['B'] }], { A: a(100, 0), B: a(100, 126) });
    expect(t.traits).toBeNull();
    expect(t.points).toEqual([]);
    expect(t.fleches).toEqual(['M100,81 V124']);
    expect(t.barre).toBe(103);
  });

  it('lien simple décalé : coudes arrondis', () => {
    const [t] = tracerJonctions([{ cle: 'A>B', sources: ['A'], cibles: ['B'] }], { A: a(100, 0), B: a(300, 126) });
    expect(t.fleches[0]).toBe('M100,81 V95 Q100,103 108,103 H292 Q300,103 300,111 V124');
  });

  it('jonction : descentes + barre pleine largeur, une flèche par cible, un point par source', () => {
    const [t] = tracerJonctions([{ cle: 'A|B>C|D', sources: ['A', 'B'], cibles: ['C', 'D'] }], {
      A: a(100, 0),
      B: a(340, 0),
      C: a(50, 126),
      D: a(400, 126),
    });
    expect(t.barre).toBe(103);
    expect(t.traits).toBe('M100,81 V103 M340,81 V103 M50,103 H400');
    expect(t.fleches).toEqual(['M50,103 V124', 'M400,103 V124']);
    expect(t.points).toEqual([
      { x: 100, y: 103 },
      { x: 340, y: 103 },
    ]);
  });

  it('décale les barres entre les mêmes lignes', () => {
    const anc = { A: a(100, 0), B: a(340, 0), C: a(100, 126), D: a(340, 126) };
    const t = tracerJonctions(
      [
        { cle: '1', sources: ['A'], cibles: ['C'] },
        { cle: '2', sources: ['B'], cibles: ['D'] },
        { cle: '3', sources: ['A', 'B'], cibles: ['D'] },
      ],
      anc,
    );
    expect(t.map((x) => x.barre)).toEqual([103, 111, 95]);
  });

  it('ignore les groupes dont les fiches ne sont pas mesurées', () => {
    expect(tracerJonctions([{ cle: 'A>B', sources: ['A'], cibles: ['B'] }], { A: a(0, 0) })).toEqual([]);
  });

  it('cheminCoude : ligne droite si aligné', () => {
    expect(cheminCoude({ x: 10, y: 0 }, { x: 10.4, y: 50 }, 25)).toBe('M10,0 V50');
  });
});
