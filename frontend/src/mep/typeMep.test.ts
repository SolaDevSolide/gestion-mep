import type { ResolutionType } from '../types';
import { estSimplifiee, resolutionsParSysteme } from './typeMep';

const resolution = (id: string, systemeCatalogueIds: string[], actif = true) =>
  ({ id, titre: id, systemeCatalogueIds, actif }) as ResolutionType;

describe('MEP simplifiée', () => {
  it('reconnaît le type', () => {
    expect(estSimplifiee({ type: 'Simplifiee' })).toBe(true);
    expect(estSimplifiee({ type: 'Complete' })).toBe(false);
  });

  it('propose toutes les résolutions actives des systèmes de l’étape, par système, puis celles sans système', () => {
    const mep = {
      systemes: [
        { id: 's-web', nom: 'WEB', catalogueId: 'c-web' },
        { id: 's-api', nom: 'API', catalogueId: 'c-api' },
        { id: 's-bdd', nom: 'BDD', catalogueId: 'c-bdd' },
        { id: 's-libre', nom: 'Libre', catalogueId: null },
      ],
    } as never;
    const catalogue = [
      resolution('web1', ['c-web']),
      resolution('api1', ['c-api']),
      resolution('commune', []),
      resolution('bdd1', ['c-bdd']),
      resolution('web-api', ['c-web', 'c-api']),
      resolution('inactive', ['c-web'], false),
    ];
    const groupes = resolutionsParSysteme(mep, { systemeIds: ['s-web', 's-api', 's-libre'] }, catalogue);
    expect(groupes.map((g) => [g.systeme, g.resolutions.map((r) => r.id)])).toEqual([
      ['WEB', ['web1', 'web-api']],
      ['API', ['api1']],
      [null, ['commune']],
    ]);
  });
});
