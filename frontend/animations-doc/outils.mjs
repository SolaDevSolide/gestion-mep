// Outils des animations de la documentation : curseur visible, gestes au rythme d'un humain, enregistrement image par
// image (screencast de Chromium) et conversion en WebP animé par ffmpeg. Voir generer.mjs.
import { spawn } from 'node:child_process';
import { mkdir, rm, writeFile } from 'node:fs/promises';
import path from 'node:path';

export const ENTETES = { 'X-Requested-With': 'fetch' };

/**
 * Script injecté dans chaque page : un curseur dessiné (Chromium sans affichage n'en montre pas), un halo à chaque clic
 * et un anneau tant que le bouton est enfoncé (glisser-déposer). Il suit les vrais événements de la souris.
 */
export function scriptCurseur() {
  if (window.curseurDocInjecte) return;
  window.curseurDocInjecte = true;
  const STYLE = `
    #curseur-doc { position: fixed; left: 0; top: 0; z-index: 2147483647; pointer-events: none; will-change: transform; }
    #curseur-doc svg { display: block; filter: drop-shadow(0 1px 1.5px rgb(0 0 0 / 0.35)); }
    #curseur-doc .anneau { position: absolute; left: -13px; top: -13px; width: 26px; height: 26px; border-radius: 50%;
      border: 2.5px solid rgb(79 70 229 / 0.85); background: rgb(79 70 229 / 0.12); opacity: 0; transition: opacity 120ms; }
    #curseur-doc.enfonce .anneau { opacity: 1; }
    .halo-doc { position: fixed; z-index: 2147483646; pointer-events: none; width: 44px; height: 44px; margin: -22px 0 0 -22px;
      border-radius: 50%; background: rgb(79 70 229 / 0.30); border: 2px solid rgb(79 70 229 / 0.75);
      animation: halo-doc 480ms ease-out forwards; }
    @keyframes halo-doc { from { transform: scale(0.3); opacity: 1; } to { transform: scale(1.25); opacity: 0; } }`;
  const installer = () => {
    if (document.getElementById('curseur-doc')) return;
    const style = document.createElement('style');
    style.textContent = STYLE;
    document.documentElement.appendChild(style);
    const curseur = document.createElement('div');
    curseur.id = 'curseur-doc';
    curseur.innerHTML =
      '<div class="anneau"></div><svg width="22" height="26" viewBox="0 0 22 26" aria-hidden="true">' +
      '<path d="M2.5 2.5 L2.5 20.5 L7 16.3 L10.2 23.6 L13.6 22.1 L10.5 15 L16.6 15 Z" fill="#111827" stroke="#fff" ' +
      'stroke-width="1.6" stroke-linejoin="round"/></svg>';
    const [x, y] = (sessionStorage.getItem('curseur-doc') ?? '-100,-100').split(',').map(Number);
    curseur.style.transform = `translate(${x}px, ${y}px)`;
    document.documentElement.appendChild(curseur);
  };
  const placer = (e) => {
    const curseur = document.getElementById('curseur-doc');
    if (!curseur) return;
    curseur.style.transform = `translate(${e.clientX}px, ${e.clientY}px)`;
    try {
      sessionStorage.setItem('curseur-doc', `${e.clientX},${e.clientY}`);
    } catch {
      // Stockage indisponible : le curseur repart du coin à la prochaine page.
    }
  };
  window.addEventListener('mousemove', placer, { capture: true, passive: true });
  window.addEventListener('pointermove', placer, { capture: true, passive: true });
  window.addEventListener(
    'mousedown',
    (e) => {
      placer(e);
      document.getElementById('curseur-doc')?.classList.add('enfonce');
      const halo = document.createElement('div');
      halo.className = 'halo-doc';
      halo.style.left = `${e.clientX}px`;
      halo.style.top = `${e.clientY}px`;
      document.documentElement.appendChild(halo);
      setTimeout(() => halo.remove(), 600);
    },
    { capture: true, passive: true },
  );
  window.addEventListener('mouseup', () => document.getElementById('curseur-doc')?.classList.remove('enfonce'), {
    capture: true,
    passive: true,
  });
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', installer);
  else installer();
}

