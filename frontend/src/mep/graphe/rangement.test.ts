import { etape } from '../../test/fabrique';
import type { Etape } from '../../types';
import {
  construireModele,
  idCercle,
  idSection,
  idTitre,
  lienAutorise,
  ordreGlobal,
  reordonner,
  trierEtapes,
} from './modeleGraphe';
import { decalageAncrage, noeudAuCentre, positionsAbsolues, ranger, type Rangement } from './rangement';
import { compterCroisements } from './routage';
import type { Section } from './sections';

const e = (id: string, ligne: number, ordre: number, dependances: string[] = [], extra: Record<string, unknown> = {}) =>
  ({ ...etape({ id, code: id.toUpperCase(), ligne, ordre, dependances }), ...extra }) as Etape;

const section = (p: Partial<Section> & { id: string }): Section => ({
  rang: 0,
  plan: 'Deploiement',
  titre: 'Section',
  description: '',
  systemeIds: [],
  auteurId: 'u1',
  auteur: 'Alice',
  creeLe: '2026-10-15T08:00:00Z',
  placee: true,
  revision: 1,
  ports: [],
  ...p,
});

/** Ordre de gauche à droite des nœuds de chaque rangée (même ordonnée). */
function rangees(r: Rangement, ids: string[]): string[][] {
  const parY = new Map<number, string[]>();
  for (const id of ids) {
    const p = r.places.get(id);
    if (!p) continue;
    parY.set(p.y, [...(parY.get(p.y) ?? []), id]);
  }
  return [...parY.entries()]
    .sort((a, b) => a[0] - b[0])
    .map(([, l]) => l.sort((a, b) => (r.places.get(a)?.x ?? 0) - (r.places.get(b)?.x ?? 0)));
}

const sansSection = (etapes: Etape[]) => construireModele({ etapes, sections: [], deplies: new Set() });

