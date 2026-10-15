import { cheminLien, milieuLien, RAYON_SAUT, sautsDesLiens, simplifier } from './routage';

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
