import { OctagonAlert } from 'lucide-react';
import { useEffect, useState } from 'react';
import { api } from '../../api';
import { useAsync } from '../../hooks';
import { useMep } from '../../mepContext';
import type { FicheRecette } from '../../types';
import { Alerte } from '../../ui/Alerte';
import { Button } from '../../ui/Button';
import { Dialog } from '../../ui/Dialog';
import { Field, Input, Select } from '../../ui/Input';
import { useToast } from '../../ui/Toast';

/**
 * « Enregistrer comme modèle » : copie la fiche (titre et étapes, sans résultat) dans la bibliothèque de recettes,
 * rattachée à un système du catalogue (par défaut celui dont vient le système de la fiche).
 */
export function EnregistrerModeleDialog({
  fiche,
  open,
  onOpenChange,
}: {
  fiche: FicheRecette;
  open: boolean;
  onOpenChange: (o: boolean) => void;
}) {
  const { mep } = useMep();
  const toast = useToast();
  const catalogue = useAsync(() => (open ? api.catalogueSystemes() : Promise.resolve([])), [open]);
  const systeme = mep.systemes.find((s) => s.id === fiche.systemeId);
  const [titre, setTitre] = useState(fiche.titre);
  const [systemeCatalogueId, setSystemeCatalogueId] = useState(systeme?.catalogueId ?? '');
  const [erreur, setErreur] = useState('');
  const [envoi, setEnvoi] = useState(false);

  useEffect(() => {
    if (!open) return;
    setTitre(fiche.titre);
    setSystemeCatalogueId(systeme?.catalogueId ?? '');
    setErreur('');
  }, [open, fiche.titre, systeme?.catalogueId]);

  async function enregistrer() {
    setErreur('');
    if (!systemeCatalogueId) {
      setErreur('Choisissez le système du catalogue testé par le modèle.');
      return;
    }
    setEnvoi(true);
    try {
      const m = await api.modeleDepuisFiche(mep.id, fiche.id, titre.trim(), systemeCatalogueId);
      toast.succes(`Modèle « ${m.titre} » ajouté à la bibliothèque de recettes.`);
      onOpenChange(false);
    } catch (e) {
      setErreur(e instanceof Error ? e.message : 'Enregistrement impossible.');
    } finally {
      setEnvoi(false);
    }
  }

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      titre="Enregistrer comme modèle"
      description="La fiche et ses étapes de test sont copiées dans la bibliothèque de recettes, pour les réutiliser dans d’autres MEP."
      pied={
        <>
          <Button variante="fantome" onClick={() => onOpenChange(false)}>
            Annuler
          </Button>
          <Button variante="primaire" chargement={envoi} onClick={() => void enregistrer()}>
            Enregistrer le modèle
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <Field label="Titre du modèle">
          {(id) => <Input id={id} value={titre} onChange={(e) => setTitre(e.target.value)} />}
        </Field>
        <Field label="Système du catalogue">
          {(id) => (
            <Select
              id={id}
              value={systemeCatalogueId}
              disabled={catalogue.donnees === null && !catalogue.erreur}
              aria-busy={(catalogue.donnees === null && !catalogue.erreur) || undefined}
              onChange={(e) => setSystemeCatalogueId(e.target.value)}
            >
              <option value="">
                {catalogue.donnees === null && !catalogue.erreur ? 'Chargement…' : '— Choisir —'}
              </option>
              {(catalogue.donnees ?? []).map((s) => (
                <option key={s.id} value={s.id}>
                  {s.nom}
                </option>
              ))}
            </Select>
          )}
        </Field>
        {erreur && (
          <Alerte ton="danger" icone={OctagonAlert} role="alert">
            {erreur}
          </Alerte>
        )}
      </div>
    </Dialog>
  );
}
