import { ArrowLeft, BookmarkPlus, RotateCcw, SearchX } from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../../api';
import { couleurSysteme } from '../../couleursSysteme';
import { useMep } from '../../mepContext';
import { Button } from '../../ui/Button';
import { ZonePresence } from '../../presence';
import { Card } from '../../ui/Card';
import { EmptyState } from '../../ui/EmptyState';
import { EnregistrerModeleDialog } from './EnregistrerModeleDialog';
import { FicheEdition } from './FicheEdition';
import { FicheLecture } from './FicheLecture';

const RETOUR = { search: '?onglet=plan&plan=recette' };

/**
 * Page d'une fiche de recette, dans Plan › Recette (adresse partageable `?onglet=plan&plan=recette&fiche=…`), avec un fil
 * « Recette / SYSTÈME / titre » pour revenir. Avant le Go : rédaction de la fiche (si l'on peut modifier le contenu).
 * Après le Go : on déroule la fiche soi-même (Conforme / Échec, observation, Refaire pour le pilote).
 */
export function PageFiche({
  ficheId,
  peutAgir,
  onRetour,
}: {
  ficheId: string;
  peutAgir: boolean;
  onRetour: () => void;
}) {
  const { mep, run } = useMep();
  const [modele, setModele] = useState(false);
  const fiche = mep.fichesRecette.find((f) => f.id === ficheId);

  if (!fiche) {
    return (
      <div className="mx-auto w-full max-w-5xl px-6 py-6">
        <Card>
          <EmptyState
            icone={SearchX}
            titre="Fiche de recette introuvable"
            description="Elle a peut-être été supprimée."
            action={
              <Button icone={<ArrowLeft className="size-4" aria-hidden />} onClick={onRetour}>
                Retour à la recette
              </Button>
            }
          />
        </Card>
      </div>
    );
  }

  const systeme = mep.systemes.find((s) => s.id === fiche.systemeId);
  const titre = fiche.titre || 'Fiche sans titre';
  const redaction = mep.droits.peutEditerContenu;
  const enRecette = mep.phase === 'Recette';
  const dejaFait = fiche.etapes.some((e) => e.resultat !== null);

  return (
    <div className="mx-auto w-full max-w-5xl space-y-5 px-6 py-6">
      <nav aria-label="Fil d’Ariane" className="flex min-w-0 flex-wrap items-center gap-1.5 text-sm text-fg-muted">
        <Link
          to={RETOUR}
          className="inline-flex items-center gap-1 rounded-md px-1.5 py-0.5 font-medium hover:bg-surface-2 hover:text-fg"
        >
          <ArrowLeft className="size-4" aria-hidden />
          Recette
        </Link>
        <span aria-hidden>/</span>
        {systeme ? (
          <span
            className="inline-flex items-center gap-1.5 font-semibold"
            style={{ color: couleurSysteme(systeme.nom) }}
          >
            <span
              className="size-2.5 rounded-full"
              style={{ backgroundColor: couleurSysteme(systeme.nom) }}
              aria-hidden
            />
            {systeme.nom}
          </span>
        ) : (
          <span className="font-semibold">Sans système</span>
        )}
        <span aria-hidden>/</span>
        <span className="min-w-0 truncate font-semibold text-fg" aria-current="page">
          {titre}
        </span>
      </nav>

      <Card>
        <div className="space-y-4 p-5">
          <div className="flex flex-wrap items-center gap-3">
            <div className="min-w-0 flex-1">
              <h2 className="truncate text-lg font-bold">{titre}</h2>
              <p className="text-sm text-fg-subtle">
                {mep.phase === 'Preparation'
                  ? redaction
                    ? 'Préparation : rédaction de la fiche.'
                    : 'Préparation : seuls les responsables techniques et le pilote rédigent la fiche.'
                  : enRecette
                    ? 'Recette en cours : déroulez les étapes ci-dessous.'
                    : mep.phase === 'Execution'
                      ? 'Déploiement en cours : la fiche se déroule une fois la MEP passée en recette.'
                      : 'Résultats de la recette.'}
              </p>
            </div>
            {enRecette && mep.droits.estPilote && dejaFait && (
              <Button
                taille="sm"
                icone={<RotateCcw className="size-4" aria-hidden />}
                onClick={() => void run(() => api.refaireFicheRecette(mep.id, fiche.id), 'Fiche à refaire.')}
              >
                Refaire la fiche
              </Button>
            )}
            {fiche.etapes.length > 0 && (
              <Button
                taille="sm"
                icone={<BookmarkPlus className="size-4" aria-hidden />}
                onClick={() => setModele(true)}
              >
                Enregistrer comme modèle
              </Button>
            )}
          </div>
          <ZonePresence cle={`recette:${fiche.id}`}>
            {redaction ? (
              <FicheEdition key={fiche.id} fiche={fiche} onSupprimee={onRetour} />
            ) : (
              <FicheLecture fiche={fiche} peutAgir={peutAgir} />
            )}
          </ZonePresence>
        </div>
      </Card>
      <EnregistrerModeleDialog fiche={fiche} open={modele} onOpenChange={setModele} />
    </div>
  );
}
