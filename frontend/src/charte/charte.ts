/**
 * Charte visuelle : rôles de couleurs, jetons dérivés pour les thèmes clair et sombre, contrôles de lisibilité (WCAG AA),
 * palette des exports Excel et lecture du fichier charte.json (version 1).
 *
 * Le serveur fait foi (CharteVisuelle.cs : il sert /charte.css et colore les exports) ; ce module en est la copie pour
 * l'aperçu en direct de la console. Les deux passent les mêmes cas de test (vecteurs-charte.json).
 */
import {
  ajusterPourContraste,
  BLANC,
  contraste,
  decaler,
  ecartTeinte,
  ENCRE,
  hexVersRvb,
  luminanceRelative,
  luminosite,
  melanger,
  normaliserHex,
  texteSur,
} from './couleurs';
import { DEFAUT_CLAIR, DEFAUT_SOMBRE } from './defauts';

export type Role = 'primaire' | 'secondaire' | 'tertiaire';
export type Etat = 'succes' | 'avertissement' | 'erreur' | 'info';
export type Gris = 'froids' | 'purs' | 'chauds' | 'teintes';
export type MenuLateral = 'pages' | 'secondaire';
export type Mode = 'clair' | 'sombre';

/** Valeur de `couleurs.tertiaire` qui fait suivre la primaire (charte par défaut). */
export const SUIT_PRIMAIRE = 'primaire';

/** Charte normalisée : couleurs « #rrggbb » en minuscules. */
export interface Charte {
  nom: string;
  couleurs: { primaire: string; secondaire: string; tertiaire: string };
  menuLateral: MenuLateral;
  gris: Gris;
  /** Retouches du thème sombre ; absente, la couleur est calculée. */
  sombre: Partial<Record<Role, string>>;
  /** Couleurs d'état personnalisées ; absente, la couleur standard. */
  etats: Partial<Record<Etat, string>>;
}

export const NOM_DEFAUT = 'Charte par défaut';

export const CHARTE_DEFAUT: Charte = {
  nom: NOM_DEFAUT,
  couleurs: { primaire: '#4f46e5', secondaire: '#0d0e1a', tertiaire: SUIT_PRIMAIRE },
  menuLateral: 'pages',
  gris: 'froids',
  sombre: {},
  etats: {},
};

export const ROLES: { role: Role; libelle: string; usage: string }[] = [
  {
    role: 'primaire',
    libelle: 'Primaire',
    usage: 'Boutons principaux, liens, onglet et menu actifs, contour de focus',
  },
  {
    role: 'secondaire',
    libelle: 'Secondaire',
    usage: 'Menu latéral (si coloré), page de connexion, titres des exports Excel',
  },
  { role: 'tertiaire', libelle: 'Tertiaire', usage: 'Sections du plan, repères du graphe, mises en avant' },
];

export const ETATS: { etat: Etat; libelle: string; usage: string; famille: string; nom: string }[] = [
  { etat: 'succes', libelle: 'Succès', usage: 'Étape réussie, MEP terminée', famille: 'ok', nom: 'vert « Réussie »' },
  {
    etat: 'avertissement',
    libelle: 'Avertissement',
    usage: 'Résolution en cours, durée dépassée',
    famille: 'warn',
    nom: 'orange « Résolution »',
  },
  { etat: 'erreur', libelle: 'Erreur', usage: 'Échec, retour arrière', famille: 'danger', nom: 'rouge « Échec »' },
  { etat: 'info', libelle: 'Information', usage: 'En cours, exécution', famille: 'run', nom: 'bleu « En cours »' },
];

/** Couleurs d'état standard (pastilles pleines du thème clair). */
export const ETAT_DEFAUT: Record<Etat, string> = {
  succes: DEFAUT_CLAIR['ok-solid'],
  avertissement: DEFAUT_CLAIR['warn-solid'],
  erreur: DEFAUT_CLAIR['danger-solid'],
  info: DEFAUT_CLAIR['run-solid'],
};

export const GRIS: Gris[] = ['froids', 'purs', 'chauds', 'teintes'];
export const LIBELLES_GRIS: Record<Gris, string> = {
  froids: 'Froids',
  purs: 'Purs',
  chauds: 'Chauds',
  teintes: 'Teintés',
};
export const LIBELLES_MENU: Record<MenuLateral, string> = { pages: 'Comme les pages', secondaire: 'Secondaire' };

