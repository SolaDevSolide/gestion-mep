import { Check, ChevronDown, ChevronRight, FlaskConical, RotateCcw, XCircle } from 'lucide-react';
import { useState } from 'react';
import { api } from '../../api';
import { useMep } from '../../mepContext';
import { ZonePresence } from '../../presence';
import type { FicheRecette } from '../../types';
import { Button } from '../../ui/Button';
import { StatusPill } from '../../ui/StatusPill';
import { FicheEdition } from './FicheEdition';
import { FicheLecture } from './FicheLecture';

/**
 * Carte repliable d'une fiche de recette : en-tête avec bilan, corps en édition (préparation)
 * ou en lecture et déroulé (recette).
 */
export function FicheCarte({ fiche, peutAgir }: { fiche: FicheRecette; peutAgir: boolean }) {
  const { mep, run } = useMep();
  const edition = mep.droits.peutEditerContenu;
  const enRecette = mep.phase === 'Recette';
  const conformes = fiche.etapes.filter((e) => e.resultat === 'Conforme').length;
  const echecs = fiche.etapes.filter((e) => e.resultat === 'Echec').length;
  const terminee = fiche.etapes.length > 0 && conformes === fiche.etapes.length;
  const [ouvert, setOuvert] = useState(edition ? fiche.etapes.length === 0 : !terminee);
  const corps = `fiche-${fiche.id}`;
  const dejaFait = conformes + echecs > 0;

  return (
    <li className="rounded-xl border border-line bg-surface shadow-sm">
      <ZonePresence cle={`recette:${fiche.id}`}>
        <div className="flex flex-wrap items-center gap-x-3 gap-y-2 px-4 py-3">
          <button
            type="button"
            aria-expanded={ouvert}
            aria-controls={corps}
            onClick={() => setOuvert(!ouvert)}
            className="flex min-w-0 flex-1 basis-56 items-center gap-2 rounded-md text-left"
          >
            {ouvert ? (
              <ChevronDown className="size-4 shrink-0 text-fg-subtle" aria-hidden />
            ) : (
              <ChevronRight className="size-4 shrink-0 text-fg-subtle" aria-hidden />
            )}
            <span className="min-w-0">
              <span className="block truncate text-[15px] font-semibold">
                {fiche.titre || <em className="font-normal text-fg-subtle not-italic">Fiche sans titre</em>}
              </span>
              <span className="block text-xs text-fg-subtle">
                {fiche.etapes.length} étape{fiche.etapes.length > 1 ? 's' : ''} de test
              </span>
            </span>
          </button>
          {(enRecette || mep.phase === 'Cloturee') &&
            fiche.etapes.length > 0 &&
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
          {enRecette && mep.droits.estPilote && dejaFait && (
            <Button
              taille="sm"
              icone={<RotateCcw className="size-4" aria-hidden />}
              aria-label={`Refaire toute la fiche ${fiche.titre}`}
              onClick={() => void run(() => api.refaireFicheRecette(mep.id, fiche.id), 'Fiche à refaire.')}
            >
              Refaire la fiche
            </Button>
          )}
        </div>
        {ouvert && (
          <div id={corps} className="border-t border-line px-4 py-4">
            {edition ? <FicheEdition fiche={fiche} /> : <FicheLecture fiche={fiche} peutAgir={peutAgir} />}
          </div>
        )}
      </ZonePresence>
    </li>
  );
}
