import { OctagonAlert } from 'lucide-react';
import { useMemo, useState } from 'react';
import { api } from '../api';
import type { SystemeCatalogue } from '../types';
import { Alerte } from '../ui/Alerte';
import { Button } from '../ui/Button';
import { Checkbox } from '../ui/Checkbox';
import { Dialog } from '../ui/Dialog';
import { Field, Input, Textarea } from '../ui/Input';
import { useToast } from '../ui/Toast';

const VIDE = { nom: '', description: '', cible: '', actif: true };

/** Création ou modification d'un système du catalogue (nom, cible, description, actif). */
export function SystemeDialog({
  existant,
  open,
  onOpenChange,
  onFait,
}: {
  existant: SystemeCatalogue | null;
  open: boolean;
  onOpenChange: (o: boolean) => void;
  onFait: () => void;
}) {
  const toast = useToast();
  const initial = useMemo(
    () =>
      existant
        ? { nom: existant.nom, description: existant.description, cible: existant.cible, actif: existant.actif }
        : VIDE,
    [existant],
  );
  const [v, setV] = useState(initial);
  const [erreur, setErreur] = useState('');
  const [envoi, setEnvoi] = useState(false);
  const [vu, setVu] = useState<unknown>(null);
  if (open && vu !== initial) {
    setVu(initial);
    setV(initial);
    setErreur('');
  }
  if (!open && vu !== null) setVu(null);

  async function enregistrer() {
    setEnvoi(true);
    setErreur('');
    const s = { nom: v.nom.trim(), description: v.description.trim(), cible: v.cible.trim(), actif: v.actif };
    try {
      if (existant) await api.modifierSystemeCatalogue(existant.id, s);
      else await api.creerSystemeCatalogue(s);
      toast.succes(existant ? 'Système modifié.' : 'Système ajouté au catalogue.');
      onFait();
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
      titre={existant ? 'Modifier le système' : 'Nouveau système'}
      description="Les MEP peuvent ensuite l’ajouter depuis le catalogue, ce qui relie ses résolutions types."
      pied={
        <>
          <Button variante="fantome" onClick={() => onOpenChange(false)}>
            Annuler
          </Button>
          <Button variante="primaire" chargement={envoi} disabled={!v.nom.trim()} onClick={() => void enregistrer()}>
            Enregistrer
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        {erreur && (
          <Alerte ton="danger" icone={OctagonAlert} role="alert">
            {erreur}
          </Alerte>
        )}
        <Field label="Nom *">
          {(id) => <Input id={id} value={v.nom} onChange={(e) => setV({ ...v, nom: e.target.value })} autoFocus />}
        </Field>
        <Field label="Cible exacte" hint="Serveur, base, URL ou chemin qui identifie le système sans ambiguïté.">
          {(id) => (
            <Input
              id={id}
              className="font-mono"
              value={v.cible}
              onChange={(e) => setV({ ...v, cible: e.target.value })}
            />
          )}
        </Field>
        <Field label="Description">
          {(id) => (
            <Textarea
              id={id}
              rows={3}
              value={v.description}
              onChange={(e) => setV({ ...v, description: e.target.value })}
            />
          )}
        </Field>
        <Checkbox
          label="Actif"
          description="Un système inactif n’est plus proposé pour les nouvelles MEP."
          checked={v.actif}
          onChange={(a) => setV({ ...v, actif: a })}
        />
      </div>
    </Dialog>
  );
}
