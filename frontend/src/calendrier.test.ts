import {
  ajouterMois,
  colonnes,
  debutSemaine,
  deplacer,
  finDuGel,
  gelsDuJour,
  grilleMois,
  heureLocale,
  horaire,
  jourComplet,
  jourLocal,
  mepsParJour,
  plageGel,
  plageVue,
  texteConflit,
  texteGelMep,
  titreVue,
} from './calendrier';
import type { OccurrenceGel } from './types';

const PARIS = 'Europe/Paris';

const gel = (debut: string, fin: string, p: Partial<OccurrenceGel> = {}): OccurrenceGel => ({
  periodeGelId: 'g1',
  nom: 'Clôture comptable',
  motif: 'Arrêtés de fin de mois',
  debut,
  fin,
  fuseauHoraire: PARIS,
  globale: true,
  derogation: null,
  ...p,
});

describe('calendrier : jours et périodes', () => {
  it('place la grille du mois du lundi précédant le 1er, sur six semaines', () => {
    const jours = grilleMois('2026-11-17');
    expect(jours).toHaveLength(42);
    expect(jours[0]).toBe('2026-10-26');
    expect(jours[41]).toBe('2026-12-06');
    expect(plageVue('mois', '2026-11-17')).toEqual({ du: '2026-10-26', au: '2026-12-06' });
  });

  it('donne la semaine du lundi au dimanche et le mois entier pour la liste', () => {
    expect(debutSemaine('2026-11-15')).toBe('2026-11-09');
    expect(plageVue('semaine', '2026-11-11')).toEqual({ du: '2026-11-09', au: '2026-11-15' });
    expect(plageVue('liste', '2026-11-11')).toEqual({ du: '2026-11-01', au: '2026-11-30' });
  });

  it('avance d’un mois (ramené au 1er) ou d’une semaine', () => {
    expect(deplacer('mois', '2026-01-31', 1)).toBe('2026-02-01');
    expect(deplacer('semaine', '2026-11-30', 1)).toBe('2026-12-07');
    expect(ajouterMois('2026-01-31', 1)).toBe('2026-02-28');
  });

  it('écrit les titres en français', () => {
    expect(titreVue('mois', '2026-11-10', '2026-10-01')).toBe('Novembre 2026');
    expect(titreVue('liste', '2026-11-10', '2026-11-02')).toBe('Novembre 2026 · à venir');
    expect(titreVue('semaine', '2026-11-10', '2026-11-02')).toBe('Semaine du 9 au 15 novembre 2026');
    expect(titreVue('semaine', '2026-12-01', '2026-11-02')).toBe('Semaine du 30 novembre au 6 décembre 2026');
    expect(jourComplet('2026-12-01')).toBe('mardi 1er décembre');
  });
});

