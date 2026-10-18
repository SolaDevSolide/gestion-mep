import {
  equipesDeLaPersonne,
  filtrerParEquipe,
  groupeCorrespond,
  grouperParPersonne,
  personneDe,
  personnesDeRecherche,
} from './personnes';
import { utilisateur } from './test/fabrique';

const jean = utilisateur({ id: 'jean', login: 'jean', nomAffiche: 'Jean Dupont', equipeIds: ['exploit'] });
const sso = utilisateur({
  id: 'sso',
  login: 'jean.dupont@ent.fr',
  nomAffiche: 'J. Dupont (SSO)',
  nomPersonne: 'Jean Dupont',
  comptePrincipalId: 'jean',
  equipeIds: ['dev'],
});
const claire = utilisateur({ id: 'claire', login: 'claire', nomAffiche: 'Claire', equipeIds: ['dev'] });
const orphelin = utilisateur({ id: 'o', login: 'o', nomAffiche: 'O', comptePrincipalId: 'absent' });

describe('personnes', () => {
  it('regroupe les comptes reliés sous leur compte principal', () => {
    expect(personneDe(sso)).toBe('jean');
    expect(personneDe(jean)).toBe('jean');
    const groupes = grouperParPersonne([sso, claire, jean, orphelin]);
    expect(groupes.map((g) => [g.principal.id, g.relies.map((r) => r.id)])).toEqual([
      ['claire', []],
      ['jean', ['sso']],
      ['o', []],
    ]);
  });

  it('une personne a les équipes de tous ses comptes', () => {
    expect(equipesDeLaPersonne(jean, [jean, sso, claire])).toEqual(new Set(['exploit', 'dev']));
    expect(equipesDeLaPersonne(claire, [jean, sso, claire])).toEqual(new Set(['dev']));
  });

  it('filtre par équipe au niveau de la personne', () => {
    expect(filtrerParEquipe([jean, sso, claire], 'exploit').map((c) => c.id)).toEqual(['jean', 'sso']);
    expect(filtrerParEquipe([jean, sso, claire], 'dev').map((c) => c.id)).toEqual(['jean', 'sso', 'claire']);
    expect(filtrerParEquipe([jean, sso, claire], '')).toHaveLength(3);
  });

  it('la recherche d’un compte relié retrouve toute la personne', () => {
    const [groupe] = grouperParPersonne([jean, sso]);
    expect(groupeCorrespond(groupe, 'ent.fr')).toBe(true);
    expect(groupeCorrespond(groupe, 'jean dupont')).toBe(true);
    expect(groupeCorrespond(groupe, 'claire')).toBe(false);
  });

  it('la recherche des MEP propose une personne par compte principal, avec tous ses identifiants', () => {
    expect(personnesDeRecherche([sso, jean, claire])).toEqual([
      { id: 'claire', nom: claire.nomPersonne, detail: claire.login },
      { id: 'jean', nom: jean.nomPersonne, detail: `${jean.login}, ${sso.login}` },
    ]);
  });
});
