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
    blocs.push(
      m[2] ? { type: 'code', contenu: code.join('\n'), langage: m[2] } : { type: 'code', contenu: code.join('\n') },
    );
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

export type ActionFormat = 'gras' | 'italique' | 'code' | 'lien' | 'liste' | 'cases';

export interface ResultatFormat {
  valeur: string;
  /** Nouvelle sélection dans la zone de saisie. */
  debut: number;
  fin: number;
}

const ENTOURAGE: Record<'gras' | 'italique' | 'code', [string, string, string]> = {
  gras: ['**', '**', 'texte en gras'],
  italique: ['_', '_', 'texte en italique'],
  code: ['`', '`', 'code'],
};

/**
 * Barre d'outils markdown : applique une mise en forme à la sélection [debut, fin[ d'un texte.
 * Sans sélection, insère un exemple sélectionné (à remplacer en tapant).
 */
export function formaterMarkdown(valeur: string, debut: number, fin: number, action: ActionFormat): ResultatFormat {
  const avant = valeur.slice(0, debut);
  const choix = valeur.slice(debut, fin);
  const apres = valeur.slice(fin);
  if (action === 'gras' || action === 'italique' || action === 'code') {
    const [g, d, exemple] = ENTOURAGE[action];
    const texte = choix || exemple;
    return {
      valeur: `${avant}${g}${texte}${d}${apres}`,
      debut: debut + g.length,
      fin: debut + g.length + texte.length,
    };
  }
  if (action === 'lien') {
    const texte = choix || 'texte du lien';
    const url = 'https://';
    const v = `${avant}[${texte}](${url})${apres}`;
    const p = debut + texte.length + 3;
    return { valeur: v, debut: p, fin: p + url.length };
  }
  // Listes : préfixe chaque ligne touchée par la sélection.
  const prefixe = action === 'liste' ? '- ' : '- [ ] ';
  const debutLigne = valeur.lastIndexOf('\n', debut - 1) + 1;
  // Une sélection qui se termine juste après un retour à la ligne ne touche pas la ligne suivante.
  const finSelection = fin > debut && valeur[fin - 1] === '\n' ? fin - 1 : fin;
  const finTrouvee = valeur.indexOf('\n', finSelection);
  const finLigne = finTrouvee < 0 ? valeur.length : finTrouvee;
  const lignes = valeur.slice(debutLigne, finLigne).split('\n');
  const deja = lignes.every((l) => l.startsWith(prefixe));
  const nouvelles = lignes.map((l) => (deja ? l.slice(prefixe.length) : `${prefixe}${l}`));
  const bloc = nouvelles.join('\n');
  return {
    valeur: valeur.slice(0, debutLigne) + bloc + valeur.slice(finLigne),
    debut: debutLigne,
    fin: debutLigne + bloc.length,
  };
}
