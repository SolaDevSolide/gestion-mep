import type { Ton } from '../../format';
import { retourArriereEffectue } from '../../domaine';
import type { Etape, Evenement, Mep, Presence, TypePlan } from '../../types';
import { infoSection, type Section } from '../graphe/sections';

/**
 * Vue d'ensemble et suivi en direct (#61) : états lus par un chef de projet, chiffres clés, blocages et ordre du tableau de
 * bord, calculés à partir de la MEP elle-même. Rien de nouveau n'est stocké ; la fin estimée suit les mêmes règles que la
 * variable de texte `{{fin_estimee}}` côté serveur (`Regles.EstimerFin`).
 */
export type EtatVue = 'faite' | 'en-cours' | 'echec' | 'resolution' | 'a-venir' | 'bloquee';

export const LIBELLE_ETAT_VUE: Record<EtatVue, string> = {
  faite: 'Réussie',
  'en-cours': 'En cours',
  echec: 'Échec',
  resolution: 'Résolution en cours',
  'a-venir': 'À venir',
  bloquee: 'Bloquée',
};

export const TON_ETAT_VUE: Record<EtatVue, Ton> = {
  faite: 'ok',
  'en-cours': 'run',
  echec: 'danger',
  resolution: 'warn',
  'a-venir': 'wait',
  bloquee: 'danger',
};

/** Minutes ajoutées à une étape en échec (la résolution avant de la refaire) et à une étape qui dépasse sa durée. */
export const SURCOUT_ECHEC_MIN = 10;
export const SURCOUT_DEPASSEMENT_MIN = 5;

const MINUTE = 60_000;

/** Plan suivi : le retour arrière une fois déclenché (y compris après la clôture d'une MEP repliée), sinon le déploiement. */
export function planSuivi(mep: Pick<Mep, 'phase' | 'issue' | 'retourArriere'>): TypePlan {
  return retourArriereEffectue(mep) ? 'RetourArriere' : 'Deploiement';
}

/** MEP lancée : le Go a été donné et elle n'est pas clôturée (exécution, recette ou retour arrière). */
export const estLancee = (mep: Pick<Mep, 'phase'>) =>
  mep.phase === 'Execution' || mep.phase === 'Recette' || mep.phase === 'RetourArriere';

/**
 * Étapes en échec dont dépend (directement ou à travers des étapes à faire) chaque étape à faire : elle est « bloquée » tant
 * qu'elles ne sont pas réglées.
 */
export function causesBlocage(etapes: Etape[]): Map<string, Etape[]> {
  const parId = new Map(etapes.map((e) => [e.id, e]));
  const memo = new Map<string, Etape[]>();
  const visiter = (e: Etape): Etape[] => {
    const m = memo.get(e.id);
    if (m) return m;
    memo.set(e.id, []); // garde contre un cycle (le serveur les refuse)
    const causes = new Map<string, Etape>();
    for (const d of e.dependances) {
      const p = parId.get(d);
      if (!p) continue;
      if (p.etat === 'Echec') causes.set(p.id, p);
      else if (p.etat === 'AFaire') for (const c of visiter(p)) causes.set(c.id, c);
    }
    const r = [...causes.values()];
    memo.set(e.id, r);
    return r;
  };
  const res = new Map<string, Etape[]>();
  for (const e of etapes) {
    if (e.etat !== 'AFaire') continue;
    const c = visiter(e);
    if (c.length > 0) res.set(e.id, c);
  }
  return res;
}

export function etatVue(e: Etape, bloquees: ReadonlyMap<string, Etape[]>): EtatVue {
  switch (e.etat) {
    case 'Reussie':
      return 'faite';
    case 'EnCours':
      return 'en-cours';
    case 'Echec':
      return e.disponibilite.statut === 'ResolutionEnCours' ? 'resolution' : 'echec';
    default:
      return bloquees.has(e.id) ? 'bloquee' : 'a-venir';
  }
}

/** État d'une section : faite, en échec, résolution en cours, en cours, bloquée ou à venir (l'échec d'abord). */
export function etatSection(etapes: Etape[], bloquees: ReadonlyMap<string, Etape[]>): EtatVue {
  const etats = etapes.map((e) => etatVue(e, bloquees));
  if (etats.length > 0 && etats.every((x) => x === 'faite')) return 'faite';
  if (etats.includes('echec')) return 'echec';
  if (etats.includes('resolution')) return 'resolution';
  if (etats.includes('en-cours') || etats.includes('faite')) return 'en-cours';
  if (etats.includes('bloquee')) return 'bloquee';
  return 'a-venir';
}

