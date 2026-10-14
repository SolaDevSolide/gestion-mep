import { Library, Phone, Plus } from 'lucide-react';
import { couleurSysteme, styleSysteme } from '../../couleursSysteme';
import { responsablesDuSysteme } from '../../domaine';
import { useMep } from '../../mepContext';
import type { FicheRecette, Systeme } from '../../types';
import { Button } from '../../ui/Button';
import { cn } from '../../ui/cn';
import { BadgeDisponibilite } from '../preparation/ResponsablesTechniquesSection';
import { FicheCarte } from './FicheCarte';

interface Props {
  /**
   * Système de la section, ou `null` pour les anciennes fiches sans système : section affichée seulement s'il y en a,
   * sans bouton d'ajout (on les rattache à un système depuis leur page).
   */
  systeme: Systeme | null;
  fiches: FicheRecette[];
  onAjouter?: () => void;
  onBibliotheque?: () => void;
}

/** Section colorée d'un système : responsables techniques à appeler et fiches de recette du système. */
export function SectionSysteme({ systeme, fiches, onAjouter, onBibliotheque }: Props) {
  const { mep } = useMep();
  const edition = mep.droits.peutEditerContenu;
  const titreId = `recette-${systeme?.id ?? 'sans-systeme'}`;
  const responsables = responsablesDuSysteme(mep, systeme?.id ?? null);
  const etapes = fiches.flatMap((f) => f.etapes);
  const conformes = etapes.filter((e) => e.resultat === 'Conforme').length;

  return (
    <section
      aria-labelledby={titreId}
      className="overflow-hidden rounded-xl border bg-surface"
      style={systeme ? { borderColor: couleurSysteme(systeme.nom) } : undefined}
    >
      <header
        className={cn(
          'flex flex-wrap items-center gap-x-4 gap-y-2 px-4 py-3',
          !systeme && 'border-b border-line bg-surface-2',
        )}
        style={systeme ? styleSysteme(systeme.nom) : undefined}
      >
        <div className="min-w-0 flex-1 basis-56">
          <h2 id={titreId} className="flex items-center gap-2 text-base font-bold">
            {systeme && (
              <span
                className="size-2.5 shrink-0 rounded-full"
                style={{ backgroundColor: couleurSysteme(systeme.nom) }}
                aria-hidden
              />
            )}
            {systeme ? systeme.nom : 'Sans système'}
          </h2>
          {systeme?.cible && <p className="truncate font-mono text-xs opacity-80">{systeme.cible}</p>}
        </div>
        {etapes.length > 0 && (mep.phase === 'Recette' || mep.phase === 'Cloturee') && (
          <span className="text-xs font-medium tnum">
            {conformes}/{etapes.length} conforme{conformes > 1 ? 's' : ''}
          </span>
        )}
        {edition && systeme && (
          <span className="flex flex-wrap gap-2">
            <Button
              variante="secondaire"
              taille="sm"
              icone={<Library className="size-4" aria-hidden />}
              aria-label={`Ajouter une fiche ${systeme.nom} depuis la bibliothèque`}
              onClick={onBibliotheque}
            >
              Depuis la bibliothèque
            </Button>
            <Button
              variante="secondaire"
              taille="sm"
              icone={<Plus className="size-4" aria-hidden />}
              aria-label={`Nouvelle fiche ${systeme.nom}`}
              onClick={onAjouter}
            >
              Nouvelle fiche
            </Button>
          </span>
        )}
      </header>
      <div className="space-y-3 px-4 py-4">
        {systeme && responsables.length > 0 && (
          <p
            className="flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-fg-muted"
            aria-label={`Responsables techniques de ${systeme.nom}`}
          >
            <span className="font-semibold text-fg">À appeler :</span>
            {responsables.map((r) => (
              <span key={r.participant.id} className="inline-flex items-center gap-1.5">
                {r.participant.nom}
                <BadgeDisponibilite valeur={r.disponibilite} />
                <span className="inline-flex items-center gap-1">
                  <Phone className="size-3" aria-hidden />
                  {r.contactJourJ || 'contact non renseigné'}
                </span>
              </span>
            ))}
          </p>
        )}
        {systeme && responsables.length === 0 && mep.phase === 'Recette' && (
          <p className="text-xs text-fg-subtle">Aucun responsable technique désigné pour {systeme.nom}.</p>
        )}
        {!systeme && (
          <p className="text-xs text-fg-subtle">
            Fiches sans système (créées avant, ou dont le système a été retiré de la MEP) : ouvrez-les pour les
            rattacher à un système.
          </p>
        )}
        {fiches.length === 0 ? (
          <p className="rounded-lg border border-dashed border-line-strong px-4 py-5 text-center text-sm text-fg-subtle">
            {systeme ? `Aucune fiche de recette pour le système ${systeme.nom}.` : 'Aucune fiche sans système.'}
          </p>
        ) : (
          <ul className="space-y-3">
            {fiches.map((f) => (
              <FicheCarte key={f.id} fiche={f} />
            ))}
          </ul>
        )}
      </div>
    </section>
  );
}
