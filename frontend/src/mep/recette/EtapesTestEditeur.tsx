import { ArrowDown, ArrowUp, Plus, Trash2 } from 'lucide-react';
import { useRef } from 'react';
import { BoutonIcone, Button } from '../../ui/Button';
import { Field, Textarea } from '../../ui/Input';
import { ChampBlocs } from '../ChampBlocs';

/** Étape de test en cours d'édition : `cle` stable pour React, `id` absent pour une nouvelle étape. */
export interface EtapeBrouillon {
  cle: string;
  id?: string;
  instructions: string;
  resultatAttendu: string;
}

function deplacer<T>(liste: T[], i: number, delta: number): T[] {
  const j = i + delta;
  if (j < 0 || j >= liste.length) return liste;
  const copie = [...liste];
  [copie[i], copie[j]] = [copie[j], copie[i]];
  return copie;
}

/**
 * Étapes de test d'une fiche (ou d'un modèle de la bibliothèque), rédigées comme une étape de déploiement : une suite
 * libre de blocs Texte et Code, puis le résultat attendu en texte simple. Les anciennes recettes dont le résultat attendu
 * contient du markdown restent lisibles : le texte est montré tel quel.
 */
export function EtapesTestEditeur({
  etapes,
  onChange,
}: {
  etapes: EtapeBrouillon[];
  onChange: (etapes: EtapeBrouillon[]) => void;
}) {
  const compteur = useRef(0);
  const maj = (i: number, patch: Partial<EtapeBrouillon>) =>
    onChange(etapes.map((e, k) => (k === i ? { ...e, ...patch } : e)));

  return (
    <div className="space-y-3">
      {etapes.length === 0 ? (
        <p className="rounded-lg border border-dashed border-line-strong px-4 py-6 text-center text-sm text-fg-subtle">
          Aucune étape de test : ajoutez-en au moins une.
        </p>
      ) : (
        <ol className="space-y-3" aria-label="Étapes de test">
          {etapes.map((e, i) => (
            <li key={e.cle} className="rounded-lg border border-line p-3">
              <div className="mb-2 flex items-center gap-2">
                <span className="flex size-6 items-center justify-center rounded-md bg-surface-2 font-mono text-xs font-bold">
                  {i + 1}
                </span>
                <h4 className="flex-1 text-sm font-semibold">Étape {i + 1}</h4>
                <BoutonIcone
                  label={`Monter l’étape ${i + 1}`}
                  taille="sm"
                  disabled={i === 0}
                  onClick={() => onChange(deplacer(etapes, i, -1))}
                >
                  <ArrowUp className="size-4" aria-hidden />
                </BoutonIcone>
                <BoutonIcone
                  label={`Descendre l’étape ${i + 1}`}
                  taille="sm"
                  disabled={i === etapes.length - 1}
                  onClick={() => onChange(deplacer(etapes, i, 1))}
                >
                  <ArrowDown className="size-4" aria-hidden />
                </BoutonIcone>
                <BoutonIcone
                  label={`Supprimer l’étape ${i + 1}`}
                  taille="sm"
                  onClick={() => onChange(etapes.filter((_, k) => k !== i))}
                >
                  <Trash2 className="size-4" aria-hidden />
                </BoutonIcone>
              </div>
              <div className="space-y-3">
                <ChampBlocs
                  label={`Déroulé du test, étape ${i + 1}`}
                  hint="Blocs Texte et Code, dans l’ordre où on les suit."
                  valeur={e.instructions}
                  onChange={(instructions) => maj(i, { instructions })}
                />
                <Field label={`Résultat attendu, étape ${i + 1}`}>
                  {(id) => (
                    <Textarea
                      id={id}
                      rows={2}
                      value={e.resultatAttendu}
                      onChange={(ev) => maj(i, { resultatAttendu: ev.target.value })}
                      placeholder="ex. Le tableau de bord s’affiche avec le menu Mes factures."
                    />
                  )}
                </Field>
              </div>
            </li>
          ))}
        </ol>
      )}
      <Button
        taille="sm"
        icone={<Plus className="size-4" aria-hidden />}
        onClick={() =>
          onChange([...etapes, { cle: `nouvelle-${++compteur.current}`, instructions: '', resultatAttendu: '' }])
        }
      >
        Ajouter une étape de test
      </Button>
    </div>
  );
}
