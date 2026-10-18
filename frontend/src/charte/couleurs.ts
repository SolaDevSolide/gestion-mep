/**
 * Calculs de couleurs de la charte visuelle : conversions, contraste WCAG, couleurs dérivées.
 * Copie exacte de CouleursCharte.cs (serveur) : les deux passent les mêmes cas de test (vecteurs-charte.json). Toute
 * modification se fait des deux côtés, dans le même ordre d'opérations.
 */

export interface Rvb {
  r: number;
  v: number;
  b: number;
}
/** Teinte (0..360), saturation et valeur (0..1). */
export interface Tsv {
  t: number;
  s: number;
  v: number;
}
/** Teinte (0..360), saturation et luminosité (0..1). */
export interface Tsl {
  t: number;
  s: number;
  l: number;
}

export const BLANC = '#ffffff';
/** Texte foncé de l'application (--fg du thème clair). */
export const ENCRE = '#16161a';

const borne = (x: number, min = 0, max = 1) => Math.min(max, Math.max(min, x));
/** Arrondi à l'entier le plus proche, .5 vers le haut (comme Math.Floor(x + 0.5) côté serveur). */
const arrondi = (x: number) => Math.floor(x + 0.5);

/** « #RRGGBB », « #RGB », avec ou sans « # » → « #rrggbb » ; null si ce n'est pas une couleur. */
export function normaliserHex(saisie: string): string | null {
  let s = saisie.trim().replace(/^#/, '').toLowerCase();
  if (/^[0-9a-f]{3}$/.test(s)) s = s.replace(/./g, (c) => c + c);
  return /^[0-9a-f]{6}$/.test(s) ? `#${s}` : null;
}

export function hexVersRvb(hex: string): Rvb {
  const h = (normaliserHex(hex) ?? '#000000').slice(1);
  return { r: parseInt(h.slice(0, 2), 16), v: parseInt(h.slice(2, 4), 16), b: parseInt(h.slice(4, 6), 16) };
}

export function rvbVersHex({ r, v, b }: Rvb): string {
  const x = (n: number) =>
    arrondi(borne(n, 0, 255))
      .toString(16)
      .padStart(2, '0');
  return `#${x(r)}${x(v)}${x(b)}`;
}

function teinte(R: number, V: number, B: number, max: number, d: number): number {
  if (d === 0) return 0;
  let t: number;
  if (max === R) t = ((V - B) / d) % 6;
  else if (max === V) t = (B - R) / d + 2;
  else t = (R - V) / d + 4;
  t *= 60;
  if (t < 0) t += 360;
  return t;
}

export function rvbVersTsv({ r, v, b }: Rvb): Tsv {
  const R = r / 255;
  const V = v / 255;
  const B = b / 255;
  const max = Math.max(R, V, B);
  const d = max - Math.min(R, V, B);
  return { t: teinte(R, V, B, max, d), s: max === 0 ? 0 : d / max, v: max };
}

function secteur(t: number, c: number, x: number): [number, number, number] {
  if (t < 60) return [c, x, 0];
  if (t < 120) return [x, c, 0];
  if (t < 180) return [0, c, x];
  if (t < 240) return [0, x, c];
  if (t < 300) return [x, 0, c];
  return [c, 0, x];
}

export function tsvVersRvb({ t, s, v }: Tsv): Rvb {
  const c = v * s;
  const x = c * (1 - Math.abs(((t / 60) % 2) - 1));
  const m = v - c;
  const [a, b, d] = secteur(t, c, x);
  return { r: (a + m) * 255, v: (b + m) * 255, b: (d + m) * 255 };
}

export function rvbVersTsl({ r, v, b }: Rvb): Tsl {
  const R = r / 255;
  const V = v / 255;
  const B = b / 255;
  const max = Math.max(R, V, B);
  const min = Math.min(R, V, B);
  const l = (max + min) / 2;
  const d = max - min;
  return { t: teinte(R, V, B, max, d), s: d === 0 ? 0 : d / (1 - Math.abs(2 * l - 1)), l };
}

export function tslVersRvb({ t, s, l }: Tsl): Rvb {
  const c = (1 - Math.abs(2 * l - 1)) * s;
  const x = c * (1 - Math.abs(((t / 60) % 2) - 1));
  const m = l - c / 2;
  const [a, b, d] = secteur(t, c, x);
  return { r: (a + m) * 255, v: (b + m) * 255, b: (d + m) * 255 };
}

/** Luminosité TSL (0 noir, 1 blanc). */
export function luminosite(hex: string): number {
  return rvbVersTsl(hexVersRvb(hex)).l;
}

/** Même teinte, luminosité décalée de `delta` (−1..1). */
export function decaler(hex: string, delta: number): string {
  const { t, s, l } = rvbVersTsl(hexVersRvb(hex));
  return rvbVersHex(tslVersRvb({ t, s, l: borne(l + delta) }));
}

/** Mélange : `part` de `a` dans `b` (1 → a, 0 → b). */
export function melanger(a: string, b: string, part: number): string {
  const x = hexVersRvb(a);
  const y = hexVersRvb(b);
  return rvbVersHex({
    r: x.r * part + y.r * (1 - part),
    v: x.v * part + y.v * (1 - part),
    b: x.b * part + y.b * (1 - part),
  });
}

function lineaire(c: number): number {
  const s = c / 255;
  return s <= 0.03928 ? s / 12.92 : Math.pow((s + 0.055) / 1.055, 2.4);
}

/** Luminance relative WCAG (0 noir, 1 blanc). */
export function luminanceRelative(hex: string): number {
  const { r, v, b } = hexVersRvb(hex);
  return 0.2126 * lineaire(r) + 0.7152 * lineaire(v) + 0.0722 * lineaire(b);
}

/** Rapport de contraste WCAG 2 (1 à 21). */
export function contraste(a: string, b: string): number {
  const la = luminanceRelative(a);
  const lb = luminanceRelative(b);
  return (Math.max(la, lb) + 0.05) / (Math.min(la, lb) + 0.05);
}

/** Texte lisible posé sur `fond` : blanc s'il atteint 4,5:1 ou contraste au moins autant que l'encre, sinon l'encre. */
export function texteSur(fond: string): string {
  const blanc = contraste(BLANC, fond);
  return blanc >= 4.5 || blanc >= contraste(ENCRE, fond) ? BLANC : ENCRE;
}

/**
 * Même teinte, luminosité ajustée pas à pas jusqu'à atteindre `cible` sur `fond` : la couleur s'éloigne du fond
 * (assombrie si elle est plus foncée que lui, éclaircie sinon ; à égalité, assombrie sur un fond clair).
 */
export function ajusterPourContraste(hex: string, fond: string, cible: number): string {
  if (contraste(hex, fond) >= cible) return hex;
  const lc = luminanceRelative(hex);
  const lf = luminanceRelative(fond);
  const sens = lc < lf ? -1 : lc > lf ? 1 : lf > 0.4 ? -1 : 1;
  let c = hex;
  for (let i = 0; i < 100 && contraste(c, fond) < cible; i++) c = decaler(c, sens * 0.01);
  return c;
}

/** Écart de teinte (0..180), pour signaler une couleur de charte trop proche d'une couleur d'état. */
export function ecartTeinte(a: string, b: string): number {
  const ta = rvbVersTsv(hexVersRvb(a)).t;
  const tb = rvbVersTsv(hexVersRvb(b)).t;
  const d = Math.abs(ta - tb) % 360;
  return d > 180 ? 360 - d : d;
}

/** « 4,5:1 ». */
export function formatRapport(r: number): string {
  return `${(Math.floor(r * 10) / 10).toFixed(1).replace('.', ',')}:1`;
}
