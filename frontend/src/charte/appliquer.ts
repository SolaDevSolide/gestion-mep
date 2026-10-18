/**
 * Feuille de la charte visuelle (/charte.css, liée dans index.html avant l'application) : elle ne redéfinit que les
 * jetons de couleur que la charte en vigueur change, et porte son empreinte (`--charte-empreinte`). Quand le serveur
 * annonce une autre empreinte (/api/version, ou réponse de l'enregistrement dans la console), la feuille est remplacée
 * sans recharger la page : la nouvelle est chargée à côté de l'ancienne, puis l'ancienne est retirée (pas de clignotement).
 */

const ID = 'charte';

/** Empreinte de la charte appliquée dans cet onglet ; null si la feuille n'est pas chargée (développement, tests). */
export function empreinteAppliquee(): string | null {
  const brute = getComputedStyle(document.documentElement).getPropertyValue('--charte-empreinte').trim();
  const valeur = brute.replace(/^["']|["']$/g, '');
  return valeur || null;
}

let enCours: string | null = null;

/** Applique la charte d'empreinte `empreinte` si ce n'est pas déjà celle de l'onglet. */
export function appliquerEmpreinte(empreinte: string | null | undefined): void {
  if (!empreinte || typeof document === 'undefined') return;
  const actuelle = document.getElementById(ID);
  if (!(actuelle instanceof HTMLLinkElement)) return;
  if (empreinte === enCours || empreinte === (actuelle.dataset.empreinte ?? empreinteAppliquee())) return;
  enCours = empreinte;
  const nouvelle = document.createElement('link');
  nouvelle.rel = 'stylesheet';
  nouvelle.href = `/charte.css?v=${encodeURIComponent(empreinte)}`;
  nouvelle.dataset.empreinte = empreinte;
  const remplacer = () => {
    if (enCours !== empreinte) {
      nouvelle.remove();
      return;
    }
    enCours = null;
    actuelle.removeAttribute('id');
    actuelle.remove();
    nouvelle.id = ID;
  };
  nouvelle.addEventListener('load', remplacer);
  nouvelle.addEventListener('error', () => {
    if (enCours === empreinte) enCours = null;
    nouvelle.remove();
  });
  actuelle.after(nouvelle);
}
