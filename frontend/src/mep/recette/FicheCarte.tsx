import { Check, ChevronRight, FlaskConical, XCircle } from 'lucide-react';
import { Link } from 'react-router-dom';
import { useMep } from '../../mepContext';
import { CadrePresence } from '../../presence';
import type { FicheRecette } from '../../types';
import { StatusPill } from '../../ui/StatusPill';

/** Adresse de la page d'une fiche (Plan › Recette), partageable. */
export const lienFiche = (ficheId: string) => ({
  search: `?onglet=plan&plan=recette&fiche=${encodeURIComponent(ficheId)}`,
});

/** Carte d'une fiche de recette dans sa section : titre, nombre d'étapes, bilan ; un clic ouvre la page de la fiche. */
export function FicheCarte({ fiche }: { fiche: FicheRecette }) {
  const { mep } = useMep();
  const conformes = fiche.etapes.filter((e) => e.resultat === 'Conforme').length;
  const echecs = fiche.etapes.filter((e) => e.resultat === 'Echec').length;
  const terminee = fiche.etapes.length > 0 && conformes === fiche.etapes.length;
  const bilan = (mep.phase === 'Recette' || mep.phase === 'Cloturee') && fiche.etapes.length > 0;

  return (
    <li>
      <CadrePresence cle={`recette:${fiche.id}`}>
        <Link
          to={lienFiche(fiche.id)}
          className="flex flex-wrap items-center gap-x-3 gap-y-2 rounded-xl border border-line bg-surface px-4 py-3 shadow-sm transition-shadow duration-150 hover:shadow-md focus-visible:ring-2 focus-visible:ring-accent focus-visible:outline-none"
        >
          <span className="min-w-0 flex-1 basis-56">
            <span className="block truncate text-[15px] font-semibold">
              {fiche.titre || <em className="font-normal text-fg-subtle not-italic">Fiche sans titre</em>}
            </span>
            <span className="block text-xs text-fg-subtle">
              {fiche.etapes.length} étape{fiche.etapes.length > 1 ? 's' : ''} de test
            </span>
          </span>
          {bilan &&
            (echecs > 0 ? (
              <StatusPill ton="danger" icone={XCircle} taille="sm">
                {echecs} en échec
              </StatusPill>
            ) : terminee ? (
              <StatusPill ton="ok" icone={Check} taille="sm">
                Conforme
              </StatusPill>
            ) : (
              <StatusPill ton="wait" icone={FlaskConical} taille="sm">
                {conformes}/{fiche.etapes.length}
              </StatusPill>
            ))}
          <ChevronRight className="size-4 shrink-0 text-fg-subtle" aria-hidden />
        </Link>
      </CadrePresence>
    </li>
  );
}
