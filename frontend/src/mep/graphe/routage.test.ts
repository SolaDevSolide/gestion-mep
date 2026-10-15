import {
  cheminLien,
  compterCroisements,
  liensAuPoint,
  milieuLien,
  RAYON_SAUT,
  sautsDesLiens,
  simplifier,
} from './routage';

describe('tracé des liens', () => {
  it('simplifie : sans doublons ni points au milieu d’un segment droit', () => {
    expect(
      simplifier([
        { x: 0, y: 0 },
        { x: 0, y: 0 },
        { x: 0, y: 10 },
        { x: 0, y: 20 },
        { x: 30, y: 20 },
      ]),
    ).toEqual([
      { x: 0, y: 0 },
      { x: 0, y: 20 },
      { x: 30, y: 20 },
    ]);
  });

  it('coudes arrondis ; ligne droite sans courbe', () => {
    expect(
      cheminLien([
        { x: 0, y: 0 },
        { x: 0, y: 100 },
      ]),
    ).toBe('M 0 0 L 0 100');
    const coude = cheminLien([
      { x: 0, y: 0 },
      { x: 0, y: 50 },
      { x: 80, y: 50 },
      { x: 80, y: 100 },
    ]);
    expect(coude.match(/Q/g)).toHaveLength(2);
  });

  it('saut d’un segment horizontal au croisement du segment vertical d’un autre lien', () => {
    const routes = new Map([
      [
        'h',
        [
          { x: 0, y: 0 },
          { x: 0, y: 50 },
          { x: 200, y: 50 },
          { x: 200, y: 100 },
        ],
      ],
      [
        'v',
        [
          { x: 100, y: 0 },
          { x: 100, y: 100 },
        ],
      ],
    ]);
    const sauts = sautsDesLiens(routes);
    expect(sauts.get('h')?.get(1)).toEqual([100]);
    expect(sauts.has('v')).toBe(false);
    const d = cheminLien(routes.get('h') ?? [], sauts.get('h'));
    expect(d).toContain(`L ${100 - RAYON_SAUT} 50 A ${RAYON_SAUT} ${RAYON_SAUT}`);
  });

  it('milieu : celui du plus long segment', () => {
    expect(
      milieuLien([
        { x: 0, y: 0 },
        { x: 0, y: 10 },
        { x: 100, y: 10 },
        { x: 100, y: 20 },
      ]),
    ).toEqual({ x: 50, y: 10 });
  });
});

describe('liens visés et croisements', () => {
  // Deux liens fusionnés au départ de a (tronçon commun jusqu'à y = 40), puis l'un vers b, l'autre vers c.
  const routes = new Map([
    [
      'a-b',
      [
        { x: 0, y: 0 },
        { x: 0, y: 40 },
        { x: -50, y: 40 },
        { x: -50, y: 100 },
      ],
    ],
    [
      'a-c',
      [
        { x: 0, y: 0 },
        { x: 0, y: 40 },
        { x: 50, y: 40 },
        { x: 50, y: 100 },
      ],
    ],
  ]);

  it('sur un tronçon partagé, tous les liens qui y passent ; ailleurs, le seul lien visé', () => {
    expect(liensAuPoint(routes, { x: 2, y: 20 }, 6).sort()).toEqual(['a-b', 'a-c']);
    expect(liensAuPoint(routes, { x: 48, y: 80 }, 6)).toEqual(['a-c']);
    expect(liensAuPoint(routes, { x: 20, y: 80 }, 6)).toEqual([]);
    // Le plus proche d'abord.
    expect(liensAuPoint(routes, { x: -3, y: 40 }, 6)[0]).toBe('a-b');
  });

  it('compte les croisements, et les superpositions entre liens sans bout commun', () => {
    const bouts = new Map([
      ['a-b', { source: 'a', cible: 'b' }],
      ['a-c', { source: 'a', cible: 'c' }],
    ]);
    // Tronçon commun d'une même source : ni croisement ni superposition.
    expect(compterCroisements(routes, bouts)).toEqual({ croisements: 0, superpositions: 0 });
    const croise = new Map([
      ...routes,
      [
        'x-y',
        [
          { x: -80, y: 70 },
          { x: 80, y: 70 },
        ],
      ],
      [
        'u-v',
        [
          { x: 0, y: 10 },
          { x: 0, y: 30 },
        ],
      ],
    ]);
    const bouts2 = new Map([...bouts, ['x-y', { source: 'x', cible: 'y' }], ['u-v', { source: 'u', cible: 'v' }]]);
    // x-y coupe les deux branches ; u-v se superpose au tronçon commun (une fois par lien qui y passe).
    expect(compterCroisements(croise, bouts2)).toEqual({ croisements: 2, superpositions: 2 });
  });
});