const FOND_SOMBRE = DEFAUT_SOMBRE.surface;
const NOIR_SOMBRE = DEFAUT_SOMBRE.bg;

export const tertiaireLiee = (c: Charte) => c.couleurs.tertiaire === SUIT_PRIMAIRE;

/** Couleur saisie d'un rôle (la tertiaire liée vaut la primaire). */
export function couleurSaisie(c: Charte, role: Role): string {
  return role === 'tertiaire' && tertiaireLiee(c) ? c.couleurs.primaire : c.couleurs[role];
}

/** Couleur d'un rôle dans un thème : la retouche du thème sombre, sinon la couleur calculée. */
export function couleurRole(c: Charte, role: Role, mode: Mode): string {
  const base = couleurSaisie(c, role);
  if (mode === 'clair') return base;
  const retouche = role === 'tertiaire' && tertiaireLiee(c) ? c.sombre.primaire : c.sombre[role];
  if (retouche) return retouche;
  if (role === 'secondaire') {
    if (base === CHARTE_DEFAUT.couleurs.secondaire) return base;
    return melanger(base, NOIR_SOMBRE, luminosite(base) > 0.3 ? 0.35 : 0.7);
  }
  // La primaire par défaut garde sa version sombre d'aujourd'hui.
  if (base === CHARTE_DEFAUT.couleurs.primaire) return DEFAUT_SOMBRE.accent;
  return luminosite(base) < 0.6 ? decaler(base, 0.06) : base;
}

/** Couleur de la primaire par défaut dans un thème. */
const primaireDefaut = (mode: Mode) => (mode === 'clair' ? DEFAUT_CLAIR.accent : DEFAUT_SOMBRE.accent);

const SUFFIXES = ['', '-hover', '-text', '-soft', '-fg'];

/** Famille de jetons d'une couleur de marque : couleur, survol, texte, fond pâle, texte posé dessus. */
function famille(prefixe: string, base: string, mode: Mode, fond: string): Record<string, string> {
  if (base === primaireDefaut(mode)) {
    const d = mode === 'clair' ? DEFAUT_CLAIR : DEFAUT_SOMBRE;
    return Object.fromEntries(SUFFIXES.map((s) => [`${prefixe}${s}`, d[`accent${s}`]]));
  }
  if (mode === 'clair') {
    const soft = melanger(base, BLANC, 0.1);
    return {
      [prefixe]: base,
      [`${prefixe}-hover`]: decaler(base, -0.07),
      [`${prefixe}-text`]: ajusterPourContraste(base, soft, 4.6),
      [`${prefixe}-soft`]: soft,
      [`${prefixe}-fg`]: texteSur(base),
    };
  }
  const soft = melanger(base, fond, 0.2);
  return {
    [prefixe]: base,
    [`${prefixe}-hover`]: decaler(base, 0.06),
    [`${prefixe}-text`]: ajusterPourContraste(decaler(base, 0.18), soft, 6),
    [`${prefixe}-soft`]: soft,
    [`${prefixe}-fg`]: texteSur(base),
  };
}

/** Menu latéral sur un fond de couleur : textes, survol et élément actif tirés du texte lisible sur ce fond. */
function nav(fond: string): Record<string, string> {
  const fg = texteSur(fond);
  return {
    'nav-bg': fond,
    'nav-fg': fg,
    'nav-muted': ajusterPourContraste(melanger(fg, fond, 0.72), fond, 4.6),
    'nav-subtle': ajusterPourContraste(melanger(fg, fond, 0.6), fond, 4.5),
    'nav-hover': melanger(fg, fond, 0.08),
    'nav-active': melanger(fg, fond, 0.16),
    'nav-active-fg': fg,
    'nav-line': melanger(fg, fond, 0.14),
    'nav-line-strong': melanger(fg, fond, 0.26),
    'nav-input': melanger(fg, fond, 0.07),
  };
}

