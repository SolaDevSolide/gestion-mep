// Gabarits des connexions aux API externes (message, adresse, en-têtes) : mêmes règles que le serveur
// (backend/src/GestionMep.Domain/RenduConnexion.cs), vérifiées des deux côtés par les mêmes cas
// (frontend/src/vecteurs-gabarits.json). Sert à l'éditeur (erreurs et couleurs pendant la saisie) ; l'aperçu exact, avec
// les valeurs d'une vraie MEP, vient du serveur. Fonctions pures, sans React.
//
// - JSON : dans une chaîne, la valeur est échappée (une liste y est jointe par des virgules) ; hors chaîne, seules une
//   variable de liste (tableau JSON) et {{donnees_json}} (objet) sont permises. Valeur absente : "" (ou [] / {}).
// - Texte et formulaire : la valeur telle quelle. Adresse : valeurs encodées, aucune variable dans le domaine.

export interface ValeurGabarit {
  texte?: string | null;
  liste?: readonly string[] | null;
}
export type ValeursGabarit = ReadonlyMap<string, ValeurGabarit>;
/** Variables connues : clé → variable de liste ou non. */
export type CatalogueGabarit = ReadonlyMap<string, boolean>;

export interface ErreurGabarit {
  ligne: number;
  colonne: number;
  message: string;
}
export interface Rendu {
  texte: string;
  manquantes: string[];
  inconnues: string[];
  erreurs: ErreurGabarit[];
}
export type FormatGabarit = 'Json' | 'Texte' | 'Formulaire';

export const DONNEES_JSON = 'donnees_json';
const RE_JETON = /\{\{\s*([a-z0-9_]+)\s*\}\}/g;
const RE_JETON_ICI = /\{\{\s*([a-z0-9_]+)\s*\}\}/y;

const estManquante = (v: ValeurGabarit | undefined) =>
  !v || ((v.texte ?? '').trim() === '' && (v.liste ?? []).length === 0);
const joindre = (v: ValeurGabarit) => v.texte ?? (v.liste ?? []).join(', ');
const ajouter = (liste: string[], cle: string) => {
  if (!liste.includes(cle)) liste.push(cle);
};

/** Clés des variables utilisées, dans l'ordre d'apparition, sans doublon. */
export function variablesGabarit(gabarit: string): string[] {
  return [...new Set([...gabarit.matchAll(RE_JETON)].map((m) => m[1]))];
}

/** Ligne et colonne (à partir de 1) d'une position du texte. */
export function position(texte: string, index: number): { ligne: number; colonne: number } {
  let ligne = 1;
  let debut = 0;
  for (let i = 0; i < index && i < texte.length; i++)
    if (texte[i] === '\n') {
      ligne++;
      debut = i + 1;
    }
  return { ligne, colonne: index - debut + 1 };
}

/** Position (index) d'une ligne et d'une colonne (à partir de 1). */
export function index(texte: string, ligne: number, colonne: number): number {
  let i = 0;
  for (let l = 1; l < ligne; l++) {
    const n = texte.indexOf('\n', i);
    if (n < 0) return texte.length;
    i = n + 1;
  }
  return Math.min(texte.length, i + colonne - 1);
}

