import { api } from './api';

describe('adresses des exports Excel', () => {
  it('reprend les filtres du journal, sans les valeurs vides', () => {
    expect(api.urlExportJournal('m1')).toBe('/api/meps/m1/journal/excel');
    expect(api.urlExportJournal('m1', { type: 'tous', recherche: '  ' })).toBe('/api/meps/m1/journal/excel');
    expect(api.urlExportJournal('m1', { type: 'etape', recherche: 'Go & no-go' })).toBe(
      '/api/meps/m1/journal/excel?type=etape&recherche=Go+%26+no-go',
    );
  });

  it('donne une adresse par vue de la MEP', () => {
    expect(api.urlExportPreparation('m1')).toBe('/api/meps/m1/preparation/excel');
    expect(api.urlExportEtapes('m1')).toBe('/api/meps/m1/etapes/excel');
    expect(api.urlExportRetourArriere('m1')).toBe('/api/meps/m1/retour-arriere/excel');
    expect(api.urlExportHistorique('c1')).toBe('/api/catalogue/systemes/c1/historique/excel');
  });
});
