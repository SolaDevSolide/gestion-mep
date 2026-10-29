// Animations de docs/Cycle-de-vie.md.
import { ouvrirMep } from './communs.mjs';

const PAGE = 'Cycle-de-vie.md';

/** Jour J + n, tel qu'on le tape dans un champ date en français (jjmmaaaa). */
function dansJours(n) {
  const d = new Date(Date.now() + n * 86_400_000);
  return [d.getDate(), d.getMonth() + 1].map((x) => String(x).padStart(2, '0')).join('') + d.getFullYear();
}

/** Ouvre une entrée du menu « ⋯ Plus d’actions » de l'en-tête. */
async function plusDActions({ page, s }, entree) {
  await s.cliquer(page.getByRole('button', { name: 'Plus d’actions' }));
  await s.cliquer(page.getByRole('menuitem', { name: new RegExp(`^${entree}`) }));
  return page.getByRole('dialog');
}

export default [
  {
    nom: 'go-no-go',
    page: PAGE,
    section: 'Go / no-go : passage en exécution',
    compte: 'bob',
    async preparer(ctx) {
      await ouvrirMep(ctx, 'Annuaire des agences');
    },
    async jouer({ page, s }) {
      await s.cliquer(page.getByRole('button', { name: 'Go / no-go' }));
      const dialogue = page.getByRole('dialog');
      for (const critere of await dialogue.getByRole('checkbox').all()) await s.cliquer(critere, { apres: 300 });
      await s.cliquer(dialogue.getByRole('button', { name: 'Go : passer en exécution' }), { apres: 1500 });
    },
  },
  {
    nom: 'reporter-une-mep',
    page: PAGE,
    section: 'Reporter une MEP',
    compte: 'alice',
    async preparer(ctx) {
      await ouvrirMep(ctx, 'Portail de démonstration');
    },
    async jouer(ctx) {
      const { s } = ctx;
      const fenetre = await plusDActions(ctx, 'Reporter la MEP');
      await s.saisir(
        fenetre.getByRole('textbox', { name: 'Motif (obligatoire)' }),
        'Préproduction indisponible jusqu’à lundi.',
      );
      await s.saisir(fenetre.getByRole('textbox', { name: 'Date visée (facultative)' }), dansJours(9), { delai: 90 });
      await s.cliquer(fenetre.getByRole('button', { name: 'Reporter la MEP' }), { apres: 1500 });
    },
  },
  {
    nom: 'annuler-une-mep',
    page: PAGE,
    section: 'Annuler une MEP',
    compte: 'alice',
    async preparer(ctx) {
      await ouvrirMep(ctx, 'Portail de démonstration');
    },
    async jouer(ctx) {
      const { s } = ctx;
      const fenetre = await plusDActions(ctx, 'Annuler la MEP');
      await s.saisir(
        fenetre.getByRole('textbox', { name: 'Motif (obligatoire)' }),
        'Projet suspendu par la direction.',
      );
      await s.cliquer(fenetre.getByRole('button', { name: 'Annuler la MEP' }), { apres: 1500 });
    },
  },
  {
    nom: 'supprimer-une-mep',
    page: PAGE,
    section: 'Supprimer une MEP',
    compte: 'alice',
    async preparer(ctx) {
      await ouvrirMep(ctx, 'Portail de démonstration');
    },
    async jouer(ctx) {
      const { page, s } = ctx;
      const fenetre = await plusDActions(ctx, 'Supprimer définitivement');
      await s.saisir(fenetre.getByRole('textbox', { name: 'Motif (obligatoire)' }), 'MEP d’essai, jamais lancée.');
      await s.saisir(fenetre.getByRole('textbox', { name: /^Pour confirmer/ }), 'Portail de démonstration 2.0-démo', {
        delai: 30,
      });
      await s.cliquer(fenetre.getByRole('button', { name: 'Supprimer définitivement' }), { apres: 300 });
      await page.getByRole('heading', { name: 'Mises en production' }).waitFor();
      await s.attendreChargement(800);
    },
  },
];