describe('calendrier : fuseau de l’instance', () => {
  it('lit les jours et les heures dans le fuseau, changement d’heure compris', () => {
    // 23:30 UTC le 27/11 : déjà le 28 à Paris (UTC+1).
    expect(jourLocal('2026-11-27T23:30:00Z', PARIS)).toBe('2026-11-28');
    expect(heureLocale('2026-07-01T19:00:00Z', PARIS)).toBe('21:00');
    expect(heureLocale('2026-11-27T20:00:00Z', PARIS)).toBe('21:00');
  });

  it('range les MEP par jour de début, celles d’avant la vue sur le premier jour', () => {
    const meps = [
      { id: 'a', debut: '2026-11-10T19:00:00Z' },
      { id: 'b', debut: '2026-11-10T20:30:00Z' },
      { id: 'c', debut: '2026-10-20T08:00:00Z' },
    ];
    const parJour = mepsParJour(meps, PARIS, '2026-10-26');
    expect(parJour.get('2026-11-10')?.map((m) => m.id)).toEqual(['a', 'b']);
    expect(parJour.get('2026-10-26')?.map((m) => m.id)).toEqual(['c']);
  });

  it('trouve les gels d’un jour, fin exclue', () => {
    const g = gel('2026-11-25T23:00:00Z', '2026-11-30T23:00:00Z');
    expect(gelsDuJour([g], '2026-11-26', PARIS)).toHaveLength(1);
    expect(gelsDuJour([g], '2026-11-30', PARIS)).toHaveLength(1);
    expect(gelsDuJour([g], '2026-12-01', PARIS)).toHaveLength(0);
    expect(gelsDuJour([g], '2026-11-25', PARIS)).toHaveLength(0);
    expect(plageGel(g, PARIS)).toBe('26/11 → 30/11');
    expect(finDuGel(g, PARIS)).toBe('Libre à partir du mardi 1er décembre');
    const heures = gel('2026-11-24T17:00:00Z', '2026-11-25T07:00:00Z');
    expect(plageGel(heures, PARIS)).toBe('24/11 18:00 → 25/11 08:00');
    expect(finDuGel(heures, PARIS)).toBe('Fin du gel : mercredi 25 novembre à 08:00');
  });

  it('écrit l’horaire d’une MEP', () => {
    const m = { debut: '2026-11-27T20:00:00Z', fin: '2026-11-27T22:30:00Z', journeeEntiere: false, enCours: false };
    expect(horaire(m, PARIS)).toBe('21:00 → 23:30');
    expect(horaire({ ...m, fin: null, enCours: true }, PARIS)).toBe('depuis 21:00');
    expect(horaire({ ...m, journeeEntiere: true }, PARIS)).toBe('journée entière');
  });
});

describe('calendrier : conflits et gels d’une MEP', () => {
  it('met côte à côte les MEP qui se chevauchent', () => {
    const meps = [
      { id: 'a', debut: '2026-11-10T19:00:00Z', fin: '2026-11-10T21:30:00Z', enCours: false },
      { id: 'b', debut: '2026-11-10T20:30:00Z', fin: '2026-11-10T22:00:00Z', enCours: false },
      { id: 'c', debut: '2026-11-10T23:00:00Z', fin: '2026-11-10T23:30:00Z', enCours: false },
    ];
    const places = colonnes(meps, Date.parse('2026-11-02T00:00:00Z'));
    expect(places.get('a')).toEqual({ colonne: 0, colonnes: 2 });
    expect(places.get('b')).toEqual({ colonne: 1, colonnes: 2 });
    expect(places.get('c')).toEqual({ colonne: 0, colonnes: 1 });
  });

  it('dit ce qu’il en est du Go selon la dérogation', () => {
    expect(texteGelMep(gel('', ''), 'Preparation')).toBe('Clôture comptable · Go bloqué sans dérogation');
    expect(texteGelMep(gel('', '', { derogation: 'Demandee' }), 'Preparation')).toBe(
      'Clôture comptable · dérogation demandée',
    );
    expect(texteGelMep(gel('', '', { derogation: 'Accordee' }), 'Preparation')).toBe(
      'Clôture comptable · dérogation accordée',
    );
    expect(texteGelMep(gel('', '', { derogation: 'Caduque' }), 'Preparation')).toBe(
      'Clôture comptable · dérogation tombée (date changée), Go bloqué',
    );
    expect(texteGelMep(gel('', ''), 'Execution')).toBe('Clôture comptable');
  });

  it('décrit un conflit avec l’autre MEP et ses heures', () => {
    const c = {
      mepId: 'x',
      outil: 'Portail adhérents',
      versionCible: '5.2.1',
      etat: 'Preparation' as const,
      debut: '2026-11-10T20:30:00Z',
      fin: '2026-11-10T22:00:00Z',
      systemes: ['WEB'],
    };
    expect(texteConflit(c, PARIS)).toBe('Conflit sur WEB avec Portail adhérents 5.2.1 (21:30 → 23:00)');
    expect(texteConflit({ ...c, fin: null }, PARIS)).toBe('Conflit sur WEB avec Portail adhérents 5.2.1 (dès 21:30)');
  });
});
