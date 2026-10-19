// Serveur simulé de la liste des MEP (#96) pour les tests : pages et comptes de la barre de filtres calculés avec les
// règles partagées du filtre (rechercheMeps.ts, les mêmes que FiltreMeps.cs), comme `GET /meps/page` et `/meps/facettes`.
import { cleSysteme } from '../pages/liste/listeMeps';
import { normaliser } from '../recherche';
import {
  correspond,
  environnementsProposes,
  filtrerMeps,
  lireFiltre,
  NOMS_ETATS,
  retient,
  systemesHorsCatalogue,
  type FiltreMeps,
  type NomEtat,
} from '../rechercheMeps';
import type { FacettesMeps, MepResume, PageMeps } from '../types';

export function page(toutes: MepResume[], p: URLSearchParams, apres?: string | null, limite = 30): PageMeps {
  const resultats = filtrerMeps(toutes, lireFiltre(p));
  const debut = apres ? Number(apres) : 0;
  return {
    meps: resultats.slice(debut, debut + limite),
    total: resultats.length,
    ensemble: toutes.length,
    suivant: debut + limite < resultats.length ? String(debut + limite) : null,
  };
}

export function facettes(toutes: MepResume[], p: URLSearchParams): FacettesMeps {
  const f = lireFiltre(p);
  const compter = (d: Record<string, number>, cle: string, g: FiltreMeps, m: MepResume) => {
    if (correspond(g, m)) d[cle] = (d[cle] ?? 0) + 1;
  };
  const systemes: Record<string, number> = {};
  const environnements: Record<string, number> = {};
  const equipes: Record<string, number> = {};
  const personnes: Record<string, number> = {};
  const etats: Record<string, number> = {};
  for (const m of toutes) {
    const cles = new Set(m.systemes.map((s) => cleSysteme(s.catalogueId ?? `libre:${s.nom}`)));
    for (const cle of cles) compter(systemes, cle, { ...f, systemes: [cle] }, m);
    const envs = new Set(
      m.systemes.filter((s) => retient(f, s)).flatMap((s) => s.environnements.map((e) => normaliser(e.nom.trim()))),
    );
    for (const e of envs) compter(environnements, e, { ...f, environnements: [e] }, m);
    for (const e of new Set(m.equipeIds)) compter(equipes, e, { ...f, equipes: [e] }, m);
    for (const i of m.intervenants) compter(personnes, i.personneId, { ...f, personne: i.personneId, roles: [] }, m);
    const etat = (Object.keys(NOMS_ETATS) as NomEtat[]).find((k) => NOMS_ETATS[k] === m.etat);
    if (etat) compter(etats, m.etat, { ...f, etats: [etat] }, m);
  }
  const sans: Record<string, number> = {};
  const nombre = (g: Partial<FiltreMeps>) => filtrerMeps(toutes, { ...f, ...g }).length;
  if (f.recherche.trim()) sans.recherche = nombre({ recherche: '' });
  if (f.systemes.length || f.echec) sans.systeme = nombre({ systemes: [], environnements: [], echec: false });
  if (f.environnements.length) sans.environnement = nombre({ environnements: [] });
  if (f.equipes.length) sans.equipe = nombre({ equipes: [] });
  if (f.personne) sans.personne = nombre({ personne: '', roles: [] });
  if (f.etats.length || f.phase || f.type) sans.etat = nombre({ etats: [], phase: '', type: '' });
  if (f.du || f.au) sans.periode = nombre({ du: '', au: '' });
  return {
    total: filtrerMeps(toutes, f).length,
    ensemble: toutes.length,
    systemes,
    environnements,
    equipes,
    personnes,
    etats,
    libres: systemesHorsCatalogue(toutes),
    environnementsProposes: environnementsProposes(f, toutes, []),
    sans,
  };
}
