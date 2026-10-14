import {
  abregerEnvironnement,
  basculerEnvironnement,
  environnementsEtape,
  environnementsParDefaut,
  nomDeProduction,
  toucheProduction,
} from './environnements';

const bdd = {
  id: 's1',
  environnements: [
    { nom: 'Préproduction', production: false },
    { nom: 'Production', production: true },
  ],
};

describe('environnements', () => {
  it('abrège les noms courants et reconnaît la production', () => {
    expect(['Production', 'Préproduction', 'Développement', 'Recette'].map(abregerEnvironnement)).toEqual([
      'Prod',
      'Préprod',
      'Dév',
      'Recette',
    ]);
    expect(nomDeProduction(' PROD-2')).toBe(true);
    expect(nomDeProduction('Préproduction')).toBe(false);
  });

  it('une MEP touche la production si l’un de ses systèmes y va', () => {
    expect(toucheProduction({ systemes: [bdd] as never })).toBe(true);
    expect(toucheProduction({ systemes: [{ ...bdd, environnements: [bdd.environnements[0]] }] as never })).toBe(false);
  });

  it('environnements d’une étape dans l’ordre du système ; un environnement retiré du système est signalé', () => {
    const etape = {
      environnements: [
        { systemeId: 's1', nom: 'Recette' },
        { systemeId: 's1', nom: 'production' },
        { systemeId: 's1', nom: 'Préproduction' },
        { systemeId: 's2', nom: 'Production' },
      ],
    };
    expect(environnementsEtape(etape, bdd)).toEqual([
      { nom: 'Préproduction', production: false, retire: false },
      { nom: 'production', production: true, retire: false },
      { nom: 'Recette', production: false, retire: true },
    ]);
  });

  it('coche d’office l’environnement unique d’un système ; bascule sans tenir compte de la casse', () => {
    expect(environnementsParDefaut(bdd)).toEqual([]);
    expect(environnementsParDefaut({ id: 's2', environnements: [{ nom: 'Production', production: true }] })).toEqual([
      { systemeId: 's2', nom: 'Production' },
    ]);
    const liste = basculerEnvironnement([], 's1', 'Production');
    expect(liste).toEqual([{ systemeId: 's1', nom: 'Production' }]);
    expect(basculerEnvironnement(liste, 's1', 'production')).toEqual([]);
  });
});
