import { ArrowDown, ArrowUp, Plus, Save, Trash2 } from 'lucide-react';
import { useRef, useState } from 'react';
import { api } from '../../api';
import { useDraft, useReportDirty } from '../../hooks';
import { useMep } from '../../mepContext';
import type { FicheRecette } from '../../types';
import { AvisConflit } from '../../ui/AvisConflit';
import { BoutonIcone, Button } from '../../ui/Button';
import { ConfirmDialog } from '../../ui/Dialog';
import { Field, Input, Select } from '../../ui/Input';
import { ChampBlocs } from '../ChampBlocs';
import { vide } from '../preparation/Formulaires';

/* ------------------------------------------------------------------ Brouillon d'édition d'une fiche */

interface EtapeBrouillon { cle: string; id?: string; instructions: string; resultatAttendu: string }
interface FicheBrouillon { titre: string; systemeId: string; etapes: EtapeBrouillon[] }

const brouillonDe = (f: FicheRecette): FicheBrouillon => ({
  titre: f.titre,
  systemeId: f.systemeId ?? '',
  etapes: f.etapes.map((e) => ({ cle: e.id, id: e.id, instructions: e.instructions, resultatAttendu: e.resultatAttendu })),
});

function deplacer<T>(liste: T[], i: number, delta: number): T[] {
  const j = i + delta;
  if (j < 0 || j >= liste.length) return liste;
  const copie = [...liste];
  [copie[i], copie[j]] = [copie[j], copie[i]];
  return copie;
}

/* ------------------------------------------------------------------ Édition (préparation) */

/** Formulaire d'édition d'une fiche de recette (titre, système, étapes de test ordonnées), en préparation. */
export function FicheEdition({ fiche }: { fiche: FicheRecette }) {
  const { mep, run } = useMep();
  const d = useDraft(brouillonDe(fiche));
  useReportDirty(d.dirty);
  const v = d.value;
  const compteur = useRef(0);
  const [suppr, setSuppr] = useState(false);
  const [envoi, setEnvoi] = useState(false);

  const majEtape = (i: number, patch: Partial<EtapeBrouillon>) => d.update({ etapes: v.etapes.map((e, k) => (k === i ? { ...e, ...patch } : e)) });

  async function enregistrer() {
    if (d.conflit) return;
    setEnvoi(true);
    try {
      const ok = await run(
        () => api.modifierFicheRecette(mep.id, fiche.id, {
          systemeId: vide(v.systemeId),
          titre: v.titre.trim(),
          etapes: v.etapes.map((e) => ({ id: e.id, instructions: e.instructions.trim(), resultatAttendu: e.resultatAttendu.trim() })),
        }),
        'Fiche de recette enregistrée.',
      );
      if (ok) d.reset();
    } finally {
      setEnvoi(false);
    }
  }

  return (
    <div className="space-y-4">
      <div className="grid gap-4 md:grid-cols-[minmax(0,1fr)_14rem]">
        <Field label="Titre de la fiche *">{(id) => <Input id={id} value={v.titre} onChange={(e) => d.update({ titre: e.target.value })} placeholder="ex. Tables clients" />}</Field>
        <Field label="Système">
          {(id) => (
            <Select id={id} value={v.systemeId} onChange={(e) => d.update({ systemeId: e.target.value })}>
              <option value="">— Sans système —</option>
              {mep.systemes.map((s) => <option key={s.id} value={s.id}>{s.nom}</option>)}
            </Select>
          )}
        </Field>
      </div>

      {v.etapes.length === 0 ? (
        <p className="rounded-lg border border-dashed border-line-strong px-4 py-6 text-center text-sm text-fg-subtle">Aucune étape de test : ajoutez-en au moins une.</p>
      ) : (
        <ol className="space-y-3" aria-label="Étapes de test">
          {v.etapes.map((e, i) => (
            <li key={e.cle} className="rounded-lg border border-line p-3">
              <div className="mb-2 flex items-center gap-2">
                <span className="flex size-6 items-center justify-center rounded-md bg-surface-2 font-mono text-xs font-bold">{i + 1}</span>
                <h4 className="flex-1 text-sm font-semibold">Étape {i + 1}</h4>
                <BoutonIcone label={`Monter l’étape ${i + 1}`} taille="sm" disabled={i === 0} onClick={() => d.update({ etapes: deplacer(v.etapes, i, -1) })}><ArrowUp className="size-4" aria-hidden /></BoutonIcone>
                <BoutonIcone label={`Descendre l’étape ${i + 1}`} taille="sm" disabled={i === v.etapes.length - 1} onClick={() => d.update({ etapes: deplacer(v.etapes, i, 1) })}><ArrowDown className="size-4" aria-hidden /></BoutonIcone>
                <BoutonIcone label={`Supprimer l’étape ${i + 1}`} taille="sm" onClick={() => d.update({ etapes: v.etapes.filter((_, k) => k !== i) })}><Trash2 className="size-4" aria-hidden /></BoutonIcone>
              </div>
              <div className="grid gap-3 lg:grid-cols-2">
                <ChampBlocs label={`Instructions de l’étape ${i + 1}`} valeur={e.instructions} onChange={(instructions) => majEtape(i, { instructions })} />
                <ChampBlocs label={`Résultat attendu de l’étape ${i + 1}`} valeur={e.resultatAttendu} onChange={(resultatAttendu) => majEtape(i, { resultatAttendu })} />
              </div>
            </li>
          ))}
        </ol>
      )}

      <AvisConflit brouillon={d} />
      <div className="flex flex-wrap items-center gap-2">
        <Button taille="sm" icone={<Plus className="size-4" aria-hidden />} onClick={() => d.update({ etapes: [...v.etapes, { cle: `nouvelle-${++compteur.current}`, instructions: '', resultatAttendu: '' }] })}>Ajouter une étape de test</Button>
        <span className="ml-auto flex items-center gap-2">
          <Button variante="danger-doux" taille="sm" icone={<Trash2 className="size-4" aria-hidden />} onClick={() => setSuppr(true)}>Supprimer la fiche</Button>
          {d.dirty && <Button variante="fantome" taille="sm" onClick={d.reset}>Annuler les modifications</Button>}
          <Button variante="primaire" taille="sm" icone={<Save className="size-4" aria-hidden />} disabled={!d.dirty || d.conflit} chargement={envoi} onClick={() => void enregistrer()}>Enregistrer la fiche</Button>
        </span>
      </div>

      <ConfirmDialog
        open={suppr}
        onOpenChange={setSuppr}
        titre="Supprimer cette fiche ?"
        description={`« ${fiche.titre || 'Fiche sans titre'} » et ses ${fiche.etapes.length} étape(s) de test seront retirées de la recette.`}
        libelleConfirmer="Supprimer"
        variante="danger"
        onConfirmer={() => run(() => api.supprimerFicheRecette(mep.id, fiche.id), 'Fiche de recette supprimée.')}
      />
    </div>
  );
}
