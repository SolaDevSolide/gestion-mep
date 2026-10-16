import { hoteMesure, mesuresManquantes, vueMontrant } from './mesuresGraphe';
import type { ModeleGraphe, NoeudModele } from './modeleGraphe';

const n = (id: string, nature: NoeudModele['nature'], parent: string | null = null): NoeudModele => ({
  id,
  nature,
  parent,
});

describe('mesuresManquantes', () => {
  it('liste les étapes et sections repliées pas encore mesurées, pas les cadres, titres ni cercles', () => {
    const modele: ModeleGraphe = {
      noeuds: [
        n('s:a', 'cadre'),
        n('t:a', 'titre', 's:a'),
        n('c:x', 'cercle', 's:a'),
        n('e1', 'etape', 's:a'),
        n('e2', 'etape', 's:a'),
        n('s:b', 'section'),
        n('e3', 'etape'),
      ],
      liens: [],
    };
    const tailles = new Map([['e3', { largeur: 212, hauteur: 104 }]]);
    expect(mesuresManquantes(modele, tailles).map((x) => x.id)).toEqual(['e1', 'e2', 's:b']);
  });
});

describe('hoteMesure', () => {
  const affiches = new Set(['s:a', 'e9']);
  it("mesure l'intérieur d'une section qu'on déplie dans la section affichée", () => {
    expect(hoteMesure(n('e1', 'etape', 's:a'), affiches, 'e9')).toBe('s:a');
  });
  it('mesure une section qu’on replie dans son cadre encore affiché', () => {
    expect(hoteMesure(n('s:a', 'section'), affiches, 'e9')).toBe('s:a');
  });
  it('sinon, dans le premier nœud affiché', () => {
    expect(hoteMesure(n('e5', 'etape'), affiches, 'e9')).toBe('e9');
    expect(hoteMesure(n('e5', 'etape'), new Set(), null)).toBeNull();
  });
});

describe('vueMontrant', () => {
  const zone = { largeur: 1000, hauteur: 600 };
  it('ne bouge pas quand la boîte est déjà visible', () => {
    const vue = { x: 0, y: 0, zoom: 1 };
    expect(vueMontrant(vue, { x: 100, y: 100, largeur: 300, hauteur: 200 }, zone, 24, 0.25)).toEqual(vue);
  });
  it('déplace au minimum pour montrer une boîte qui dépasse en bas', () => {
    const v = vueMontrant({ x: 0, y: 0, zoom: 1 }, { x: 100, y: 500, largeur: 300, hauteur: 200 }, zone, 24, 0.25);
    expect(v).toEqual({ x: 0, y: 600 - 24 - 700, zoom: 1 });
  });
  it('dézoome seulement si la boîte ne tient pas, et la centre', () => {
    const v = vueMontrant({ x: 0, y: 0, zoom: 1 }, { x: 0, y: 0, largeur: 1904, hauteur: 400 }, zone, 24, 0.25);
    expect(v.zoom).toBeCloseTo(0.5);
    expect(v.x).toBeCloseTo(500 - 952 * 0.5);
    expect(v.y).toBeCloseTo(300 - 200 * 0.5);
  });
  it('ne dézoome pas sous le minimum ; la boîte trop haute est montrée par le haut', () => {
    const v = vueMontrant({ x: 0, y: 0, zoom: 1 }, { x: 0, y: 100, largeur: 400, hauteur: 10000 }, zone, 24, 0.25);
    expect(v.zoom).toBe(0.25);
    expect(v.y).toBe(24 - 100 * 0.25);
  });
});
