import { TriangleAlert } from 'lucide-react';
import { Alerte } from './Alerte';
import { Button } from './Button';

/**
 * Avertit que la section a changé depuis le début de la saisie (voir `useDraft().conflit`) et
 * laisse choisir : reprendre la version à jour ou garder sa saisie (qui écrasera alors ce changement).
 */
export function AvisConflit({ brouillon, className }: { brouillon: { conflit: boolean; reset: () => void; garderSaisie: () => void }; className?: string }) {
  if (!brouillon.conflit) return null;
  return (
    <Alerte
      ton="warn"
      icone={TriangleAlert}
      role="alert"
      className={className}
      titre="Modifié pendant votre saisie"
      action={
        <span className="flex flex-wrap gap-2">
          <Button taille="sm" onClick={brouillon.reset}>Abandonner ma saisie</Button>
          <Button taille="sm" variante="fantome" onClick={brouillon.garderSaisie}>Garder ma saisie</Button>
        </span>
      }
    >
      Ces champs ont changé depuis que vous avez commencé à les modifier. Enregistrer maintenant écraserait ce changement.
    </Alerte>
  );
}