describe('modèle du graphe libre', () => {
  it('liens directs dans l’ordre stable (ligne, ordre, code)', () => {
    const m = sansSection([e('c', 1, 0, ['a']), e('a', 0, 0), e('b', 0, 1)]);
    expect(m.noeuds.map((n) => n.id)).toEqual(['a', 'b', 'c']);
    expect(m.liens).toEqual([expect.objectContaining({ source: 'a', cible: 'c', logique: { de: 'a', vers: 'c' } })]);
  });

  it('section repliée : un cadre, ses cercles en poignées ; les liens passent par les cercles', () => {
    const s = section({
      id: 's1',
      ports: [
        { id: 'in', sens: 'Entree', titre: 'Début', description: '', ordre: 0, etapes: ['x'], sources: ['a'] },
        { id: 'out', sens: 'Sortie', titre: 'Fin', description: '', ordre: 0, etapes: ['y'], sources: [] },
      ],
    });
    const etapes = [
      e('a', 0, 0),
      e('x', 1, 0, ['a'], { sectionId: 's1', dependancesDerivees: ['a'] }),
      e('y', 2, 0, ['x'], { sectionId: 's1' }),
      e('b', 3, 0, ['y'], { portsAttendus: ['out'], dependancesDerivees: ['y'] }),
    ];
    const replie = construireModele({ etapes, sections: [s], deplies: new Set() });
    expect(replie.noeuds.map((n) => n.id)).toEqual(['a', idSection('s1'), 'b']);
    expect(replie.liens.map((l) => [l.source, l.sourcePoignee, l.cible, l.ciblePoignee])).toEqual([
      ['a', 'bas', idSection('s1'), 'in'],
      [idSection('s1'), 'out', 'b', 'haut'],
    ]);

    const deplie = construireModele({ etapes, sections: [s], deplies: new Set(['s1']) });
    expect(deplie.noeuds.map((n) => [n.id, n.parent])).toEqual([
      ['a', null],
      [idSection('s1'), null],
      [idTitre('s1'), idSection('s1')],
      [idCercle('in'), idSection('s1')],
      ['x', idSection('s1')],
      ['y', idSection('s1')],
      [idCercle('out'), idSection('s1')],
      ['b', null],
    ]);
    expect(deplie.liens.map((l) => `${l.source}>${l.cible}`).sort()).toEqual(
      ['x>y', 'a>c:in', 'c:in>x', 'y>c:out', 'c:out>b'].sort(),
    );
  });

  it('section au bac : ni cadre ni étapes dans le plan ; vue de la section : ses étapes et ses cercles', () => {
    const s = section({
      id: 's1',
      placee: false,
      ports: [{ id: 'in', sens: 'Entree', titre: 'Début', description: '', ordre: 0, etapes: ['x'], sources: [] }],
    });
    const etapes = [e('a', 0, 0), e('x', 0, 1, [], { sectionId: 's1' })];
    expect(construireModele({ etapes, sections: [s], deplies: new Set() }).noeuds.map((n) => n.id)).toEqual(['a']);
    const vue = construireModele({ etapes, sections: [s], deplies: new Set(), sectionVue: 's1' });
    expect(vue.noeuds.map((n) => n.id)).toEqual([idCercle('in'), 'x']);
    expect(vue.liens.map((l) => `${l.source}>${l.cible}`)).toEqual(['c:in>x']);
  });

  it('lien autorisé : ni boucle, ni lien existant, ni cycle (plus de règle de ligne)', () => {
    const etapes = [e('a', 0, 0), e('b', 1, 0, ['a']), e('c', 2, 0, ['b'])];
    expect(lienAutorise(etapes, 'c', 'a')).toBe('cycle');
    expect(lienAutorise(etapes, 'a', 'b')).toBe('existe');
    expect(lienAutorise(etapes, 'a', 'a')).toBe('soi');
    expect(lienAutorise(etapes, 'a', 'c')).toBe('ok');
    // Une étape « plus bas » peut être le préalable d'une étape « plus haut » : seule compte l'absence de cycle.
    expect(lienAutorise([...etapes, e('d', 0, 1)], 'c', 'd')).toBe('ok');
  });
});

