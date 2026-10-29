// Animations de docs/Exécuter-une-MEP.md.
import { carte, ouvrirMep, planPleinEcran } from './communs.mjs';

const PAGE = 'Exécuter-une-MEP.md';

/** Masque la mini-carte du graphe affiché (elle couvre un coin du plan). */
async function masquerMiniCarte({ page }) {
  await page.getByRole('application', { name: 'Graphe des étapes' }).waitFor();
  const miniCarte = page.getByRole('button', { name: 'Masquer la mini-carte' });
  if ((await miniCarte.getAttribute('aria-pressed')) === 'true') await miniCarte.click();
}

/** Onglet Exécution d'une MEP de démonstration, mini-carte masquée. */
async function execution(ctx, outil) {
  const mep = await ouvrirMep(ctx, outil, '?onglet=execution');
  await masquerMiniCarte(ctx);
  return mep;
}

/** Go d'Annuaire des agences (pilote : Bob), tous critères cochés : E0001 devient disponible. */
async function goAnnuaire({ api }) {
  const mep = await api.mep('Annuaire des agences');
  const criteres = await api.get('/criteres-go-no-go');
  await api.post(`/meps/${mep.id}/phase`, { phase: 'Execution', criteresCoches: criteres.map((c) => c.id) });
}

/** Ouvre une étape par « Aller à » (code puis Entrée), comme on le fait dans un grand plan. */
async function allerA({ page, s }, code) {
  await s.saisir(page.getByRole('combobox', { name: 'Aller à une étape (code)' }), code, { delai: 80, apres: 250 });
  await s.touche('Enter', 900);
  return page.getByRole('dialog', { name: `Étape ${code}` });
}

/** Ouvre une entrée du menu « ⋯ Plus d’actions » de l'en-tête. */
async function plusDActions({ page, s }, entree) {
  await s.cliquer(page.getByRole('button', { name: 'Plus d’actions' }));
  await s.cliquer(page.getByRole('menuitem', { name: new RegExp(`^${entree}`) }));
  return page.getByRole('dialog');
}

