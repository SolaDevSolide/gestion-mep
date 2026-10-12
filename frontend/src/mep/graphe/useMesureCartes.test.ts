import { geometrieRetenue, type Geometrie } from './useMesureCartes';

const geo = (zone: number): Geometrie => ({
  ancrages: { a: { x: zone / 2, haut: 0, bas: 40 } },
  largeur: zone,
  hauteur: 1000,
  zone,
});

describe('geometrieRetenue', () => {
  it('prend une nouvelle mesure', () => {
    const affichee = geo(1124);
    expect(geometrieRetenue(affichee, null, geo(1200))).toEqual(geo(1200));
  });

  it('garde l’état affiché quand la mesure ne change pas', () => {
    const affichee = geo(1124);
    expect(geometrieRetenue(affichee, geo(1000), geo(1124))).toBe(affichee);
  });

  it('arrête une oscillation entre deux mesures (barre de défilement qui apparaît puis disparaît)', () => {
    // Relevé du 12/10 : zone 1124 → 1114 → 1124 → … jusqu'à « Maximum update depth exceeded ».
    const affichee = geo(1114);
    expect(geometrieRetenue(affichee, geo(1124), geo(1124))).toBe(affichee);
  });
});
