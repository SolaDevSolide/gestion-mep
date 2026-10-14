import {
  analyserRecherche,
  correspondRecherche,
  filtrerEtapesModeles,
  nombre,
  rechercheVide,
  repartitionTypes,
  trierParUsage,
} from './bibliotheque';
import type { ModeleEtape } from './types';

const modele = (p: Partial<ModeleEtape>): ModeleEtape => ({
  id: 'm',
  action: 'Étape',
  systemeCatalogueIds: ['c-bdd'],
  type: 'Standard',
  criticite: 'Moderee',
  dureeEstimeeMinutes: null,
  instructions: '',
  resultatAttendu: '',
  fin: 'TestUnitaire',
  sauvegardeEmplacement: '',
  sauvegardeMethodeRecuperation: '',
  resolutionIds: [],
  utilisations: 0,
  creeLe: '2026-01-01T00:00:00Z',
  version: 1,
  modifieLe: '2026-01-01T00:00:00Z',
  modifieParId: null,
  modifiePar: null,
  ...p,
});

const noms: Record<string, string> = { 'c-bdd': 'BDD', 'c-api': 'API', 'c-web': 'WEB' };

describe('Bibliothèques : recherche, tri et filtres', () => {
  it('reconnaît les noms de systèmes (casse et accents ignorés) ; les autres mots restent à chercher', () => {
    expect(analyserRecherche('bdd Sauvegardé', ['BDD', 'API'])).toEqual({ systemes: ['BDD'], mots: ['sauvegarde'] });
    expect(analyserRecherche('  ', ['BDD'])).toEqual({ systemes: [], mots: [] });
    expect(rechercheVide(analyserRecherche('', ['BDD']))).toBe(true);
  });

  it('un élément correspond s’il touche tous les systèmes cités et contient tous les mots', () => {
    const a = analyserRecherche('API BDD connexion', ['API', 'BDD']);
    expect(correspondRecherche(a, { systemes: ['API', 'BDD'], textes: ['Connexion refusée'] })).toBe(true);
    expect(correspondRecherche(a, { systemes: ['API'], textes: ['Connexion refusée'] })).toBe(false);
    expect(correspondRecherche(a, { systemes: ['API', 'BDD'], textes: ['Refus'] })).toBe(false);
    // Un élément sans système précis vaut pour tous.
    expect(correspondRecherche(a, { systemes: [], tousSystemes: true, textes: ['Titre', 'Connexion'] })).toBe(true);
  });

  it('trie par utilisations, puis par ordre de création (le plus ancien d’abord)', () => {
    const l = [
      { id: 'a', utilisations: 1, creeLe: '2026-01-01T00:00:00Z' },
      { id: 'b', utilisations: 3, creeLe: '2026-03-01T00:00:00Z' },
      { id: 'c', utilisations: 3, creeLe: '2026-02-01T00:00:00Z' },
    ];
    expect(trierParUsage(l, (x) => x.creeLe).map((x) => x.id)).toEqual(['c', 'b', 'a']);
  });

  it('filtre les étapes modèles par système, systèmes proposés, type et recherche (nom ou type)', () => {
    const liste = [
      modele({ id: '1', action: 'Sauvegarde complète', type: 'Sauvegarde', utilisations: 2 }),
      modele({ id: '2', action: 'Migration du schéma', utilisations: 5 }),
      modele({ id: '3', action: 'Déploiement', systemeCatalogueIds: ['c-api', 'c-bdd'], utilisations: 9 }),
      modele({ id: '4', action: 'Purge du cache', systemeCatalogueIds: ['c-web'] }),
    ];
    const nom = (id: string) => noms[id];
    const ids = (l: ModeleEtape[]) => l.map((m) => m.id);
    const vide = analyserRecherche('', []);
    expect(ids(filtrerEtapesModeles(liste, nom, vide, { systemeId: 'c-bdd' }))).toEqual(['3', '2', '1']);
    expect(ids(filtrerEtapesModeles(liste, nom, vide, { type: 'Sauvegarde' }))).toEqual(['1']);
    expect(ids(filtrerEtapesModeles(liste, nom, vide, { parmi: ['c-api', 'c-web'] }))).toEqual(['3', '4']);
    // « sauvegarde » trouvé par le nom ou par le type ; « normale » par le type.
    const a = analyserRecherche('BDD sauvegarde', Object.values(noms));
    expect(ids(filtrerEtapesModeles(liste, nom, a))).toEqual(['1']);
    expect(ids(filtrerEtapesModeles(liste, nom, analyserRecherche('api normale', Object.values(noms))))).toEqual(['3']);
  });

  it('résume la répartition par type et accorde les nombres', () => {
    expect(repartitionTypes([modele({}), modele({}), modele({ type: 'Sauvegarde' })])).toBe(
      '2 normales · 1 sauvegarde',
    );
    expect(repartitionTypes([])).toBe('Aucune étape modèle');
    expect(nombre(0, ['étape', 'étapes'])).toBe('0 étape');
    expect(nombre(2, ['étape', 'étapes'])).toBe('2 étapes');
  });
});
