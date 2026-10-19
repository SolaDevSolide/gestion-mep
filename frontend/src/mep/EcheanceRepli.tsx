import { CalendarClock } from 'lucide-react';
import { useState } from 'react';
import { api } from '../api';
import { depuisInputDateTimeFuseau, formatDateHeureFuseau, fuseauValide, versInputDateTimeFuseau } from '../format';
import { useMep } from '../mepContext';
import { Button, type TailleBouton } from '../ui/Button';
import { Checkbox } from '../ui/Checkbox';
import { ConfirmDialog } from '../ui/Dialog';
import { Field, Input, Textarea } from '../ui/Input';

/** « 23/10/2026 23:00 (Europe/Paris) », ou « Aucune ». */
export function texteEcheance(echeance: string | null, fuseau: string): string {
  return echeance ? `${formatDateHeureFuseau(echeance, fuseau)} (${fuseauValide(fuseau)})` : 'Aucune';
}

/**
 * Bouton « Modifier l'échéance » et son dialogue : date et heure dans le fuseau de la MEP, motif obligatoire (sauf première
 * saisie en préparation), tracé au journal avec l'ancienne et la nouvelle valeur. Rien si l'utilisateur n'en a pas le droit.
 */
export function BoutonModifierEcheance({ taille = 'sm' }: { taille?: TailleBouton }) {
  const { mep, run } = useMep();
  const [ouvert, setOuvert] = useState(false);
  const [saisie, setSaisie] = useState('');
  const [retirer, setRetirer] = useState(false);
  const [motif, setMotif] = useState('');
  if (!mep.droits.peutModifierEcheance) return null;

  const fuseau = fuseauValide(mep.fuseauHoraire);
  const premiere = mep.phase === 'Preparation' && mep.echeanceRepli === null;
  const nouvelle = retirer ? null : depuisInputDateTimeFuseau(saisie, fuseau);
  const inchangee = nouvelle === mep.echeanceRepli || (!retirer && nouvelle === null);
  const motifManquant = !premiere && !motif.trim();

  function ouvrir() {
    setSaisie(versInputDateTimeFuseau(mep.echeanceRepli, fuseau));
    setRetirer(false);
    setMotif('');
    setOuvert(true);
  }

  return (
    <>
      <Button taille={taille} icone={<CalendarClock className="size-4" aria-hidden />} onClick={ouvrir}>
        {mep.echeanceRepli ? 'Modifier l’échéance' : 'Fixer l’échéance'}
      </Button>
      <ConfirmDialog
        open={ouvert}
        onOpenChange={setOuvert}
        titre="Échéance de retour arrière"
        description={
          mep.phase === 'Recette'
            ? 'En recette, l’échéance atteinte est seulement signalée : le pilote décide du retour arrière.'
            : 'En exécution, l’échéance atteinte déclenche le retour arrière automatiquement.'
        }
        libelleConfirmer="Enregistrer l’échéance"
        desactive={inchangee || motifManquant}
        taille="md"
        onConfirmer={() =>
          run(
            () => api.modifierEcheance(mep.id, { version: mep.version, echeanceRepli: nouvelle, motif: motif.trim() }),
            'Échéance enregistrée.',
          )
        }
      >
        <div className="space-y-4">
          <p className="text-sm text-fg-muted">
            Échéance actuelle : <span className="font-medium text-fg">{texteEcheance(mep.echeanceRepli, fuseau)}</span>
          </p>
          <Field label={`Nouvelle échéance (heure de ${fuseau})`} hint="Elle doit être dans le futur.">
            {(id) => (
              <Input
                id={id}
                type="datetime-local"
                value={saisie}
                disabled={retirer}
                className="md:max-w-xs"
                onChange={(e) => setSaisie(e.target.value)}
              />
            )}
          </Field>
          {mep.echeanceRepli !== null && (
            <Checkbox
              label="Retirer l’échéance"
              description="Plus de compte à rebours ni de déclenchement automatique du retour arrière."
              checked={retirer}
              onChange={setRetirer}
            />
          )}
          <Field
            label={premiere ? 'Motif (facultatif)' : 'Motif (obligatoire)'}
            hint="Tracé au journal avec l’ancienne et la nouvelle échéance."
          >
            {(id) => (
              <Textarea
                id={id}
                rows={2}
                value={motif}
                onChange={(e) => setMotif(e.target.value)}
                placeholder="ex. Migration plus longue que prévu, accord du métier pour 30 minutes de plus."
              />
            )}
          </Field>
        </div>
      </ConfirmDialog>
    </>
  );
}