/** Gris purs et chauds (les froids sont ceux d'aujourd'hui ; les teintés se calculent depuis la primaire). */
const GRIS_FIXES: Record<'purs' | 'chauds', Record<Mode, Record<string, string>>> = {
  purs: {
    clair: {
      bg: '#f6f6f6',
      'surface-2': '#f2f2f2',
      sunken: '#ececec',
      line: '#e3e3e3',
      'line-strong': '#cacaca',
      'fg-muted': '#4d4d4d',
      'fg-subtle': '#6b6b6b',
    },
    sombre: {
      bg: '#0c0c0c',
      surface: '#131313',
      'surface-2': '#1b1b1b',
      sunken: '#090909',
      line: '#282828',
      'line-strong': '#3c3c3c',
    },
  },
  chauds: {
    clair: {
      bg: '#f7f6f3',
      'surface-2': '#f2f0eb',
      sunken: '#ece9e3',
      line: '#e4e0d8',
      'line-strong': '#cdc7bb',
      'fg-muted': '#514c44',
      'fg-subtle': '#716a60',
    },
    sombre: {
      bg: '#0f0e0c',
      surface: '#171613',
      'surface-2': '#1f1d19',
      sunken: '#0b0a09',
      line: '#2c2a25',
      'line-strong': '#403c35',
    },
  },
};

const GRIS_TEINTES = ['bg', 'surface-2', 'sunken', 'line', 'line-strong'];

/** Familles qui ont une couleur pleine, et donc un texte posé dessus (`-solid-fg`). */
const FAMILLES_PLEINES = ['ok', 'run', 'wait', 'done', 'danger', 'warn', 'later', 'rollback'];
/** Familles dont la couleur sert aussi de texte (`-text`). */
const FAMILLES_TEXTE = new Set(['ok', 'warn']);
/**
 * Icônes de la page de connexion (Tailwind emerald, amber et rose 300) : changées seulement si elles n'atteignent pas 3:1
 * sur une secondaire personnalisée.
 */
const ICONES_CONNEXION: Record<string, string> = {
  'connexion-ok': '#6ee7b7',
  'connexion-attention': '#fcd34d',
  'connexion-alerte': '#fda4af',
};

/** Pastille d'état personnalisée : fond pâle, bordure, couleur pleine et texte. */
function etat(base: string, mode: Mode): Record<string, string> {
  if (mode === 'clair') {
    const bg = melanger(base, BLANC, 0.16);
    return {
      bg,
      bd: melanger(base, BLANC, 0.5),
      solid: base,
      fg: ajusterPourContraste(decaler(base, -0.25), bg, 7),
    };
  }
  const bg = melanger(base, FOND_SOMBRE, 0.22);
  return {
    bg,
    bd: melanger(base, FOND_SOMBRE, 0.55),
    solid: decaler(base, 0.05),
    fg: ajusterPourContraste(decaler(base, 0.25), bg, 7),
  };
}

