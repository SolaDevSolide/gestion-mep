// Génère les animations (docs/images/*.webp) et les captures (docs/images/*.png) de la documentation, en pilotant
// l'application de démonstration.
//
//   npm run docs:animations                         tout
//   npm run docs:animations -- go-no-go journal     seulement celles-ci (noms de fichier sans extension)
//   npm run docs:animations -- --liste              la liste, avec la page et la section de chacune
//   npm run docs:animations -- --garder go-no-go    garder les images brutes (test-results/animations-doc)
//
// L'application doit tourner en mode démonstration (DEMO=true) à l'adresse ANIMATIONS_URL (par défaut le serveur de
// développement Vite, http://localhost:5173). Chaque scénario part d'une démonstration réinitialisée : la base est
// remise à zéro, ne jamais viser une installation réelle. Chromium vient de Playwright ; ffmpeg doit être installé.
import { chromium } from '@playwright/test';
import { mkdir, rm, stat } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { ENTETES, Enregistreur, Scene, scriptCurseur, scriptSansTransitions, versWebp } from './outils.mjs';
import { SCENARIOS } from './scenarios/index.mjs';

const ICI = path.dirname(fileURLToPath(import.meta.url));
const SORTIE = path.resolve(ICI, '../../docs/images');
const BROUILLON = path.resolve(ICI, '../test-results/animations-doc');
const URL_APP = process.env.ANIMATIONS_URL ?? 'http://localhost:5173';

/** Fenêtre : lisible sans zoom une fois affichée dans le wiki, et assez haute pour éviter la vue compacte (800 px). */
const FENETRE = { width: 1200, height: 800 };

const args = process.argv.slice(2);
if (args.includes('--liste')) {
  for (const s of SCENARIOS)
    console.log(`${(s.nom + (s.capture ? '.png' : '.webp')).padEnd(36)} ${s.page} › ${s.section}`);
  process.exit(0);
}
const demandes = args.filter((a) => !a.startsWith('--'));
const inconnus = demandes.filter((n) => !SCENARIOS.some((s) => s.nom === n));
if (inconnus.length > 0) {
  console.error(`Inconnu : ${inconnus.join(', ')} (voir --liste).`);
  process.exit(2);
}
const garder = args.includes('--garder');
const choisis = demandes.length > 0 ? SCENARIOS.filter((s) => demandes.includes(s.nom)) : SCENARIOS;

/** Appels de l'API avec la session de la page (mêmes cookies que le navigateur). */
function clientApi(page) {
  const appel = async (methode, chemin, data) => {
    const r = await page.request.fetch(`/api${chemin}`, { method: methode, data, headers: ENTETES });
    if (!r.ok()) throw new Error(`${methode} /api${chemin} : ${r.status()} ${await r.text()}`);
    const texte = await r.text();
    return texte ? JSON.parse(texte) : null;
  };
  const api = {
    get: (chemin) => appel('GET', chemin),
    post: (chemin, data) => appel('POST', chemin, data),
    put: (chemin, data) => appel('PUT', chemin, data),
    connecter: (login) => appel('POST', '/auth/connexion', { login, motDePasse: 'demo' }),
    async reinitialiser() {
      await api.connecter('alice');
      await api.post('/demo/reinitialiser');
    },
    /** Une MEP de démonstration, complète, par son outil. */
    async mep(outil) {
      const meps = await api.get('/meps');
      const mep = meps.find((m) => m.outil === outil);
      if (!mep) throw new Error(`MEP de démonstration introuvable : ${outil}`);
      return api.get(`/meps/${mep.id}`);
    },
  };
  return api;
}

