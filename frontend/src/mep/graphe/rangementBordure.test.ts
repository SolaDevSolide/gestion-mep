import { describe, expect, it } from 'vitest';
import { construireModele, type ModeleGraphe } from './modeleGraphe';
import { positionsAbsolues, type Point, type Rangement } from './rangement';
import { simplifier } from './routage';
import { CERCLE_BORD, TITRE_BORD, grapheBordure, rangerBordure } from './rangementBordure';
import { faussesRelations, sautsSansJonctions } from './routageBordure';

// ---------- Données d'essai : étapes et sections minimales, comme le serveur les envoie (champs utiles au rangement).

interface CercleSection {
  id: string;
  sens: 'Entree' | 'Sortie';
  titre: string;
  ordre: number;
  etapes: string[];
  sources: string[];
}
interface Section {
  id: string;
  plan: 'Deploiement';
  titre: string;
  creeLe: string;
  placee: boolean;
  ports: CercleSection[];
}
interface Etape {
  id: string;
  code: string;
  plan: 'Deploiement';
  ligne: number;
  ordre: number;
  rang?: number;
  sectionId?: string | null;
  dependances: string[];
  portsAttendus?: string[];
  dependancesDerivees?: string[];
}

function etape(id: string, ligne: number, dependances: string[] = [], extra: Partial<Etape> = {}): Etape {
  return {
    id,
    code: id,
    plan: 'Deploiement',
    ligne,
    ordre: 0,
    rang: 0,
    sectionId: null,
    dependances,
    portsAttendus: [],
    dependancesDerivees: [],
    ...extra,
  };
}

function section(id: string, ports: Omit<CercleSection, 'id'>[], creeLe = '2026-01-01'): Section {
  return {
    id,
    plan: 'Deploiement',
    titre: id,
    creeLe,
    placee: true,
    ports: ports.map((p, i) => ({ ...p, id: `${id}-c${i}` })),
  };
}

/** Facturation (capture de Hugo) réduite : Tables (A1 → A2), Export (B1, B2, B3 derrière « Sauvegarde »), Fin. */
function facturation(deplier = true): ModeleGraphe {
  const etapes = [
    etape('A1', 0, [], { sectionId: 'T' }),
    etape('A2', 1, ['A1'], { sectionId: 'T' }),
    etape('B1', 1, ['A1'], { sectionId: 'X', dependancesDerivees: ['A1'] }),
    etape('B2', 1, ['A1'], { sectionId: 'X', dependancesDerivees: ['A1'] }),
    etape('B3', 2, ['B1'], { sectionId: 'X' }),
    etape('F', 3, ['A2', 'B2', 'B3'], { portsAttendus: ['T-c1', 'X-c1'], dependancesDerivees: ['A2', 'B2', 'B3'] }),
  ];
  const sections = [
    section('T', [
      { sens: 'Entree', titre: 'Début', ordre: 0, etapes: ['A1'], sources: [] },
      { sens: 'Sortie', titre: 'Tables migrées', ordre: 0, etapes: ['A2'], sources: [] },
      { sens: 'Sortie', titre: 'Sauvegarde faite', ordre: 1, etapes: ['A1'], sources: [] },
    ]),
    section(
      'X',
      [
        { sens: 'Entree', titre: 'Sauvegarde', ordre: 0, etapes: ['B1', 'B2'], sources: ['T-c2'] },
        { sens: 'Sortie', titre: 'Fin', ordre: 0, etapes: ['B2', 'B3', 'B1'], sources: [] },
      ],
      '2026-01-02',
    ),
  ];
  return construireModele({
    etapes: etapes as never,
    sections: sections as never,
    deplies: new Set(deplier ? ['T', 'X'] : []),
  });
}

const absolues = (m: ModeleGraphe, r: Rangement) =>
  positionsAbsolues(r, new Map(m.noeuds.map((n) => [n.id, n.parent])));
const sans = new Map();

/** Les liens de `ids` partent-ils du même point avec un premier segment commun (ou arrivent-ils ainsi, `fin`) ? */
function fondus(r: Rangement, ids: string[], fin: boolean): boolean {
  const tr = ids.map((id) => {
    const p = simplifier(r.routes.get(id) ?? []);
    return fin ? [...p].reverse() : p;
  });
  const a = tr[0][0];
  return tr.every(
    (t) =>
      t.length > 1 &&
      Math.hypot(t[0].x - a.x, t[0].y - a.y) < 1 &&
      Math.sign(t[1].x - t[0].x) === Math.sign(tr[0][1].x - tr[0][0].x) &&
      Math.sign(t[1].y - t[0].y) === Math.sign(tr[0][1].y - tr[0][0].y),
  );
}

