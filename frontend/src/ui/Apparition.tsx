import type { ReactNode } from 'react';
import { cn } from './cn';
import { EnChargement, SqueletteLignes } from './Squelette';

/**
 * Contenu affiché en une fois, complet, au lieu de se remplir par morceaux. Tant que `pret` est faux, un squelette aux
 * dimensions du contenu (`squelette`, sinon quelques lignes) tient sa place ; il n'apparaît qu'après un court délai (un
 * chargement court ne se voit pas). Puis le contenu apparaît en fondu.
 */
export function Apparition({
  pret,
  squelette,
  libelle,
  className,
  children,
}: {
  pret: boolean;
  squelette?: ReactNode;
  /** Ce qui se charge, pour les lecteurs d'écran (« Chargement des étapes… »). */
  libelle?: string;
  className?: string;
  children: ReactNode;
}) {
  if (!pret)
    return (
      <EnChargement libelle={libelle} className={className}>
        {squelette ?? <SqueletteLignes lignes={4} className="py-6" />}
      </EnChargement>
    );
  return <div className={cn('apparition', className)}>{children}</div>;
}
