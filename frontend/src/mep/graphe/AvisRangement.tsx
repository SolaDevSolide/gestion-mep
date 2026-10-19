import { RotateCw, TriangleAlert, X } from 'lucide-react';
import { useState } from 'react';
import { BoutonIcone, Button } from '../../ui/Button';
import type { CauseIndisponible } from './moteurElk';

/**
 * Message non bloquant, en haut du graphe, quand le rangement automatique a échoué : le graphe garde ses places (on peut
 * continuer à travailler), « Réessayer » relance le rangement avec un moteur neuf. Rien quand le code du moteur a disparu
 * après un déploiement : c'est alors le message « nouvelle version » qui s'affiche.
 */
export function AvisRangement({ cause, onReessayer }: { cause: CauseIndisponible | null; onReessayer: () => void }) {
  // Masqué jusqu'au prochain échec.
  const [masque, setMasque] = useState<CauseIndisponible | null>(null);
  const [vu, setVu] = useState(cause);
  if (vu !== cause) {
    setVu(cause);
    setMasque(null);
  }
  if (!cause || cause === 'version' || masque === cause) return null;
  return (
    <div
      role="status"
      data-avis-rangement
      className="absolute top-3 left-1/2 z-20 flex max-w-[calc(100%-1.5rem)] -translate-x-1/2 items-center gap-2 rounded-xl border border-warn-bd bg-warn-bg py-1 pr-1 pl-3 text-[13px] font-medium text-warn-fg shadow-md"
      title={
        cause === 'delai'
          ? 'Le rangement a pris plus de 15 secondes : les cartes restent à leurs places actuelles.'
          : 'Les cartes restent à leurs places actuelles : vous pouvez continuer à travailler.'
      }
    >
      <TriangleAlert className="size-4 shrink-0" aria-hidden />
      <span className="min-w-0 truncate">Rangement automatique indisponible</span>
      <Button taille="sm" icone={<RotateCw className="size-4" aria-hidden />} onClick={onReessayer}>
        Réessayer
      </Button>
      <BoutonIcone label="Masquer ce message" taille="sm" onClick={() => setMasque(cause)}>
        <X className="size-4" aria-hidden />
      </BoutonIcone>
    </div>
  );
}
