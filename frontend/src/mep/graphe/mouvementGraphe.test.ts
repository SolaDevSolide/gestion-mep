import {
  adoucir,
  cheminArrondi,
  dansCadre,
  etatA,
  rampeApparition,
  rampeDisparition,
  routeSuivant,
  type Boite,
} from './mouvementGraphe';

const boite = (x: number, y: number, largeur: number, hauteur: number): Boite => ({ x, y, largeur, hauteur });

describe('mouvement : avancement', () => {
  it('part vite et arrive en douceur, de 0 à 1', () => {
    expect(adoucir(0)).toBe(0);
    expect(adoucir(1)).toBe(1);
    expect(adoucir(0.1)).toBeGreaterThan(0.25);
  });
  it('ce qui paraît est visible tôt ; ce qui part a disparu à mi-chemin', () => {
    expect(rampeApparition(0)).toBe(0);
    expect(rampeApparition(0.3)).toBeCloseTo(0.5);
    expect(rampeApparition(0.6)).toBe(1);
    expect(rampeDisparition(0.5)).toBe(1);
  });
});

describe('etatA', () => {
  const a = { boite: boite(0, 0, 100, 50), opacite: 1, echelle: 0.5 };
  const b = { boite: boite(100, 100, 300, 150), opacite: 1, echelle: 1 };
  it('interpole boîte et échelle, aux deux bouts exactement', () => {
    expect(etatA(a, b, 0)).toEqual(a);
    expect(etatA(a, b, 1)).toEqual(b);
    expect(etatA(a, b, 0.5).boite).toEqual(boite(50, 50, 200, 100));
    expect(etatA(a, b, 0.5).echelle).toBe(0.75);
  });
  it('fait paraître ou disparaître en fondu', () => {
    expect(etatA({ ...a, opacite: 0 }, b, 0.3).opacite).toBeCloseTo(0.5);
    expect(etatA(a, { ...b, opacite: 0 }, 0.25).opacite).toBeCloseTo(0.5);
  });
});

describe('dansCadre', () => {
  const ouvert = boite(100, 100, 600, 300);
  const replie = boite(100, 100, 200, 100);
  it('le contenu d’un cadre qui se replie rapetisse avec lui et reste dedans', () => {
    const r = dansCadre(boite(400, 250, 212, 104), ouvert, replie);
    expect(r.echelle).toBeCloseTo(1 / 3);
    expect(r.boite.x).toBeCloseTo(100 + 300 / 3);
    expect(r.boite.y).toBeCloseTo(100 + 150 / 3);
    expect(r.boite.x + r.boite.largeur).toBeLessThanOrEqual(replie.x + replie.largeur);
    expect(r.boite.y + r.boite.hauteur).toBeLessThanOrEqual(replie.y + replie.hauteur);
  });
  it('un cercle de la bordure reste sur la bordure, à sa taille', () => {
    // Cercle centré sur le bord bas du cadre ouvert.
    const r = dansCadre(boite(300, 384, 132, 32), ouvert, replie, 'bord');
    expect(r.echelle).toBe(1);
    expect(r.boite.largeur).toBe(132);
    expect(r.boite.y + r.boite.hauteur / 2).toBeCloseTo(replie.y + replie.hauteur);
  });
  it('le titre reste attaché au coin du cadre', () => {
    const r = dansCadre(boite(124, 82, 296, 36), ouvert, boite(50, 60, 200, 100), 'titre');
    expect(r.boite).toEqual(boite(74, 42, 296, 36));
  });
});

describe('routeSuivant', () => {
  const route = [
    { x: 50, y: 100 },
    { x: 50, y: 150 },
    { x: 250, y: 150 },
    { x: 250, y: 200 },
  ];
  const source = boite(0, 0, 100, 100);
  const cible = boite(200, 200, 100, 100);
  it('à l’arrivée, c’est exactement le tracé de référence', () => {
    expect(routeSuivant(route, source, source, cible, cible)).toEqual(route);
  });
  it('les deux bouts restent sur leurs nœuds quand ils bougent ou changent de taille', () => {
    const s2 = boite(20, -40, 100, 100);
    const c2 = boite(300, 260, 200, 50);
    const r = routeSuivant(route, source, s2, cible, c2);
    expect(r[0]).toEqual({ x: 70, y: 60 }); // milieu du bas de la source déplacée
    expect(r[r.length - 1]).toEqual({ x: 400, y: 260 }); // milieu du haut de la cible agrandie
  });
  it('un lien dont les deux nœuds glissent pareil glisse tout entier, sans se déformer', () => {
    const d = (b: Boite) => boite(b.x + 30, b.y + 10, b.largeur, b.hauteur);
    const r = routeSuivant(route, source, d(source), cible, d(cible));
    expect(r).toEqual(route.map((p) => ({ x: p.x + 30, y: p.y + 10 })));
  });
});

describe('cheminArrondi', () => {
  it('trace une ligne brisée aux coudes arrondis, du premier au dernier point', () => {
    const d = cheminArrondi([
      { x: 0, y: 0 },
      { x: 0, y: 50 },
      { x: 100, y: 50 },
    ]);
    expect(d.startsWith('M 0 0')).toBe(true);
    expect(d).toContain('Q 0 50 8 50');
    expect(d.endsWith('L 100 50')).toBe(true);
  });
  it('ignore les points confondus', () => {
    expect(
      cheminArrondi([
        { x: 0, y: 0 },
        { x: 0, y: 0 },
      ]),
    ).toBe('');
  });
});
