import { RefreshCw } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import { Button } from '../ui/Button';

const INTERVALLE_MS = 60_000;

async function lireVersion(): Promise<string | null> {
  try {
    const r = await fetch('/api/version', { cache: 'no-store' });
    if (!r.ok) return null;
    const corps = (await r.json()) as { version?: string };
    return corps.version ?? null;
  } catch {
    return null;
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
 */
export function NouvelleVersion() {
  const initiale = useRef<string | null>(null);
  const [disponible, setDisponible] = useState(false);

  useEffect(() => {
    if (typeof fetch === 'undefined') return;
    let actif = true;
    const verifier = async () => {
      const [v, entree] = await Promise.all([lireVersion(), lireEntree()]);
      if (!actif) return;
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