/**
 * Script injecté dans chaque page : transitions et animations CSS instantanées. Un fondu coûte une image entière par pas
 * (taille du fichier presque doublée) et, à 12 images par seconde, ne se voit guère ; les mouvements du graphe, calculés
 * en JavaScript, restent animés.
 */
export function scriptSansTransitions() {
  const style = document.createElement('style');
  style.textContent =
    '*:not(.halo-doc), *::before, *::after { transition-duration: 0s !important; transition-delay: 0s !important; ' +
    'animation-duration: 0s !important; animation-delay: 0s !important; }';
  if (document.head) document.head.appendChild(style);
  else document.addEventListener('DOMContentLoaded', () => document.head.appendChild(style));
}

const pause = (ms) => new Promise((r) => setTimeout(r, ms));
const adoucir = (t) => (t < 0.5 ? 4 * t * t * t : 1 - (-2 * t + 2) ** 3 / 2);

/**
 * Une scène : la page d'un scénario, avec ses gestes. Tous les gestes passent par la vraie souris et le vrai clavier
 * (événements de confiance), à un rythme lisible.
 */
export class Scene {
  constructor(page) {
    this.page = page;
    this.x = -100;
    this.y = -100;
  }

  /** Attente lisible entre deux gestes. */
  async pause(ms = 600) {
    await this.page.waitForTimeout(ms);
  }

  /** Attend la fin des chargements visibles (squelettes, zones « aria-busy »), puis un court repos. */
  async attendreChargement(repos = 300) {
    await this.page.waitForFunction(() => !document.querySelector('[aria-busy="true"], .squelette'), null, {
      timeout: 20_000,
    });
    await this.pause(repos);
  }

  /** Déplace le curseur en douceur (accélération puis freinage), en 0,35 à 0,9 s selon la distance. */
  async aller(x, y) {
    const depart = { x: this.x < 0 ? x - 160 : this.x, y: this.y < 0 ? y + 120 : this.y };
    const distance = Math.hypot(x - depart.x, y - depart.y);
    const pas = Math.max(10, Math.min(36, Math.round(distance / 22)));
    for (let i = 1; i <= pas; i++) {
      const t = adoucir(i / pas);
      await this.page.mouse.move(depart.x + (x - depart.x) * t, depart.y + (y - depart.y) * t);
      await pause(14);
    }
    this.x = x;
    this.y = y;
  }

  /** Centre (ou point relatif) d'un élément visible, amené au milieu de la fenêtre s'il en sort. */
  async point(cible, decalage) {
    // Un lien du graphe (groupe SVG) n'est jamais « visible » pour Playwright : seule sa boîte compte.
    if (!(await cible.evaluate((e) => e instanceof SVGElement).catch(() => false))) {
      await cible.waitFor({ state: 'visible' });
      const dehors = await cible.evaluate((e) => {
        const r = e.getBoundingClientRect();
        return r.top < 8 || r.bottom > window.innerHeight - 8 || r.left < 0 || r.right > window.innerWidth;
      });
      // Saut direct plutôt que défilement doux : un défilement réencode la page entière à chaque image (fichier
      // plus lourd d'un quart à un tiers), pour un geste que le curseur suffit à faire suivre.
      if (dehors) {
        await cible.evaluate((e) => e.scrollIntoView({ block: 'center', inline: 'nearest' }));
        await this.pause(500);
      }
    }
    const boite = await cible.boundingBox();
    if (!boite) throw new Error('Élément sans dimensions : ' + cible);
    return decalage
      ? { x: boite.x + decalage.x, y: boite.y + decalage.y }
      : { x: boite.x + boite.width / 2, y: boite.y + boite.height / 2 };
  }

  /** Amène le curseur sur un élément, sans cliquer. */
  async survoler(cible, decalage) {
    const p = await this.point(cible, decalage);
    await this.aller(p.x, p.y);
    await this.pause(250);
  }

