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

/**
 * Prévient qu'une nouvelle version de l'application a été déployée pendant que l'onglet était ouvert :
 * l'application d'une seule page garde sinon l'ancien code jusqu'à un rechargement complet.
 */
export function NouvelleVersion() {
  const initiale = useRef<string | null>(null);
  const [disponible, setDisponible] = useState(false);

  useEffect(() => {
    if (typeof fetch === 'undefined') return;
    let actif = true;
    const verifier = async () => {
      const v = await lireVersion();
      if (!actif || !v) return;
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
