import { describe, expect, it } from 'vitest';
import {
  ecrireFiltre,
  environnementsProposes,
  estVide,
  FILTRE_VIDE,
  filtrerMeps,
  lienListeSysteme,
  lireFiltre,
  nbCriteres,
  rolesDeLaPersonne,
  systemesHorsCatalogue,
  systemesTries,
  urlExportListe,
} from './rechercheMeps';
import type { MepResume } from './types';
import vecteurs from './vecteurs-filtre-meps.json';

/** MEP des vecteurs, complétées des champs que le filtre ne lit pas. */
const meps = vecteurs.meps.map(
  (m) =>
    ({
      ...m,
      dateMep: null,
      echeanceRepli: null,
      nbEtapes: 0,
      nbReussies: 0,
      nbEchecs: 0,
      toucheProduction: false,
      estModele: false,
      issue: null,
      retourArriereActive: false,
      go: null,
      clotureeLe: null,
    }) as unknown as MepResume & { id: string },
);

describe('cas partagés avec le serveur (vecteurs-filtre-meps.json)', () => {
  it.each(vecteurs.cas.map((c) => [c.nom, c] as const))('%s', (_, c) => {
    const f = lireFiltre(new URLSearchParams(c.parametres as Record<string, string>));
    expect(filtrerMeps(meps, f).map((m) => m.id)).toEqual(c.attendus);
  });
});

describe('filtre dans l’adresse', () => {
  it('relit ce qu’il écrit, sans paramètre pour un critère vide', () => {
    const f = {
      ...FILTRE_VIDE,
      recherche: 'stocks',
      systemes: ['a', 'libre:GED'],
      environnements: ['Production'],
      personne: 'p',
      roles: ['pilote' as const],
      etats: ['incidents' as const, 'repliee' as const],
      du: '2026-01-01',
      echec: true,
    };
    const p = ecrireFiltre(f);
    expect(p.toString()).toBe(
      'recherche=stocks&systeme=a%2Clibre%3AGED&environnement=Production&personne=p&role=pilote&etat=incidents%2Crepliee&du=2026-01-01&echec=1',
    );
    expect(lireFiltre(p)).toEqual(f);
    expect(ecrireFiltre(FILTRE_VIDE).toString()).toBe('');
  });

  it('garde les autres paramètres et ignore les valeurs inconnues', () => {
    const p = ecrireFiltre({ ...FILTRE_VIDE, equipes: ['e'] }, new URLSearchParams('onglet=x'));
    expect(p.toString()).toBe('onglet=x&equipe=e');
    expect(p.get('onglet')).toBe('x');
    expect(lireFiltre(new URLSearchParams('etat=abandonnee,reussie&role=chef&du=hier&echec=peut-etre'))).toEqual({
      ...FILTRE_VIDE,
      etats: ['reussie'],
    });
  });

  it('un rôle sans personne ne s’écrit pas et ne compte pas', () => {
    const f = { ...FILTRE_VIDE, roles: ['pilote' as const] };
    expect(ecrireFiltre(f).has('role')).toBe(false);
    expect(estVide(f)).toBe(true);
    expect(nbCriteres({ ...FILTRE_VIDE, du: '2026-01-01', au: '2026-02-01', recherche: 'x' })).toBe(2);
  });

  it('export Excel et lien vers la liste', () => {
    expect(urlExportListe({ ...FILTRE_VIDE, systemes: ['s'], echec: true }, false)).toBe(
      '/api/meps/excel?systeme=s&echec=1',
    );
    expect(urlExportListe({ ...FILTRE_VIDE, recherche: 'x', systemes: ['s'] }, true)).toBe(
      '/api/meps/excel?recherche=x&modele=true',
    );
    expect(urlExportListe(FILTRE_VIDE, false)).toBe('/api/meps/excel');
    expect(lienListeSysteme('s', { environnements: ['Recette'] })).toBe('/?systeme=s&environnement=Recette');
  });
});

describe('aides de la barre de filtres', () => {
  const ids = vecteurs.systemes;
  it('environnements des systèmes filtrés, production à la fin', () => {
    const catalogue = [
      {
        id: ids.BDD,
        environnements: [
          { nom: 'Production', production: true },
          { nom: 'Recette', production: false },
        ],
      },
      { id: ids.API, environnements: [{ nom: 'Préproduction', production: false }] },
    ];
    expect(environnementsProposes({ ...FILTRE_VIDE, systemes: [ids.BDD] }, meps, catalogue)).toEqual([
      { nom: 'Recette', production: false },
      { nom: 'Production', production: true },
    ]);
    expect(environnementsProposes(FILTRE_VIDE, meps, catalogue).map((e) => e.nom)).toEqual([
      'Recette',
      'Préproduction',
      'Production',
    ]);
  });

  it('systèmes hors catalogue, systèmes filtrés en premier, rôles de la personne', () => {
    expect(systemesHorsCatalogue(meps)).toEqual(['Ged']);
    const m3 = meps.find((m) => m.id === 'm3')!;
    expect(systemesTries({ ...FILTRE_VIDE, systemes: [ids.LDAP] }, m3.systemes).map((s) => s.nom)).toEqual([
      'LDAP',
      'BDD',
    ]);
    expect(rolesDeLaPersonne({ ...FILTRE_VIDE, personne: vecteurs.personnes.Alice }, m3)).toEqual([
      'Pilote',
      'Suppleant',
    ]);
    expect(rolesDeLaPersonne({ ...FILTRE_VIDE, personne: vecteurs.personnes.Claire }, m3)).toBeNull();
  });
});
