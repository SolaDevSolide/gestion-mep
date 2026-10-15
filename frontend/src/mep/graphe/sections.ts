import type { CercleSection, Etape, Mep, Section, SensCercle, TypePlan } from '../../types';

/**
 * Sections du plan (#60) : un cadre qui regroupe des étapes, avec des cercles nommés d'entrée et de sortie. Un repère,
 * pas une logique d'exécution : le serveur compile les cercles en préalables effectifs (`Etape.dependances`), seuls
 * utilisés par l'exécution. Le front lit les cercles pour dessiner le graphe et la vue de la section.
 */
export type { CercleSection, Section, SensCercle };

/** Champs des étapes liés aux sections. */
export type EtapeSection = Pick<Etape, 'sectionId' | 'portsAttendus' | 'dependancesDerivees'>;

/** Lecture tolérante : une donnée de test ou un brouillon peut ne pas porter ces champs. */
export const sectionsDe = (mep: Pick<Mep, 'sections'>): Section[] => mep.sections ?? [];

export function infoSection(e: Etape): EtapeSection {
  return {
    sectionId: e.sectionId ?? null,
    portsAttendus: e.portsAttendus ?? [],
    dependancesDerivees: e.dependancesDerivees ?? [],
  };
}

export const sectionsDuPlan = (mep: Pick<Mep, 'sections'>, plan: TypePlan): Section[] =>
  sectionsDe(mep).filter((s) => s.plan === plan);

/** Préalables saisis directement (hors cercles). */
export function dependancesDirectes(e: Etape): string[] {
  const derivees = new Set(infoSection(e).dependancesDerivees);
  return e.dependances.filter((d) => !derivees.has(d));
}

export const cerclesTries = (s: Section, sens: SensCercle): CercleSection[] =>
  s.ports.filter((c) => c.sens === sens).sort((a, b) => a.ordre - b.ordre || a.titre.localeCompare(b.titre));

export const etapesDeSection = (etapes: Etape[], sectionId: string): Etape[] =>
  etapes.filter((e) => infoSection(e).sectionId === sectionId);

/** Section et cercle d'un identifiant de cercle. */
export function trouverCercle(sections: Section[], id: string): { section: Section; cercle: CercleSection } | null {
  for (const section of sections) {
    const cercle = section.ports.find((c) => c.id === id);
    if (cercle) return { section, cercle };
  }
  return null;
}

export interface Avancement {
  total: number;
  reussies: number;
  echecs: number;
  enCours: number;
}

export function avancementSection(etapes: Etape[]): Avancement {
  return {
    total: etapes.length,
    reussies: etapes.filter((e) => e.etat === 'Reussie').length,
    echecs: etapes.filter((e) => e.etat === 'Echec').length,
    enCours: etapes.filter((e) => e.etat === 'EnCours').length,
  };
}

/** « 4/7, 1 en échec ». */
export function libelleAvancement(a: Avancement): string {
  const base = `${a.reussies}/${a.total}`;
  return a.echecs > 0 ? `${base}, ${a.echecs} en échec` : base;
}

/**
 * Repli d'une section : choix mémorisé de la personne s'il existe, sinon dépliées celles qu'elle a créées et repliées
 * les autres.
 */
export function estDepliee(s: Section, choix: Record<string, boolean>, moi: string | null): boolean {
  const memo = choix[s.id];
  if (memo !== undefined) return memo;
  return moi !== null && s.auteurId === moi;
}
