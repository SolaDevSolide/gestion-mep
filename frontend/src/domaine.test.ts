import { anomaliesHorsApprobation, bloqueurs, etapeParDefaut, planActif, segmentsProgression } from './domaine';
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
  it('complétude des sections, alignée sur les anomalies de la vérification du plan', () => {
    const c = completude(mep({ participants: [], systemes: [] }));
    expect(c.personnes).toBe(false);
    expect(c.systemes).toBe(false);
    // Validation : suit les anomalies de relecture et d'approbation.
    expect(completude(mep({ erreursPlan: ['La procédure doit être relue par un opérateur.'] })).validation).toBe(false);
    expect(completude(mep({ erreursPlan: ['Il faut 1 approbation(s).'] })).validation).toBe(false);
    expect(completude(mep({ erreursPlan: ['Claire a refusé : non'] })).validation).toBe(false);
    expect(completude(mep({ erreursPlan: [] })).validation).toBe(true);
  });
  it('un champ facultatif ne rend pas une section orange', () => {
    // Heures et indisponibilité absentes, restauration non testée : la vérification du plan ne les réclame pas.
    const base = mep({ heureDebut: null, heureFin: null, indisponibiliteMinutes: null, erreursPlan: [] });
    base.systemes[0].sauvegarde = { statut: 'Requise', moment: 'AvantMep', contenu: '', justification: '' };
    base.sauvegarde.restaurationTestee = false;
    const c = completude(base);
    expect(c.intervention).toBe(true);
    expect(c.sauvegarde).toBe(true);
  });
  it('les anomalies du plan rendent la section concernée orange', () => {
    expect(completude(mep({ erreursPlan: ['Renseigner une échéance de retour arrière future.'] })).intervention).toBe(false);
    expect(completude(mep({ erreursPlan: ['Renseigner le pilote.'] })).intervention).toBe(false);
    expect(completude(mep({ erreursPlan: ['Sauvegarde de API pendant la MEP : ajouter une étape de déploiement de type Sauvegarde sur ce système.'] })).sauvegarde).toBe(false);
    expect(completude(mep()).sauvegarde).toBe(false); // le système est encore « à renseigner »
  });
  it('responsables techniques : complète s\'il y en a et sans avertissement', () => {
    const rt = { participantId: 'p2', systemeIds: [], perimetre: '', disponibilite: 'Joignable' as const, contactJourJ: '' };
    expect(completude(mep()).techniques).toBe(false);
    expect(completude(mep({ responsablesTechniques: [rt] })).techniques).toBe(true);
    expect(completude(mep({ responsablesTechniques: [rt], avertissements: ['E0002 : Bob sera absent, ajouter une résolution type pour que quelqu\'un d\'autre puisse la reprendre.'] })).techniques).toBe(false);
  });
});

describe('anomaliesHorsApprobation', () => {
  it('écarte les anomalies d’approbation', () => {
    const erreursPlan = ['Il faut 1 approbation(s).', 'Claire a refusé : trop risqué', 'Ajouter au moins une fiche de recette.'];
    expect(anomaliesHorsApprobation({ erreursPlan })).toEqual(['Ajouter au moins une fiche de recette.']);
  });
});