export default [
  {
    nom: 'execution',
    page: PAGE,
    section: 'L’onglet Exécution (capture)',
    compte: 'alice',
    capture: true,
    async preparer(ctx) {
      const { page } = ctx;
      await planPleinEcran(ctx, 'Paiement mobile', null, '?onglet=execution');
      for (const section of ['Écran de paiement', 'Service paiement', 'Base de données']) {
        await page.getByRole('button', { name: `Déplier la section ${section}` }).click();
        await page.waitForTimeout(500);
      }
      await page.getByRole('button', { name: 'Ajuster à l’écran' }).click();
      await page.waitForTimeout(800);
    },
  },
  {
    nom: 'derouler-une-etape',
    page: PAGE,
    section: 'Dérouler une étape',
    compte: 'bob',
    async preparer(ctx) {
      await goAnnuaire(ctx);
      await execution(ctx, 'Annuaire des agences');
    },
    async jouer({ page, s }) {
      await s.cliquer(carte(page, 'E0001'), { apres: 900 });
      const etape = page.getByRole('dialog', { name: 'Étape E0001' });
      await s.cliquer(etape.getByRole('button', { name: 'Démarrer' }), { apres: 1200 });
      await s.saisir(
        etape.getByRole('textbox', { name: 'Preuve : ce que vous avez constaté' }),
        'annuaire-1.8.tgz créée (48 Mo).',
      );
      await s.cliquer(etape.getByRole('button', { name: 'Réussie' }));
      const confirmation = page.getByRole('dialog', { name: 'E0001 réussie ?' });
      await s.saisir(confirmation.getByRole('textbox', { name: 'Résultat constaté *' }), 'Archive de 48 Mo.');
      await s.cliquer(confirmation.getByRole('button', { name: 'J’ai vérifié et obtenu le résultat attendu' }), {
        apres: 1200,
      });
      // Le panneau se referme : E0002 et E0003 deviennent disponibles.
      await s.survoler(carte(page, 'E0002'));
    },
  },
  {
    nom: 'prendre-la-main',
    page: PAGE,
    section: 'Prendre la main',
    compte: 'alice',
    async preparer(ctx) {
      await execution(ctx, 'Paiement mobile');
    },
    async jouer(ctx) {
      const { page, s } = ctx;
      const etape = await allerA(ctx, 'E0004');
      await s.cliquer(etape.getByRole('button', { name: 'Prendre la main' }));
      const fenetre = page.getByRole('dialog', { name: 'Prendre la main sur E0004 ?' });
      await s.saisir(fenetre.getByRole('textbox', { name: /^Pourquoi prenez-vous la main/ }), 'Bob est injoignable.');
      await s.cliquer(fenetre.getByRole('button', { name: 'Prendre la main' }), { apres: 1500 });
    },
  },
  {
    nom: 'resolution-guidee',
    page: PAGE,
    section: 'Quand une étape échoue',
    compte: 'claire',
    async preparer(ctx) {
      await execution(ctx, 'Formulaire de contact');
    },
    async jouer(ctx) {
      const { page, s } = ctx;
      const etape = await allerA(ctx, 'E0002');
      const resolution = etape.getByRole('listitem').filter({ hasText: 'connexion refusée après bascule' });
      await s.cliquer(resolution.getByRole('button', { name: 'Appliquer cette résolution' }), { apres: 900 });
      const confirmer = page.getByRole('alertdialog').getByRole('button', { name: /^(Appliquer|Confirmer)/ });
      if (await confirmer.isVisible()) await s.cliquer(confirmer, { apres: 900 });
      const constats = ['Hôte et utilisateur remis.', 'Service redémarré.', 'Connexion à la base rétablie.'];
      for (const constat of constats) {
        const courante = etape.getByRole('group', { name: /à faire maintenant/ });
        await s.saisir(courante.getByRole('textbox'), constat, { delai: 35 });
        await s.cliquer(courante.getByRole('button', { name: 'Conforme' }), { apres: 1000 });
      }
    },
  },
  {
    nom: 'autoriser-le-passage',
    page: PAGE,
    section: 'Point de non-retour',
    compte: 'alice',
    async preparer(ctx) {
      await execution(ctx, 'Paiement mobile');
    },
    async jouer(ctx) {
      const { page, s } = ctx;
      const etape = await allerA(ctx, 'E0006');
      await s.cliquer(etape.getByRole('button', { name: 'Autoriser le passage' }), { apres: 900 });
      const confirmer = page.getByRole('alertdialog').getByRole('button', { name: /^(Autoriser|Confirmer)/ });
      if (await confirmer.isVisible()) await s.cliquer(confirmer, { apres: 1200 });
    },
  },
  {
    nom: 'revenir-en-preparation',
    page: PAGE,
    section: 'Revenir en préparation',
    compte: 'claire',
    async preparer(ctx) {
      await execution(ctx, 'Formulaire de contact');
    },
    async jouer({ page, s }) {
      await s.cliquer(page.getByRole('button', { name: 'Revenir en préparation' }));
      const fenetre = page.getByRole('dialog');
      await s.saisir(fenetre.getByRole('textbox', { name: /^Motif/ }), 'Corriger le redémarrage du service messages.');
      await s.cliquer(fenetre.getByRole('button', { name: 'Revenir en préparation' }), { apres: 300 });
      await s.attendreChargement(1200);
    },
  },
  {
    nom: 'ajouter-une-etape-corrective',
    page: PAGE,
    section: 'Ajouter une étape corrective',
    compte: 'bob',
    async preparer(ctx) {
      await goAnnuaire(ctx);
      await ouvrirMep(ctx, 'Annuaire des agences', '?onglet=plan&plan=etapes');
      await masquerMiniCarte(ctx);
    },
    async jouer(ctx) {
      const { page, s } = ctx;
      await s.cliquer(page.getByRole('button', { name: /Ajouter une étape corrective/ }));
      const fenetre = page.getByRole('dialog', { name: /^Étape corrective/ });
      await s.saisir(
        fenetre.getByRole('textbox', { name: 'Action (obligatoire)' }),
        'Purge du cache de la page agences',
      );
      await s.cliquer(fenetre.getByRole('checkbox', { name: 'WEB' }));
      await s.saisir(
        fenetre.getByRole('textbox', { name: 'Instructions (obligatoire)' }),
        'Vider le cache du frontal.',
        {
          delai: 30,
        },
      );
      await s.saisir(
        fenetre.getByRole('textbox', { name: 'Résultat attendu (obligatoire)' }),
        'Les nouvelles agences s’affichent.',
        {
          delai: 30,
        },
      );
      await s.saisir(fenetre.getByRole('spinbutton', { name: 'Durée estimée (minutes)' }), '10', { vider: true });
      await s.cliquer(fenetre.getByRole('button', { name: 'Ajouter l’étape' }), { apres: 1200 });
      // La nouvelle étape, en bas du plan, s'ouvre par « Aller à » : mention « Ajoutée après le Go », fiche figée.
      await allerA(ctx, 'E0006');
    },
  },
  {
    nom: 'derouler-la-recette',
    page: PAGE,
    section: 'Recette et clôture',
    compte: 'alice',
    async preparer(ctx) {
      await ouvrirMep(ctx, 'Portail RH', '?onglet=plan&plan=recette');
    },
    async jouer({ page, s }) {
      await s.cliquer(page.getByRole('region', { name: 'BDD' }).getByRole('link').first(), { apres: 900 });
      await s.saisir(
        page.getByRole('textbox', { name: 'Observation de l’étape 1 (obligatoire)' }),
        '1 250 soldes, un par salarié.',
      );
      await s.cliquer(page.getByRole('button', { name: 'Conforme', exact: true }), { apres: 1200 });
      await s.cliquer(page.getByRole('navigation', { name: 'Fil d’Ariane' }).getByRole('link', { name: 'Recette' }), {
        apres: 900,
      });
    },
  },
  {
    nom: 'declencher-le-retour-arriere',
    page: PAGE,
    section: 'Retour arrière',
    compte: 'alice',
    async preparer(ctx) {
      await execution(ctx, 'Paiement mobile');
    },
    async jouer({ page, s }) {
      await s.cliquer(page.getByRole('button', { name: 'Déclencher le retour arrière' }));
      const fenetre = page.getByRole('dialog');
      await s.saisir(
        fenetre.getByRole('textbox', { name: 'Motif (obligatoire)' }),
        'Écran de paiement en échec, correction impossible ce soir.',
        {
          delai: 30,
        },
      );
      await s.cliquer(fenetre.getByRole('button', { name: 'Déclencher le retour arrière' }), { apres: 2000 });
    },
  },
  {
    nom: 'passer-le-pilotage',
    page: PAGE,
    section: 'Changer de pilote pendant la MEP',
    compte: 'alice',
    async preparer(ctx) {
      await execution(ctx, 'Paiement mobile');
    },
    async jouer(ctx) {
      const { s } = ctx;
      const fenetre = await plusDActions(ctx, 'Passer le pilotage');
      const choix = fenetre.getByRole('combobox', { name: 'Nouveau pilote' });
      await s.survoler(choix);
      await choix.selectOption({ label: 'Bob · Développeur API' });
      await s.pause(600);
      await s.saisir(fenetre.getByRole('textbox', { name: 'Motif (obligatoire)' }), 'Relève de nuit.');
      await s.cliquer(fenetre.getByRole('button', { name: 'Passer le pilotage' }), { apres: 1500 });
    },
  },
  {
    nom: 'journal',
    page: PAGE,
    section: 'Le journal',
    compte: 'alice',
    async preparer(ctx) {
      await ouvrirMep(ctx, 'Paiement mobile', '?onglet=journal');
    },
    async jouer({ page, s }) {
      const recherche = page.getByRole('textbox', { name: 'Rechercher dans le journal' });
      await s.saisir(recherche, 'E0004', { delai: 90, apres: 1500 });
      await recherche.fill('');
      await s.pause(300);
      const filtre = page.getByRole('combobox', { name: 'Filtrer par type d’événement' });
      await s.survoler(filtre);
      await filtre.selectOption({ label: 'Résolutions' });
      await s.pause(1500);
      await s.cliquer(page.getByRole('region', { name: 'Tests unitaires' }).getByText('Tests unitaires'), {
        apres: 1500,
      });
    },
  },
];
