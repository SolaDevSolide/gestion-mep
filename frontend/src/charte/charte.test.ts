import css from '../index.css?raw';
import {
  appliquerCorrection,
  CHARTE_DEFAUT,
  controleReussi,
  controles,
  couleurRole,
  ecarts,
  jetons,
  lireFichier,
  lireTexte,
  memeCharte,
  paletteExcel,
  styleApercu,
  versFichier,
  type Charte,
  type CharteFichier,
} from './charte';
import { ajusterPourContraste, contraste, formatRapport, normaliserHex, texteSur } from './couleurs';
import { DEFAUT_CLAIR, DEFAUT_SOMBRE } from './defauts';
import vecteurs from './vecteurs-charte.json';

const lue = (f: unknown): Charte => {
  const c = lireFichier(f).charte;
  if (!c) throw new Error('charte invalide');
  return c;
};

describe('cas partagés avec le serveur (vecteurs-charte.json)', () => {
  // Le serveur (CharteVisuelleTests) passe les mêmes cas : l'aperçu de la console montre ce que le serveur appliquera.
  it.each(vecteurs.derivations.map((d) => [d.nom, d] as const))('calcule « %s » comme le serveur', (_, d) => {
    const c = lue(d.charte);
    expect({
      primaire: couleurRole(c, 'primaire', 'sombre'),
      secondaire: couleurRole(c, 'secondaire', 'sombre'),
      tertiaire: couleurRole(c, 'tertiaire', 'sombre'),
    }).toEqual(d.couleursSombre);
    expect({ clair: ecarts(c, 'clair'), sombre: ecarts(c, 'sombre') }).toEqual(d.ecarts);
    expect(
      controles(c).map((x) => ({
        id: x.id,
        type: x.type,
        role: x.role,
        rapport: Math.round(x.rapport * 1e6) / 1e6,
        minimum: x.minimum,
        reussi: controleReussi(x),
        ...(x.correction ? { correction: x.correction } : {}),
      })),
    ).toEqual(d.controles);
    expect(paletteExcel(c)).toEqual(d.excel);
  });

  it.each(vecteurs.lectures.map((l) => [l.nom, l] as const))('lit « %s » comme le serveur', (_, l) => {
    const r = lireTexte(l.texte);
    expect(r.erreurs.map((e) => ({ code: e.code, champ: e.champ }))).toEqual(l.erreurs);
    expect(r.avertissements.map((e) => ({ code: e.code, champ: e.champ }))).toEqual(l.avertissements);
    expect(r.charte ? versFichier(r.charte) : null).toEqual(l.charte);
  });
});

