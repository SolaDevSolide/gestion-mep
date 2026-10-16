import type { Mep } from '../types';

/** Onglets d'une MEP. Vue d'ensemble et Exécution n'existent pas pour un modèle. */
export type Onglet = 'ensemble' | 'preparation' | 'plan' | 'execution' | 'journal';
/** Sous-onglets de « Plan », affichés sous la barre d'onglets. */
export type SousOnglet = 'etapes' | 'retour' | 'recette';

export const ONGLETS: Onglet[] = ['ensemble', 'preparation', 'plan', 'execution', 'journal'];
export const SOUS_ONGLETS: SousOnglet[] = ['etapes', 'retour', 'recette'];

export const estOnglet = (v: string | null): v is Onglet => ONGLETS.some((o) => o === v);
export const estSousOnglet = (v: string | null): v is SousOnglet => SOUS_ONGLETS.some((o) => o === v);

export interface Vue {
  onglet: Onglet;
  /** Sous-onglet du plan (significatif seulement quand `onglet` vaut `plan`). */
  sous: SousOnglet;
}

/**
 * Une MEP sans plan de retour arrière (case décochée) n'a pas de sous-onglet Retour arrière, sauf si un retour arrière
 * a déjà été déclenché.
 */
export const retourVisible = (m: Pick<Mep, 'retourArriereActive' | 'retourArriere'>) =>
  m.retourArriereActive || m.retourArriere !== null;

/** Sous-onglets du plan affichés pour cette MEP. */
export const sousOngletsVisibles = (m: Mep): SousOnglet[] =>
  SOUS_ONGLETS.filter((s) => s !== 'retour' || retourVisible(m));

/** Sous-onglet du plan qui suit la phase : retour arrière pendant (ou après) un repli, recette pendant la recette. */
export function sousOngletParDefaut(m: Mep): SousOnglet {
  if ((m.phase === 'RetourArriere' || (m.phase === 'Cloturee' && m.issue === 'Repliee')) && retourVisible(m))
    return 'retour';
  if (m.phase === 'Recette') return 'recette';
  return 'etapes';
}

/**
 * Exécute la MEP en cours : le pilote, et chaque personne qui a (ou aura) une étape du plan qui se déroule, comme
 * responsable ou comme exécutant.
 */
export function executeLaMep(m: Mep): boolean {
  const moi = m.droits.participantId;
  if (moi === null) return false;
  const plan = m.phase === 'RetourArriere' ? 'RetourArriere' : 'Deploiement';
  return (
    moi === m.piloteId || m.etapes.some((e) => e.plan === plan && (e.responsableId === moi || e.executantId === moi))
  );
}

/**
 * Vue d'arrivée sur une MEP selon sa phase. Pendant l'exécution et le retour arrière, la personne qui n'exécute aucune
 * étape (chef de projet, curieux) arrive sur la vue d'ensemble ; les exécutants, sur l'exécution ou le plan.
 */
export function vueParDefaut(m: Mep): Vue {
  const sous = sousOngletParDefaut(m);
  switch (m.phase) {
    case 'Preparation':
      return { onglet: 'preparation', sous };
    case 'Execution':
      return { onglet: m.estModele || executeLaMep(m) ? 'execution' : 'ensemble', sous };
    case 'RetourArriere':
      return { onglet: m.estModele || executeLaMep(m) ? 'plan' : 'ensemble', sous };
    case 'Recette':
      return { onglet: 'plan', sous };
    case 'Cloturee':
      return m.issue === 'Repliee' ? { onglet: 'plan', sous } : { onglet: 'execution', sous };
  }
}

/**
 * Vue demandée par l'adresse (`?onglet=…&plan=…`). Les anciennes adresses `?onglet=etapes|retour|recette` mènent au
 * sous-onglet correspondant de « Plan ». Une demande inconnue (Exécution pour un modèle, Retour arrière masqué) donne la
 * vue par défaut, ou le sous-onglet par défaut.
 */
export function lireVue(params: URLSearchParams, m: Mep): Vue {
  const visibles = sousOngletsVisibles(m);
  const permis = (s: string | null): s is SousOnglet => estSousOnglet(s) && visibles.includes(s);
  const demande = params.get('onglet');
  if (estSousOnglet(demande)) return { onglet: 'plan', sous: permis(demande) ? demande : sousOngletParDefaut(m) };
  const sousDemande = params.get('plan');
  const sous = permis(sousDemande) ? sousDemande : sousOngletParDefaut(m);
  if (estOnglet(demande) && !(m.estModele && (demande === 'execution' || demande === 'ensemble')))
    return { onglet: demande, sous };
  return vueParDefaut(m);
}

/** Vrai si l'adresse est une ancienne forme (`?onglet=etapes|retour|recette`) à réécrire. */
export function estAncienneAdresse(params: URLSearchParams): boolean {
  return estSousOnglet(params.get('onglet'));
}

/** Paramètres d'adresse d'une vue ; les autres paramètres donnés (`?etape=…`) sont conservés. */
export function parametresVue(v: Vue, autres?: URLSearchParams): URLSearchParams {
  const p = new URLSearchParams(autres);
  p.set('onglet', v.onglet);
  if (v.onglet === 'plan') p.set('plan', v.sous);
  else p.delete('plan');
  return p;
}

/** Lien relatif vers un sous-onglet du plan (`?onglet=plan&plan=retour`). */
export const lienPlan = (sous: SousOnglet) => `?onglet=plan&plan=${sous}`;