describe('rangerBordure', () => {
  it('pose les cercles des sections dépliées sur la bordure, point sur le bord, étiquette dedans', async () => {
    const m = facturation();
    const r = await rangerBordure(m, sans);
    for (const n of m.noeuds.filter((x) => x.nature === 'cercle')) {
      const p = r.places.get(n.id);
      const cadre = r.places.get(n.parent ?? '');
      expect(p && cadre).toBeTruthy();
      if (!p || !cadre) continue;
      const bord = n.sens === 'Entree' ? p.y : p.y + p.hauteur - CERCLE_BORD.dehors - cadre.hauteur;
      expect(bord).toBe(n.sens === 'Entree' ? -CERCLE_BORD.dehors : 0);
      // Abscisse du point connue aussi par `cercles` (comme pour une section repliée).
      expect(r.cercles.get(n.cercleId ?? '')).toBe(p.x + p.largeur / 2);
    }
  });

  it('pose le titre du cadre sur la bordure haute, à gauche des cercles d’entrée', async () => {
    const m = facturation();
    const r = await rangerBordure(m, sans);
    const titre = r.places.get('t:X');
    const cercle = r.places.get('c:X-c0');
    expect(titre?.y).toBe(-TITRE_BORD.hauteur / 2);
    expect((titre?.x ?? 0) + (titre?.largeur ?? 0)).toBeLessThanOrEqual(cercle?.x ?? 0);
  });

  it('fait arriver les liens extérieurs sur le point de la bordure et partir les liens intérieurs sous l’étiquette', async () => {
    const m = facturation();
    const r = await rangerBordure(m, sans);
    const abs = absolues(m, r);
    const cadre = abs.get('s:X');
    const dehors = m.liens.find((l) => l.cible === 'c:X-c0');
    const dedans = m.liens.filter((l) => l.source === 'c:X-c0');
    expect(dehors && cadre).toBeTruthy();
    expect(r.routes.get(dehors?.id ?? '')?.at(-1)?.y).toBe(cadre?.y);
    for (const l of dedans) expect(r.routes.get(l.id)?.[0].y).toBe((cadre?.y ?? 0) + CERCLE_BORD.dedans);
  });

  it('fond les arrivées sur un même cercle et les départs d’un même cercle', async () => {
    const m = facturation();
    const r = await rangerBordure(m, sans);
    const versFin = m.liens.filter((l) => l.cible === 'c:X-c1').map((l) => l.id);
    const depuisSauvegarde = m.liens.filter((l) => l.source === 'c:X-c0').map((l) => l.id);
    expect(versFin.length).toBe(3);
    expect(fondus(r, versFin, true)).toBe(true);
    expect(fondus(r, depuisSauvegarde, false)).toBe(true);
  });

  it('fond les arrivées sur une carte (plusieurs préalables) et les départs d’une carte', async () => {
    const etapes = [etape('S1', 0), etape('S2', 0), etape('C1', 1, ['S1', 'S2']), etape('C2', 1, ['S1', 'S2'])];
    const m = construireModele({ etapes: etapes as never, sections: [], deplies: new Set() });
    const r = await rangerBordure(m, sans);
    expect(
      fondus(
        r,
        m.liens.filter((l) => l.cible === 'C1').map((l) => l.id),
        true,
      ),
    ).toBe(true);
    expect(
      fondus(
        r,
        m.liens.filter((l) => l.source === 'S1').map((l) => l.id),
        false,
      ),
    ).toBe(true);
  });

  it('ne fond jamais deux liens dont le tronc commun suggérerait une relation absente (groupes complets)', async () => {
    // S1 → C1, C2 ; S2 → C2, C3 : un seul bus ferait lire S1 → C3 et S2 → C1.
    const etapes = [
      etape('S1', 0),
      etape('S2', 0),
      etape('C1', 1, ['S1']),
      etape('C2', 1, ['S1', 'S2']),
      etape('C3', 1, ['S2']),
    ];
    const m = construireModele({ etapes: etapes as never, sections: [], deplies: new Set() });
    const g = grapheBordure(m, sans);
    const ports = (cible: string) =>
      g.graphe.edges?.filter((e) => e.targets[0].startsWith(cible)).map((e) => e.sources[0]) ?? [];
    // S1 part par deux ports : un pour {C1}, un pour {C2} ; idem S2.
    expect(new Set([...ports('C1'), ...ports('C2')]).size).toBe(3);
    const r = await rangerBordure(m, sans);
    expect(faussesRelations(r.routes, m.liens)).toEqual([]);
  });

  it('range sans erreur un lien direct qui entre dans une section dépliée (port implicite sur le bord)', async () => {
    const etapes = [
      etape('P', 0),
      etape('I1', 1, ['P'], { sectionId: 'Z' }),
      etape('I2', 2, ['I1'], { sectionId: 'Z' }),
      etape('Q', 3, ['I2']),
    ];
    const sections = [section('Z', [{ sens: 'Entree', titre: 'Début', ordre: 0, etapes: [], sources: [] }])];
    const m = construireModele({ etapes: etapes as never, sections: sections as never, deplies: new Set(['Z']) });
    const r = await rangerBordure(m, sans);
    const abs = absolues(m, r);
    for (const l of m.liens) {
      const pts = r.routes.get(l.id);
      expect.soft(pts, l.id).toBeTruthy();
      const cible = abs.get(l.cible);
      const place = r.places.get(l.cible);
      // Arrivée au milieu du haut de la carte cible.
      expect(pts?.at(-1)).toEqual({ x: (cible?.x ?? 0) + (place?.largeur ?? 0) / 2, y: cible?.y });
    }
  });

  it('respecte l’ordre du modèle dans une rangée (rang imposé), dedans comme dehors', async () => {
    const etapes = [
      etape('R', 0),
      etape('Z3', 1, ['R'], { rang: 1 }),
      etape('Z1', 1, ['R'], { rang: 2 }),
      etape('Z2', 1, ['R'], { rang: 3 }),
    ];
    const m = construireModele({ etapes: etapes as never, sections: [], deplies: new Set() });
    const r = await rangerBordure(m, sans);
    const x = (id: string) => r.places.get(id)?.x ?? NaN;
    expect(x('Z3')).toBeLessThan(x('Z1'));
    expect(x('Z1')).toBeLessThan(x('Z2'));
  });

  it('garde l’intérieur d’une section tel quel quand on ajoute une étape ailleurs', async () => {
    const avant = facturation();
    const r1 = await rangerBordure(avant, sans);
    const m2: ModeleGraphe = {
      noeuds: [...avant.noeuds, { id: 'N', nature: 'etape', parent: null, etapeId: 'N' }],
      liens: [
        ...avant.liens,
        { id: 'F:bas>N:haut', source: 'F', sourcePoignee: 'bas', cible: 'N', ciblePoignee: 'haut' },
      ],
    };
    const r2 = await rangerBordure(m2, sans);
    for (const n of avant.noeuds.filter((x) => x.parent === 's:X'))
      expect.soft(r2.places.get(n.id), n.id).toEqual(r1.places.get(n.id));
  });

  it('prend les tailles mesurées des cartes', async () => {
    const etapes = [etape('S1', 0), etape('C1', 1, ['S1'])];
    const m = construireModele({ etapes: etapes as never, sections: [], deplies: new Set() });
    const r = await rangerBordure(m, new Map([['C1', { largeur: 260, hauteur: 140 }]]));
    expect(r.places.get('C1')).toMatchObject({ largeur: 260, hauteur: 140 });
  });

  it('pose les cercles d’une section repliée sur son bord, dans l’ordre de la section', async () => {
    const m = facturation(false);
    const r = await rangerBordure(m, sans);
    const t = r.places.get('s:T');
    const migrees = r.cercles.get('T-c1') ?? NaN;
    const sauvegarde = r.cercles.get('T-c2') ?? NaN;
    expect(migrees).toBeGreaterThan(0);
    expect(sauvegarde).toBeLessThan(t?.largeur ?? 0);
    expect(migrees).toBeLessThan(sauvegarde);
  });
});

