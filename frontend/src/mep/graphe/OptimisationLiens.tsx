import { Check, Route, X } from 'lucide-react';
import type { LienRedondant } from '../../types';
import { Button } from '../../ui/Button';

/**
 * Bouton de la barre d'outils : calcule les liens inutiles du plan (ou, dans la vue d'une section, ceux de la section) et les
 * montre avant de les retirer.
 */
export function BoutonOptimiserLiens({
  actif,
  dansSection = false,
  onClick,
}: {
  actif: boolean;
  dansSection?: boolean;
  onClick: () => unknown;
}) {
  return (
    <Button
      taille="sm"
      icone={<Route className="size-4" aria-hidden />}
      aria-pressed={actif}
      action={onClick}
      title={
        dansSection
          ? 'Repérer les liens inutiles entre les éléments de cette section (déjà garantis par un autre chemin dans la section) et les retirer, sans toucher au reste du plan'
          : 'Repérer les liens inutiles (déjà garantis par un autre chemin) et les retirer, sans changer l’ordre d’exécution'
      }
    >
      Optimiser les liens
    </Button>
  );
}

/**
 * Aperçu de l'optimisation, dans le cadre du graphe : les liens retirés sont en rouge dans le plan et listés ici ; rien
 * ne change avant « Confirmer ».
 */
export function PanneauOptimisation({
  liens,
  enCours,
  onConfirmer,
  onAnnuler,
}: {
  liens: LienRedondant[];
  enCours: boolean;
  onConfirmer: () => void;
  onAnnuler: () => void;
}) {
  const n = liens.length;
  return (
    <section
      aria-label="Optimiser les liens"
      className="grid gap-2 border-b border-danger-bd bg-danger-bg px-3 py-2 text-ui text-danger-fg"
    >
      <div className="flex flex-wrap items-center gap-2">
        <p className="min-w-0 flex-1 font-semibold">
          {n} lien{n > 1 ? 's' : ''} inutile{n > 1 ? 's' : ''} : chaque étape attend déjà ces préalables par un autre
          chemin. {n > 1 ? 'Ils seront retirés' : 'Il sera retiré'} sans changer l’ordre d’exécution.
        </p>
        <Button taille="sm" icone={<X className="size-4" aria-hidden />} onClick={onAnnuler}>
          Annuler
        </Button>
        <Button
          taille="sm"
          variante="danger"
          icone={<Check className="size-4" aria-hidden />}
          onClick={onConfirmer}
          disabled={enCours}
        >
          Confirmer
        </Button>
      </div>
      <ul aria-label="Liens qui seront retirés" className="flex flex-wrap gap-1.5">
        {liens.map((l) => (
          <li
            key={`${l.de}>${l.vers}`}
            className="rounded-full border border-danger-bd bg-surface px-2 py-0.5 font-medium tnum"
          >
            {l.libelle}
          </li>
        ))}
      </ul>
    </section>
  );
}
