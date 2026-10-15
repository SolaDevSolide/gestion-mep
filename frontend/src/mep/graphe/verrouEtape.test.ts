import type { Verrou } from '../../types';
import { etatApresPrise, etatDepuisFlux, libellePriseDeMain, lireVerrous, nouvellesPrisesDeMain } from './verrouEtape';

const verrou = (session: string, nom = 'Alice'): Verrou => ({
  etapeId: 'e1',
  utilisateurId: 'u',
  nom,
  session,
  prisLe: '',
  expireLe: '',
  reprisDeUtilisateurId: null,
  reprisDeNom: null,
  reprisDeSession: null,
});

describe('verrou d’édition d’une étape', () => {
  it('à l’ouverture : pris, ou « Alice est dessus »', () => {
    expect(etatApresPrise('libre', { pris: true, verrou: verrou('moi') })).toBe('moi');
    expect(etatApresPrise('libre', { pris: false, verrou: verrou('autre') })).toBe('autre');
  });

  it('renouvellement refusé pendant l’édition : quelqu’un a pris la main', () => {
    expect(etatApresPrise('moi', { pris: false, verrou: verrou('autre', 'Bob') })).toBe('perdu');
  });

  it('flux temps réel : prise de main par un autre onglet, relâche (on reprend), retour à soi', () => {
    expect(etatDepuisFlux('moi', verrou('b'), 'a')).toEqual({ etat: 'perdu', reprendre: false });
    expect(etatDepuisFlux('autre', undefined, 'a')).toEqual({ etat: 'autre', reprendre: true });
    expect(etatDepuisFlux('moi', verrou('a'), 'a')).toEqual({ etat: 'moi', reprendre: false });
    expect(etatDepuisFlux('moi', undefined, 'a')).toEqual({ etat: 'moi', reprendre: false });
  });

  it('lecture tolérante de l’événement « verrous »', () => {
    expect(lireVerrous([verrou('a'), { autre: 1 }])).toHaveLength(1);
    expect(lireVerrous({})).toBeNull();
  });
});

describe('prise de main annoncée à tous', () => {
  const prise = {
    ...verrou('onglet-claire', 'Claire Martin'),
    reprisDeNom: 'Alice Durand',
    reprisDeSession: 'onglet-alice',
  };

  it('repère les prises de main apparues depuis la liste précédente, une seule fois', () => {
    expect(nouvellesPrisesDeMain([verrou('onglet-alice')], [prise])).toEqual([prise]);
    expect(nouvellesPrisesDeMain([prise], [prise])).toEqual([]);
    // Première liste reçue : prises anciennes, pas annoncées ; un verrou ordinaire ne l'est jamais.
    expect(nouvellesPrisesDeMain(null, [prise])).toEqual([]);
    expect(nouvellesPrisesDeMain([], [verrou('onglet-bob', 'Bob')])).toEqual([]);
  });

  it('« Claire a pris la main sur E0005 à Alice »', () => {
    expect(libellePriseDeMain(prise, 'E0005')).toBe('Claire a pris la main sur E0005 à Alice');
  });
});
