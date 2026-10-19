import { useEffect, useState } from 'react';
import { suivreActivite } from '../api';

/**
 * Indicateur discret des chargements en cours (premier chargement ou rechargement en arrière-plan) : un mince trait animé
 * en haut de la fenêtre, qui n'apparaît que si la réponse se fait attendre. Le contenu déjà affiché reste en place.
 */
export function ActiviteReseau({ delai = 250 }: { delai?: number }) {
  const [enCours, setEnCours] = useState(false);
  const [visible, setVisible] = useState(false);
  useEffect(() => suivreActivite((n) => setEnCours(n > 0)), []);
  useEffect(() => {
    if (!enCours) {
      setVisible(false);
      return;
    }
    const t = window.setTimeout(() => setVisible(true), delai);
    return () => window.clearTimeout(t);
  }, [enCours, delai]);
  if (!visible) return null;
  return (
    <div
      role="progressbar"
      aria-label="Chargement en cours"
      className="pointer-events-none fixed inset-x-0 top-0 z-toast h-0.5 overflow-hidden bg-accent-soft animate-fade-in"
    >
      <div className="activite h-full w-2/5 rounded-full bg-accent" />
    </div>
  );
}
