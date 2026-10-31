import type { ApplicationLiaison, CatalogueLiaison, LiaisonNoosphere } from '../../types';
import {
  basculerApplication,
  basculerDomaine,
  etatDomaine,
  libelleOperation,
  liaisonCourante,
  rapprochementsInitiaux,
  resumeChoix,
  resultatEchange,
} from './liaisons';
import { estAdresseDemo, raisonInactif } from './communs';

const app = (id: string, domaineId: string | null, p: Partial<ApplicationLiaison> = {}): ApplicationLiaison => ({
  id,
  nom: id.toUpperCase(),
  description: '',
  domaineId,
  domaine: domaineId ?? '',
  equipe: '',
  type: 'app',
  cycle: 'live',
  url: '',
  suivie: false,
  systemeId: null,
  systeme: null,
  suggestions: [],
  ...p,
});

const c: CatalogueLiaison = {
  luLe: '2026-10-31T12:00:00Z',
  nomInstance: 'N',
  domaines: [
    { id: 'fin', nom: 'Finance', suivi: false, nbApplications: 3 },
    { id: 'rh', nom: 'RH', suivi: false, nbApplications: 1 },
  ],
  applications: [app('a', 'fin'), app('b', 'fin'), app('old', 'fin', { cycle: 'retired' }), app('r', 'rh')],
};

describe('choix des applications suivies', () => {
  it('cocher un domaine suit ses applications, sauf les retirées', () => {
    const ch = basculerDomaine(c, { domaines: [], applications: [] }, 'fin', true);
    expect(ch).toEqual({ domaines: ['fin'], applications: ['a', 'b'] });
    expect(etatDomaine(c, ch, 'fin')).toMatchObject({ coche: true, entier: true, suivies: 2, total: 3 });
    expect(resumeChoix(c, ch)).toBe('Finance (2)');
  });

  it('décocher une application d’un domaine suivi passe ce domaine en choix une par une', () => {
    const ch = basculerApplication(c, { domaines: ['fin'], applications: ['a', 'b'] }, 'a', false);
    expect(ch).toEqual({ domaines: [], applications: ['b'] });
    expect(etatDomaine(c, ch, 'fin').coche).toBe('indetermine');
    const plus = basculerApplication(c, basculerDomaine(c, ch, 'rh', true), 'b', true);
    expect(resumeChoix(c, plus)).toBe('RH (1) et B');
  });

  it('rapprochement de départ : même nom proposé, nom proche à choisir, sinon création', () => {
    const r = rapprochementsInitiaux([
      app('x', null, { suggestions: [{ id: 's1', nom: 'X', memeNom: true }] }),
      app('y', null, { suggestions: [{ id: 's2', nom: 'Y-API', memeNom: false }] }),
      app('z', null),
    ]);
    expect(r).toEqual({ x: { type: 'rattacher', systemeId: 's1', nom: 'X' }, z: { type: 'creer' } });
  });
});

describe('libellés', () => {
  it('opérations du journal selon le sens et le résultat', () => {
    expect(libelleOperation({ operation: 'catalog', sens: 'sortant', resultat: 'ok' })).toBe('Lecture du catalogue');
    expect(libelleOperation({ operation: 'status', sens: 'entrant', resultat: 'ok' })).toBe(
      'État demandé par Noosphere',
    );
    expect(libelleOperation({ operation: 'notices', sens: 'entrant', resultat: 'refuse' })).toBe('Requête refusée');
    expect(resultatEchange('demo')).toEqual({ libelle: 'Démonstration', ton: 'later' });
  });

  it('liaison en service : la première qui n’est pas coupée', () => {
    const l = (id: string, etat: LiaisonNoosphere['etat']) => ({ id, etat }) as LiaisonNoosphere;
    expect(liaisonCourante([l('a', 'Coupee'), l('b', 'EnAttente')])?.id).toBe('b');
    expect(liaisonCourante([l('a', 'Coupee')])).toBeNull();
  });

  it('adresse de démonstration et raison d’un système inactif', () => {
    expect(estAdresseDemo('https://noosphere.demo.invalid/applications/api')).toBe(true);
    expect(estAdresseDemo('https://docs.alize.example')).toBe(false);
    expect(estAdresseDemo('pas une adresse')).toBe(false);
    expect(raisonInactif({ cycle: 'retired', absente: false })).toBe('retirée dans Noosphere');
    expect(raisonInactif({ cycle: 'live', absente: true })).toBe('absente de Noosphere');
    expect(raisonInactif(null)).toBeNull();
  });
});
