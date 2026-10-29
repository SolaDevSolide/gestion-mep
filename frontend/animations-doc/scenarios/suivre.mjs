// Animations de docs/Suivre-une-MEP.md.
import { ouvrirMep } from './communs.mjs';

const PAGE = 'Suivre-une-MEP.md';

/**
 * Bob, dans un autre navigateur, termine E0004 de Paiement mobile (il l'exécute) : la mise à jour arrive en direct dans
 * la page enregistrée.
 */
async function bobTermineE0004(ctx) {
  const { api } = await ctx.autreCompte('bob');
  const mep = await api.mep('Paiement mobile');
  const etape = mep.etapes.find((e) => e.code === 'E0004');
  await api.post(`/meps/${mep.id}/etapes/${etape.id}/terminer`, {
    revision: etape.revision,
    resultat: 'Reussie',
    preuve: 'Service paiement 4.2 démarré.',
    resultatVerifie: true,
    resultatConstate: 'GET /sante : 200, version 4.2.',
  });
}

export default [
  {
    nom: 'vue-d-ensemble',
    page: PAGE,
    section: 'Vue d’ensemble d’une MEP',
    compte: 'claire',
    async preparer(ctx) {
      await ouvrirMep(ctx, 'Paiement mobile', '?onglet=ensemble');
      await ctx.page.getByRole('application', { name: 'Graphe des étapes' }).waitFor();
    },
    async jouer(ctx) {
      const { page, s } = ctx;
      await s.cliquer(page.getByRole('button', { name: 'Plan en plein écran' }), { apres: 900 });
      await s.cliquer(page.getByRole('button', { name: 'Déplier la section Service paiement' }), { apres: 1200 });
      // Pendant qu'on regarde, Bob termine E0004 : la carte, les chiffres et le panneau suivent sans recharger.
      await bobTermineE0004(ctx);
      await s.pause(3000);
      await s.touche('Escape', 900);
    },
  },
  {
    nom: 'suivi-en-direct',
    page: PAGE,
    section: 'Suivi en direct',
    compte: 'claire',
    async preparer({ page }) {
      await page.goto('/suivi');
      await page.getByRole('list', { name: 'MEP en cours' }).waitFor();
    },
    async jouer(ctx) {
      const { page, s } = ctx;
      const carte = page.getByRole('listitem', { name: 'Paiement mobile → 4.2' });
      await s.survoler(carte.getByRole('heading', { level: 2 }));
      await s.pause(600);
      await bobTermineE0004(ctx);
      await s.pause(2500);
      await s.cliquer(carte.getByRole('link', { name: 'Vue d’ensemble' }), { apres: 300 });
      await s.attendreChargement(1200);
    },
  },
  {
    nom: 'rapport-de-fin',
    page: PAGE,
    section: 'Rapport de fin',
    compte: 'alice',
    async preparer(ctx) {
      await ouvrirMep(ctx, 'Gestion des stocks');
    },
    async jouer({ page, s }) {
      await s.cliquer(page.getByRole('link', { name: /Rapport de fin/ }), { apres: 300 });
      await s.attendreChargement(1500);
      // Saut de partie en partie (un défilement continu alourdirait l'animation).
      for (const partie of ['2. Points bloquants', '4. Tests unitaires', '5. Qui a fait chaque étape']) {
        await s.survoler(page.getByRole('heading', { name: partie }));
        await s.pause(1600);
      }
      await s.survoler(page.getByRole('button', { name: 'Exporter en Excel' }));
    },
  },
  {
    nom: 'rechercher-des-mep',
    page: PAGE,
    section: 'Rechercher des MEP',
    compte: 'alice',
    async preparer({ page }) {
      await page.goto('/');
      await page.getByRole('button', { name: /^Système/ }).waitFor();
    },
    async jouer({ page, s }) {
      await s.cliquer(page.getByRole('button', { name: /^Système/ }));
      const fenetre = page.getByRole('dialog');
      await s.saisir(fenetre.getByRole('combobox', { name: 'Systèmes' }), 'BDD', { delai: 110 });
      await s.cliquer(fenetre.getByRole('option', { name: /^BDD / }), { apres: 900 });
      await s.cliquer(fenetre.getByRole('checkbox', { name: /^Avec un échec sur ces systèmes/ }), { apres: 900 });
      await s.touche('Escape', 1500);
    },
  },
  {
    nom: 'palette-de-commandes',
    page: PAGE,
    section: 'Rechercher des MEP (palette de commandes)',
    compte: 'alice',
    async preparer({ page }) {
      await page.goto('/');
      await page.getByRole('button', { name: /^Système/ }).waitFor();
    },
    async jouer({ page, s }) {
      await s.survoler(page.getByRole('button', { name: 'Ouvrir la palette de commandes' }));
      await s.touche('Control+k', 600);
      await page.keyboard.type('claire', { delay: 110 });
      await s.pause(1200);
      await s.cliquer(page.getByRole('option', { name: 'Claire Ses MEP' }), { apres: 300 });
      await s.attendreChargement(1500);
    },
  },
  {
    nom: 'historique-d-un-systeme',
    page: PAGE,
    section: 'Historique d’un système',
    compte: 'alice',
    async preparer(ctx) {
      await ouvrirMep(ctx, 'Paiement mobile', '?onglet=ensemble');
    },
    async jouer({ page, s }) {
      await s.cliquer(page.getByRole('button', { name: 'BDD : historique et MEP de ce système' }).first());
      await s.cliquer(page.getByRole('menuitem', { name: /Historique des MEP/ }), { apres: 300 });
      await s.attendreChargement(1500);
      await s.survoler(page.getByRole('list', { name: 'MEP des douze derniers mois' }));
      await s.pause(900);
      await s.cliquer(page.getByRole('checkbox', { name: 'Avec un échec sur BDD' }), { apres: 1500 });
    },
  },
];
