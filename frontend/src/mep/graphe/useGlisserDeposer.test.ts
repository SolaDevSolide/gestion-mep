import { cibleClavier, deplacementSansEffet, rangSous, vitesseDefilement, type Rang } from './useGlisserDeposer';

/** Trois lignes affichées (la dernière haute : fiche à plusieurs systèmes), séparées de 46 px, puis « Nouvelle ligne ». */
const RANGS: Rang[] = [
  { ligne: 0, haut: 100, bas: 186 },
  { ligne: 1, haut: 232, bas: 318 },
  { ligne: 2, haut: 364, bas: 520 },
  { ligne: 3, haut: 566, bas: 624 },
];

describe('rangSous', () => {
  it('sur une ligne, même loin de son centre (ligne haute), ou juste à son bord : cette ligne', () => {
    expect(rangSous(150, RANGS)).toEqual({ ligne: 0, nouvelle: false });
    // Bas d'une ligne haute : à 76 px du centre, ignoré auparavant (seuil de 60 px autour du centre).
    expect(rangSous(515, RANGS)).toEqual({ ligne: 2, nouvelle: false });
    expect(rangSous(192, RANGS)).toEqual({ ligne: 0, nouvelle: false });
    expect(rangSous(226, RANGS)).toEqual({ ligne: 1, nouvelle: false });
  });

  it('entre deux lignes, ou juste au-dessus de la première : une nouvelle ligne insérée là', () => {
    expect(rangSous(209, RANGS)).toEqual({ ligne: 1, nouvelle: true });
    expect(rangSous(341, RANGS)).toEqual({ ligne: 2, nouvelle: true });
    expect(rangSous(60, RANGS)).toEqual({ ligne: 0, nouvelle: true });
  });

  it('sous la dernière ligne (jusqu’à 60 px) : la dernière ; trop loin : rien', () => {
    expect(rangSous(660, RANGS)).toEqual({ ligne: 3, nouvelle: false });
    expect(rangSous(700, RANGS)).toBeNull();
    expect(rangSous(20, RANGS)).toBeNull();
    expect(rangSous(100, [])).toBeNull();
  });
});

describe('vitesseDefilement', () => {
  const vue = { left: 0, top: 200, right: 1000, bottom: 900 };
  it('nulle loin des bords, croissante en approchant d’un bord, plafonnée au-delà', () => {
    expect(vitesseDefilement({ x: 500, y: 500 }, vue)).toEqual({ dx: 0, dy: 0 });
    expect(vitesseDefilement({ x: 500, y: 890 }, vue).dy).toBeGreaterThan(0);
    expect(vitesseDefilement({ x: 500, y: 870 }, vue).dy).toBeLessThan(vitesseDefilement({ x: 500, y: 895 }, vue).dy);
    expect(vitesseDefilement({ x: 500, y: 980 }, vue).dy).toBe(18);
    expect(vitesseDefilement({ x: 500, y: 210 }, vue).dy).toBeLessThan(0);
    expect(vitesseDefilement({ x: 5, y: 500 }, vue).dx).toBeLessThan(0);
    expect(vitesseDefilement({ x: 995, y: 500 }, vue).dx).toBeGreaterThan(0);
  });

  it('zone trop petite : pas de défilement automatique', () => {
    expect(vitesseDefilement({ x: 5, y: 5 }, { left: 0, top: 0, right: 100, bottom: 100 })).toEqual({ dx: 0, dy: 0 });
  });
});

/** Lignes d'identifiants : [['a', 'b'], ['c'], ['d']]. */
const plan = (...lignes: string[][]) => {
  const ls = lignes.map((l) => l.map((id) => ({ id })));
  const de = new Map<string, number>();
  ls.forEach((l, r) => l.forEach((e) => de.set(e.id, r)));
  return [ls, de] as const;
};

describe('deplacementSansEffet', () => {
  const [ls, de] = plan(['a', 'b'], ['c'], ['d']);
  it('même place, ou seule sur la dernière ligne et lâchée sur « Nouvelle ligne »', () => {
    expect(deplacementSansEffet('a', { ligne: 0, index: 0, nouvelle: false }, ls, de)).toBe(true);
    expect(deplacementSansEffet('a', { ligne: 0, index: 1, nouvelle: false }, ls, de)).toBe(false);
    expect(deplacementSansEffet('d', { ligne: 3, index: 0, nouvelle: false }, ls, de)).toBe(true);
    expect(deplacementSansEffet('c', { ligne: 3, index: 0, nouvelle: false }, ls, de)).toBe(false);
  });

  it('nouvelle ligne : sans effet seulement pour une fiche déjà seule, juste au-dessus ou en dessous d’elle', () => {
    expect(deplacementSansEffet('c', { ligne: 1, index: 0, nouvelle: true }, ls, de)).toBe(true);
    expect(deplacementSansEffet('c', { ligne: 2, index: 0, nouvelle: true }, ls, de)).toBe(true);
    expect(deplacementSansEffet('c', { ligne: 0, index: 0, nouvelle: true }, ls, de)).toBe(false);
    expect(deplacementSansEffet('a', { ligne: 0, index: 0, nouvelle: true }, ls, de)).toBe(false);
  });
});

describe('cibleClavier', () => {
  const [ls, de] = plan(['a', 'b'], ['c'], ['d']);
  it('Alt + flèches : dans la ligne, ligne voisine, nouvelle ligne en bas', () => {
    expect(cibleClavier('a', 'ArrowRight', false, ls, de)).toEqual({ ligne: 0, index: 1, nouvelle: false });
    expect(cibleClavier('a', 'ArrowLeft', false, ls, de)).toBeNull();
    expect(cibleClavier('b', 'ArrowDown', false, ls, de)).toEqual({ ligne: 1, index: 1, nouvelle: false });
    expect(cibleClavier('c', 'ArrowUp', false, ls, de)).toEqual({ ligne: 0, index: 0, nouvelle: false });
    expect(cibleClavier('d', 'ArrowDown', false, ls, de)).toBeNull();
    expect(cibleClavier('c', 'ArrowDown', false, ls, de)).toEqual({ ligne: 2, index: 0, nouvelle: false });
  });

  it('Alt + Maj + ↑ / ↓ : sur une ligne à elle, au-dessus ou en dessous (de l’autre côté de la voisine si déjà seule)', () => {
    expect(cibleClavier('b', 'ArrowUp', true, ls, de)).toEqual({ ligne: 0, index: 0, nouvelle: true });
    expect(cibleClavier('b', 'ArrowDown', true, ls, de)).toEqual({ ligne: 1, index: 0, nouvelle: true });
    expect(cibleClavier('c', 'ArrowUp', true, ls, de)).toEqual({ ligne: 0, index: 0, nouvelle: true });
    expect(cibleClavier('c', 'ArrowDown', true, ls, de)).toEqual({ ligne: 3, index: 0, nouvelle: true });
    expect(cibleClavier('d', 'ArrowDown', true, ls, de)).toBeNull();
    expect(cibleClavier('a', 'Home', false, ls, de)).toBeNull();
  });
});