/** Minutes écoulées depuis le démarrage d'une étape en cours (0 sinon). */
export function ecoulee(e: Etape, maintenant: number): number {
  return e.etat === 'EnCours' && e.debutReel ? (maintenant - Date.parse(e.debutReel)) / MINUTE : 0;
}

/** Étape en cours qui a dépassé sa durée estimée. */
export const depasse = (e: Etape, maintenant: number) =>
  e.etat === 'EnCours' && !!e.dureeEstimeeMinutes && ecoulee(e, maintenant) > e.dureeEstimeeMinutes;

/** Durée restante d'une étape (en minutes) pour l'estimation de fin. */
export function tempsRestant(e: Etape, maintenant: number): number {
  const d = e.dureeEstimeeMinutes ?? 0;
  switch (e.etat) {
    case 'Reussie':
      return 0;
    case 'EnCours': {
      const passe = ecoulee(e, maintenant);
      return passe > d ? SURCOUT_DEPASSEMENT_MIN : d - passe;
    }
    case 'Echec':
      return d + SURCOUT_ECHEC_MIN;
    default:
      return d;
  }
}

/** Plus long chemin (en minutes) à travers les préalables : ce qui fixe la fin du plan. */
export function cheminCritique(etapes: Etape[], dureeDe: (e: Etape) => number): number {
  const parId = new Map(etapes.map((e) => [e.id, e]));
  const memo = new Map<string, number>();
  const fin = (e: Etape): number => {
    const m = memo.get(e.id);
    if (m !== undefined) return m;
    memo.set(e.id, 0);
    const avant = Math.max(
      0,
      ...e.dependances
        .map((d) => parId.get(d))
        .filter((p): p is Etape => !!p)
        .map(fin),
    );
    const v = avant + dureeDe(e);
    memo.set(e.id, v);
    return v;
  };
  return Math.max(0, ...etapes.map(fin));
}

export interface Chiffres {
  total: number;
  faites: number;
  enCours: number;
  echecs: number;
  bloquees: number;
  pourcentage: number;
  /** Départ du suivi : le Go (ou le déclenchement du retour arrière) ; null avant le Go. */
  depart: number | null;
  passeMin: number;
  prevuMin: number;
  /** Fin prévue : départ + plus long chemin des durées estimées. */
  finPrevue: number | null;
  /** Fin estimée : maintenant + plus long chemin des durées restantes ; fin réelle une fois le plan terminé. */
  finEstimee: number | null;
  /** Retard estimé en minutes (négatif : en avance). */
  retardMin: number;
  /** Toutes les étapes sont faites : la fin est réelle. */
  terminee: boolean;
  /** Aucune durée estimée (MEP simplifiée sans durées) : ni fin estimée ni retard. */
  sansDuree: boolean;
}

/** Départ du suivi : le Go, ou pour le retour arrière son déclenchement. */
export function depart(mep: Mep): number | null {
  if (planSuivi(mep) === 'RetourArriere' && mep.retourArriere) return Date.parse(mep.retourArriere.le);
  return mep.demarrageAutorise ? Date.parse(mep.demarrageAutorise.le) : null;
}

/** Chiffres clés du plan suivi (`etapes`) à l'instant `maintenant` (en millisecondes). */
export function chiffres(mep: Mep, etapes: Etape[], maintenant: number): Chiffres {
  const bloquees = causesBlocage(etapes);
  const faites = etapes.filter((e) => e.etat === 'Reussie').length;
  const go = depart(mep);
  const prevuMin = cheminCritique(etapes, (e) => e.dureeEstimeeMinutes ?? 0);
  const sansDuree = prevuMin === 0;
  const terminee = etapes.length > 0 && faites === etapes.length;
  const finReelle = Math.max(0, ...etapes.map((e) => (e.finReelle ? Date.parse(e.finReelle) : 0)));
  const finPrevue = go === null || sansDuree ? null : go + prevuMin * MINUTE;
  const finEstimee =
    go === null
      ? null
      : terminee
        ? finReelle
        : sansDuree
          ? null
          : maintenant + cheminCritique(etapes, (e) => tempsRestant(e, maintenant)) * MINUTE;
  const fin = terminee ? finReelle : maintenant;
  return {
    total: etapes.length,
    faites,
    enCours: etapes.filter((e) => e.etat === 'EnCours').length,
    echecs: etapes.filter((e) => e.etat === 'Echec').length,
    bloquees: bloquees.size,
    pourcentage: etapes.length === 0 ? 0 : Math.round((faites / etapes.length) * 100),
    depart: go,
    passeMin: go === null ? 0 : Math.max(0, Math.round((fin - go) / MINUTE)),
    prevuMin,
    finPrevue,
    finEstimee,
    retardMin: finPrevue === null || finEstimee === null ? 0 : Math.round((finEstimee - finPrevue) / MINUTE),
    terminee,
    sansDuree,
  };
}