/** Encodage d'une valeur dans une adresse, comme Uri.EscapeDataString (RFC 3986 : seuls A-Z a-z 0-9 - _ . ~ restent). */
export const encoderValeurUrl = (s: string) =>
  encodeURIComponent(s).replace(/[!'()*]/g, (c) => `%${c.charCodeAt(0).toString(16).toUpperCase()}`);

/** Texte ou formulaire : chaque variable remplacée par sa valeur (une liste jointe par des virgules). */
export function rendreTexte(
  gabarit: string,
  valeurs: ValeursGabarit,
  catalogue: CatalogueGabarit,
  encoder?: (s: string) => string,
): Rendu {
  const manquantes: string[] = [];
  const inconnues: string[] = [];
  const texte = gabarit.replace(RE_JETON, (_, cle: string) => {
    if (!catalogue.has(cle)) {
      ajouter(inconnues, cle);
      return '';
    }
    const v = valeurs.get(cle);
    if (!v || estManquante(v)) {
      ajouter(manquantes, cle);
      return '';
    }
    const t = joindre(v);
    return encoder ? encoder(t) : t;
  });
  return { texte, manquantes, inconnues, erreurs: [] };
}

/** Position de la fin de l'origine (schéma, domaine, port) : le premier « / », « ? » ou « # » après « // ». */
export function finOrigine(url: string): number {
  let debut = url.indexOf('//');
  debut = debut < 0 ? 0 : debut + 2;
  for (let i = debut; i < url.length; i++) if ('/?#'.includes(url[i])) return i;
  return url.length;
}

/** Adresse absolue au sens du serveur : un schéma suivi de « : » et de quelque chose. */
function adresseAbsolue(s: string): boolean {
  if (!/^[a-z][a-z0-9+.-]*:\S/i.test(s) || /\s/.test(s)) return false;
  try {
    new URL(s);
    return true;
  } catch {
    return false;
  }
}

/** Adresse : valeurs encodées ; erreur pour une variable dans le domaine ou une adresse incomplète. */
export function rendreUrl(gabarit: string, valeurs: ValeursGabarit, catalogue: CatalogueGabarit): Rendu {
  const erreurs: ErreurGabarit[] = [];
  const fin = finOrigine(gabarit);
  RE_JETON.lastIndex = 0;
  const m = RE_JETON.exec(gabarit);
  RE_JETON.lastIndex = 0;
  if (m && m.index < fin)
    erreurs.push({
      ...position(gabarit, m.index),
      message: "Les variables sont permises dans le chemin et les paramètres de l'adresse, pas dans le domaine.",
    });
  const rendu = rendreTexte(gabarit, valeurs, catalogue, encoderValeurUrl);
  if (erreurs.length === 0 && !adresseAbsolue(rendu.texte))
    erreurs.push({ ligne: 1, colonne: 1, message: 'Adresse complète attendue, commençant par https://.' });
  return { ...rendu, erreurs };
}

interface Segment {
  rendu: number;
  gabarit: number;
  jeton: boolean;
}

/** Texte d'une valeur dans une chaîne JSON, sans les guillemets. */
const echapperJson = (s: string) => JSON.stringify(s).slice(1, -1);

/** Message JSON : rempli selon les règles d'échappement, puis vérifié (erreur à la ligne et à la colonne du gabarit). */
export function rendreJson(gabarit: string, valeurs: ValeursGabarit, catalogue: CatalogueGabarit): Rendu {
  let sortie = '';
  const segments: Segment[] = [];
  const manquantes: string[] = [];
  const inconnues: string[] = [];
  const erreurs: ErreurGabarit[] = [];
  let dansChaine = false;
  let echappe = false;
  let litteral = -1;
  for (let i = 0; i < gabarit.length;) {
    if (gabarit[i] === '{' && gabarit[i + 1] === '{') {
      RE_JETON_ICI.lastIndex = i;
      const m = RE_JETON_ICI.exec(gabarit);
      if (m) {
        litteral = -1;
        segments.push({ rendu: sortie.length, gabarit: i, jeton: true });
        const cle = m[1];
        const liste = catalogue.get(cle);
        const v = valeurs.get(cle);
        const manque = !v || estManquante(v);
        if (liste === undefined) ajouter(inconnues, cle);
        else if (manque) ajouter(manquantes, cle);
        if (dansChaine) {
          if (liste !== undefined && v && !manque) sortie += echapperJson(joindre(v));
        } else if (cle === DONNEES_JSON) sortie += manque || !v ? '{}' : (v.texte ?? '');
        else if (liste === true) sortie += JSON.stringify(v?.liste ?? (v?.texte != null ? [v.texte] : []));
        else {
          if (liste !== undefined)
            erreurs.push({
              ...position(gabarit, i),
              message: `Mettez {{${cle}}} entre guillemets : seules les listes et {{donnees_json}} s'écrivent hors guillemets.`,
            });
          sortie += 'null';
        }
        i += m[0].length;
        continue;
      }
    }
    const ch = gabarit[i];
    if (dansChaine) {
      if (echappe) echappe = false;
      else if (ch === '\\') echappe = true;
      else if (ch === '"') dansChaine = false;
    } else if (ch === '"') dansChaine = true;
    if (litteral < 0) {
      litteral = i;
      segments.push({ rendu: sortie.length, gabarit: i, jeton: false });
    }
    sortie += ch;
    i++;
  }
  if (erreurs.length === 0) {
    const e = validerJson(sortie);
    if (e) erreurs.push({ ...position(gabarit, versGabarit(segments, e.index)), message: e.message });
  }
  return { texte: sortie, manquantes, inconnues, erreurs };
}

/** Position dans le gabarit d'une position du rendu (le début de la variable si elle tombe dans une valeur). */
function versGabarit(segments: Segment[], index: number): number {
  let trouve: Segment | undefined;
  for (const s of segments) {
    if (s.rendu > index) break;
    trouve = s;
  }
  if (!trouve) return 0;
  return trouve.jeton ? trouve.gabarit : trouve.gabarit + (index - trouve.rendu);
}

/** Message selon le format (formulaire : à rendre champ par champ avec rendreTexte). */
export function rendreMessage(
  format: FormatGabarit,
  gabarit: string,
  valeurs: ValeursGabarit,
  catalogue: CatalogueGabarit,
): Rendu {
  return format === 'Json' ? rendreJson(gabarit, valeurs, catalogue) : rendreTexte(gabarit, valeurs, catalogue);
}

// ---------------------------------------------------------------------------------------------------------------------
// Vérification du JSON : messages en français, à l'endroit où il manque quelque chose (mêmes messages que le serveur).

export interface ErreurJson {
  index: number;
  message: string;
}

class Echec extends Error {
  readonly index: number;
  constructor(index: number, message: string) {
    super(message);
    this.index = index;
  }
}

const estChiffre = (c: string | undefined) => c !== undefined && c >= '0' && c <= '9';
const nomListe = (nom: string) => (nom === 'du message' ? 'du message' : `de la liste « ${nom} »`);
const nomObjet = (nom: string) => (nom === 'du message' ? 'du message' : `de l'objet « ${nom} »`);

export function validerJson(t: string): ErreurJson | null {
  let i = 0;
  const fin = () => i >= t.length;
  const blancs = () => {
    while (!fin() && ' \t\r\n'.includes(t[i])) i++;
  };

  const mot = (m: string) => {
    if (t.slice(i, i + m.length) !== m)
      throw new Echec(i, "Valeur inconnue : un texte s'écrit entre guillemets (true, false et null s'écrivent sans).");
    i += m.length;
  };

  const nombre = () => {
    const debut = i;
    if (t[i] === '-') i++;
    if (!estChiffre(t[i])) throw new Echec(debut, 'Nombre invalide.');
    if (t[i] === '0') i++;
    else while (estChiffre(t[i])) i++;
    if (t[i] === '.') {
      i++;
      if (!estChiffre(t[i])) throw new Echec(debut, 'Nombre invalide.');
      while (estChiffre(t[i])) i++;
    }
    if (t[i] === 'e' || t[i] === 'E') {
      i++;
      if (t[i] === '+' || t[i] === '-') i++;
      if (!estChiffre(t[i])) throw new Echec(debut, 'Nombre invalide.');
      while (estChiffre(t[i])) i++;
    }
  };

  const chaine = (): string => {
    const debut = i;
    i++;
    let s = '';
    for (;;) {
      if (fin()) throw new Echec(debut, 'Texte non fermé : un guillemet manque.');
      const c = t[i];
      if (c === '"') {
        i++;
        return s;
      }
      if (c === '\\') {
        i++;
        if (fin()) throw new Echec(debut, 'Texte non fermé : un guillemet manque.');
        if ('"\\/bfnrt'.includes(t[i])) i++;
        else if (t[i] === 'u') {
          if (i + 4 >= t.length || !/^[0-9a-fA-F]{4}$/.test(t.slice(i + 1, i + 5)))
            throw new Echec(
              i - 1,
              'Échappement invalide dans un texte : \\u doit être suivi de 4 chiffres hexadécimaux.',
            );
          i += 5;
        } else
          throw new Echec(i - 1, 'Échappement invalide dans un texte : écrivez \\\\ pour une barre oblique inverse.');
        continue;
      }
      if (c < ' ')
        throw new Echec(
          i,
          c === '\n'
            ? 'Retour à la ligne dans un texte : fermez le guillemet, ou écrivez \\n.'
            : 'Caractère de contrôle interdit dans un texte.',
        );
      s += c;
      i++;
    }
  };

  const objet = (nom: string) => {
    i++;
    blancs();
    if (t[i] === '}') {
      i++;
      return;
    }
    for (;;) {
      blancs();
      if (fin()) throw new Echec(i, `L'objet ${nomObjet(nom)} n'est pas fermé : « } » manque.`);
      if (t[i] === '}') throw new Echec(i, `Virgule en trop avant « } » dans l'objet ${nomObjet(nom)}.`);
      if (t[i] !== '"') throw new Echec(i, 'Un nom de propriété entre guillemets est attendu.');
      const cle = chaine();
      blancs();
      if (fin() || t[i] !== ':') throw new Echec(i, `« : » est attendu après le nom « ${cle} ».`);
      i++;
      valeur(cle);
      const finValeur = i;
      blancs();
      if (fin()) throw new Echec(finValeur, `L'objet ${nomObjet(nom)} n'est pas fermé : « } » manque.`);
      if (t[i] === ',') {
        i++;
        continue;
      }
      if (t[i] === '}') {
        i++;
        return;
      }
      throw new Echec(finValeur, `Une virgule est attendue avant la propriété suivante ${nomObjet(nom)}.`);
    }
  };

  const liste = (nom: string) => {
    i++;
    blancs();
    if (t[i] === ']') {
      i++;
      return;
    }
    for (;;) {
      blancs();
      if (fin()) throw new Echec(i, `La liste ${nomListe(nom)} n'est pas fermée : « ] » manque.`);
      if (t[i] === ']') throw new Echec(i, `Virgule en trop avant « ] » dans la liste ${nomListe(nom)}.`);
      valeur(nom);
      const finValeur = i;
      blancs();
      if (fin()) throw new Echec(finValeur, `La liste ${nomListe(nom)} n'est pas fermée : « ] » manque.`);
      if (t[i] === ',') {
        i++;
        continue;
      }
      if (t[i] === ']') {
        i++;
        return;
      }
      throw new Echec(finValeur, `Une virgule est attendue avant l'élément suivant ${nomListe(nom)}.`);
    }
  };

  function valeur(nom: string): void {
    blancs();
    if (fin()) throw new Echec(i, "Le JSON s'arrête trop tôt : une valeur est attendue.");
    const c = t[i];
    if (c === '{') objet(nom);
    else if (c === '[') liste(nom);
    else if (c === '"') chaine();
    else if (c === 't') mot('true');
    else if (c === 'f') mot('false');
    else if (c === 'n') mot('null');
    else if (c === '-' || estChiffre(c)) nombre();
    else
      throw new Echec(
        i,
        'Une valeur est attendue : texte entre guillemets, nombre, objet, liste, true, false ou null.',
      );
  }

  try {
    blancs();
    if (fin()) return { index: 0, message: 'Le message est vide : un JSON est attendu.' };
    valeur('du message');
    const finJson = i;
    blancs();
    if (!fin())
      return {
        index: finJson,
        message: 'Contenu en trop après la fin du JSON : un seul objet ou une seule liste est attendu.',
      };
    return null;
  } catch (e) {
    if (e instanceof Echec) return { index: e.index, message: e.message };
    throw e;
  }
}

// ---------------------------------------------------------------------------------------------------------------------
// Éditeur : découpage pour le calque coloré, remise en forme du JSON, suggestion pour une variable inconnue.

export type TypeMorceau = 'texte' | 'variable' | 'inconnue' | 'cle' | 'chaine' | 'nombre' | 'mot' | 'ponctuation';
export interface MorceauGabarit {
  type: TypeMorceau;
  texte: string;
}

/**
 * Découpe un gabarit pour le calque coloré de l'éditeur : variables (connues ou non) et, en JSON, noms de propriétés,
 * textes, nombres, true/false/null et ponctuation. La concaténation des morceaux redonne exactement le gabarit.
 */
export function decouper(
  gabarit: string,
  format: FormatGabarit | 'Adresse',
  catalogue: CatalogueGabarit,
): MorceauGabarit[] {
  const out: MorceauGabarit[] = [];
  const pousser = (type: TypeMorceau, texte: string) => {
    if (!texte) return;
    const dernier = out[out.length - 1];
    if (dernier && dernier.type === type && type !== 'variable' && type !== 'inconnue') dernier.texte += texte;
    else out.push({ type, texte });
  };
  const jeton = (i: number) => {
    if (gabarit[i] !== '{' || gabarit[i + 1] !== '{') return null;
    RE_JETON_ICI.lastIndex = i;
    return RE_JETON_ICI.exec(gabarit);
  };
  const json = format === 'Json';
  let i = 0;
  while (i < gabarit.length) {
    const m = jeton(i);
    if (m) {
      pousser(catalogue.has(m[1]) ? 'variable' : 'inconnue', m[0]);
      i += m[0].length;
      continue;
    }
    const c = gabarit[i];
    if (json && c === '"') {
      // Chaîne : jusqu'au guillemet fermant (non échappé), les variables à l'intérieur gardent leur couleur.
      let j = i + 1;
      let echappe = false;
      while (j < gabarit.length && gabarit[j] !== '\n') {
        if (echappe) echappe = false;
        else if (gabarit[j] === '\\') echappe = true;
        else if (gabarit[j] === '"') break;
        j++;
      }
      const finChaine = j < gabarit.length && gabarit[j] === '"' ? j + 1 : j;
      let k = finChaine;
      while (k < gabarit.length && ' \t'.includes(gabarit[k])) k++;
      const type: TypeMorceau = gabarit[k] === ':' ? 'cle' : 'chaine';
      let debut = i;
      for (let p = i; p < finChaine;) {
        const mj = jeton(p);
        if (mj) {
          pousser(type, gabarit.slice(debut, p));
          pousser(catalogue.has(mj[1]) ? 'variable' : 'inconnue', mj[0]);
          p += mj[0].length;
          debut = p;
        } else p++;
      }
      pousser(type, gabarit.slice(debut, finChaine));
      i = finChaine;
      continue;
    }
    if (json && (c === '-' || estChiffre(c))) {
      let j = i + 1;
      while (j < gabarit.length && /[0-9.eE+-]/.test(gabarit[j])) j++;
      pousser('nombre', gabarit.slice(i, j));
      i = j;
      continue;
    }
    if (json && /[tfn]/.test(c)) {
      const mot = ['true', 'false', 'null'].find((x) => gabarit.startsWith(x, i));
      if (mot) {
        pousser('mot', mot);
        i += mot.length;
        continue;
      }
    }
    pousser(json && '{}[],:'.includes(c) ? 'ponctuation' : 'texte', c);
    i++;
  }
  return out;
}

/**
 * Remet en forme un gabarit JSON (indentation de 2 espaces, une propriété par ligne), sans toucher aux textes ni aux
 * variables. Renvoie null si le gabarit n'est pas un JSON valide (avec des valeurs d'exemple) : rien n'est changé.
 */
export function mettreEnFormeJson(gabarit: string, catalogue: CatalogueGabarit): string | null {
  const exemple = new Map<string, ValeurGabarit>(
    [...catalogue].map(([cle, liste]) => [
      cle,
      liste ? { liste: ['exemple'] } : { texte: cle === DONNEES_JSON ? '{}' : 'exemple' },
    ]),
  );
  if (rendreJson(gabarit, exemple, catalogue).erreurs.length > 0) return null;
  let out = '';
  let niveau = 0;
  const retour = () => '\n' + '  '.repeat(niveau);
  for (let i = 0; i < gabarit.length;) {
    const c = gabarit[i];
    if (c === '"') {
      let j = i + 1;
      let echappe = false;
      while (j < gabarit.length) {
        if (echappe) echappe = false;
        else if (gabarit[j] === '\\') echappe = true;
        else if (gabarit[j] === '"') break;
        j++;
      }
      out += gabarit.slice(i, j + 1);
      i = j + 1;
      continue;
    }
    if (c === '{' && gabarit[i + 1] === '{') {
      RE_JETON_ICI.lastIndex = i;
      const m = RE_JETON_ICI.exec(gabarit);
      if (m) {
        out += m[0];
        i += m[0].length;
        continue;
      }
    }
    if (' \t\r\n'.includes(c)) {
      i++;
      continue;
    }
    if (c === '{' || c === '[') {
      // Objet ou liste vide : sur une ligne.
      let j = i + 1;
      while (j < gabarit.length && ' \t\r\n'.includes(gabarit[j])) j++;
      if (gabarit[j] === (c === '{' ? '}' : ']')) {
        out += c + gabarit[j];
        i = j + 1;
        continue;
      }
      niveau++;
      out += c + retour();
    } else if (c === '}' || c === ']') {
      niveau--;
      out += retour() + c;
    } else if (c === ',') out += ',' + retour();
    else if (c === ':') out += ': ';
    else out += c;
    i++;
  }
  return out + '\n';
}

/** Distance d'édition (insertion, suppression, remplacement, inversion de deux lettres voisines). */
function distance(a: string, b: string): number {
  const d: number[][] = Array.from({ length: a.length + 1 }, (_, i) => [i, ...Array<number>(b.length).fill(0)]);
  for (let j = 1; j <= b.length; j++) d[0][j] = j;
  for (let i = 1; i <= a.length; i++)
    for (let j = 1; j <= b.length; j++) {
      const cout = a[i - 1] === b[j - 1] ? 0 : 1;
      d[i][j] = Math.min(d[i - 1][j] + 1, d[i][j - 1] + 1, d[i - 1][j - 1] + cout);
      if (i > 1 && j > 1 && a[i - 1] === b[j - 2] && a[i - 2] === b[j - 1])
        d[i][j] = Math.min(d[i][j], d[i - 2][j - 2] + 1);
    }
  return d[a.length][b.length];
}

/** Variable connue la plus proche d'une variable inconnue (« Vouliez-vous dire … ? »), ou null si aucune n'est proche. */
export function suggestion(inconnue: string, cles: Iterable<string>): string | null {
  let meilleure: string | null = null;
  let min = Infinity;
  for (const c of cles) {
    const d = distance(inconnue, c);
    if (d < min) {
      min = d;
      meilleure = c;
    }
  }
  const seuil = Math.max(1, Math.min(3, Math.floor(inconnue.length / 3)));
  return meilleure !== null && min <= seuil ? meilleure : null;
}

/** Catalogue à partir des variables proposées par le serveur. */
export const catalogueDepuis = (variables: readonly { cle: string; liste: boolean }[]): CatalogueGabarit =>
  new Map(variables.map((v) => [v.cle, v.liste]));
