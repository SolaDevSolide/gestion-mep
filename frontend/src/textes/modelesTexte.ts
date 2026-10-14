// Modèles de texte : remplissage d'un modèle avec les valeurs d'une MEP, puis rendu mis en forme (aperçu, HTML copié)
// ou en texte simple. Fonctions pures, sans React.
//
// Syntaxe : `{{variable}}`, `**gras**`, une ligne qui commence par « - » est une puce. Une variable de liste seule sur sa
// ligne devient une liste à puces (sinon ses éléments sont séparés par des virgules). Une information manquante apparaît
// en « [Libellé à compléter] ».
import type { UsageModeleTexte, ValeurVariable, VariableTexte } from '../types';

export const LIBELLES_USAGE: Record<UsageModeleTexte, string> = {
  Annonce: 'Annonce aux utilisateurs',
  Rappel: 'Rappel',
  DemandeMep: 'Demande de MEP',
  CompteRendu: 'Compte rendu de fin',
  Autre: 'Autre',
};

export type Morceau = { type: 'texte'; texte: string; gras: boolean } | { type: 'manque'; cle: string; gras: boolean };
export interface Ligne {
  puce: boolean;
  morceaux: Morceau[];
}
/** Paragraphe (lignes consécutives) ou liste à puces. */
export type Bloc = { type: 'paragraphe'; lignes: Ligne[] } | { type: 'liste'; elements: Ligne[] };

export type Valeurs = ReadonlyMap<string, ValeurVariable>;
export type Libelles = ReadonlyMap<string, string>;

const RE_VARIABLE = /\{\{\s*([a-z0-9_]+)\s*\}\}/g;
const RE_SEULE = /^\s*\{\{\s*([a-z0-9_]+)\s*\}\}\s*$/;
const RE_PUCE = /^\s*[-•]\s+/;

export const valeursDepuis = (liste: ValeurVariable[]): Valeurs => new Map(liste.map((v) => [v.cle, v]));
export const libellesDepuis = (variables: VariableTexte[]): Libelles =>
  new Map(variables.map((v) => [v.cle, v.libelle]));

const estManquante = (v: ValeurVariable | undefined) =>
  !v || ((v.texte ?? '').trim() === '' && (v.liste ?? []).length === 0);

/** Clés des variables utilisées par un texte, dans l'ordre d'apparition, sans doublon. */
export function variablesUtilisees(texte: string): string[] {
  return [...new Set([...texte.matchAll(RE_VARIABLE)].map((m) => m[1]))];
}

/** Variables utilisées et absentes de la MEP (ou inconnues). */
export function manquantes(texte: string, valeurs: Valeurs): string[] {
  return variablesUtilisees(texte).filter((c) => estManquante(valeurs.get(c)));
}

/** Variables utilisées qui n'existent pas (faute de frappe) : elles restent « à compléter » dans le texte. */
export function inconnues(texte: string, libelles: Libelles): string[] {
  return variablesUtilisees(texte).filter((c) => !libelles.has(c));
}

/** Gras (`**…**`) puis variables d'une ligne. */
function morceaux(ligne: string, valeurs: Valeurs): Morceau[] {
  const out: Morceau[] = [];
  for (const segment of ligne.split(/(\*\*[^*]+\*\*)/)) {
    if (!segment) continue;
    const gras = segment.length > 4 && segment.startsWith('**') && segment.endsWith('**');
    const texte = gras ? segment.slice(2, -2) : segment;
    let dernier = 0;
    for (const m of texte.matchAll(RE_VARIABLE)) {
      const debut = m.index ?? 0;
      if (debut > dernier) out.push({ type: 'texte', texte: texte.slice(dernier, debut), gras });
      const v = valeurs.get(m[1]);
      if (estManquante(v)) out.push({ type: 'manque', cle: m[1], gras });
      else out.push({ type: 'texte', texte: v?.texte?.trim() || (v?.liste ?? []).join(', '), gras });
      dernier = debut + m[0].length;
    }
    if (dernier < texte.length) out.push({ type: 'texte', texte: texte.slice(dernier), gras });
  }
  return out;
}

