import type { ObjetImpacte } from '../../types';
import { correspond, nommerCategorie, parCategorie, resumeObjets } from './objets';

const objet = (categorie: string, nom: string): ObjetImpacte => ({
  id: nom,
  systemeId: 's1',
  objetCatalogueId: nom,
  categorie,
  type: '',
  nom,
  libelle: '',
  precision: '',
  version: '',
  repris: false,
});

describe('objets impactés', () => {
  it('accorde le nom de la catégorie au nombre', () => {
    expect(nommerCategorie('Tables', 1)).toBe('table');
    expect(nommerCategorie('Tables', 2)).toBe('tables');
    expect(nommerCategorie('Ordonnancement', 2)).toBe('ordonnancements');
    expect(nommerCategorie('Fichiers d’échange', 1)).toBe('fichier d’échange');
    expect(nommerCategorie('Procédures stockées', 1)).toBe('procédure stockée');
    expect(nommerCategorie('Base de données', 1)).toBe('base de données');
    expect(nommerCategorie('Index', 3)).toBe('index');
    expect(nommerCategorie('API', 2)).toBe('API');
  });

  it('range par catégorie dans l’ordre de la fiche et résume', () => {
    const groupes = parCategorie(
      [objet('Vues', 'v1'), objet('Tables', 'commandes'), objet('Tables', 'clients'), objet('Autre', 'x')],
      ['Tables', 'Vues'],
    );
    expect(groupes.map((g) => g.nom)).toEqual(['Tables', 'Vues', 'Autre']);
    expect(groupes[0].objets.map((o) => o.nom)).toEqual(['clients', 'commandes']);
    expect(resumeObjets(groupes)).toBe('2 tables, 1 vue, 1 autre');
  });

  it('recherche multi-termes sur le nom, le libellé, le type et la précision, sans accents', () => {
    const edtpdf = { nom: 'EDTPDF', libelle: 'Édition des PDF', type: 'Job', precision: 'après FACT03' };
    expect(correspond(edtpdf, 'FACT')).toBe(true);
    expect(correspond(edtpdf, 'edition job')).toBe(true);
    expect(correspond(edtpdf, 'edition chaine')).toBe(false);
    expect(correspond(edtpdf, '  ')).toBe(true);
  });
});
