// Animations de docs/Administrer.md.
const PAGE = 'Administrer.md';

/** Console d'administration, sur l'onglet voulu. */
async function ouvrirConsole({ page }, onglet) {
  await page.goto('/admin');
  const tab = page.getByRole('tab', { name: onglet });
  await tab.click();
  await page.getByRole('tabpanel', { name: onglet }).waitFor();
}

export default [
  {
    nom: 'creer-un-compte',
    page: PAGE,
    section: 'Console d’administration › Comptes',
    compte: 'alice',
    async preparer(ctx) {
      await ouvrirConsole(ctx, 'Comptes');
    },
    async jouer({ page, s }) {
      await s.cliquer(page.getByRole('button', { name: 'Nouveau compte' }));
      const fenetre = page.getByRole('dialog', { name: 'Nouveau compte' });
      await s.saisir(fenetre.getByRole('textbox', { name: 'Identifiant *' }), 'david');
      await s.saisir(fenetre.getByRole('textbox', { name: 'Nom affiché *' }), 'David');
      await s.saisir(fenetre.getByRole('textbox', { name: 'Rôle' }), 'Développeur batch');
      await s.cliquer(fenetre.getByRole('button', { name: 'Créer le compte' }), { apres: 1200 });
      const cree = page.getByRole('dialog', { name: 'Compte créé' });
      await s.cliquer(cree.getByRole('button', { name: 'Copier : Mot de passe provisoire' }), { apres: 1000 });
      await s.cliquer(cree.getByRole('button', { name: 'J’ai transmis le mot de passe' }), { apres: 1200 });
    },
  },
  {
    nom: 'creer-une-equipe',
    page: PAGE,
    section: 'Console d’administration › Équipes',
    compte: 'alice',
    async preparer(ctx) {
      await ouvrirConsole(ctx, 'Équipes');
    },
    async jouer({ page, s }) {
      await s.cliquer(page.getByRole('button', { name: 'Nouvelle équipe' }));
      const fenetre = page.getByRole('dialog', { name: 'Nouvelle équipe' });
      await s.saisir(fenetre.getByRole('textbox', { name: 'Nom *' }), 'Paiements');
      await s.saisir(fenetre.getByRole('textbox', { name: 'Description' }), 'Moyens de paiement et portefeuilles.', {
        delai: 30,
      });
      await s.cliquer(fenetre.getByRole('button', { name: 'Membres' }));
      const suggestions = page.getByRole('listbox', { name: 'Suggestions' });
      await s.cliquer(suggestions.getByRole('option', { name: /^Alice/ }));
      await s.cliquer(suggestions.getByRole('option', { name: /^Bob/ }));
      await s.touche('Escape', 600);
      await s.cliquer(fenetre.getByRole('button', { name: 'Créer l’équipe' }), { apres: 1500 });
    },
  },
  {
    nom: 'charte-visuelle',
    page: PAGE,
    section: 'Console d’administration › Charte visuelle',
    compte: 'alice',
    async preparer(ctx) {
      await ouvrirConsole(ctx, 'Charte visuelle');
    },
    async jouer({ page, s }) {
      await s.cliquer(page.getByRole('button', { name: 'Choisir la couleur primaire' }), { apres: 900 });
      const selecteur = page.getByRole('dialog', { name: 'Primaire · thème clair' });
      await s.cliquer(selecteur.getByRole('button', { name: '#0F766E' }), { apres: 1200 });
      await s.touche('Escape', 600);
      // Aperçu : rien n'est enregistré, l'application ne change pour personne avant « Enregistrer ».
      await s.cliquer(page.getByRole('radio', { name: 'Thème sombre' }), { apres: 1800 });
      await s.cliquer(page.getByRole('radio', { name: 'Excel' }), { apres: 1800 });
    },
  },
];