describe('rangement ELK', () => {
  it('de haut en bas ; chaque lien routé à angle droit, du bas de sa source au haut de sa cible', async () => {
    const modele = sansSection([e('a', 0, 0), e('b', 1, 0, ['a']), e('c', 1, 1, ['a']), e('d', 2, 0, ['b', 'c'])]);
    const r = await ranger(modele, new Map());
    const [a, b, d] = ['a', 'b', 'd'].map((id) => r.places.get(id)!);
    expect(a.y).toBeLessThan(b.y);
    expect(b.y).toBeLessThan(d.y);
    for (const l of modele.liens) {
      const pts = r.routes.get(l.id)!;
      const s = r.places.get(l.source)!;
      const t = r.places.get(l.cible)!;
      expect(pts[0].y).toBeCloseTo(s.y + s.hauteur, 0);
      expect(pts.at(-1)!.y).toBeCloseTo(t.y, 0);
      for (let i = 1; i < pts.length; i++)
        expect(Math.abs(pts[i].x - pts[i - 1].x) < 0.6 || Math.abs(pts[i].y - pts[i - 1].y) < 0.6).toBe(true);
    }
  });

  it('ordre d’entrée : chaque étape au barycentre de ses préalables (sans rang choisi)', () => {
    // c attend b, d attend a : dans l'ordre de création (c avant d), leurs liens se croiseraient.
    const plan = [e('a', 0, 0), e('b', 0, 1), e('c', 1, 0, ['b']), e('d', 1, 1, ['a'])];
    expect(trierEtapes(plan, false).map((x) => x.id)).toEqual(['a', 'b', 'd', 'c']);
    // Rang choisi à la main : il fait foi.
    const ordonne = plan.map((x, i) => ({ ...x, rang: [1, 2, 3, 4][i] }));
    expect(trierEtapes(ordonne, true).map((x) => x.id)).toEqual(['a', 'b', 'c', 'd']);
  });

  it('liens rangés sans croisement évitable ni superposition : départ commun, arrivées séparées', async () => {
    const plan = [
      e('a', 0, 0),
      e('b', 0, 1),
      e('c', 1, 0, ['b']),
      e('d', 1, 1, ['a']),
      e('f', 2, 0, ['c', 'd']),
      e('g', 2, 1, ['d']),
    ];
    const modele = sansSection(plan);
    const r = await ranger(modele, new Map());
    const bouts = new Map(modele.liens.map((l) => [l.id, { source: l.source, cible: l.cible }]));
    expect(compterCroisements(r.routes, bouts)).toEqual({ croisements: 0, superpositions: 0 });
    // Deux liens vers f : deux arrivées distinctes sur son bord haut.
    const versF = modele.liens.filter((l) => l.cible === 'f').map((l) => r.routes.get(l.id)!.at(-1)!.x);
    expect(new Set(versF.map((x) => Math.round(x))).size).toBe(2);
    // Deux liens depuis d : un seul départ, au milieu de son bord bas.
    const d = r.places.get('d')!;
    for (const l of modele.liens.filter((x) => x.source === 'd'))
      expect(r.routes.get(l.id)![0].x).toBeCloseTo(d.x + d.largeur / 2, 0);
  });

  it('ordre choisi (rang) : respecté dans la rangée, même contre l’ordre de création', async () => {
    const plan = [e('a', 0, 0, [], { rang: 1 }), e('b', 1, 0, ['a'], { rang: 3 }), e('c', 1, 1, ['a'], { rang: 2 })];
    const r = await ranger(sansSection(plan), new Map());
    expect(r.places.get('c')!.x).toBeLessThan(r.places.get('b')!.x);
  });

  it('stable : un ajout dans une branche n’échange pas l’ordre des autres cartes', async () => {
    const base = [
      e('a', 0, 0),
      e('b1', 1, 0, ['a']),
      e('b2', 1, 1, ['a']),
      e('b3', 1, 2, ['a']),
      e('c1', 2, 0, ['b1']),
      e('c3', 2, 1, ['b3']),
      e('z', 3, 0, ['c1', 'b2', 'c3']),
    ];
    const ids = base.map((x) => x.id);
    const avant = rangees(await ranger(sansSection(base), new Map()), ids);
    // Ajouts ailleurs : sous b2, puis une étape isolée, puis une insertion sur le lien b3 → c3.
    const ajouts: Etape[][] = [
      [...base, e('n1', 2, 2, ['b2'])],
      [...base, e('n2', 0, 1)],
      [...base.map((x) => (x.id === 'c3' ? e('c3', 3, 1, ['n3']) : x)), e('n3', 2, 2, ['b3'])],
    ];
    for (const [k, plan] of ajouts.entries()) {
      const r = await ranger(sansSection(plan), new Map());
      const apres = rangees(r, ids);
      // Deux cartes côte à côte avant comme après gardent leur ordre de gauche à droite (les rangées peuvent glisser).
      const inversions = avant.flatMap((rangee) =>
        rangee.flatMap((a, i) =>
          rangee.slice(i + 1).filter((b) => {
            const ensemble = apres.find((l) => l.includes(a) && l.includes(b));
            return !!ensemble && ensemble.indexOf(a) > ensemble.indexOf(b);
          }),
        ),
      );
      expect(inversions).toEqual([]);
      // Chaque carte reste à sa profondeur : une rangée ne perd aucune carte d'origine (hors insertion sur un lien).
      const rangeesApres = avant.map((rangee) =>
        apres.find((l) => l.includes(rangee[0]))?.filter((id) => ids.includes(id)),
      );
      expect(k === 2 || JSON.stringify(rangeesApres) === JSON.stringify(avant)).toBe(true);
      // Et chaque carte d'origine reste dans la même colonne relative : b1 à gauche de b3, c1 à gauche de c3.
      const x = (id: string) => r.places.get(id)!.x;
      expect(x('b1')).toBeLessThan(x('b3'));
      expect(x('c1')).toBeLessThan(x('c3'));
    }
  });

  it('section repliée avec ses cercles ; dépliée, un cadre qui contient ses étapes', async () => {
    const s = section({
      id: 's1',
      ports: [
        { id: 'in', sens: 'Entree', titre: 'Début', description: '', ordre: 0, etapes: ['x'], sources: ['a'] },
        { id: 'out', sens: 'Sortie', titre: 'Fin', description: '', ordre: 0, etapes: ['x'], sources: [] },
      ],
    });
    const etapes = [e('a', 0, 0), e('x', 1, 0, ['a'], { sectionId: 's1', dependancesDerivees: ['a'] })];
    const replie = await ranger(construireModele({ etapes, sections: [s], deplies: new Set() }), new Map());
    expect(replie.places.get(idSection('s1'))!.y).toBeGreaterThan(replie.places.get('a')!.y);
    expect(replie.cercles.has('in')).toBe(true);

    const modele = construireModele({ etapes, sections: [s], deplies: new Set(['s1']) });
    const deplie = await ranger(modele, new Map());
    const cadre = deplie.places.get(idSection('s1'))!;
    const x = deplie.places.get('x')!;
    expect(x.x).toBeGreaterThanOrEqual(0);
    expect(x.y + x.hauteur).toBeLessThanOrEqual(cadre.hauteur);
    expect(deplie.places.get(idCercle('in'))!.y).toBeLessThan(x.y);
    expect(deplie.places.get(idCercle('out'))!.y).toBeGreaterThan(x.y);
    const abs = positionsAbsolues(deplie, new Map(modele.noeuds.map((n) => [n.id, n.parent])));
    expect(abs.get('x')!.x).toBe(cadre.x + x.x);
  });
});

