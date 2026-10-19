/**
 * Reconnexion Microsoft après une session expirée : la redirection vers le fournisseur quitte la page. Avant de partir,
 * l'adresse de la page et les saisies non enregistrées (signalées par `useReportDirty`) sont copiées dans le stockage de
 * session de l'onglet ; au retour, l'application rouvre cette adresse et chaque formulaire reprend sa saisie.
 */
const CLE_RETOUR = 'gestion-mep.retour-connexion';
const CLE_BROUILLONS = 'gestion-mep.brouillons';
/** Au-delà, une copie n'est plus reprise (reconnexion abandonnée puis reprise bien plus tard). */
const DUREE_MS = 60 * 60 * 1000;

interface Copie {
  chemin: string;
  le: number;
  brouillons: Record<string, unknown>;
}

function lire(cle: string): unknown {
  try {
    const brut = window.sessionStorage.getItem(cle);
    return brut === null ? null : (JSON.parse(brut) as unknown);
  } catch {
    return null;
  }
}

function ecrire(cle: string, valeur: unknown) {
  try {
    if (valeur === null) window.sessionStorage.removeItem(cle);
    else window.sessionStorage.setItem(cle, JSON.stringify(valeur));
  } catch {
    /* stockage indisponible : la saisie ne pourra pas être reprise */
  }
}

/** Adresse interne (« /meps/… »), jamais une autre origine. */
const adresseInterne = (a: string) => a.startsWith('/') && !a.startsWith('//');

/** Retient la page à rouvrir après la connexion (connexion Microsoft, qui revient sur « / »). */
export function retenirRetour(adresse: string) {
  if (adresseInterne(adresse) && !adresse.startsWith('/connexion')) ecrire(CLE_RETOUR, { adresse, le: Date.now() });
}

/** Page à rouvrir après une connexion Microsoft, lue une seule fois. */
export function prendreRetour(): string | null {
  const v = lire(CLE_RETOUR);
  ecrire(CLE_RETOUR, null);
  if (typeof v !== 'object' || v === null || !('adresse' in v) || !('le' in v)) return null;
  const { adresse, le } = v;
  if (typeof adresse !== 'string' || typeof le !== 'number' || Date.now() - le > DUREE_MS) return null;
  return adresseInterne(adresse) ? adresse : null;
}

/** Copie les saisies non enregistrées de la page `chemin` (par libellé de formulaire). */
export function copierBrouillons(chemin: string, brouillons: { libelle: string; valeur: unknown }[]) {
  const copie: Copie = { chemin, le: Date.now(), brouillons: {} };
  for (const b of brouillons) if (b.valeur !== null && b.valeur !== undefined) copie.brouillons[b.libelle] = b.valeur;
  ecrire(CLE_BROUILLONS, Object.keys(copie.brouillons).length > 0 ? copie : null);
}

/** Vrai s'il reste des saisies copiées à reprendre. */
export function brouillonsEnAttente(): boolean {
  const v = lire(CLE_BROUILLONS);
  return typeof v === 'object' && v !== null && 'brouillons' in v;
}

/** Saisie copiée pour le formulaire `libelle` de la page `chemin`, retirée de la copie ; undefined s'il n'y en a pas. */
export function reprendreBrouillon(chemin: string, libelle: string): unknown {
  const v = lire(CLE_BROUILLONS);
  if (typeof v !== 'object' || v === null || !('chemin' in v) || !('le' in v) || !('brouillons' in v)) return undefined;
  const { brouillons, le } = v;
  if (v.chemin !== chemin || typeof le !== 'number' || typeof brouillons !== 'object' || brouillons === null)
    return undefined;
  if (Date.now() - le > DUREE_MS) {
    ecrire(CLE_BROUILLONS, null);
    return undefined;
  }
  const restants: Record<string, unknown> = { ...brouillons };
  if (!(libelle in restants)) return undefined;
  const valeur = restants[libelle];
  delete restants[libelle];
  ecrire(CLE_BROUILLONS, Object.keys(restants).length > 0 ? { chemin, le, brouillons: restants } : null);
  return valeur;
}
