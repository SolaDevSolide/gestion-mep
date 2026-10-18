import { RefreshCw } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import { appliquerEmpreinte } from '../charte/appliquer';
import { Button } from '../ui/Button';

const INTERVALLE_MS = 60_000;

/** Version du serveur, et empreinte de la charte visuelle en vigueur. */
async function lireVersion(): Promise<{ version: string | null; charte: string | null }> {
  try {
    const r = await fetch('/api/version', { cache: 'no-store' });
    if (!r.ok) return { version: null, charte: null };
    const corps = (await r.json()) as { version?: string; charte?: string };
    return { version: corps.version ?? null, charte: corps.charte ?? null };
  } catch {
    return { version: null, charte: null };
  }
}

/** Script d'entrée de l'interface construite (nom haché, différent à chaque construction) dans un index.html. */
const ENTREE = /<script[^>]*\ssrc="([^"]*\/assets\/[^"]+\.js)"/;

/** Script d'entrée de l'interface qui tourne dans cet onglet (null en développement). */
function entreeCourante(): string | null {
  const s = document.querySelector<HTMLScriptElement>('script[type="module"][src*="/assets/"]');
  return s ? new URL(s.src, window.location.href).pathname : null;
}

/** Script d'entrée de l'interface servie maintenant (index.html n'est jamais mis en cache). */
async function lireEntree(): Promise<string | null> {
  try {
    const r = await fetch('/', { cache: 'no-store', headers: { Accept: 'text/html' } });
    if (!r.ok) return null;
    const m = ENTREE.exec(await r.text());
    return m ? new URL(m[1], window.location.href).pathname : null;
  } catch {
    return null;
  }
}

/**
 * Prévient qu'une nouvelle version de l'application a été déployée pendant que l'onglet était ouvert :
 * l'application d'une seule page garde sinon l'ancien code jusqu'à un rechargement complet. Deux signes : le numéro de
 * version du serveur a changé, ou l'interface servie n'est plus celle de l'onglet (nouvelle construction, même sans
 * nouveau numéro : image reconstruite depuis une branche, numéro « 0.0.0-dev »).
 * La même vérification applique la charte visuelle modifiée entre-temps (console, fichier, ligne de commande), sans
 * rechargement : au plus une minute après le changement, ou dès le retour sur l'onglet.
 */
export function NouvelleVersion() {
  const initiale = useRef<string | null>(null);
  const [disponible, setDisponible] = useState(false);

  useEffect(() => {
    if (typeof fetch === 'undefined') return;
    let actif = true;
    const verifier = async () => {
      const [{ version: v, charte }, entree] = await Promise.all([lireVersion(), lireEntree()]);
      if (!actif) return;
      appliquerEmpreinte(charte);
      const courante = entreeCourante();
      if (courante && entree && entree !== courante) setDisponible(true);
      if (!v) return;
      if (initiale.current === null) initiale.current = v;
      else if (v !== initiale.current) setDisponible(true);
    };
    void verifier();
    const minuteur = window.setInterval(() => void verifier(), INTERVALLE_MS);
    const auFocus = () => void verifier();
    window.addEventListener('focus', auFocus);
    return () => {
      actif = false;
      window.clearInterval(minuteur);
      window.removeEventListener('focus', auFocus);
    };
  }, []);

  if (!disponible) return null;
  return (
    <div role="status" className="fixed inset-x-0 bottom-4 z-[100] flex justify-center px-4">
      <div className="flex items-center gap-3 rounded-xl border border-line bg-surface px-4 py-2.5 text-sm text-fg shadow-lg">
        <RefreshCw className="size-4 text-accent-text" aria-hidden />
        <span>Une nouvelle version de l’application est disponible.</span>
        <Button variante="primaire" taille="sm" onClick={() => window.location.reload()}>
          Recharger
        </Button>
      </div>
    </div>
  );
}