describe('ancrage de la vue', () => {
  it('compense le déplacement du nœud ancré, au zoom près', () => {
    const avant = new Map([['a', { x: 100, y: 100 }]]);
    const apres = new Map([['a', { x: 148, y: 244 }]]);
    expect(decalageAncrage(avant, apres, 'a', 0.5)).toEqual({ dx: -24, dy: -72 });
    expect(decalageAncrage(avant, apres, 'inconnu', 1)).toEqual({ dx: 0, dy: 0 });
  });

  it('sans sélection, le nœud le plus proche du centre de l’écran', () => {
    const pos = new Map([
      ['a', { x: 0, y: 0 }],
      ['b', { x: 400, y: 400 }],
    ]);
    expect(noeudAuCentre(pos, new Map(), { x: 480, y: 450 })).toBe('b');
  });
});

describe('ordre gauche-droite choisi', () => {
  it('ordre global : étapes et sections ; une section se place avant sa première étape tant que rien n’est choisi', () => {
    const s = section({ id: 's1' });
    const etapes = [e('a', 0, 0), e('x', 1, 0, ['a'], { sectionId: 's1' }), e('b', 1, 1, ['a'])];
    expect(ordreGlobal(etapes, [s])).toEqual(['a', 's1', 'x', 'b']);
    const ranges = [e('a', 0, 0, [], { rang: 2 }), e('b', 1, 1, ['a'], { rang: 1 })];
    expect(ordreGlobal(ranges, [section({ id: 's1', rang: 3 })])).toEqual(['b', 'a', 's1']);
  });

  it('réordonner une rangée ne touche qu’aux places de ses éléments', () => {
    expect(reordonner(['a', 'b', 'c', 'd', 'e'], ['b', 'd'], ['d', 'b'])).toEqual(['a', 'd', 'c', 'b', 'e']);
    expect(reordonner(['a', 'b', 'c'], ['a', 'b', 'c'], ['c', 'a', 'b'])).toEqual(['c', 'a', 'b']);
  });
});