/** Tous les jetons d'un thème avec la charte appliquée. */
export function jetons(c: Charte, mode: Mode): Record<string, string> {
  const d = mode === 'clair' ? DEFAUT_CLAIR : DEFAUT_SOMBRE;
  const t: Record<string, string> = { ...d };
  const p = couleurRole(c, 'primaire', mode);
  // Gris
  if (c.gris === 'purs' || c.gris === 'chauds') Object.assign(t, GRIS_FIXES[c.gris][mode]);
  if (c.gris === 'teintes') for (const k of GRIS_TEINTES) t[k] = melanger(p, d[k], mode === 'clair' ? 0.05 : 0.07);
  const fond = mode === 'clair' ? BLANC : t.surface;
  // Primaire (jetons « accent »), tertiaire
  Object.assign(t, famille('accent', p, mode, fond));
  const ter = couleurRole(c, 'tertiaire', mode);
  if (tertiaireLiee(c)) for (const s of SUFFIXES) t[`tertiaire${s}`] = t[`accent${s}`];
  else Object.assign(t, famille('tertiaire', ter, mode, fond));
  // Secondaire et menu latéral
  const s = couleurRole(c, 'secondaire', mode);
  t.secondaire = s;
  t['secondaire-fg'] = texteSur(s);
  Object.assign(
    t,
    c.menuLateral === 'secondaire'
      ? nav(s)
      : {
          'nav-bg': t.surface,
          'nav-fg': t.fg,
          'nav-muted': t['fg-muted'],
          'nav-subtle': t['fg-subtle'],
          'nav-hover': t['surface-2'],
          'nav-active': t['accent-soft'],
          'nav-active-fg': t['accent-text'],
          'nav-line': t.line,
          'nav-line-strong': t['line-strong'],
          'nav-input': t['surface-2'],
        },
  );
  // États
  for (const e of ETATS) {
    const base = c.etats[e.etat];
    if (!base) continue;
    for (const [k, v] of Object.entries(etat(base, mode))) t[`${e.famille}-${k}`] = v;
    if (e.etat === 'succes') {
      const fonce = decaler(base, -0.12);
      Object.assign(t, { 'done-bg': fonce, 'done-solid': fonce, 'done-bd': decaler(base, -0.18) });
    }
    if (e.etat === 'erreur') {
      const fonce = decaler(base, -0.05);
      Object.assign(t, { 'rollback-bg': fonce, 'rollback-solid': fonce });
    }
  }
  // Texte posé sur les couleurs pleines ; vert et orange employés comme texte (couleurs d'état personnalisées).
  for (const f of FAMILLES_PLEINES) t[`${f}-solid-fg`] = texteSur(t[`${f}-solid`]);
  for (const e of ETATS)
    if (c.etats[e.etat] && FAMILLES_TEXTE.has(e.famille))
      t[`${e.famille}-text`] = ajusterPourContraste(
        ajusterPourContraste(t[`${e.famille}-solid`], t[`${e.famille}-bg`], 4.5),
        t.surface,
        4.5,
      );
  // Page de connexion : panneau en secondaire, logo et halos aux couleurs de la charte.
  if (p !== primaireDefaut(mode)) {
    t['connexion-logo'] = p;
    t['connexion-logo-fg'] = texteSur(p);
    t['connexion-halo-1'] = p;
  }
  if (ter !== primaireDefaut(mode)) t['connexion-halo-2'] = ter;
  if (s !== CHARTE_DEFAUT.couleurs.secondaire) {
    t['connexion-texte'] = t['secondaire-fg'];
    t['connexion-discret'] = t['secondaire-fg'];
    for (const [k, v] of Object.entries(ICONES_CONNEXION)) {
      const icone = ajusterPourContraste(v, s, 3);
      if (icone !== v) t[k] = icone;
    }
  }
  return t;
}

/** Jetons qui diffèrent du thème par défaut : ce que /charte.css redéfinit. Vide pour la charte par défaut. */
export function ecarts(c: Charte, mode: Mode): Record<string, string> {
  const d = mode === 'clair' ? DEFAUT_CLAIR : DEFAUT_SOMBRE;
  return Object.fromEntries(Object.entries(jetons(c, mode)).filter(([k, v]) => d[k] !== v));
}

/** Variables CSS d'un aperçu (style en ligne d'un conteneur), indépendant du thème de la page. */
export function styleApercu(c: Charte, mode: Mode): Record<string, string> {
  return Object.fromEntries(Object.entries(jetons(c, mode)).map(([k, v]) => [`--${k}`, v]));
}

// ---------- Contrôles de lisibilité ----------

export type Cible = Role | Etat;

export interface Controle {
  id: string;
  libelle: string;
  mode: Mode;
  type: 'contraste' | 'distinction';
  /** Rôle (ligne de la console) concerné. */
  role: Cible;
  avant: string;
  arriere: string;
  rapport: number;
  /** 4,5 (texte), 3 (élément graphique) ; 0 pour une distinction. */
  minimum: number;
  /** Couleur proposée par « Corriger » (même teinte) et ce qu'elle remplace. */
  correction?: { cible: Cible; couleur: string };
  message?: string;
}

export const controleReussi = (c: Controle) => c.type === 'contraste' && c.rapport >= c.minimum;

