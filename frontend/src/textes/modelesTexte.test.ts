import {
  blocs,
  enHtml,
  enTexteSimple,
  generer,
  inconnues,
  manquantes,
  valeursDepuis,
  variablesUtilisees,
} from './modelesTexte';

const valeurs = valeursDepuis([
  { cle: 'application', texte: 'Portail', liste: null },
  { cle: 'date', texte: 'mardi 20 octobre 2026', liste: null },
  { cle: 'benefices', texte: null, liste: ['Plus rapide', 'Plus **simple**'] },
  { cle: 'indisponibilite', texte: null, liste: null },
  { cle: 'signature', texte: 'L’équipe <Infra>', liste: null },
]);
const libelles = new Map([
  ['application', 'Application'],
  ['date', 'Date de la MEP'],
  ['benefices', 'Bénéfices et nouveautés'],
  ['indisponibilite', 'Indisponibilité prévue'],
  ['signature', 'Signature'],
]);

const MODELE = `Bonjour,

MEP de **{{application}}** le {{ date }}.
Indisponibilité : {{indisponibilite}}

**Ce qui change**
{{benefices}}
- point fixe

{{signature}}
`;

describe('modèles de texte', () => {
  it('remplit les variables, met en liste une variable de liste seule sur sa ligne et signale les manques', () => {
    const lignes = generer(MODELE, valeurs);
    expect(enTexteSimple(lignes, libelles)).toBe(
      [
        'Bonjour,',
        '',
        'MEP de Portail le mardi 20 octobre 2026.',
        'Indisponibilité : [Indisponibilité prévue à compléter]',
        '',
        'Ce qui change',
        '- Plus rapide',
        // Les données de la MEP ne sont jamais interprétées comme du gras.
        '- Plus **simple**',
        '- point fixe',
        '',
        'L’équipe <Infra>',
      ].join('\n'),
    );
    expect(manquantes(MODELE, valeurs)).toEqual(['indisponibilite']);
    expect(variablesUtilisees(MODELE)).toEqual(['application', 'date', 'indisponibilite', 'benefices', 'signature']);
  });

  it('une liste au milieu d’une phrase est séparée par des virgules', () => {
    expect(enTexteSimple(generer('Nouveautés : {{benefices}}.', valeurs), libelles)).toBe(
      'Nouveautés : Plus rapide, Plus **simple**.',
    );
  });

  it('regroupe les lignes en paragraphes et en listes', () => {
    const b = blocs(generer(MODELE, valeurs));
    expect(b.map((x) => x.type)).toEqual(['paragraphe', 'paragraphe', 'paragraphe', 'liste', 'paragraphe']);
  });

  it('produit un HTML échappé, avec le gras, les listes et les manques visibles', () => {
    const html = enHtml(generer(MODELE, valeurs), libelles);
    expect(html).toContain('<p>MEP de <strong>Portail</strong> le mardi 20 octobre 2026.<br>Indisponibilité : ');
    expect(html).toContain('[Indisponibilité prévue à compléter]</span>');
    expect(html).toContain('<ul><li>Plus rapide</li><li>Plus **simple**</li><li>point fixe</li></ul>');
    expect(html).toContain('<p>L’équipe &lt;Infra&gt;</p>');
  });

  it('repère les variables inconnues (faute de frappe)', () => {
    expect(inconnues('{{application}} {{aplication}}', libelles)).toEqual(['aplication']);
    expect(enTexteSimple(generer('{{aplication}}', valeurs), libelles)).toBe('[aplication à compléter]');
  });
});