/** Remplit un modèle : une ligne par ligne du modèle, une variable de liste seule sur sa ligne donne une puce par élément. */
export function generer(modele: string, valeurs: Valeurs): Ligne[] {
  const lignes: Ligne[] = [];
  for (const brute of modele.replace(/\r\n/g, '\n').split('\n')) {
    const seule = RE_SEULE.exec(brute);
    const liste = seule ? valeurs.get(seule[1])?.liste : undefined;
    if (liste && liste.length > 0) {
      // Les éléments sont des données de la MEP : jamais interprétés comme du gras ou des variables.
      for (const x of liste) lignes.push({ puce: true, morceaux: [{ type: 'texte', texte: x, gras: false }] });
      continue;
    }
    const puce = RE_PUCE.test(brute);
    lignes.push({ puce, morceaux: morceaux(puce ? brute.replace(RE_PUCE, '') : brute.trimEnd(), valeurs) });
  }
  // Pas de lignes vides en début ni en fin.
  while (lignes.length > 0 && lignes[0].morceaux.length === 0 && !lignes[0].puce) lignes.shift();
  while (lignes.length > 0 && lignes[lignes.length - 1].morceaux.length === 0 && !lignes[lignes.length - 1].puce)
    lignes.pop();
  return lignes;
}

/** Regroupe les lignes en paragraphes (séparés par une ligne vide) et en listes à puces. */
export function blocs(lignes: Ligne[]): Bloc[] {
  const res: Bloc[] = [];
  for (const l of lignes) {
    const dernier = res[res.length - 1];
    if (l.puce) {
      if (dernier?.type === 'liste') dernier.elements.push(l);
      else res.push({ type: 'liste', elements: [l] });
    } else if (l.morceaux.length === 0) {
      res.push({ type: 'paragraphe', lignes: [] }); // séparateur, retiré plus bas
    } else if (dernier?.type === 'paragraphe' && dernier.lignes.length > 0) {
      dernier.lignes.push(l);
    } else {
      res.push({ type: 'paragraphe', lignes: [l] });
    }
  }
  return res.filter((b) => b.type === 'liste' || b.lignes.length > 0);
}

export const texteManque = (cle: string, libelles: Libelles) => `[${libelles.get(cle) ?? cle} à compléter]`;

const texteLigne = (l: Ligne, libelles: Libelles) =>
  l.morceaux.map((m) => (m.type === 'manque' ? texteManque(m.cle, libelles) : m.texte)).join('');

/** Texte simple, tel qu'il est copié : puces « - », sans gras. */
export function enTexteSimple(lignes: Ligne[], libelles: Libelles): string {
  return lignes.map((l) => (l.puce ? '- ' : '') + texteLigne(l, libelles)).join('\n');
}

const echapper = (s: string) =>
  s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');

function htmlLigne(l: Ligne, libelles: Libelles): string {
  return l.morceaux
    .map((m) => {
      const contenu =
        m.type === 'manque'
          ? `<span style="background-color:#fef3c7">${echapper(texteManque(m.cle, libelles))}</span>`
          : echapper(m.texte);
      return m.gras ? `<strong>${contenu}</strong>` : contenu;
    })
    .join('');
}

/** HTML mis en forme pour une messagerie (paragraphes, gras, listes) : copié avec le texte simple. */
export function enHtml(lignes: Ligne[], libelles: Libelles): string {
  return blocs(lignes)
    .map((b) =>
      b.type === 'liste'
        ? `<ul>${b.elements.map((l) => `<li>${htmlLigne(l, libelles)}</li>`).join('')}</ul>`
        : `<p>${b.lignes.map((l) => htmlLigne(l, libelles)).join('<br>')}</p>`,
    )
    .join('\n');
}

/**
 * Copie dans le presse-papiers : le HTML mis en forme (avec le texte simple pour les messageries qui ne le lisent pas),
 * ou le texte simple seul. Lève une erreur si le navigateur refuse.
 */
export async function copier(texte: string, html?: string): Promise<void> {
  if (!navigator.clipboard)
    throw new Error('Copie impossible : le navigateur n’autorise pas l’accès au presse-papiers.');
  if (html !== undefined && typeof ClipboardItem !== 'undefined' && navigator.clipboard.write) {
    await navigator.clipboard.write([
      new ClipboardItem({
        'text/html': new Blob([html], { type: 'text/html' }),
        'text/plain': new Blob([texte], { type: 'text/plain' }),
      }),
    ]);
    return;
  }
  await navigator.clipboard.writeText(texte);
}