/** Contrôles WCAG AA dans les deux thèmes, puis ressemblance avec les couleurs d'état. */
export function controles(c: Charte): Controle[] {
  const res: Controle[] = [];
  const liee = tertiaireLiee(c);
  for (const mode of ['clair', 'sombre'] as Mode[]) {
    const t = jetons(c, mode);
    const ajoute = (
      id: string,
      libelle: string,
      role: Cible,
      avant: string,
      arriere: string,
      minimum: number,
      corriger?: { cible: Cible; couleur: string; contre: string },
    ) => {
      const rapport = contraste(avant, arriere);
      const r: Controle = {
        id: `${id}-${mode}`,
        libelle,
        mode,
        type: 'contraste',
        role,
        avant,
        arriere,
        rapport,
        minimum,
      };
      if (rapport < minimum && corriger)
        r.correction = {
          cible: corriger.cible,
          couleur: ajusterPourContraste(corriger.couleur, corriger.contre, minimum),
        };
      res.push(r);
    };
    const p = t.accent;
    ajoute('bouton', 'Texte des boutons principaux', 'primaire', t['accent-fg'], p, 4.5, {
      cible: 'primaire',
      couleur: p,
      contre: t['accent-fg'],
    });
    ajoute('lien', 'Liens, onglet et menu actifs', 'primaire', t['accent-text'], t.surface, 4.5);
    ajoute('focus', 'Contour de focus sur le fond de page', 'primaire', p, t.bg, 3, {
      cible: 'primaire',
      couleur: p,
      contre: t.bg,
    });
    if (c.menuLateral === 'secondaire')
      ajoute('menu', 'Texte du menu latéral', 'secondaire', t['nav-fg'], t['nav-bg'], 4.5, {
        cible: 'secondaire',
        couleur: t['nav-bg'],
        contre: t['nav-fg'],
      });
    const roleTer: Role = liee ? 'primaire' : 'tertiaire';
    ajoute('etiquette', 'Étiquettes des sections', roleTer, t['tertiaire-text'], t['tertiaire-soft'], 4.5);
    ajoute('cadre', 'Cadre des sections sur le fond du plan', roleTer, t.tertiaire, t.surface, 3, {
      cible: roleTer,
      couleur: t.tertiaire,
      contre: t.surface,
    });
  }
  // Couleurs d'état personnalisées : la pastille pleine doit se voir sur les cartes.
  for (const e of ETATS) {
    const base = c.etats[e.etat];
    if (!base) continue;
    const rapport = contraste(base, BLANC);
    const r: Controle = {
      id: `etat-${e.etat}-clair`,
      libelle: `Couleur « ${e.libelle} » sur fond blanc`,
      mode: 'clair',
      type: 'contraste',
      role: e.etat,
      avant: base,
      arriere: BLANC,
      rapport,
      minimum: 3,
    };
    if (rapport < 3) r.correction = { cible: e.etat, couleur: ajusterPourContraste(base, BLANC, 3) };
    res.push(r);
  }
  // Couleurs d'état personnalisées : le texte des pastilles et boutons pleins (blanc ou encre) doit se lire.
  for (const e of ETATS) {
    const base = c.etats[e.etat];
    if (!base) continue;
    for (const mode of ['clair', 'sombre'] as Mode[]) {
      const t = jetons(c, mode);
      const avant = t[`${e.famille}-solid-fg`];
      const arriere = t[`${e.famille}-solid`];
      const rapport = contraste(avant, arriere);
      const r: Controle = {
        id: `plein-${e.etat}-${mode}`,
        libelle: `Texte sur la couleur « ${e.libelle} » pleine`,
        mode,
        type: 'contraste',
        role: e.etat,
        avant,
        arriere,
        rapport,
        minimum: 4.5,
      };
      // La couleur pleine du thème sombre est calculée : seule celle du thème clair se corrige directement.
      if (rapport < 4.5 && mode === 'clair')
        r.correction = { cible: e.etat, couleur: ajusterPourContraste(base, avant, 4.5) };
      res.push(r);
    }
  }
  // Une couleur de marque trop proche d'une couleur d'état : un bouton se lirait comme un état de la MEP.
  for (const role of ['primaire', 'tertiaire'] as Role[]) {
    if (role === 'tertiaire' && liee) continue;
    const coul = couleurSaisie(c, role);
    for (const e of [ETATS[3], ETATS[0], ETATS[2], ETATS[1]]) {
      const ref = c.etats[e.etat] ?? ETAT_DEFAUT[e.etat];
      if (ecartTeinte(coul, ref) < 14 && Math.abs(luminosite(coul) - luminosite(ref)) < 0.18)
        res.push({
          id: `distinction-${role}-${e.etat}`,
          libelle: `${role === 'primaire' ? 'Primaire' : 'Tertiaire'} proche du ${e.nom}`,
          mode: 'clair',
          type: 'distinction',
          role,
          avant: coul,
          arriere: ref,
          rapport: contraste(coul, ref),
          minimum: 0,
          message: 'Un bouton ou une section risque de se lire comme un état de la MEP.',
        });
    }
  }
  return res;
}