  /** Amène le curseur sur un élément puis clique dessus. */
  async cliquer(cible, options = {}) {
    const p = await this.point(cible, options.position);
    await this.aller(p.x, p.y);
    await this.pause(options.avant ?? 220);
    // Un lien du graphe (SVG) n'est pas « visible » pour Playwright : clic à la souris, au point visé.
    if (await cible.evaluate((e) => e instanceof SVGElement).catch(() => false))
      await this.page.mouse.click(p.x, p.y, { delay: 60 });
    else await cible.click({ position: options.position, delay: 60 });
    await this.pause(options.apres ?? 550);
  }

  /** Clique dans un champ puis tape le texte, lettre par lettre. */
  async saisir(cible, texte, options = {}) {
    await this.cliquer(cible, { apres: 200 });
    if (options.vider) await cible.fill('');
    await cible.pressSequentially(texte, { delay: options.delai ?? 45 });
    await this.pause(options.apres ?? 450);
  }

  /** Touche du clavier (Entrée, Échap…). */
  async touche(nom, apres = 450) {
    await this.page.keyboard.press(nom);
    await this.pause(apres);
  }

  /** Glisser-déposer à la souris, à vitesse humaine, d'un élément (ou d'un point) vers un autre. */
  async glisser(source, cible, options = {}) {
    const a = await this.point(source, options.depuis);
    const b = typeof cible.x === 'number' ? cible : await this.point(cible, options.vers);
    await this.aller(a.x, a.y);
    await this.pause(250);
    await this.page.mouse.down();
    await this.pause(200);
    // Petit décollage, puis le trajet : les bibliothèques de glisser-déposer attendent quelques pixels de mouvement.
    await this.aller(a.x + 6, a.y + 6);
    const pas = Math.max(24, Math.min(60, Math.round(Math.hypot(b.x - a.x, b.y - a.y) / 12)));
    for (let i = 1; i <= pas; i++) {
      const t = adoucir(i / pas);
      await this.page.mouse.move(a.x + 6 + (b.x - a.x - 6) * t, a.y + 6 + (b.y - a.y - 6) * t);
      await pause(18);
    }
    this.x = b.x;
    this.y = b.y;
    await this.pause(options.survol ?? 700);
    await this.page.mouse.up();
    await this.pause(options.apres ?? 700);
  }

  /** Molette, pour faire défiler une zone en douceur. */
  async defiler(dy, options = {}) {
    if (options.sur) await this.survoler(options.sur);
    const pas = Math.max(6, Math.round(Math.abs(dy) / 40));
    for (let i = 0; i < pas; i++) {
      await this.page.mouse.wheel(0, dy / pas);
      await pause(28);
    }
    await this.pause(options.apres ?? 500);
  }
}

/**
 * Enregistre la page image par image (Page.startScreencast : une image à chaque changement de l'écran, avec son heure),
 * puis assemble les images à leur rythme réel et les convertit par ffmpeg.
 */
export class Enregistreur {
  constructor(page, dossier) {
    this.page = page;
    this.dossier = dossier;
    this.images = [];
    this.ecritures = [];
  }

  async demarrer() {
    await rm(this.dossier, { recursive: true, force: true });
    await mkdir(this.dossier, { recursive: true });
    this.cdp = await this.page.context().newCDPSession(this.page);
    this.cdp.on('Page.screencastFrame', ({ data, metadata, sessionId }) => {
      const nom = `i${String(this.images.length).padStart(5, '0')}.png`;
      this.images.push({ nom, t: metadata.timestamp ?? Date.now() / 1000 });
      this.ecritures.push(writeFile(path.join(this.dossier, nom), Buffer.from(data, 'base64')));
      this.cdp.send('Page.screencastFrameAck', { sessionId }).catch(() => {});
    });
    await this.cdp.send('Page.startScreencast', { format: 'png', everyNthFrame: 1 });
    // Une première image même si rien ne bouge : un repeint forcé.
    await this.page.evaluate(() => {
      document.documentElement.style.outline = '0 solid transparent';
      requestAnimationFrame(() => (document.documentElement.style.outline = ''));
    });
    await this.page.waitForTimeout(150);
    this.debut = Date.now() / 1000;
  }

