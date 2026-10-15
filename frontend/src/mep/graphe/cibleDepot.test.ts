import { chercherCible, fantomePour, MARGE_CARTE_PX, TOLERANCE_LIEN_PX, type Geometrie } from './cibleDepot';
import { deplacementPour, placePour } from './connexions';
import type { LienModele } from './modeleGraphe';

const lien = (source: string, cible: string): LienModele => ({
  id: `${source}>${cible}`,
  source,
  sourcePoignee: 'bas',
  cible,
  ciblePoignee: 'haut',
  logique: { de: source, vers: cible },
});

// A (0,0) → B (0,300), lien vertical x = 100 ; C seule à droite (400,0) ; un cercle de sortie sous C.
const geo: Geometrie = {
  cartes: [
    { id: 'a', x: 0, y: 0, largeur: 200, hauteur: 100 },
    { id: 'b', x: 0, y: 300, largeur: 200, hauteur: 100 },
    { id: 'c', x: 400, y: 0, largeur: 200, hauteur: 100 },
  ],
  cercles: [{ id: 'fin', noeud: 'c:fin', x: 440, y: 400, largeur: 120, hauteur: 32 }],
  liens: [
    {
      lien: lien('a', 'b'),
      route: [
        { x: 100, y: 100 },
        { x: 100, y: 300 },
      ],
    },
  ],
};

describe('Cible d’un dépôt', () => {
  it('sur une carte : après elle ; sur un cercle : après lui', () => {
    expect(chercherCible({ x: 50, y: 50 }, geo, 1)).toEqual({ type: 'carte', id: 'a' });
    expect(chercherCible({ x: 500, y: 410 }, geo, 1)).toEqual({ type: 'cercle', id: 'fin', noeud: 'c:fin' });
  });

  it('près d’un lien, dans une zone d’accroche large (indépendante du zoom à l’écran)', () => {
    const pres = { x: 100 + TOLERANCE_LIEN_PX - 2, y: 200 };
    expect(chercherCible(pres, geo, 1)).toMatchObject({ type: 'lien', lien: { id: 'a>b' } });
    // Dézoomé de moitié : la même distance à l'écran vaut deux fois plus dans le graphe.
    expect(chercherCible({ x: 100 + 2 * TOLERANCE_LIEN_PX - 4, y: 200 }, geo, 0.5)).toMatchObject({ type: 'lien' });
    expect(chercherCible({ x: 100 + TOLERANCE_LIEN_PX + 4, y: 200 }, geo, 1)?.type).not.toBe('lien');
  });

  it('de deux liens proches, le plus proche l’emporte', () => {
    const deux: Geometrie = {
      ...geo,
      liens: [
        ...geo.liens,
        {
          lien: lien('a', 'x'),
          route: [
            { x: 120, y: 100 },
            { x: 120, y: 300 },
          ],
        },
      ],
    };
    expect(chercherCible({ x: 104, y: 200 }, deux, 1)).toMatchObject({ lien: { id: 'a>b' } });
    expect(chercherCible({ x: 116, y: 200 }, deux, 1)).toMatchObject({ lien: { id: 'a>x' } });
  });

  it('sous une carte sans lien : après elle ; sur son côté : en parallèle', () => {
    expect(chercherCible({ x: 500, y: 100 + MARGE_CARTE_PX / 2 }, geo, 1)).toEqual({ type: 'carte', id: 'c' });
    expect(chercherCible({ x: 600 + MARGE_CARTE_PX / 2, y: 50 }, geo, 1)).toEqual({
      type: 'carte',
      id: 'c',
      parallele: true,
    });
    expect(chercherCible({ x: 1000, y: 1000 }, geo, 1)).toBeNull();
  });

  it('se traduit en place de création et en déplacement', () => {
    expect(placePour({ type: 'carte', id: 'c', parallele: true })).toEqual({ paralleleA: 'c' });
    expect(placePour({ type: 'carte', id: 'c' })).toEqual({ apres: 'c' });
    expect(placePour({ type: 'cercle', id: 'fin', noeud: 'c:fin' })).toEqual({ apres: 'fin' });
    expect(placePour({ type: 'lien', lien: lien('a', 'b') })).toEqual({ entre: { de: 'a', vers: 'b' } });
    expect(deplacementPour('x', { type: 'carte', id: 'c', parallele: true })).toEqual({ paralleleA: 'c' });
    expect(deplacementPour('x', { type: 'cercle', id: 'fin', noeud: 'c:fin' })).toEqual({ apres: 'fin' });
  });
});

describe('Aperçu du dépôt', () => {
  const boites = new Map(geo.cartes.map((c) => [c.id, c]));
  const code = (id: string) => id.toUpperCase();

  it('après une carte : sous elle, reliée à elle', () => {
    const f = fantomePour({ type: 'carte', id: 'c' }, boites, geo.cercles, [], new Map(), code);
    expect(f?.libelle).toBe('Après C');
    expect(f?.boite.y).toBeGreaterThan(100);
    expect(f?.liens).toEqual([{ de: { x: 500, y: 100 }, vers: { x: 500, y: f?.boite.y } }]);
  });

  it('en parallèle : à côté, avec les mêmes liens que la carte', () => {
    const liens = [lien('a', 'b')];
    const routes = new Map([['a>b', geo.liens[0].route]]);
    const f = fantomePour({ type: 'carte', id: 'b', parallele: true }, boites, [], liens, routes, code);
    expect(f?.libelle).toBe('En parallèle de B');
    expect(f?.boite.x).toBeGreaterThan(200);
    expect(f?.liens).toHaveLength(1);
    expect(f?.liens[0].de).toEqual({ x: 100, y: 100 });
  });

  it('sur un lien : au milieu, entre ses deux bouts', () => {
    const routes = new Map([['a>b', geo.liens[0].route]]);
    const f = fantomePour({ type: 'lien', lien: lien('a', 'b') }, boites, [], [], routes, code);
    expect(f?.liens.map((l) => [l.de, l.vers])).toEqual([
      [
        { x: 100, y: 100 },
        { x: 100, y: f?.boite.y },
      ],
      [
        { x: 100, y: (f?.boite.y ?? 0) + (f?.boite.hauteur ?? 0) },
        { x: 100, y: 300 },
      ],
    ]);
  });
});
