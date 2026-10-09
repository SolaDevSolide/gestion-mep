import { bloqueurs, etapeParDefaut, planActif, segmentsProgression } from './domaine';
import { completude } from './mep/completude';
import { etape, mep } from './test/fabrique';

describe('étape par défaut', () => {
  it('privilégie une étape en cours puis la première disponible à moi', () => {
    const a = etape({ id: 'a', ordre: 1, etat: 'Reussie' }, 'Terminee');
    const b = etape({ id: 'b', ordre: 2, responsableId: 'p2' }, 'Disponible');
    const c = etape({ id: 'c', ordre: 3, responsableId: 'p1' }, 'Disponible');
    expect(etapeParDefaut([a, b, c], 'p1')?.id).toBe('c');
  });
  it('sinon la première non terminée, sinon la première', () => {
    const a = etape({ id: 'a', ordre: 1, etat: 'Reussie' }, 'Terminee');
    const b = etape({ id: 'b', ordre: 2, responsableId: 'p2' }, 'Disponible');
    expect(etapeParDefaut([a, b], 'p1')?.id).toBe('b');
    expect(etapeParDefaut([a], 'p1')?.id).toBe('a');
    expect(etapeParDefaut([], 'p1')).toBeUndefined();
  });
  it('tient compte de la prise de main', () => {
    const a = etape({ id: 'a', ordre: 1, responsableId: 'p2', executantId: 'p1' }, 'Disponible');
    expect(etapeParDefaut([a], 'p1')?.id).toBe('a');
  });
});

describe('domaine', () => {
  it('liste les préalables non réussis', () => {
    const a = etape({ id: 'a', etat: 'Reussie' }, 'Terminee');
    const b = etape({ id: 'b', etat: 'AFaire' });
    const c = etape({ id: 'c', dependances: ['a', 'b'] }, 'Attend');
    expect(bloqueurs(c, [a, b, c]).map((e) => e.id)).toEqual(['b']);
  });
  it('plan actif selon la phase', () => {
    expect(planActif('Execution')).toBe('Deploiement');
    expect(planActif('RetourArriere')).toBe('RetourArriere');
    expect(planActif('Preparation')).toBeNull();
  });
  it('segments de progression', () => {
    const s = segmentsProgression([etape({ id: 'a', etat: 'Reussie' }), etape({ id: 'b', etat: 'EnCours' }), etape({ id: 'c', etat: 'Echec' }), etape({ id: 'd' })]);
    expect(s.map((x) => x.valeur)).toEqual([1, 1, 1, 1]);
  });
  it('complétude des sections', () => {
    const c = completude(mep({ participants: [], systemes: [] }));
    expect(c.personnes).toBe(false);
    expect(c.systemes).toBe(false);
    expect(c.validation).toBe(false);
    expect(completude(mep({ procedureRelue: { par: 'Bob', le: '2026-10-09T10:00:00Z' } })).validation).toBe(true);
  });
});
