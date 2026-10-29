// Animations de docs/Démarrage-rapide.md.
import { carte, ouvrirMep, planPleinEcran } from './communs.mjs';

const PAGE = 'Démarrage-rapide.md';

export default [
  {
    nom: 'connexion-rapide',
    page: PAGE,
    section: 'Comptes de démonstration',
    // Démonstration réinitialisée : personne n'est connecté, l'écran de connexion propose la connexion rapide.
    compte: null,
    bandeau: true,
    async preparer({ page }) {
      await page.goto('/connexion');
      await page.getByRole('button', { name: 'Se connecter en tant que Alice (alice)' }).waitFor();
    },
    async jouer({ page, s }) {
      await s.pause(500);
      await s.cliquer(page.getByRole('button', { name: 'Se connecter en tant que Alice (alice)' }));
      await page.getByRole('heading', { name: 'Mises en production' }).waitFor();
    },
  },
  {
    nom: 'premier-parcours',
    page: PAGE,
    section: 'Premier parcours',
    compte: 'alice',
    async preparer({ page }) {
      await page.goto('/');
      await page.getByRole('heading', { name: 'Mises en production' }).waitFor();
    },
    async jouer({ page, s }) {
      await s.cliquer(page.getByRole('link', { name: /Portail de démonstration/ }).first(), { apres: 200 });
      await s.attendreChargement(1200);
      await s.cliquer(page.getByRole('tab', { name: 'Plan' }), { apres: 200 });
      await s.attendreChargement(1200);
      await s.cliquer(carte(page, 'E0005'), { apres: 2000 });
    },
  },
  {
    nom: 'liste-des-mep',
    page: PAGE,
    section: 'Premier parcours (capture)',
    compte: 'alice',
    capture: true,
    async preparer({ page }) {
      await page.goto('/');
      await page.getByRole('button', { name: /^Système/ }).waitFor();
    },
  },
  {
    nom: 'graphe-des-etapes',
    page: `${PAGE}, Préparer-une-MEP.md`,
    section: 'Premier parcours ; Le plan d’étapes (capture)',
    compte: 'alice',
    capture: true,
    async preparer(ctx) {
      await planPleinEcran(ctx, 'Portail de démonstration');
      await ctx.page.getByRole('button', { name: 'Ajuster à l’écran' }).click();
      await ctx.page.waitForTimeout(800);
    },
  },
  {
    nom: 'simuler-des-collegues',
    page: PAGE,
    section: 'Voir travailler des collègues',
    compte: 'alice',
    bandeau: true,
    async preparer(ctx) {
      const { page } = ctx;
      await ouvrirMep(ctx, 'Facturation électronique', '?onglet=plan&plan=etapes');
      await page.getByRole('application', { name: 'Graphe des étapes' }).waitFor();
      await page.getByRole('button', { name: 'Tout déplier' }).click();
      await page.getByRole('button', { name: 'Masquer la mini-carte' }).click();
      await page.getByRole('button', { name: 'Replier le bac' }).click();
    },
    async jouer({ page, s }) {
      await s.cliquer(page.getByRole('button', { name: 'Simuler des collègues' }));
      await s.cliquer(page.getByRole('button', { name: 'Plan en plein écran' }));
      await s.cliquer(page.getByRole('button', { name: 'Ajuster à l’écran' }), { apres: 300 });
      await s.cliquer(page.getByRole('button', { name: 'Zoomer', exact: true }));
      await s.aller(1150, 760);
      await s.pause(14_000);
    },
  },
  {
    nom: 'reinitialiser-la-demo',
    page: PAGE,
    section: 'Repartir de zéro',
    compte: 'alice',
    bandeau: true,
    async preparer({ page }) {
      await page.goto('/');
      await page
        .getByRole('link', { name: /Portail de démonstration/ })
        .first()
        .waitFor();
    },
    async jouer({ page, s }) {
      await s.cliquer(page.getByRole('button', { name: 'Réinitialiser la démonstration' }));
      await s.cliquer(page.getByRole('dialog').getByRole('button', { name: 'Réinitialiser', exact: true }), {
        apres: 600,
      });
      await page.getByRole('button', { name: 'Se connecter en tant que Alice (alice)' }).waitFor();
    },
  },
];
