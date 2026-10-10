import { useCallback, useEffect, useRef, useState } from 'react';

/** Ligne d'aide sous la barre des systèmes : texte par défaut, remplacé quelques secondes par un message. */
export function useAstuce(defaut: string, duree = 5000) {
  const [message, setMessage] = useState<string | null>(null);
  const minuteur = useRef<number | undefined>(undefined);
  useEffect(() => () => window.clearTimeout(minuteur.current), []);
  const flash = useCallback(
    (m: string) => {
      setMessage(m);
      window.clearTimeout(minuteur.current);
      minuteur.current = window.setTimeout(() => setMessage(null), duree);
    },
    [duree],
  );
  return { texte: message ?? defaut, flash: message !== null, afficher: flash };
}
