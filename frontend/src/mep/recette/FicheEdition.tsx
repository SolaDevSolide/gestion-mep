import { Save, Trash2 } from 'lucide-react';
import { useState } from 'react';
import { api } from '../../api';
import { useDraft, useReportDirty } from '../../hooks';
import { useMep } from '../../mepContext';
import type { FicheRecette } from '../../types';
import { AvisConflit } from '../../ui/AvisConflit';
import { Button } from '../../ui/Button';
import { ConfirmDialog } from '../../ui/Dialog';
import { Field, Input, Select } from '../../ui/Input';
import { EtapesTestEditeur, type EtapeBrouillon } from './EtapesTestEditeur';

interface FicheBrouillon {
  titre: string;
  /** Système choisi pour rattacher une ancienne fiche sans système (« » : inchangé). */
  rattacher: string;
  etapes: EtapeBrouillon[];
}

const brouillonDe = (f: FicheRecette): FicheBrouillon => ({
  titre: f.titre,
  rattacher: '',
  etapes: f.etapes.map((e) => ({
    cle: e.id,
    id: e.id,
    instructions: e.instructions,
    resultatAttendu: e.resultatAttendu,
  })),
});

/**
 * Rédaction d'une fiche de recette (préparation) : titre (la partie du système testée) et étapes de test ordonnées.
 * La fiche garde son système ; une ancienne fiche sans système peut y être rattachée.
 */
export function FicheEdition({ fiche, onSupprimee }: { fiche: FicheRecette; onSupprimee?: () => void }) {
  const { mep, run } = useMep();
  const d = useDraft(brouillonDe(fiche));
  useReportDirty(d.dirty, `Fiche de recette « ${fiche.titre || 'sans titre'} »`);
  const v = d.value;
  const [suppr, setSuppr] = useState(false);
  const [envoi, setEnvoi] = useState(false);
  const sansSysteme = !mep.systemes.some((s) => s.id === fiche.systemeId);

  async function enregistrer() {
    if (d.conflit) return;
    setEnvoi(true);
    try {
      const ok = await run(
        () =>
          api.modifierFicheRecette(mep.id, fiche.id, {
            ...(v.rattacher ? { systemeId: v.rattacher } : {}),
            titre: v.titre.trim(),
            etapes: v.etapes.map((e) => ({
              id: e.id,
              instructions: e.instructions.trim(),
              resultatAttendu: e.resultatAttendu.trim(),
            })),
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
      <div className={sansSysteme ? 'grid gap-4 md:grid-cols-[minmax(0,1fr)_16rem]' : undefined}>
        <Field label="Titre de la fiche (partie du système testée) *">
          {(id) => (
            <Input
              id={id}
              value={v.titre}
              onChange={(e) => d.update({ titre: e.target.value })}
              placeholder="ex. Tables clients"
            />
          )}
        </Field>
        {sansSysteme && (
          <Field label="Rattacher au système" hint="Une fiche appartient à un seul système de la MEP.">
            {(id) => (
              <Select id={id} value={v.rattacher} onChange={(e) => d.update({ rattacher: e.target.value })}>
                <option value="">— Choisir —</option>
                {mep.systemes.map((s) => (
                  <option key={s.id} value={s.id}>
                    {s.nom}
                  </option>
                ))}
              </Select>
            )}
          </Field>
        )}
      </div>

      <EtapesTestEditeur etapes={v.etapes} onChange={(etapes) => d.update({ etapes })} />

      <AvisConflit brouillon={d} />
      <div className="flex flex-wrap items-center justify-end gap-2 border-t border-line pt-4">
        <Button
          variante="danger-doux"
          taille="sm"
          icone={<Trash2 className="size-4" aria-hidden />}
          onClick={() => setSuppr(true)}
        >
          Supprimer la fiche
        </Button>
        {d.dirty && (
          <Button variante="fantome" taille="sm" onClick={d.reset}>
            Annuler les modifications
          </Button>
        )}
        <Button
          variante="primaire"
          taille="sm"
          icone={<Save className="size-4" aria-hidden />}
          disabled={!d.dirty || d.conflit}
          chargement={envoi}
          onClick={() => void enregistrer()}
        >
          Enregistrer la fiche
        </Button>
      </div>

      <ConfirmDialog
        open={suppr}
        onOpenChange={setSuppr}
        titre="Supprimer cette fiche ?"
        description={`« ${fiche.titre || 'Fiche sans titre'} » et ses ${fiche.etapes.length} étape(s) de test seront retirées de la recette.`}
        libelleConfirmer="Supprimer"
        variante="danger"
        onConfirmer={async () => {
          const ok = await run(() => api.supprimerFicheRecette(mep.id, fiche.id), 'Fiche de recette supprimée.');
          if (ok) onSupprimee?.();
          return ok;
        }}
      />
    </div>
  );
}
