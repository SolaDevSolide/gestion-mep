import { Loader2 } from 'lucide-react';
import { useEffect, useState, type ReactNode } from 'react';

/**
 * Délai de grâce : tant que `pret` est faux, rien n'est affiché pendant `delai` ms (un chargement court ne se voit
 * pas), puis un indicateur discret. Le contenu n'apparaît qu'une fois complet, au lieu de se remplir par morceaux.
 */
export function Apparition({ pret, delai = 300, children }: { pret: boolean; delai?: number; children: ReactNode }) {
  const [attenteLongue, setAttenteLongue] = useState(false);
  useEffect(() => {
    if (pret) return;
    const t = window.setTimeout(() => setAttenteLongue(true), delai);
    return () => window.clearTimeout(t);
  }, [pret, delai]);

  if (pret) return children;
  return (
    <div aria-busy="true" className="flex min-h-40 items-center justify-center text-fg-subtle">
      {attenteLongue && (
        <output className="flex items-center text-sm">
          <Loader2 className="mr-2 size-5 animate-spin" aria-hidden /> Chargement…
        </output>
      )}
    </div>
  );
}
