// Animations de docs/Modèles-de-texte.md.
import { ouvrirMep } from './communs.mjs';

const PAGE = 'Modèles-de-texte.md';

export default [
  {
    nom: 'copier-un-texte',
    page: PAGE,
    section: 'Copier un texte',
    compte: 'alice',
    async preparer(ctx) {
      await ouvrirMep(ctx, 'Paiement mobile', '?onglet=ensemble');
    },
    async jouer({ page, s }) {
      await s.cliquer(page.getByRole('button', { name: 'Textes à copier' }), { apres: 900 });
      const fenetre = page.getByRole('dialog', { name: 'Textes à copier' });
      await s.cliquer(fenetre.getByRole('button', { name: 'Rappel le jour J' }), { apres: 1200 });
      await s.cliquer(fenetre.getByRole('radio', { name: 'Texte simple' }), { apres: 1200 });
      await s.cliquer(fenetre.getByRole('button', { name: 'Copier l’objet' }), { apres: 900 });
      await s.cliquer(fenetre.getByRole('button', { name: /^Copier le texte/ }), { apres: 1200 });
    },
  },
  {
    nom: 'ecrire-un-modele',
    page: PAGE,
    section: 'Écrire un modèle',
    compte: 'alice',
    async preparer({ page }) {
      await page.goto('/parametres/textes');
      await page.getByRole('button', { name: 'Nouveau modèle' }).waitFor();
    },
    async jouer({ page, s }) {
      await s.cliquer(page.getByRole('button', { name: 'Nouveau modèle' }), { apres: 300 });
      await s.attendreChargement(600);
      await s.saisir(page.getByRole('textbox', { name: 'Nom du modèle *' }), 'Point d’avancement');
      await s.saisir(page.getByRole('textbox', { name: 'Objet du courriel' }), 'Avancement de ');
      const variables = page.getByRole('region', { name: 'Insérer une variable' });
      await s.cliquer(variables.getByRole('button', { name: 'Application', exact: true }));
      await s.saisir(page.getByRole('textbox', { name: 'Texte' }), '**Où en est-on :** ');
      await s.saisir(variables.getByRole('textbox', { name: 'Rechercher une variable' }), 'fin estimée');
      await s.cliquer(variables.getByRole('button', { name: 'Fin estimée' }), { apres: 600 });
      const apercu = page.getByRole('combobox', { name: 'Avec les informations de la MEP' });
      await s.survoler(apercu);
      const options = await apercu.locator('option').allTextContents();
      await apercu.selectOption({ label: options.find((o) => o.startsWith('Paiement mobile')) });
      await s.pause(1500);
    },
  },
];
