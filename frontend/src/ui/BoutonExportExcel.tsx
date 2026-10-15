import { FileSpreadsheet } from 'lucide-react';
import { telecharger } from '../telechargement';
import { Button } from './Button';

/**
 * Bouton « Exporter en Excel » des vues : télécharge le classeur servi par l'API (le navigateur reste sur la page). Même
 * aspect partout (petit bouton secondaire, icône tableur), comme l'export de la recette.
 */
export function BoutonExportExcel({
  url,
  libelle = 'Exporter en Excel',
  title,
  disabled,
  className,
}: {
  url: string;
  libelle?: string;
  title?: string;
  disabled?: boolean;
  className?: string;
}) {
  return (
    <Button
      taille="sm"
      icone={<FileSpreadsheet className="size-4" aria-hidden />}
      title={title}
      disabled={disabled}
      className={className}
      onClick={() => telecharger(url)}
    >
      {libelle}
    </Button>
  );
}
