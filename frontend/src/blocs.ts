/**
 * Instructions en blocs Texte / Code (spécification jalon 4, §7).
 *
 * Les instructions restent des chaînes markdown côté serveur. Un bloc Code est un bloc markdown délimité
 * (``` … ```) ; le texte entre les blocs délimités forme les blocs Texte. Fonctions pures, sans React.
 */

export type TypeBloc = 'texte' | 'code';

export interface Bloc {
  type: TypeBloc;
  contenu: string;
  /** Langage indiqué après la clôture d'ouverture (```bash), conservé tel quel. */
  langage?: string;
}

const OUVERTURE = /^ {0,3}(`{3,}|~{3,})[ \t]*([^`\s]*)[^`]*$/;

/** Retire les lignes vides en tête et les blancs en fin de texte (l'indentation interne est conservée). */
const rogner = (t: string) => t.replace(/^(?:[ \t]*\n)+/, '').replace(/\s+$/, '');

/** Découpe une chaîne markdown en blocs Texte (markdown libre) et Code (blocs délimités). */
export function decouperBlocs(source: string | null | undefined): Bloc[] {
  const lignes = (source ?? '').replace(/\r\n?/g, '\n').split('\n');
  const blocs: Bloc[] = [];
  let texte: string[] = [];
  const pousserTexte = () => {
    const t = rogner(texte.join('\n'));
    if (t.trim()) blocs.push({ type: 'texte', contenu: t });
    texte = [];
  };

  for (let i = 0; i < lignes.length; i++) {
    const m = OUVERTURE.exec(lignes[i]);
    if (!m) {
      texte.push(lignes[i]);
      continue;
    }
    const cloture = m[1];
    const fin = new RegExp(`^ {0,3}${cloture[0] === '`' ? '`' : '~'}{${cloture.length},}[ \\t]*$`);
    const code: string[] = [];
    let j = i + 1;
    while (j < lignes.length && !fin.test(lignes[j])) code.push(lignes[j++]);
    pousserTexte();
    blocs.push(m[2] ? { type: 'code', contenu: code.join('\n'), langage: m[2] } : { type: 'code', contenu: code.join('\n') });
    // Bloc non fermé : il court jusqu'à la fin (comme en CommonMark).
    i = j;
  }
  pousserTexte();
  return blocs;
}

function delimiter(b: Bloc): string {
  // Clôture plus longue que toute suite de ``` présente dans le code.
  const longueur = Math.max(2, ...(b.contenu.match(/`{3,}/g) ?? []).map((s) => s.length)) + 1;
  const cloture = '`'.repeat(longueur);
  return `${cloture}${b.langage ?? ''}\n${b.contenu.replace(/\n+$/, '')}\n${cloture}`;
}

/** Recompose la chaîne markdown enregistrée ; les blocs vides sont ignorés. */
export function recomposerBlocs(blocs: Bloc[]): string {
  return blocs
    .filter((b) => b.contenu.trim() !== '')
    .map((b) => (b.type === 'code' ? delimiter(b) : rogner(b.contenu)))
    .join('\n\n');
}

/** Première ligne utile (aperçu sur une ligne d'une instruction, sans les délimiteurs de code). */
export function premiereLigne(source: string): string {
  for (const b of decouperBlocs(source)) {
    const l = b.contenu.split('\n').find((x) => x.trim());
    if (l) return l.trim();
  }
  return '';
}