/** Applique une correction proposée : la couleur claire, la retouche du thème sombre ou la couleur d'état. */
export function appliquerCorrection(c: Charte, ctrl: Controle): Charte {
  if (!ctrl.correction) return c;
  const { cible, couleur } = ctrl.correction;
  if (cible === 'primaire' || cible === 'secondaire' || cible === 'tertiaire') {
    if (ctrl.mode === 'sombre') return { ...c, sombre: { ...c.sombre, [cible]: couleur } };
    return { ...c, couleurs: { ...c.couleurs, [cible]: couleur } };
  }
  return { ...c, etats: { ...c.etats, [cible]: couleur } };
}

// ---------- Exports Excel (thème clair) ----------

/** Arrondi au pair le plus proche, comme Math.Round de .NET (PaletteExcel.Eclaircir). */
function arrondiPair(x: number): number {
  const f = Math.floor(x);
  const d = x - f;
  if (d > 0.5) return f + 1;
  if (d < 0.5) return f;
  return f % 2 === 0 ? f : f + 1;
}

/** Couleur mélangée à du blanc (`part` = 1 donne du blanc), comme PaletteExcel.Eclaircir. */
function eclaircir(hex: string, part: number): string {
  const { r, v, b } = hexVersRvb(hex);
  const canal = (c: number) => {
    const x = c / 255;
    return arrondiPair((x + (1 - x) * part) * 255)
      .toString(16)
      .padStart(2, '0');
  };
  return `#${canal(r)}${canal(v)}${canal(b)}`;
}

export interface PaletteExcel {
  primaire: string;
  secondaire: string;
  tertiaire: string;
  fondEnTete: string;
  texteEnTete: string;
  texteTitre: string;
  texteLien: string;
  fondSection: string;
  texteSection: string;
  succes: [string, string];
  echec: [string, string];
  enCours: [string, string];
  attention: [string, string];
}

/**
 * Palette des classeurs (même calcul que PaletteExcel côté serveur) : en-têtes en primaire, titres en secondaire (la
 * secondaire par défaut garde le texte d'accent d'aujourd'hui), intertitres sur le fond pâle de la tertiaire.
 */
export function paletteExcel(c: Charte): PaletteExcel {
  const t = jetons(c, 'clair');
  const primaire = t.accent;
  const secondaire =
    c.couleurs.secondaire === CHARTE_DEFAUT.couleurs.secondaire ? t['accent-text'] : c.couleurs.secondaire;
  const tertiaire = t['tertiaire-soft'];
  const texteSurExcel = (fond: string) => (contraste(BLANC, fond) >= contraste(ENCRE, fond) ? BLANC : ENCRE);
  const texteTitre = contraste(secondaire, BLANC) >= 4.5 ? secondaire : ENCRE;
  const fondSection = luminanceRelative(tertiaire) >= 0.75 ? tertiaire : eclaircir(tertiaire, 0.85);
  return {
    primaire,
    secondaire,
    tertiaire,
    fondEnTete: primaire,
    texteEnTete: texteSurExcel(primaire),
    texteTitre,
    texteLien: contraste(primaire, BLANC) >= 4.5 ? primaire : texteTitre,
    fondSection,
    texteSection: contraste(texteTitre, fondSection) >= 4.5 ? texteTitre : texteSurExcel(fondSection),
    succes: [t['ok-bg'], t['ok-fg']],
    echec: [t['danger-bg'], t['danger-fg']],
    enCours: [t['run-bg'], t['run-fg']],
    attention: [t['warn-bg'], t['warn-fg']],
  };
}

// ---------- Fichier charte.json (version 1) ----------