  /** Arrête l'enregistrement et écrit la liste ffconcat (chaque image avec sa durée réelle). Renvoie son chemin. */
  async arreter() {
    const fin = Date.now() / 1000;
    await this.cdp.send('Page.stopScreencast');
    await this.cdp.detach();
    await Promise.all(this.ecritures);
    if (this.images.length === 0) throw new Error('Aucune image enregistrée.');
    const lignes = ['ffconcat version 1.0'];
    this.images.forEach((image, i) => {
      const suivante = i + 1 < this.images.length ? this.images[i + 1].t : fin;
      lignes.push(`file '${image.nom}'`, `duration ${Math.max(0.001, suivante - image.t).toFixed(4)}`);
    });
    // ffconcat ignore la durée de la dernière entrée : la dernière image est répétée.
    lignes.push(`file '${this.images.at(-1).nom}'`);
    const liste = path.join(this.dossier, 'images.ffconcat');
    await writeFile(liste, lignes.join('\n') + '\n');
    return { liste, duree: fin - this.images[0].t, images: this.images.length };
  }
}

/** Lance ffmpeg et attend sa fin (erreur avec sa sortie s'il échoue). */
export function ffmpeg(args) {
  return new Promise((resolve, reject) => {
    const p = spawn('ffmpeg', ['-hide_banner', '-loglevel', 'error', '-y', ...args], {
      stdio: ['ignore', 'ignore', 'pipe'],
    });
    let erreurs = '';
    p.stderr.on('data', (d) => (erreurs += d));
    p.on('error', (e) =>
      reject(new Error(`ffmpeg introuvable (${e.message}) : l'installer pour générer les animations.`)),
    );
    p.on('close', (code) => (code === 0 ? resolve() : reject(new Error(`ffmpeg a échoué (${code}) : ${erreurs}`))));
  });
}

/**
 * Pas de quantification des couleurs. En WebP avec perte, libwebp tient pour inchangé un pixel qui diffère de moins de
 * 6 niveaux de l'image précédente (qualité 72) : un fondu laisse alors une trace pâle qui ne s'efface jamais. Arrondir
 * chaque composante à un multiple de 7 rend tout changement visible pour l'encodeur, sans écart perceptible à l'œil.
 */
const PAS_COULEUR = 7;

/** Filtres ffmpeg : rythme constant, recadrage, largeur finale et quantification. */
export function filtres({ ips, cadre, largeur }) {
  const f = [`fps=${ips}`];
  if (cadre) f.push(`crop=${cadre.width}:${cadre.height}:${cadre.x}:${cadre.y}`);
  if (largeur) f.push(`scale=${largeur}:-2:flags=lanczos`);
  const arrondi = `clip(round(val/${PAS_COULEUR})*${PAS_COULEUR}\\,0\\,255)`;
  f.push('format=rgb24', `lutrgb=r='${arrondi}':g='${arrondi}':b='${arrondi}'`);
  return f.join(',');
}

/**
 * WebP animé avec perte (qualité 72, 12 images par seconde) : 25 à 60 % plus léger qu'un GIF, en couleurs vraies ;
 * libwebp n'encode que les zones qui changent et allonge la durée d'une image identique à la précédente.
 */
export async function versWebp(liste, sortie, { ips = 12, qualite = 72, cadre, largeur } = {}) {
  const entree = ['-f', 'concat', '-safe', '0', '-i', liste];
  const encodage = ['-c:v', 'libwebp_anim', '-lossless', '0', '-quality', String(qualite), '-compression_level', '6'];
  await ffmpeg([
    ...entree,
    '-vf',
    filtres({ ips, cadre, largeur }),
    ...encodage,
    '-preset',
    'drawing',
    '-loop',
    '0',
    '-an',
    sortie,
  ]);
}
