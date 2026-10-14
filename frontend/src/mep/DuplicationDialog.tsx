import { Copy, OctagonAlert } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../api';
import { Alerte } from '../ui/Alerte';
import { Button } from '../ui/Button';
import { Checkbox } from '../ui/Checkbox';
import { Dialog } from '../ui/Dialog';
import { Field, Select } from '../ui/Input';
import { useToast } from '../ui/Toast';

export interface SourceDuplication {
  id: string;
  outil: string;
  estModele: boolean;
}

interface Props {
  open: boolean;
  onOpenChange: (o: boolean) => void;
  /** MEP ou modèle à dupliquer. */
  source?: SourceDuplication | null;
  /** Mode « nouvelle MEP depuis un modèle » : choix du modèle dans la liste. */
  modeles?: SourceDuplication[];
}

/**
 * Duplication d'une MEP (« en faire un modèle ») ; navigue vers la nouvelle MEP. Les systèmes sont copiés avec leurs
 * environnements, et chaque étape avec les siens.
 */
export function DuplicationDialog({ open, onOpenChange, source, modeles }: Props) {
  const navigate = useNavigate();
  const toast = useToast();
  const [choix, setChoix] = useState('');
  const [commeModele, setCommeModele] = useState(false);
  const [erreur, setErreur] = useState('');
  const [envoi, setEnvoi] = useState(false);

  const depuisModele = modeles !== undefined;
  const courant = depuisModele ? (modeles.find((m) => m.id === choix) ?? null) : (source ?? null);

  useEffect(() => {
    if (!open) return;
    setErreur('');
    setCommeModele(false);
    const premier = depuisModele ? null : (source ?? null);
    setChoix(premier?.id ?? '');
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  // Mode « depuis un modèle » : les modèles arrivent après l'ouverture, on présélectionne le premier.
  const premierModele = depuisModele ? modeles[0] : undefined;
  useEffect(() => {
    if (open && premierModele && !choix) setChoix(premierModele.id);
  }, [open, premierModele, choix]);

  async function dupliquer() {
    if (!courant) return;
    setEnvoi(true);
    setErreur('');
    try {
      const mep = await api.dupliquer(courant.id, { commeModele });
      toast.succes(
        commeModele ? 'Modèle créé.' : courant.estModele ? 'MEP créée depuis le modèle.' : 'MEP créée par duplication.',
      );
      onOpenChange(false);
      void navigate(`/meps/${mep.id}`);
    } catch (e) {
      setErreur(e instanceof Error ? e.message : 'Duplication impossible.');
    } finally {
      setEnvoi(false);
    }
  }

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      titre={
        depuisModele ? 'Nouvelle MEP depuis un modèle' : `Dupliquer ${courant?.estModele ? 'le modèle' : 'la MEP'}`
      }
      description={
        depuisModele
          ? 'Le plan, les personnes, les systèmes et leurs environnements, les étapes et les recettes du modèle sont copiés.'
          : 'Copie le plan, les personnes, les systèmes et leurs environnements, les étapes et les recettes. Dates, résultats et bilan ne sont pas copiés.'
      }
      pied={
        <>
          <Button variante="fantome" onClick={() => onOpenChange(false)}>
            Annuler
          </Button>
          <Button
            variante="primaire"
            icone={<Copy className="size-4" aria-hidden />}
            chargement={envoi}
            disabled={!courant}
            onClick={() => void dupliquer()}
          >
            {depuisModele ? 'Créer la MEP' : commeModele ? 'Créer le modèle' : 'Dupliquer'}
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
        {depuisModele && (
          <Field label="Modèle">
            {(id) =>
              modeles.length === 0 ? (
                <p className="rounded-lg border border-dashed border-line-strong px-3 py-2.5 text-sm text-fg-subtle">
                  Aucun modèle pour l’instant. Dupliquez une MEP en cochant « En faire un modèle ».
                </p>
              ) : (
                <Select id={id} value={choix} onChange={(e) => setChoix(e.target.value)}>
                  {modeles.map((m) => (
                    <option key={m.id} value={m.id}>
                      {m.outil || 'Modèle sans nom'}
                    </option>
                  ))}
                </Select>
              )
            }
          </Field>
        )}
        {!depuisModele && courant && (
          <p className="text-sm text-fg-muted">
            Source : <strong className="font-semibold text-fg">{courant.outil || 'MEP sans nom'}</strong>
          </p>
        )}
        {!depuisModele && (
          <Checkbox
            label="En faire un modèle"
            description="Un modèle sert de base à de nouvelles MEP mais ne peut pas être exécuté."
            checked={commeModele}
            onChange={setCommeModele}
          />
        )}
      </div>
    </Dialog>
  );
}
