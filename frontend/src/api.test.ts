import { api, ApiError } from './api';

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

describe('erreurs de l’API', () => {
  afterEach(() => vi.unstubAllGlobals());

  function repondre(statut: number, corps: unknown) {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        new Response(JSON.stringify(corps), {
          status: statut,
          headers: { 'Content-Type': 'application/problem+json' },
        }),
      ),
    );
  }

  it('lit le message et les motifs d’un corps « problem details »', async () => {
    repondre(400, {
      type: 'about:blank',
      title: 'Saisie invalide',
      status: 400,
      detail: 'Formulaire incomplet.',
      message: 'Formulaire incomplet.',
      erreurs: ['Le titre est obligatoire.', 'Choisir un système.'],
    });
    const err = await api.utilisateurs().catch((e: unknown) => e);
    expect(err).toBeInstanceOf(ApiError);
    expect(err).toMatchObject({
      status: 400,
      message: 'Formulaire incomplet.',
      erreurs: ['Le titre est obligatoire.', 'Choisir un système.'],
    });
  });

  it('garde la phrase d’un refus de droits', async () => {
    repondre(403, { title: 'Droits insuffisants', status: 403, detail: 'Réservé aux administrateurs.' });
    await expect(api.adminUtilisateurs()).rejects.toMatchObject({
      status: 403,
      message: 'Réservé aux administrateurs.',
    });
  });
});