describe('sautsSansJonctions', () => {
  const routes = (o: Record<string, Point[]>) => new Map(Object.entries(o));
  it('saute un vrai croisement', () => {
    const s = sautsSansJonctions(
      routes({
        h: [
          { x: 0, y: 50 },
          { x: 200, y: 50 },
        ],
        v: [
          { x: 100, y: 0 },
          { x: 100, y: 100 },
        ],
      }),
    );
    expect(s.get('h')?.get(0)).toEqual([100]);
  });
  it('ne saute pas une jonction (un autre tracé y tourne : tronc commun d’une fusion)', () => {
    const s = sautsSansJonctions(
      routes({
        h: [
          { x: 0, y: 50 },
          { x: 200, y: 50 },
        ],
        v: [
          { x: 100, y: 0 },
          { x: 100, y: 100 },
        ],
        w: [
          { x: 100, y: 0 },
          { x: 100, y: 50 },
          { x: 200, y: 50 },
        ],
      }),
    );
    expect(s.get('h')).toBeUndefined();
  });
});

describe('faussesRelations', () => {
  it('signale un tronçon partagé par deux liens sans bout commun', () => {
    const liens = [
      { id: 'a', source: 'S1', sourcePoignee: 'bas', cible: 'C1', ciblePoignee: 'haut' },
      { id: 'b', source: 'S2', sourcePoignee: 'bas', cible: 'C2', ciblePoignee: 'haut' },
    ];
    const r = new Map<string, Point[]>([
      [
        'a',
        [
          { x: 0, y: 0 },
          { x: 0, y: 50 },
          { x: 100, y: 50 },
          { x: 100, y: 100 },
        ],
      ],
      [
        'b',
        [
          { x: 50, y: 0 },
          { x: 50, y: 50 },
          { x: 150, y: 50 },
          { x: 150, y: 100 },
        ],
      ],
    ]);
    expect(faussesRelations(r, liens)).toEqual(['S1>C2', 'S2>C1']);
  });

  it('une section dépliée vide garde la place de son titre et de ses cercles, sans chevauchement', async () => {
    const sections = [
      section('V', [
        { sens: 'Entree', titre: 'Début', ordre: 0, etapes: [], sources: [] },
        { sens: 'Sortie', titre: 'Fin', ordre: 0, etapes: [], sources: [] },
      ]),
    ];
    const m = construireModele({ etapes: [] as never, sections: sections as never, deplies: new Set(['V']) });
    const r = await rangerBordure(m, sans);
    const cadre = r.places.get('s:V');
    const titre = r.places.get('t:V');
    const debut = r.places.get('c:V-c0');
    const fin = r.places.get('c:V-c1');
    expect(cadre && titre && debut && fin).toBeTruthy();
    if (!cadre || !titre || !debut || !fin) return;
    expect(debut.x).toBeGreaterThanOrEqual(titre.x + titre.largeur);
    expect(cadre.largeur).toBeGreaterThanOrEqual(debut.x + debut.largeur);
    // Les deux bords ne se touchent pas : l'entrée est en haut, la sortie en bas, bien séparées.
    expect(fin.y - (debut.y + debut.hauteur)).toBeGreaterThan(CERCLE_BORD.hauteur);
  });

  it('vue d’une section : ses cercles sont des nœuds à leur taille, et les liens partent de leur point', async () => {
    const etapes = [etape('A1', 0, [], { sectionId: 'Z' }), etape('A2', 1, ['A1'], { sectionId: 'Z' })];
    const sections = [
      section('Z', [
        { sens: 'Entree', titre: 'Début', ordre: 0, etapes: ['A1'], sources: [] },
        { sens: 'Sortie', titre: 'Fin', ordre: 0, etapes: ['A2'], sources: [] },
      ]),
    ];
    const m = construireModele({
      etapes: etapes as never,
      sections: sections as never,
      deplies: new Set(),
      sectionVue: 'Z',
    });
    const r = await rangerBordure(m, sans);
    const debut = r.places.get('c:Z-c0');
    expect(debut).toMatchObject({ largeur: CERCLE_BORD.largeur, hauteur: CERCLE_BORD.hauteur });
    const lien = m.liens.find((l) => l.source === 'c:Z-c0');
    const depart = lien && r.routes.get(lien.id)?.[0];
    expect(depart?.y).toBe((debut?.y ?? 0) + (debut?.hauteur ?? 0));
  });
});

describe('repli après un dépli : même rangement qu’au chargement', () => {
  it('une mesure de section prise pendant un mouvement ne change ni sa forme ni le rangement', async () => {
    const m = facturation(false);
    const carte = { largeur: 212, hauteur: 126 };
    const neuf = new Map([
      ['F', carte],
      ['s:T', { largeur: 288, hauteur: 136 }],
      ['s:X', { largeur: 288, hauteur: 136 }],
    ]);
    // Largeur du cadre déplié (ou d'une image du mouvement) gardée pour la section repliée.
    const apresMouvement = new Map(neuf).set('s:X', { largeur: 624, hauteur: 136 });
    const a = await rangerBordure(m, neuf);
    const b = await rangerBordure(m, apresMouvement);
    expect(Object.fromEntries(b.places)).toEqual(Object.fromEntries(a.places));
    expect(b.places.get('s:X')?.largeur).toBe(288);
  });
});
