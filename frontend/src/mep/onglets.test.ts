import { mep } from '../test/fabrique';
import { estAncienneAdresse, lireVue, parametresVue, sousOngletsVisibles, vueParDefaut } from './onglets';

const p = (s: string) => new URLSearchParams(s);

describe('Onglets d’une MEP', () => {
  it('ouvre la vue qui suit la phase', () => {
    expect(vueParDefaut(mep({ phase: 'Preparation' }))).toEqual({ onglet: 'preparation', sous: 'etapes' });
    expect(vueParDefaut(mep({ phase: 'Execution' }))).toEqual({ onglet: 'execution', sous: 'etapes' });
    expect(vueParDefaut(mep({ phase: 'Recette' }))).toEqual({ onglet: 'plan', sous: 'recette' });
    expect(vueParDefaut(mep({ phase: 'RetourArriere' }))).toEqual({ onglet: 'plan', sous: 'retour' });
    expect(vueParDefaut(mep({ phase: 'Cloturee', issue: 'Repliee' }))).toEqual({ onglet: 'plan', sous: 'retour' });
    expect(vueParDefaut(mep({ phase: 'Cloturee', issue: 'Reussie' }))).toEqual({ onglet: 'execution', sous: 'etapes' });
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
    expect(vueParDefaut(declenche)).toEqual({ onglet: 'plan', sous: 'retour' });
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
