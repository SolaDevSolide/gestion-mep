import { CheckCircle2, ClipboardList } from 'lucide-react';
import { useEffect, useState } from 'react';
import type { BilanSaisie } from '../types';
import { Alerte } from '../ui/Alerte';
import { Button } from '../ui/Button';
import { Dialog } from '../ui/Dialog';
import { Field, Textarea } from '../ui/Input';

interface Props {
  open: boolean;
  onOpenChange: (o: boolean) => void;
  /** Clôture après retour arrière (MEP « repliée ») ou clôture réussie. */
  repli: boolean;
  bilanActive: boolean;
  /** Retourne true si la clôture a abouti. */
  onConfirmer: (bilan: BilanSaisie) => Promise<boolean | void> | boolean | void;
}

const VIDE: BilanSaisie = { ceQuiAMarche: '', incidents: '', actions: '' };

/** Clôture de la MEP avec le bilan (ce qui a marché, incidents, actions). */
export function ClotureDialog({ open, onOpenChange, repli, bilanActive, onConfirmer }: Props) {
  const [b, setB] = useState<BilanSaisie>(VIDE);
  const [envoi, setEnvoi] = useState(false);
  useEffect(() => {
    if (open) setB(VIDE);
  }, [open]);

  const incomplet = bilanActive && (!b.ceQuiAMarche.trim() || !b.incidents.trim());

  async function confirmer() {
    setEnvoi(true);
    try {
      const r = await onConfirmer({
        ceQuiAMarche: b.ceQuiAMarche.trim(),
        incidents: b.incidents.trim(),
        actions: b.actions.trim(),
      });
      if (r !== false) onOpenChange(false);
    } finally {
      setEnvoi(false);
    }
  }

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      taille="md"
      titre={repli ? 'Le service est-il rétabli ?' : 'Clôturer la MEP'}
      description={
        repli
          ? 'Toutes les étapes de retour arrière sont réussies. La MEP sera clôturée comme « repliée » et passera en lecture seule.'
          : 'Tous les tests de recette sont conformes. La MEP sera clôturée comme « réussie » et passera en lecture seule.'
      }
      pied={
        <>
          <Button variante="fantome" onClick={() => onOpenChange(false)}>
            Annuler
          </Button>
          <Button
            variante="succes"
            icone={<CheckCircle2 className="size-4" aria-hidden />}
            chargement={envoi}
            disabled={incomplet}
            onClick={() => void confirmer()}
          >
            {repli ? 'Service rétabli : clôturer' : 'Clôturer la MEP'}
          </Button>
        </>
      }
    >
      {bilanActive ? (
        <div className="space-y-4">
          <p className="flex items-center gap-2 text-sm font-semibold">
            <ClipboardList className="size-4 text-accent-text" aria-hidden />
            Bilan de clôture
          </p>
          <Field label="Ce qui a marché *" hint="« Rien à signaler » est accepté.">
            {(id) => (
              <Textarea
                id={id}
                rows={3}
                value={b.ceQuiAMarche}
                onChange={(e) => setB({ ...b, ceQuiAMarche: e.target.value })}
                placeholder="ex. Migration de la base sans interruption, déploiement dans les temps."
                autoFocus
              />
            )}
          </Field>
          <Field label="Incidents *" hint="Ce qui a posé problème, même résolu. « Rien à signaler » est accepté.">
            {(id) => (
              <Textarea
                id={id}
                rows={3}
                value={b.incidents}
                onChange={(e) => setB({ ...b, incidents: e.target.value })}
                placeholder="ex. Redémarrage de l’API plus long que prévu (résolu par la résolution type)."
              />
            )}
          </Field>
          <Field label="Actions" hint="Suites à donner, améliorations à prévoir (facultatif).">
            {(id) => (
              <Textarea
                id={id}
                rows={2}
                value={b.actions}
                onChange={(e) => setB({ ...b, actions: e.target.value })}
                placeholder="ex. Automatiser la vérification de santé après redémarrage."
              />
            )}
          </Field>
        </div>
      ) : (
        <Alerte ton="wait" icone={ClipboardList}>
          Le bilan de clôture est désactivé pour cette MEP.
        </Alerte>
      )}
    </Dialog>
  );
}