export const VERSION_FICHIER = 1;
export const LONGUEUR_NOM = 80;

/** Forme du fichier charte.json et des échanges avec l'API (couleurs « #RRGGBB »). */
export interface CharteFichier {
  $schema?: string;
  version?: number;
  nom?: string;
  couleurs?: { primaire?: string; secondaire?: string; tertiaire?: string };
  menuLateral?: string;
  gris?: string;
  sombre?: { primaire?: string; secondaire?: string; tertiaire?: string };
  etats?: { succes?: string; avertissement?: string; erreur?: string; info?: string };
}

export interface Probleme {
  code: string;
  champ: string;
  message: string;
}

export interface LectureCharte {
  charte: Charte | null;
  erreurs: Probleme[];
  avertissements: Probleme[];
}

const majuscules = (hex: string) => hex.toUpperCase();

/** Charte au format du fichier (ce qu'exporte la console et `gestion-mep charte exporter`). */
export function versFichier(c: Charte, schema?: string): CharteFichier {
  const f: CharteFichier = {};
  if (schema) f.$schema = schema;
  f.version = VERSION_FICHIER;
  f.nom = c.nom;
  f.couleurs = {
    primaire: majuscules(c.couleurs.primaire),
    secondaire: majuscules(c.couleurs.secondaire),
    tertiaire: tertiaireLiee(c) ? SUIT_PRIMAIRE : majuscules(c.couleurs.tertiaire),
  };
  f.menuLateral = c.menuLateral;
  f.gris = c.gris;
  const sombre = Object.fromEntries(
    (['primaire', 'secondaire', 'tertiaire'] as Role[]).flatMap((r) =>
      c.sombre[r] ? [[r, majuscules(c.sombre[r])]] : [],
    ),
  );
  if (Object.keys(sombre).length) f.sombre = sombre;
  const etats = Object.fromEntries(
    ETATS.flatMap((e) => (c.etats[e.etat] ? [[e.etat, majuscules(c.etats[e.etat]!)]] : [])),
  );
  if (Object.keys(etats).length) f.etats = etats;
  return f;
}

const CLES_RACINE = ['$schema', 'version', 'nom', 'couleurs', 'menuLateral', 'gris', 'sombre', 'etats'];
const CLES_ROLES = ['primaire', 'secondaire', 'tertiaire'];
const CLES_ETATS = ETATS.map((e) => e.etat as string);

const estObjet = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null && !Array.isArray(v);

/**
 * Lit une charte au format du fichier (objet déjà analysé). Erreurs bloquantes : version inconnue, couleur invalide,
 * valeur hors liste, type inattendu, primaire absente. Avertissement : clé inconnue (ignorée). Mêmes codes que le serveur.
 */
