import { mep, utilisateur } from '../../test/fabrique';
import { participantDepuisCompte, responsableDepuisSuggestion, suggestionsResponsablesTechniques } from './suggestions';

const sys = (id: string, nom: string, catalogueId: string | null) => ({ ...mep().systemes[0], id, nom, catalogueId });

describe('suggestions de responsables techniques', () => {
  const systemes = [sys('s1', 'API', 'c-api'), sys('s2', 'BDD', 'c-bdd'), sys('s3', 'Libre', null)];
  const bob = utilisateur({
    id: 'u2',
    nomAffiche: 'Bob',
    expertises: [
      { systemeCatalogueId: 'c-api', detail: 'Expert API' },
      { systemeCatalogueId: 'c-bdd', detail: 'Expert API' },
    ],
  });
  const alice = utilisateur({
    id: 'u3',
    nomAffiche: 'Alice',
    expertises: [{ systemeCatalogueId: 'c-bdd', detail: 'DBA' }],
  });
  const claire = utilisateur({
    id: 'u1',
    nomAffiche: 'Claire',
    expertises: [{ systemeCatalogueId: 'c-web', detail: 'Front' }],
  });

  it('retient les comptes dont une expertise porte sur un système (catalogue) de la MEP, le plus couvrant d’abord', () => {
    const r = suggestionsResponsablesTechniques({ ...mep(), systemes }, [alice, claire, bob]);
    expect(r.map((s) => s.utilisateur.nomAffiche)).toEqual(['Bob', 'Alice']);
    expect(r[0].systemes.map((x) => x.systeme.nom)).toEqual(['API', 'BDD']);
  });

  it('ignore les systèmes libres (sans catalogueId) et les comptes sans expertise utile', () => {
    const libre = utilisateur({ id: 'u5', expertises: [{ systemeCatalogueId: 'libre', detail: '' }] });
    expect(suggestionsResponsablesTechniques({ ...mep(), systemes }, [libre, claire])).toEqual([]);
  });

  it('exclut un compte dont le participant est déjà responsable technique, et relie le participant existant', () => {
    const m = {
      ...mep(),
      systemes,
      responsablesTechniques: [
        {
          participantId: 'p3',
          systemeIds: ['s2'],
          perimetre: '',
          disponibilite: 'Joignable' as const,
          contactJourJ: '',
        },
      ],
    };
    // p3 = Alice (utilisateur u3), p2 = Bob (u2)
    const r = suggestionsResponsablesTechniques(m, [
      { ...alice, id: 'u3' },
      { ...bob, id: 'u2' },
    ]);
    expect(r.map((s) => s.utilisateur.nomAffiche)).toEqual(['Bob']);
    expect(r[0].participant?.id).toBe('p2');
  });

  it('construit le responsable : systèmes concernés, périmètre = détails distincts, contact', () => {
    const [s] = suggestionsResponsablesTechniques({ ...mep(), systemes }, [bob]);
    expect(responsableDepuisSuggestion(s, 'p9', '06')).toEqual({
      participantId: 'p9',
      systemeIds: ['s1', 's2'],
      perimetre: 'Expert API',
      disponibilite: 'Joignable',
      contactJourJ: '06',
    });
  });

  it('pré-remplit une personne depuis le profil du compte', () => {
    expect(
      participantDepuisCompte(utilisateur({ id: 'u7', nomAffiche: 'Dana', role: 'DBA', contact: 'd@x.fr' })),
    ).toEqual({ nom: 'Dana', role: 'DBA', contact: 'd@x.fr', utilisateurId: 'u7' });
  });
});
