import { ancrageFiche, calculerAncrages, geometrieRetenue, type Geometrie, type Rectangle } from './useMesureCartes';

/** Géométrie d'un graphe de largeur `largeur` (par défaut celle de la zone) : une fiche au milieu. */
const geo = (zone: number, largeur = zone): Geometrie => ({
  ancrages: { a: { x: largeur / 2, haut: 28, bas: 140 } },
  largeur,
  hauteur: 1000,
  zone,
});

const rect = (left: number, top: number, width: number, height: number): Rectangle => ({ left, top, width, height });

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
    const retenue = geometrieRetenue(affichee, geo(1124), geo(1124));
    expect([retenue.largeur, retenue.hauteur, retenue.zone]).toEqual([1114, 1000, 1114]);
    // La mesure suivante (même mise en page) ne relance plus de rendu.
    expect(geometrieRetenue(retenue, geo(1124), geo(1124))).toBe(retenue);
  });

  it('oscillation arrêtée : les liens suivent quand même la position réelle des fiches (issue #44)', () => {
    // Avant : la géométrie entière était figée, liens compris, sur la mise en page à 1114 px alors que le graphe
    // occupait 1124 px : toutes les extrémités de liens 5 px à gauche du centre des ronds.
    const affichee = geo(1124, 1114);
    const mesuree = geo(1124);
    const retenue = geometrieRetenue(affichee, geo(1124), mesuree);
    expect(retenue.ancrages.a.x).toBe(562);
    expect(retenue.ancrages).toEqual(mesuree.ancrages);
    expect(retenue.largeur).toBe(1114);
  });

  it('reprend des ancrages qui changent sans changement de taille (fiche qui grandit dans sa ligne)', () => {
    const affichee = geo(1124);
    const mesuree: Geometrie = { ...geo(1124), ancrages: { a: { x: 562, haut: 28, bas: 158 } } };
    expect(geometrieRetenue(affichee, geo(1000), mesuree)).toEqual(mesuree);
  });
});

describe('ancrageFiche', () => {
  // Zone à 281 px du bord avec une bordure de 1 px et une barre de défilement verticale à droite : le SVG des liens est
  // à l'origine du graphe (282, 271) ; la fiche (212 px) est centrée dans le graphe de 1124 px.
  const svg = rect(282, 271, 1124, 970);
  const fiche = rect(282 + 456, 271 + 28, 212, 112);

  it('centre horizontal et bords haut / bas dans le repère du SVG', () => {
    expect(ancrageFiche(fiche, svg, 1)).toEqual({ x: 562, haut: 28, bas: 140 });
  });

  it('ne dépend ni de la bordure, ni de la barre de défilement, ni du défilement de la zone', () => {
    // Bordure de 2 px (plan de retour arrière), zone défilée de 37 px vers la droite et de 50 px vers le bas :
    // fiche et SVG bougent ensemble à l'écran, l'ancrage ne change pas.
    const decale = (r: Rectangle): Rectangle => rect(r.left + 1 - 37, r.top + 1 - 50, r.width, r.height);
    expect(ancrageFiche(decale(fiche), decale(svg), 1)).toEqual({ x: 562, haut: 28, bas: 140 });
  });

  it('ramène les mesures à l’échelle 1 quand le graphe est zoomé', () => {
    // Zoom 0,5 (« Ajuster ») : tout est deux fois plus petit à l'écran.
    const z = 0.5;
    const ficheZ = rect(282 + 456 * z, 271 + 28 * z, 212 * z, 112 * z);
    expect(ancrageFiche(ficheZ, rect(282, 271, 1124 * z, 970 * z), z)).toEqual({ x: 562, haut: 28, bas: 140 });
    // Zoom 1,2 (+).
    const z2 = 1.2;
    const ficheZ2 = rect(282 + 456 * z2, 271 + 28 * z2, 212 * z2, 112 * z2);
    const a = ancrageFiche(ficheZ2, rect(282, 271, 1124 * z2, 970 * z2), z2);
    expect(a.x).toBeCloseTo(562);
    expect(a.haut).toBeCloseTo(28);
    expect(a.bas).toBeCloseTo(140);
  });

  it('chaque fiche garde son propre bord bas, même plus courte que ses voisines', () => {
    const ancrages = calculerAncrages(
      [
        { id: 'E0002', rect: rect(282 + 456, 299, 212, 112) },
        { id: 'E0003', rect: rect(282 + 690, 299, 212, 94) },
      ],
      svg,
      1,
    );
    expect(ancrages.E0002).toEqual({ x: 562, haut: 28, bas: 140 });
    expect(ancrages.E0003).toEqual({ x: 796, haut: 28, bas: 122 });
  });
});
