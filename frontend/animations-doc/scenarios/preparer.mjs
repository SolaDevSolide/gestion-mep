// Animations de docs/Préparer-une-MEP.md.
import { carte, ouvrirMep, planPleinEcran } from './communs.mjs';

const PAGE = 'Préparer-une-MEP.md';

/** Ouvre une section de l'onglet Préparation par son sommaire (« 6 Sauvegarde »…). */
async function sectionPreparation({ page, s }, numero) {
  await s.cliquer(page.getByRole('button', { name: new RegExp(`^${numero} `) }), { apres: 300 });
  await s.attendreChargement(500);
}

export default [
  {
    nom: 'preparation',
    page: PAGE,
    section: 'L’onglet Préparation (capture)',
    compte: 'alice',
    capture: true,
    async preparer(ctx) {
      await ouvrirMep(ctx, 'Facturation électronique');
    },
  },
  {
    nom: 'nouvelle-mep',
    page: PAGE,
    section: 'Créer la MEP',
    compte: 'alice',
    async preparer({ page }) {
      await page.goto('/');
    },
    async jouer({ page, s }) {
      await s.cliquer(page.getByRole('button', { name: 'Nouvelle MEP', exact: true }));
      const fenetre = page.getByRole('dialog');
      await s.saisir(fenetre.getByRole('textbox', { name: 'Outil ou application' }), 'Portail fournisseurs');
      await s.saisir(
        fenetre.getByRole('textbox', { name: 'Objet de l’intervention' }),
        'Ouverture du dépôt de factures',
      );
      await s.cliquer(fenetre.getByRole('button', { name: 'Créer la MEP' }), { apres: 200 });
      await page.getByRole('heading', { name: /Portail fournisseurs/ }).waitFor();
      await s.attendreChargement(1200);
    },
  },
  {
    nom: 'passer-en-mep-simplifiee',
    page: PAGE,
    section: 'MEP complète ou simplifiée',
    compte: 'alice',
    async preparer(ctx) {
      await ouvrirMep(ctx, 'Portail de démonstration');
    },
    async jouer({ page, s }) {
      await s.cliquer(page.getByRole('button', { name: 'Passer en MEP simplifiée' }));
      const fenetre = page.getByRole('dialog');
      await s.pause(1200);
      await s.cliquer(fenetre.getByRole('checkbox', { name: /^J’ai compris/ }));
      await s.cliquer(fenetre.getByRole('button', { name: 'Supprimer et passer en simplifiée' }), { apres: 1500 });
    },
  },
  {
    nom: 'renseigner-la-sauvegarde',
    page: PAGE,
    section: 'L’onglet Préparation (6. Sauvegarde)',
    compte: 'alice',
    async preparer(ctx) {
      await ouvrirMep(ctx, 'Facturation électronique');
    },
    async jouer(ctx) {
      const { page, s } = ctx;
      await sectionPreparation(ctx, 6);
      const batch = page.getByRole('listitem', { name: 'Sauvegarde de BATCH' });
      await s.cliquer(batch.getByRole('radio', { name: 'Requise', exact: true }));
      await s.cliquer(batch.getByRole('radio', { name: 'Avant la MEP' }));
      await s.saisir(batch.getByRole('textbox', { name: 'Emplacement de la sauvegarde *' }), '/sauvegardes/batch.tar', {
        delai: 35,
      });
      await s.saisir(batch.getByRole('textbox', { name: 'Méthode de récupération *' }), 'Restaurer l’archive.', {
        delai: 35,
      });
      await s.cliquer(batch.getByRole('button', { name: 'Enregistrer' }), { apres: 1000 });
      const web = page.getByRole('listitem', { name: 'Sauvegarde de WEB' });
      await s.saisir(web.getByRole('textbox', { name: 'Justification *' }), 'Service sans état.', { delai: 35 });
      await s.cliquer(web.getByRole('button', { name: 'Enregistrer' }), { apres: 1200 });
      await s.survoler(page.getByRole('button', { name: /^Vérifier le plan/ }));
    },
  },
  {
    nom: 'creer-une-etape',
    page: PAGE,
    section: 'Le plan d’étapes (Plan › Étapes)',
    compte: 'alice',
    async preparer(ctx) {
      await planPleinEcran(ctx, 'Portail de démonstration');
    },
    async jouer({ page, s }) {
      await s.survoler(carte(page, 'E0006'));
      await s.cliquer(page.getByRole('button', { name: 'Ajouter une étape après E0006' }));
      await s.cliquer(page.getByRole('menuitem', { name: /Étape vierge/ }), { apres: 800 });
      const fenetre = page.getByRole('dialog', { name: /^Étape E00/ });
      await s.saisir(fenetre.getByRole('textbox', { name: 'Action' }), 'Vidage des caches du portail', { vider: true });
      await s.cliquer(fenetre.getByRole('button', { name: 'Enregistrer' }), { apres: 900 });
      await s.cliquer(fenetre.getByRole('button', { name: 'Fermer' }), { apres: 1200 });
    },
  },
  {
    nom: 'glisser-une-carte',
    page: PAGE,
    section: 'Le plan d’étapes (Plan › Étapes)',
    compte: 'alice',
    async preparer(ctx) {
      await planPleinEcran(ctx, 'Portail de démonstration');
    },
    async jouer({ page, s }) {
      const lien = page.getByRole('group', { name: 'Lien E0007 vers E0008' });
      await s.glisser(carte(page, 'E0006'), lien, { depuis: { x: 70, y: 40 }, survol: 1400, apres: 2200 });
    },
  },
  {
    nom: 'tracer-et-optimiser-les-liens',
    page: PAGE,
    section: 'Le plan d’étapes (Plan › Étapes)',
    compte: 'alice',
    // Deux rangements animés du graphe : 10 images par seconde suffisent.
    ips: 10,
    async preparer(ctx) {
      await planPleinEcran(ctx, 'Portail de démonstration');
    },
    async jouer({ page, s }) {
      // Poignée du bas de E0004 vers la poignée du haut de E0008 : un lien inutile (E0008 attend déjà E0004 par E0007).
      const source = await carte(page, 'E0004').boundingBox();
      const cible = await carte(page, 'E0008').boundingBox();
      await s.glisser(
        carte(page, 'E0004'),
        { x: cible.x + cible.width / 2, y: cible.y + 1 },
        { depuis: { x: source.width / 2, y: source.height }, survol: 800, apres: 1500 },
      );
      await s.cliquer(page.getByRole('button', { name: 'Optimiser les liens' }), { apres: 2200 });
      await s.cliquer(page.getByRole('button', { name: 'Confirmer' }), { apres: 1800 });
    },
  },
  {
    nom: 'ouvrir-une-section',
    page: PAGE,
    section: 'Les sections',
    compte: 'alice',
    async preparer(ctx) {
      await ouvrirMep(ctx, 'Facturation électronique', '?onglet=plan&plan=etapes');
      await ctx.page.getByRole('application', { name: 'Graphe des étapes' }).waitFor();
      await ctx.page.getByRole('button', { name: 'Masquer la mini-carte' }).click();
      await ctx.page.getByRole('button', { name: 'Replier le bac' }).click();
      await ctx.page.getByRole('button', { name: 'Replier la section Tables factures' }).click();
    },
    async jouer({ page, s }) {
      await s.cliquer(page.getByRole('button', { name: 'Déplier la section Export et téléchargement' }), {
        apres: 1500,
      });
      await s.cliquer(page.getByRole('button', { name: 'Ouvrir la section Export et téléchargement' }), {
        apres: 2000,
      });
      await s.cliquer(page.getByRole('button', { name: 'Retour au plan' }), { apres: 1200 });
    },
  },
  {
    nom: 'placer-une-section',
    page: PAGE,
    section: 'Les sections',
    compte: 'alice',
    async preparer(ctx) {
      await planPleinEcran(ctx, 'Facturation électronique');
      await ctx.page.getByRole('button', { name: /^Afficher le bac/ }).click();
    },
    async jouer({ page, s }) {
      await s.cliquer(page.getByRole('button', { name: 'Placer dans le graphe' }), { apres: 2500 });
    },
  },
  {
    nom: 'editer-une-etape',
    page: PAGE,
    section: 'Le plan d’étapes (espace d’édition d’une étape)',
    compte: 'alice',
    async preparer(ctx) {
      const { page } = ctx;
      await planPleinEcran(ctx, 'Facturation électronique');
      // « Aller à » centre la carte et ouvre l'étape.
      await page.getByRole('combobox', { name: 'Aller à une étape (code)' }).fill('E0005');
      await page.keyboard.press('Enter');
      await page.getByRole('dialog', { name: 'Étape E0005' }).waitFor();
    },
    async jouer({ page, s }) {
      const fenetre = page.getByRole('dialog', { name: 'Étape E0005' });
      await s.saisir(fenetre.getByRole('textbox', { name: 'Action' }), 'Contrôle d’une facture Factur-X', {
        vider: true,
      });
      await s.cliquer(fenetre.getByRole('button', { name: 'Ajouter un bloc code' }));
      await s.saisir(
        fenetre.getByRole('textbox', { name: 'Instructions, bloc 2 (code)' }),
        'curl -s https://factures.exemple/sante',
        {
          delai: 30,
        },
      );
      await s.cliquer(fenetre.getByRole('option', { name: /^BATCH : chaîne nocturne bloquée/ }), { apres: 800 });
      await s.cliquer(fenetre.getByRole('button', { name: 'Enregistrer' }), { apres: 1500 });
    },
  },
  {
    nom: 'choisir-les-objets-impactes',
    page: PAGE,
    section: 'Environnements et objets impactés',
    compte: 'alice',
    async preparer(ctx) {
      await ouvrirMep(ctx, 'Facturation électronique');
    },
    async jouer(ctx) {
      const { page, s } = ctx;
      await sectionPreparation(ctx, 5);
      const bdd = page.getByRole('region', { name: 'Objets impactés de BDD' });
      await s.cliquer(bdd.getByRole('button', { name: /^Tables/ }));
      await s.cliquer(bdd.getByRole('button', { name: 'Choisir les objets…' }));
      const fenetre = page.getByRole('dialog');
      await s.saisir(fenetre.getByRole('textbox', { name: 'Rechercher un objet' }), 'clients');
      await s.cliquer(fenetre.getByRole('checkbox', { name: 'Cocher clients' }));
      await s.saisir(
        fenetre
          .getByRole('textbox', { name: 'Version de clients' })
          .or(fenetre.getByRole('textbox', { name: 'Version ou correction de clients' })),
        'V3.5',
      );
      await s.cliquer(fenetre.getByRole('button', { name: /^Enregistrer \(/ }), { apres: 1500 });
    },
  },
  {
    nom: 'retour-arriere-facultatif',
    page: PAGE,
    section: 'Retour arrière facultatif',
    compte: 'alice',
    async preparer(ctx) {
      await ouvrirMep(ctx, 'Portail de démonstration');
    },
    async jouer({ page, s }) {
      await s.cliquer(page.getByRole('checkbox', { name: /^Plan de retour arrière/ }));
      await s.pause(1000);
      await s.cliquer(page.getByRole('dialog').getByRole('button', { name: 'Désactiver' }));
      await s.cliquer(page.getByRole('button', { name: 'Enregistrer' }), { apres: 1200 });
      await s.cliquer(page.getByRole('tab', { name: 'Plan' }), { apres: 1500 });
    },
  },
  {
    nom: 'fiche-de-recette',
    page: PAGE,
    section: 'Les fiches de recette (Plan › Recette)',
    compte: 'alice',
    async preparer(ctx) {
      await ouvrirMep(ctx, 'Facturation électronique', '?onglet=plan&plan=recette');
    },
    async jouer({ page, s }) {
      await s.cliquer(page.getByRole('button', { name: 'Ajouter une fiche BDD depuis la bibliothèque' }));
      await s.cliquer(
        page
          .getByRole('dialog')
          .getByRole('button', { name: /^Insérer / })
          .first(),
        { apres: 1200 },
      );
      await s.cliquer(page.getByRole('region', { name: 'BDD' }).getByRole('link').first(), { apres: 2000 });
    },
  },
  {
    nom: 'inserer-une-etape-modele',
    page: PAGE,
    section: 'Insérer une étape modèle dans le plan',
    compte: 'alice',
    async preparer(ctx) {
      await planPleinEcran(ctx, 'Portail de démonstration');
    },
    async jouer({ page, s }) {
      await s.cliquer(page.getByRole('button', { name: 'Parcourir la bibliothèque' }));
      const fenetre = page.getByRole('dialog');
      await s.cliquer(fenetre.getByRole('button', { name: 'WEB : 5 étapes' }));
      await s.cliquer(fenetre.getByRole('button', { name: /^Vidage des caches du frontal/ }), { apres: 900 });
      await s.cliquer(fenetre.getByRole('button', { name: 'Insérer une copie' }), { apres: 900 });
      await s.cliquer(page.getByRole('group', { name: 'Lien E0007 vers E0008' }), { apres: 2200 });
    },
  },
  {
    nom: 'prendre-la-main-sur-une-etape',
    page: PAGE,
    section: 'Préparer à plusieurs',
    compte: 'alice',
    async preparer(ctx) {
      const { page } = ctx;
      const mep = await planPleinEcran(ctx, 'Facturation électronique');
      // Bob, dans un autre navigateur (non enregistré), a ouvert E0005 : il en tient le verrou.
      const { page: pageBob } = await ctx.autreCompte('bob');
      await pageBob.goto(`/meps/${mep.id}?onglet=plan&plan=etapes`);
      await pageBob.getByRole('combobox', { name: 'Aller à une étape (code)' }).fill('E0005');
      await pageBob.keyboard.press('Enter');
      await pageBob.getByRole('dialog', { name: 'Étape E0005' }).waitFor();
      await page.waitForTimeout(1500);
    },
    async jouer(ctx) {
      const { page, s } = ctx;
      await s.saisir(page.getByRole('combobox', { name: 'Aller à une étape (code)' }), 'E0005', { delai: 120 });
      await s.touche('Enter', 1800);
      await s.cliquer(page.getByRole('button', { name: 'Prendre la main' }), { apres: 600 });
      const confirmer = page.getByRole('dialog').getByRole('button', { name: 'Prendre la main' });
      if (await confirmer.isVisible().catch(() => false)) await s.cliquer(confirmer, { apres: 1200 });
      await s.pause(1200);
    },
  },
  {
    nom: 'verifier-le-plan',
    page: PAGE,
    section: 'Vérifier le plan',
    compte: 'alice',
    async preparer(ctx) {
      await ouvrirMep(ctx, 'Facturation électronique');
    },
    async jouer({ page, s }) {
      await s.cliquer(page.getByRole('button', { name: /^Vérifier le plan/ }), { apres: 1000 });
      await s.survoler(page.getByRole('region', { name: 'Anomalies' }).getByRole('listitem').nth(2));
      await s.pause(1200);
      await s.survoler(page.getByRole('region', { name: 'Avertissements' }).getByRole('listitem').first());
      await s.pause(1500);
    },
  },
  {
    nom: 'approuver-une-mep',
    page: PAGE,
    section: 'Relecture et approbations',
    compte: 'claire',
    async preparer(ctx) {
      await ouvrirMep(ctx, 'Portail de démonstration');
    },
    async jouer(ctx) {
      const { page, s } = ctx;
      await sectionPreparation(ctx, 7);
      await s.cliquer(
        page.getByRole('list', { name: 'Décisions des approbateurs' }).getByRole('button', { name: 'Approuver' }),
        {
          apres: 600,
        },
      );
      const confirmer = page.getByRole('dialog').getByRole('button', { name: /^Approuver/ });
      if (await confirmer.isVisible().catch(() => false)) await s.cliquer(confirmer, { apres: 1500 });
    },
  },
  {
    nom: 'dupliquer-une-mep',
    page: PAGE,
    section: 'Partir d’une MEP existante',
    compte: 'alice',
    async preparer({ page }) {
      await page.goto('/');
    },
    async jouer({ page, s }) {
      await s.cliquer(page.getByRole('button', { name: 'Dupliquer Portail de démonstration' }));
      const fenetre = page.getByRole('dialog');
      await s.cliquer(fenetre.getByRole('checkbox', { name: /^En faire un modèle/ }));
      await s.cliquer(fenetre.getByRole('button', { name: 'Créer le modèle' }), { apres: 300 });
      await s.attendreChargement(1500);
    },
  },
  {
    nom: 'creer-depuis-un-modele',
    page: PAGE,
    section: 'Modèles',
    compte: 'alice',
    async preparer({ page }) {
      await page.goto('/');
    },
    async jouer({ page, s }) {
      await s.cliquer(page.getByRole('button', { name: 'Nouvelle MEP depuis un modèle' }));
      await s.cliquer(page.getByRole('dialog').getByRole('button', { name: 'Créer la MEP' }), { apres: 300 });
      await s.attendreChargement(1500);
    },
  },
];
