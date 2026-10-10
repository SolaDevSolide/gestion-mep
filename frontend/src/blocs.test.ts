import { decouperBlocs, formaterMarkdown, premiereLigne, recomposerBlocs, type Bloc } from './blocs';

describe('decouperBlocs', () => {
  it('chaîne vide : aucun bloc', () => {
    expect(decouperBlocs('')).toEqual([]);
    expect(decouperBlocs(null)).toEqual([]);
    expect(decouperBlocs('  \n\n ')).toEqual([]);
  });

  it('texte seul : un bloc Texte, lignes vides de bord retirées', () => {
    expect(decouperBlocs('\n\nFaire la **sauvegarde**.\n- a\n- b\n\n')).toEqual([
      { type: 'texte', contenu: 'Faire la **sauvegarde**.\n- a\n- b' },
    ]);
  });

  it('alterne texte et code délimité', () => {
    const md =
      'Prévenir le support.\n\n```\ncd /opt/commandes && ./deploy.sh 2.4.0\n```\n\nPuis vérifier `GET /sante`.';
    expect(decouperBlocs(md)).toEqual([
      { type: 'texte', contenu: 'Prévenir le support.' },
      { type: 'code', contenu: 'cd /opt/commandes && ./deploy.sh 2.4.0' },
      { type: 'texte', contenu: 'Puis vérifier `GET /sante`.' },
    ]);
  });

  it('code sur plusieurs lignes, langage conservé, indentation intacte', () => {
    expect(decouperBlocs('```bash\nif true; then\n  echo ok\nfi\n```')).toEqual([
      { type: 'code', contenu: 'if true; then\n  echo ok\nfi', langage: 'bash' },
    ]);
  });

  it('bloc de code non fermé : court jusqu’à la fin', () => {
    expect(decouperBlocs('Texte\n```\nls -la')).toEqual([
      { type: 'texte', contenu: 'Texte' },
      { type: 'code', contenu: 'ls -la' },
    ]);
  });

  it('accepte les fins de ligne Windows et les clôtures plus longues', () => {
    expect(decouperBlocs('````\r\nécho ```\r\n````\r\n')).toEqual([{ type: 'code', contenu: 'écho ```' }]);
  });

  it('un bloc de code vide reste un bloc Code', () => {
    expect(decouperBlocs('```\n```')).toEqual([{ type: 'code', contenu: '' }]);
  });
});

describe('recomposerBlocs', () => {
  it('délimite les blocs de code et sépare par une ligne vide', () => {
    const blocs: Bloc[] = [
      { type: 'texte', contenu: 'Avant' },
      { type: 'code', contenu: 'pg_dump clients > s.sql' },
      { type: 'texte', contenu: 'Après' },
    ];
    expect(recomposerBlocs(blocs)).toBe('Avant\n\n```\npg_dump clients > s.sql\n```\n\nAprès');
  });

  it('ignore les blocs vides', () => {
    expect(
      recomposerBlocs([
        { type: 'texte', contenu: '  ' },
        { type: 'code', contenu: '' },
        { type: 'code', contenu: 'ls' },
      ]),
    ).toBe('```\nls\n```');
    expect(recomposerBlocs([])).toBe('');
  });

  it('allonge la clôture si le code contient ```', () => {
    expect(recomposerBlocs([{ type: 'code', contenu: 'echo ```' }])).toBe('````\necho ```\n````');
  });

  it('aller-retour : découper(recomposer(blocs)) redonne les blocs', () => {
    const blocs: Bloc[] = [
      { type: 'code', contenu: 'sudo systemctl restart commandes' },
      {
        type: 'texte',
        contenu: 'Si le journal indique **port déjà utilisé** :\n- arrêter l’ancienne instance\n- relancer',
      },
      { type: 'code', contenu: 'curl -fsS https://commandes.interne/sante\n  | jq .version', langage: 'sh' },
      { type: 'code', contenu: 'echo ```' },
    ];
    expect(decouperBlocs(recomposerBlocs(blocs))).toEqual(blocs);
  });

  it('aller-retour : recomposer(découper(md)) est stable', () => {
    const md = 'Texte\n\n```\nls\n```\n\n- [ ] case';
    expect(recomposerBlocs(decouperBlocs(md))).toBe(md);
  });
});

describe('premiereLigne', () => {
  it('ignore les délimiteurs de code', () => {
    expect(premiereLigne('```\n\nsystemctl restart api\nautre\n```')).toBe('systemctl restart api');
    expect(premiereLigne('')).toBe('');
  });
});

describe('formaterMarkdown (barre d’outils)', () => {
  it('entoure la sélection', () => {
    expect(formaterMarkdown('faire la sauvegarde', 9, 19, 'gras')).toEqual({
      valeur: 'faire la **sauvegarde**',
      debut: 11,
      fin: 21,
    });
    expect(formaterMarkdown('lancer deploy.sh', 7, 16, 'code')).toEqual({
      valeur: 'lancer `deploy.sh`',
      debut: 8,
      fin: 17,
    });
  });

  it('sans sélection : insère un exemple sélectionné', () => {
    const r = formaterMarkdown('Avant ', 6, 6, 'italique');
    expect(r.valeur).toBe('Avant _texte en italique_');
    expect(r.valeur.slice(r.debut, r.fin)).toBe('texte en italique');
  });

  it('lien : sélectionne l’adresse à compléter', () => {
    const r = formaterMarkdown('voir la doc', 8, 11, 'lien');
    expect(r.valeur).toBe('voir la [doc](https://)');
    expect(r.valeur.slice(r.debut, r.fin)).toBe('https://');
  });

  it('listes : préfixe chaque ligne sélectionnée, puis retire au second clic', () => {
    const r = formaterMarkdown('a\nb\nc', 0, 3, 'liste');
    expect(r.valeur).toBe('- a\n- b\nc');
    expect(formaterMarkdown(r.valeur, r.debut, r.fin, 'liste').valeur).toBe('a\nb\nc');
    expect(formaterMarkdown('x\ncase', 3, 3, 'cases').valeur).toBe('x\n- [ ] case');
  });
});