/** Retard qui mérite l'attention : orange au-delà de 5 minutes, rouge au-delà de 30. */
export const tonRetard = (retardMin: number): Ton => (retardMin > 30 ? 'danger' : retardMin > 5 ? 'warn' : 'ok');

/**
 * Ordre du tableau de bord « Suivi en direct » : les retours arrière d'abord, puis les MEP qui ont des échecs à traiter,
 * puis les autres ; à égalité, la plus ancienne lancée d'abord.
 */
export function rangSuivi(mep: Mep): number {
  if (mep.phase === 'RetourArriere') return 0;
  return mep.etapes.some((e) => e.plan === planSuivi(mep) && e.etat === 'Echec') ? 1 : 2;
}

export function trierSuivi(meps: Mep[]): Mep[] {
  return [...meps].sort(
    (a, b) =>
      rangSuivi(a) - rangSuivi(b) ||
      (depart(a) ?? Infinity) - (depart(b) ?? Infinity) ||
      a.outil.localeCompare(b.outil),
  );
}

/**
 * Où se trouve une personne présente sur la MEP, d'après sa présence localisée (`ici:etape:{id}`, `etape:{id}`,
 * `ici:section:{id}`, `section:{id}`, `preparation:…`, `recette:…`) : « sur E0004 », « dans la section Sauvegardes »…
 */
export function ouSeTrouve(p: Pick<Presence, 'elements'>, mep: Mep): string | null {
  for (const cle of p.elements) {
    const etape = /^(?:ici:)?etape:(.+)$/.exec(cle);
    const e = etape && mep.etapes.find((x) => x.id === etape[1]);
    if (e) return `sur ${e.code}`;
  }
  for (const cle of p.elements) {
    const section = /^(?:ici:)?section:(.+)$/.exec(cle);
    const s = section && mep.sections.find((x) => x.id === section[1]);
    if (s) return `dans la section ${s.titre}`;
    if (cle.startsWith('preparation:')) return 'dans la préparation';
    if (cle.startsWith('recette:')) return 'dans la recette';
  }
  return null;
}

/** Étapes de chaque section (tableau de bord) ; les étapes hors section n'y figurent pas. */
export function etapesParSection(etapes: Etape[], sections: Section[]): Map<string, Etape[]> {
  const m = new Map<string, Etape[]>(sections.map((s) => [s.id, []]));
  for (const e of etapes) {
    const sid = infoSection(e).sectionId;
    if (sid) m.get(sid)?.push(e);
  }
  return m;
}

/** Jour d'une MEP prévue (« AAAA-MM-JJ ») par rapport à `maintenant` : « Aujourd'hui », « Demain », sinon la date courte. */
export function jourRelatif(date: string, maintenant: number): string {
  const d = new Date(maintenant);
  const jour = (x: Date) =>
    `${x.getFullYear()}-${String(x.getMonth() + 1).padStart(2, '0')}-${String(x.getDate()).padStart(2, '0')}`;
  if (date === jour(d)) return 'Aujourd’hui';
  d.setDate(d.getDate() + 1);
  if (date === jour(d)) return 'Demain';
  const [a, m, j] = date.split('-');
  return `${j}/${m}/${a}`;
}

/** Événements marquants du journal : Go, échecs et réussites d'étape, résolutions, prises de main, non-retour, repli, clôture. */
const MARQUANTS = new Set([
  'gonogo.go',
  'phase.recette',
  'etape.echec',
  'etape.reussie',
  'etape.prise-en-main',
  'etape.non-retour-autorise',
  'resolution.demarree',
  'resolution.reussie',
  'resolution.echouee',
  'repli.declenche',
  'mep.cloturee',
]);

export const estMarquant = (ev: Pick<Evenement, 'type'>) => MARQUANTS.has(ev.type);

/** « 1 h 35 », « 35 min ». */
export function duree(min: number): string {
  const m = Math.abs(Math.round(min));
  if (m < 60) return `${m} min`;
  const h = Math.floor(m / 60);
  const r = m % 60;
  return r === 0 ? `${h} h` : `${h} h ${String(r).padStart(2, '0')}`;
}

export const heure = (ms: number | null) =>
  ms === null ? '—' : new Date(ms).toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' });