export function lireFichier(valeur: unknown): LectureCharte {
  const erreurs: Probleme[] = [];
  const avertissements: Probleme[] = [];
  const erreur = (code: string, champ: string, message: string) => erreurs.push({ code, champ, message });
  if (!estObjet(valeur)) {
    erreur('racine', '', 'Un objet JSON { … } est attendu.');
    return { charte: null, erreurs, avertissements };
  }
  const inconnues = (o: Record<string, unknown>, connues: string[], prefixe: string) => {
    for (const k of Object.keys(o))
      if (!connues.includes(k))
        avertissements.push({
          code: 'cle-inconnue',
          champ: prefixe + k,
          message: `Clé inconnue ignorée : « ${prefixe + k} ».`,
        });
  };
  inconnues(valeur, CLES_RACINE, '');
  const c: Charte = structuredClone(CHARTE_DEFAUT);
  const v = valeur.version;
  if (v !== undefined && v !== VERSION_FICHIER)
    erreur('version', 'version', `Version ${JSON.stringify(v)} inconnue : seule la version 1 est lue.`);
  if (valeur.nom !== undefined) {
    if (typeof valeur.nom !== 'string') erreur('type', 'nom', 'nom : texte attendu.');
    else if (valeur.nom.trim().length > LONGUEUR_NOM)
      erreur('longueur', 'nom', `nom : ${LONGUEUR_NOM} caractères au plus.`);
    else if (valeur.nom.trim()) c.nom = valeur.nom.trim();
  }
  const couleur = (champ: string, brute: unknown, accepterPrimaire = false): string | null => {
    if (typeof brute !== 'string') {
      erreur('type', champ, `${champ} : code couleur attendu entre guillemets, par exemple "#0F766E".`);
      return null;
    }
    if (accepterPrimaire && brute.trim().toLowerCase() === SUIT_PRIMAIRE) return SUIT_PRIMAIRE;
    const n = normaliserHex(brute);
    if (!n)
      erreur(
        'couleur',
        champ,
        `${champ} : « ${brute} » n'est pas une couleur (attendu #RRGGBB ou #RGB, par exemple #0F766E).`,
      );
    return n;
  };
  if (valeur.couleurs === undefined) erreur('obligatoire', 'couleurs.primaire', 'couleurs.primaire est obligatoire.');
  else if (!estObjet(valeur.couleurs)) erreur('type', 'couleurs', 'couleurs : objet attendu.');
  else {
    const o = valeur.couleurs;
    inconnues(o, CLES_ROLES, 'couleurs.');
    if (o.primaire === undefined) erreur('obligatoire', 'couleurs.primaire', 'couleurs.primaire est obligatoire.');
    else c.couleurs.primaire = couleur('couleurs.primaire', o.primaire) ?? c.couleurs.primaire;
    if (o.secondaire !== undefined)
      c.couleurs.secondaire = couleur('couleurs.secondaire', o.secondaire) ?? c.couleurs.secondaire;
    if (o.tertiaire !== undefined)
      c.couleurs.tertiaire = couleur('couleurs.tertiaire', o.tertiaire, true) ?? c.couleurs.tertiaire;
  }
  const choix = <T extends string>(champ: string, brute: unknown, valeurs: readonly T[]): T | null => {
    if (brute === undefined) return null;
    if (typeof brute === 'string' && (valeurs as readonly string[]).includes(brute)) return brute as T;
    erreur('valeur', champ, `${champ} : ${valeurs.map((x) => `« ${x} »`).join(', ')} attendu.`);
    return null;
  };
  c.menuLateral = choix('menuLateral', valeur.menuLateral, ['pages', 'secondaire'] as const) ?? c.menuLateral;
  c.gris = choix('gris', valeur.gris, GRIS) ?? c.gris;
  if (valeur.sombre !== undefined) {
    if (!estObjet(valeur.sombre)) erreur('type', 'sombre', 'sombre : objet attendu.');
    else {
      inconnues(valeur.sombre, CLES_ROLES, 'sombre.');
      for (const r of CLES_ROLES) {
        const brute = valeur.sombre[r];
        if (brute === undefined) continue;
        const n = couleur(`sombre.${r}`, brute);
        if (n) c.sombre[r as Role] = n;
      }
    }
  }
  if (valeur.etats !== undefined) {
    if (!estObjet(valeur.etats)) erreur('type', 'etats', 'etats : objet attendu.');
    else {
      inconnues(valeur.etats, CLES_ETATS, 'etats.');
      for (const e of CLES_ETATS) {
        const brute = valeur.etats[e];
        if (brute === undefined) continue;
        const n = couleur(`etats.${e}`, brute);
        if (n) c.etats[e as Etat] = n;
      }
    }
  }
  return { charte: erreurs.length ? null : c, erreurs, avertissements };
}

/** Lecture d'une charte connue valide (réponse de l'API). */
export function depuisFichier(f: CharteFichier): Charte {
  return lireFichier(f).charte ?? CHARTE_DEFAUT;
}

/** Deux chartes identiques (comparaison des formes normalisées). */
export function memeCharte(a: Charte, b: Charte): boolean {
  return JSON.stringify(versFichier(a)) === JSON.stringify(versFichier(b));
}

/** Lit le texte d'un fichier charte.json : JSON illisible → erreur « json », puis {@link lireFichier}. */
export function lireTexte(texte: string): LectureCharte {
  let valeur: unknown;
  try {
    valeur = JSON.parse(texte);
  } catch {
    return {
      charte: null,
      erreurs: [{ code: 'json', champ: '', message: 'Fichier illisible : JSON invalide.' }],
      avertissements: [],
    };
  }
  return lireFichier(valeur);
}