async function generer(navigateur, scenario) {
  const fenetre = scenario.fenetre ?? FENETRE;
  const contexte = await navigateur.newContext({
    baseURL: URL_APP,
    viewport: fenetre,
    deviceScaleFactor: 1,
    locale: 'fr-FR',
    timezoneId: 'Europe/Paris',
    colorScheme: scenario.theme === 'sombre' ? 'dark' : 'light',
    permissions: ['clipboard-read', 'clipboard-write'],
  });
  await contexte.addInitScript(scriptCurseur);
  if (!scenario.transitions) await contexte.addInitScript(scriptSansTransitions);
  const page = await contexte.newPage();
  page.setDefaultTimeout(20_000);
  const api = clientApi(page);
  // Départ identique à chaque fois : démonstration réinitialisée, puis session du compte du scénario.
  await api.reinitialiser();
  if (scenario.compte) await api.connecter(scenario.compte);
  // Bandeau « Mode démonstration » masqué, sauf pour les scénarios qui le montrent.
  if (!scenario.bandeau) await page.route(/\/api\/demo$/, (r) => r.fulfill({ status: 404, body: '' }));

  const s = new Scene(page);
  // Autres personnes (Bob qui tient un verrou, qui termine une étape pendant qu'on regarde…), dans des navigateurs à
  // part, non enregistrés.
  const autres = [];
  async function autreCompte(login) {
    const autre = await navigateur.newContext({ baseURL: URL_APP, viewport: fenetre, locale: 'fr-FR' });
    autres.push(autre);
    const pageAutre = await autre.newPage();
    const apiAutre = clientApi(pageAutre);
    await apiAutre.connecter(login);
    return { page: pageAutre, api: apiAutre };
  }
  const ctx = { page, api, s, fenetre, autreCompte };
  const brouillon = path.join(BROUILLON, scenario.nom);
  const fichier = path.join(SORTIE, `${scenario.nom}.${scenario.capture ? 'png' : 'webp'}`);
  let resultat;
  try {
    await scenario.preparer?.(ctx);
    // Pas d'attente « réseau au repos » : le flux temps réel reste ouvert. On attend que les squelettes aient disparu.
    await s.attendreChargement(600);
    if (scenario.capture) {
      await page.addStyleTag({ content: '#curseur-doc { display: none !important; }' });
      await scenario.jouer?.(ctx);
      await s.pause(400);
      await page.screenshot({ path: fichier, clip: scenario.cadre });
    } else {
      // Le curseur part d'un endroit neutre (ou de celui du scénario), pas du dernier clic de la préparation.
      const depart = scenario.depart ?? { x: Math.round(fenetre.width * 0.62), y: 300 };
      await page.mouse.move(depart.x, depart.y);
      s.x = depart.x;
      s.y = depart.y;
      const enregistreur = new Enregistreur(page, brouillon);
      await enregistreur.demarrer();
      await scenario.jouer(ctx);
      await s.pause(scenario.finale ?? 1800);
      resultat = await enregistreur.arreter();
    }
  } catch (e) {
    await page.screenshot({ path: `${brouillon}-echec.png` }).catch(() => {});
    throw e;
  } finally {
    for (const autre of autres) await autre.close();
    await contexte.close();
  }
  let resume = '';
  if (resultat) {
    await versWebp(resultat.liste, fichier, { ips: scenario.ips, qualite: scenario.qualite, cadre: scenario.cadre });
    if (!garder) await rm(brouillon, { recursive: true, force: true });
    resume = `${resultat.duree.toFixed(1).padStart(5)} s  ${String(resultat.images).padStart(4)} images`;
  }
  const taille = (await stat(fichier)).size;
  console.log(`${path.basename(fichier).padEnd(36)} ${resume.padEnd(20)} ${(taille / 1024).toFixed(0).padStart(4)} Ko`);
}

await mkdir(BROUILLON, { recursive: true });
// Langue du système en français : les champs de date s'affichent jour/mois/année, comme chez les utilisateurs.
const LANGUE = 'fr_FR.UTF-8';
const navigateur = await chromium.launch({
  args: ['--lang=fr-FR'],
  env: { ...process.env, LANG: LANGUE, LANGUAGE: 'fr_FR:fr', LC_ALL: LANGUE },
});
let echecs = 0;
for (const scenario of choisis) {
  try {
    await generer(navigateur, scenario);
  } catch (e) {
    echecs++;
    console.error(`${scenario.nom} : échec.\n${e.stack ?? e}`);
  }
}
await navigateur.close();
if (!garder && echecs === 0) await rm(BROUILLON, { recursive: true, force: true });
if (echecs > 0) {
  console.error(`${echecs} scénario(s) en échec (capture de l'écran au moment de l'échec : ${BROUILLON}).`);
  process.exit(1);
}
