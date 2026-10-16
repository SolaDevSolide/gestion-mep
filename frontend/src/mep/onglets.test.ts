import { etape, mep } from '../test/fabrique';
import { estAncienneAdresse, executeLaMep, lireVue, parametresVue, sousOngletsVisibles, vueParDefaut } from './onglets';

const p = (s: string) => new URLSearchParams(s);

describe('Onglets d’une MEP', () => {
  it('ouvre la vue qui suit la phase', () => {
    expect(vueParDefaut(mep({ phase: 'Preparation' }))).toEqual({ onglet: 'preparation', sous: 'etapes' });
    // p1 (la personne qui ouvre) est pilote : elle exécute la MEP.
    expect(vueParDefaut(mep({ phase: 'Execution', piloteId: 'p1' }))).toEqual({ onglet: 'execution', sous: 'etapes' });
    expect(vueParDefaut(mep({ phase: 'Recette' }))).toEqual({ onglet: 'plan', sous: 'recette' });
    expect(vueParDefaut(mep({ phase: 'RetourArriere', piloteId: 'p1' }))).toEqual({ onglet: 'plan', sous: 'retour' });
    expect(vueParDefaut(mep({ phase: 'Cloturee', issue: 'Repliee' }))).toEqual({ onglet: 'plan', sous: 'retour' });
    expect(vueParDefaut(mep({ phase: 'Cloturee', issue: 'Reussie' }))).toEqual({ onglet: 'execution', sous: 'etapes' });
  });

  it('pendant l’exécution, ouvre la vue d’ensemble à qui n’exécute aucune étape (#61)', () => {
    const deploiement = etape({ id: 'e1', plan: 'Deploiement', responsableId: 'p2', executantId: null });
    const enCours = mep({ phase: 'Execution', etapes: [deploiement] });
    expect(vueParDefaut(enCours).onglet).toBe('ensemble');
    expect(executeLaMep(enCours)).toBe(false);
    // Responsable d'une étape à venir, exécutant d'une étape démarrée, ou pilote : l'exécution.
    expect(vueParDefaut({ ...enCours, etapes: [{ ...deploiement, responsableId: 'p1' }] }).onglet).toBe('execution');
    expect(vueParDefaut({ ...enCours, etapes: [{ ...deploiement, executantId: 'p1' }] }).onglet).toBe('execution');
    // Retour arrière : seules comptent les étapes du plan de retour arrière.
    const repli = mep({ phase: 'RetourArriere', etapes: [{ ...deploiement, responsableId: 'p1' }] });
    expect(vueParDefaut(repli).onglet).toBe('ensemble');
    expect(
      vueParDefaut({ ...repli, etapes: [{ ...deploiement, plan: 'RetourArriere', responsableId: 'p1' }] }),
    ).toEqual({
      onglet: 'plan',
      sous: 'retour',
    });
    // Personne qui n'est pas participante.
    expect(vueParDefaut({ ...enCours, droits: { ...enCours.droits, participantId: null } }).onglet).toBe('ensemble');
    // La vue d'ensemble se demande par l'adresse, sauf pour un modèle.
    expect(lireVue(p('onglet=ensemble'), mep({ phase: 'Preparation' })).onglet).toBe('ensemble');
    expect(lireVue(p('onglet=ensemble'), mep({ estModele: true, phase: 'Preparation' })).onglet).toBe('preparation');
  });

  it('mène les anciennes adresses au bon sous-onglet de Plan', () => {
    const m = mep({ phase: 'Preparation' });
    expect(lireVue(p('onglet=etapes'), m)).toEqual({ onglet: 'plan', sous: 'etapes' });
    expect(lireVue(p('onglet=retour'), m)).toEqual({ onglet: 'plan', sous: 'retour' });
    expect(lireVue(p('onglet=recette'), m)).toEqual({ onglet: 'plan', sous: 'recette' });
    expect(estAncienneAdresse(p('onglet=recette'))).toBe(true);
    expect(estAncienneAdresse(p('onglet=plan&plan=recette'))).toBe(false);
  });

  it('lit l’onglet et le sous-onglet demandés, sinon la vue par défaut', () => {
    const m = mep({ phase: 'Recette' });
    expect(lireVue(p('onglet=plan&plan=etapes'), m)).toEqual({ onglet: 'plan', sous: 'etapes' });
    expect(lireVue(p('onglet=plan'), m)).toEqual({ onglet: 'plan', sous: 'recette' });
    expect(lireVue(p('onglet=plan&plan=inconnu'), m)).toEqual({ onglet: 'plan', sous: 'recette' });
    expect(lireVue(p('onglet=journal'), m).onglet).toBe('journal');
    expect(lireVue(p('onglet=nimporte'), m)).toEqual({ onglet: 'plan', sous: 'recette' });
    // Un modèle n'a pas d'onglet Exécution.
    expect(lireVue(p('onglet=execution'), mep({ estModele: true, phase: 'Preparation' })).onglet).toBe('preparation');
  });

  it('sans plan de retour arrière, pas de sous-onglet Retour arrière (sauf retour arrière déjà déclenché)', () => {
    const sans = mep({ phase: 'Preparation', retourArriereActive: false });
    expect(sousOngletsVisibles(sans)).toEqual(['etapes', 'recette']);
    expect(lireVue(p('onglet=retour'), sans)).toEqual({ onglet: 'plan', sous: 'etapes' });
    expect(lireVue(p('onglet=plan&plan=retour'), sans)).toEqual({ onglet: 'plan', sous: 'etapes' });
    const declenche = mep({
      phase: 'RetourArriere',
      retourArriereActive: false,
      retourArriere: { le: '2026-10-09T21:00:00Z', par: 'Alice', motif: 'Échec' },
    });
    expect(sousOngletsVisibles(declenche)).toEqual(['etapes', 'retour', 'recette']);
    expect(vueParDefaut({ ...declenche, piloteId: 'p1' })).toEqual({ onglet: 'plan', sous: 'retour' });
  });

  it('écrit l’adresse d’une vue en gardant les autres paramètres si demandé', () => {
    expect(parametresVue({ onglet: 'plan', sous: 'retour' }).toString()).toBe('onglet=plan&plan=retour');
    expect(parametresVue({ onglet: 'journal', sous: 'retour' }, p('onglet=plan&plan=retour')).toString()).toBe(
      'onglet=journal',
    );
    expect(parametresVue({ onglet: 'plan', sous: 'etapes' }, p('onglet=etapes&etape=e6')).toString()).toBe(
      'onglet=plan&etape=e6&plan=etapes',
    );
  });
});