/** Déclarations d'un bloc de index.css, telles qu'écrites. */
function blocIndexCss(selecteur: string): Record<string, string> {
  const debut = css.indexOf(`${selecteur} {`);
  const bloc = css.slice(debut + selecteur.length + 2, css.indexOf('}', debut)).replace(/\/\*[\s\S]*?\*\//g, '');
  return Object.fromEntries([...bloc.matchAll(/--([a-z0-9-]+):\s*([^;]+);/g)].map((m) => [m[1], m[2].trim()]));
}

/** Valeurs calculées d'un thème : `var(--x)` résolu dans ce thème (comme le navigateur sur :root). */
function resoudre(brut: Record<string, string>): Record<string, string> {
  const valeur = (v: string): string => {
    const m = /^var\(--([a-z0-9-]+)\)$/.exec(v);
    return m ? valeur(brut[m[1]]) : v;
  };
  return Object.fromEntries(Object.entries(brut).map(([k, v]) => [k, valeur(v)]));
}

describe('charte par défaut', () => {
  it('reprend exactement les couleurs de index.css, dans les deux thèmes', () => {
    const racine = blocIndexCss(':root');
    const clair = resoudre(racine);
    const sombre = resoudre({ ...racine, ...blocIndexCss(':root.dark') });
    for (const [k, v] of Object.entries(DEFAUT_CLAIR)) expect([k, clair[k]]).toEqual([k, v]);
    for (const [k, v] of Object.entries(DEFAUT_SOMBRE)) expect([k, sombre[k]]).toEqual([k, v]);
  });

  it('ne change aucun jeton et passe tous les contrôles', () => {
    expect(ecarts(CHARTE_DEFAUT, 'clair')).toEqual({});
    expect(ecarts(CHARTE_DEFAUT, 'sombre')).toEqual({});
    expect(jetons(CHARTE_DEFAUT, 'sombre')).toEqual(DEFAUT_SOMBRE);
    expect(controles(CHARTE_DEFAUT).every(controleReussi)).toBe(true);
  });

  it('garde les couleurs des classeurs Excel d’avant la charte', () => {
    const p = paletteExcel(CHARTE_DEFAUT);
    expect([p.primaire, p.secondaire, p.tertiaire]).toEqual(['#4f46e5', '#4338ca', '#eceafd']);
  });
});

describe('lecture et écriture du fichier', () => {
  it('normalise les codes, garde « primaire » et relit ce qu’il écrit', () => {
    const c = lue({ couleurs: { primaire: '0F766E', secondaire: '#123', tertiaire: 'Primaire' } });
    expect(c.couleurs).toEqual({ primaire: '#0f766e', secondaire: '#112233', tertiaire: 'primaire' });
    const f: CharteFichier = versFichier({ ...c, sombre: { primaire: '#14b8a6' }, etats: { succes: '#15803d' } });
    expect(f.couleurs).toEqual({ primaire: '#0F766E', secondaire: '#112233', tertiaire: 'primaire' });
    expect(f.sombre).toEqual({ primaire: '#14B8A6' });
    expect(
      memeCharte(lue(JSON.parse(JSON.stringify(f))), {
        ...c,
        sombre: { primaire: '#14b8a6' },
        etats: { succes: '#15803d' },
      }),
    ).toBe(true);
  });

  it('refuse une couleur invalide ou une valeur hors liste, ignore une clé inconnue', () => {
    const r = lireFichier({ couleurs: { primaire: 'bleu' }, menuLateral: 'sombre', police: 'Marianne' });
    expect(r.charte).toBeNull();
    expect(r.erreurs.map((e) => e.code)).toEqual(['couleur', 'valeur']);
    expect(r.avertissements.map((e) => e.champ)).toEqual(['police']);
    expect(lireTexte('{ pas du json').erreurs[0].code).toBe('json');
  });
});

describe('couleurs', () => {
  it('lit les codes hexadécimaux', () => {
    expect(normaliserHex(' #ABC ')).toBe('#aabbcc');
    expect(normaliserHex('0f766e')).toBe('#0f766e');
    expect(normaliserHex('#12345')).toBeNull();
  });

  it('calcule le contraste WCAG et le texte lisible', () => {
    expect(contraste('#000000', '#ffffff')).toBeCloseTo(21, 5);
    expect(formatRapport(4.4999)).toBe('4,4:1');
    expect(texteSur('#0f766e')).toBe('#ffffff');
    expect(texteSur('#facc15')).toBe('#16161a');
  });

  it('corrige une couleur en gardant sa teinte, en l’éloignant du fond', () => {
    const pale = '#f0a6f5';
    const corrigee = ajusterPourContraste(pale, '#ffffff', 3);
    expect(contraste(corrigee, '#ffffff')).toBeGreaterThanOrEqual(3);
    // Plus claire que le fond sombre : éclaircie, pas assombrie.
    const surSombre = ajusterPourContraste('#3b3b5c', '#121318', 3);
    expect(contraste(surSombre, '#121318')).toBeGreaterThanOrEqual(3);
  });
});

describe('contrôles de lisibilité', () => {
  it('signale un cadre trop pâle et propose une correction qui passe', () => {
    const c = lue({ couleurs: { primaire: '#2F6FE0', tertiaire: '#F0A6F5' } });
    const aRevoir = controles(c).filter((x) => !controleReussi(x));
    expect(aRevoir.map((x) => x.id)).toEqual(['cadre-clair', 'distinction-primaire-info']);
    const corrigee = appliquerCorrection(c, aRevoir[0]);
    expect(controles(corrigee).find((x) => x.id === 'cadre-clair')?.rapport).toBeGreaterThanOrEqual(3);
  });

  it('contrôle le menu latéral seulement quand il est en secondaire', () => {
    const c = lue({ couleurs: { primaire: '#0F766E', secondaire: '#1B2A41' } });
    expect(controles(c).some((x) => x.id.startsWith('menu'))).toBe(false);
    expect(controles({ ...c, menuLateral: 'secondaire' }).some((x) => x.id === 'menu-clair')).toBe(true);
  });

  it('pose les jetons de l’aperçu en variables CSS', () => {
    const style = styleApercu(lue({ couleurs: { primaire: '#0F766E' } }), 'clair');
    expect(style['--accent']).toBe('#0f766e');
    expect(style['--tertiaire']).toBe('#0f766e');
  });
});
