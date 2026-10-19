import css from '../index.css?raw';

/**
 * Garde-fou de la charte : les tailles de texte, les couleurs de texte posées sur un fond plein et les étages (z-index)
 * passent par les jetons de index.css. Ce test échoue si une taille arbitraire `text-[…px]`, un `text-white`, une couleur
 * nommée de Tailwind ou un `z-[…]` revient dans le code de l'application (tests exclus).
 */
const BRUTS = import.meta.glob<string>(['../**/*.{ts,tsx}', '!../**/*.test.{ts,tsx}', '!../test/**', '!../**/*.d.ts'], {
  query: '?raw',
  import: 'default',
  eager: true,
});

const SOURCES = Object.entries(BRUTS).map(([chemin, texte]) => ({
  fichier: chemin.replace(/^\.\.\//, ''),
  lignes: texte.split('\n'),
}));

/** Lignes « fichier:numéro : extrait » où `motif` apparaît, hors des fichiers autorisés. */
function occurrences(motif: RegExp, autorises: string[] = []): string[] {
  return SOURCES.filter((s) => !autorises.includes(s.fichier)).flatMap((s) =>
    s.lignes.flatMap((l, i) => [...l.matchAll(motif)].map((m) => `${s.fichier}:${i + 1} : ${m[0]}`)),
  );
}

/** Aperçu et sélecteur de la charte : ils dessinent sur la couleur choisie, qui n'est pas un jeton. */
const CHARTE = ['charte/SelecteurCouleur.tsx', 'charte/ApercuCharte.tsx', 'admin/CharteTab.tsx'];

describe('échelles et jetons de la charte', () => {
  it('lit bien le code de l’application', () => {
    expect(SOURCES.some((s) => s.fichier === 'ui/Button.tsx')).toBe(true);
    expect(SOURCES.some((s) => s.fichier.endsWith('.test.ts'))).toBe(false);
  });

  it('aucune taille de texte arbitraire en px : l’échelle text-2xs … text-2xl', () => {
    expect(occurrences(/\btext-\[\d+(\.\d+)?px\]/g)).toEqual([]);
  });

  it('aucun texte blanc écrit en dur : le jeton du fond (-solid-fg, -fg) ou texteSur()', () => {
    expect(occurrences(/\btext-white\b/g, CHARTE)).toEqual([]);
    expect(occurrences(/\bbg-black\/\d+/g, CHARTE)).toEqual([]);
  });

  it('aucune couleur nommée de Tailwind : les jetons de index.css', () => {
    const couleurs =
      'slate|gray|zinc|neutral|stone|red|orange|amber|yellow|lime|green|emerald|teal|cyan|sky|blue|indigo|violet|purple|fuchsia|pink|rose';
    expect(occurrences(new RegExp(`\\b(text|bg|border|ring|fill|stroke)-(${couleurs})-\\d{2,3}\\b`, 'g'))).toEqual([]);
  });

  it('aucun z-index arbitraire : les étages z-graphe … z-toast', () => {
    expect(occurrences(/\bz-\[[^\]]+\]/g)).toEqual([]);
  });

  it('les jetons de l’échelle existent dans index.css', () => {
    for (const t of ['--text-2xs:', '--text-ui:', '--text-md:'])
      expect(css.slice(css.indexOf('@theme inline'))).toContain(t);
    const valeurs = ['graphe', 'panneau', 'dialogue', 'menu', 'toast'].map((n) =>
      Number(new RegExp(`--z-${n}: (\\d+);`).exec(css)?.[1]),
    );
    // Du plus bas au plus haut : un menu s'ouvre au-dessus du dialogue qui le contient.
    expect(valeurs.every((v, i) => Number.isFinite(v) && (i === 0 || v > valeurs[i - 1]))).toBe(true);
  });
});
