import { KeyRound, OctagonAlert } from 'lucide-react';
import { useState, type FormEvent, type ReactNode } from 'react';
import { api } from '../api';
import { useAuth } from '../auth';
import { Alerte } from '../ui/Alerte';
import { Button } from '../ui/Button';
import { Field, Input } from '../ui/Input';

export const LONGUEUR_MIN_MOT_DE_PASSE = 10;

interface Props {
  libelleAncien: string;
  /** Appelé après un changement réussi (l'utilisateur connecté est déjà mis à jour). */
  onChange?: () => void;
  /** Contenu du pied (à côté du bouton), par exemple un lien de déconnexion. */
  pied?: ReactNode;
  className?: string;
}

/** Ancien mot de passe, nouveau, confirmation : validation côté front puis POST /auth/mot-de-passe. */
export function FormulaireMotDePasse({ libelleAncien, onChange, pied, className }: Props) {
  const { majUtilisateur } = useAuth();
  const [ancien, setAncien] = useState('');
  const [nouveau, setNouveau] = useState('');
  const [confirmation, setConfirmation] = useState('');
  const [erreur, setErreur] = useState('');
  const [envoi, setEnvoi] = useState(false);

  const tropCourt = nouveau !== '' && nouveau.length < LONGUEUR_MIN_MOT_DE_PASSE;
  const different = confirmation !== '' && confirmation !== nouveau;
  const identique = nouveau !== '' && nouveau === ancien;
  const valide = ancien !== '' && nouveau.length >= LONGUEUR_MIN_MOT_DE_PASSE && confirmation === nouveau && !identique;

  async function valider(e: FormEvent) {
    e.preventDefault();
    if (!valide) return;
    setEnvoi(true);
    setErreur('');
    try {
      const u = await api.changerMotDePasse({ ancien, nouveau });
      setAncien('');
      setNouveau('');
      setConfirmation('');
      majUtilisateur(u);
      onChange?.();
    } catch (err) {
      setErreur(err instanceof Error ? err.message : 'Changement impossible.');
    } finally {
      setEnvoi(false);
    }
  }

  return (
    <form onSubmit={(e) => void valider(e)} className={className ?? 'space-y-4'}>
      <Field label={libelleAncien}>
        {(id) => (
          <Input
            id={id}
            type="password"
            value={ancien}
            onChange={(e) => setAncien(e.target.value)}
            autoComplete="current-password"
          />
        )}
      </Field>
      <Field
        label="Nouveau mot de passe"
        hint={`${LONGUEUR_MIN_MOT_DE_PASSE} caractères au minimum, différent de l’ancien.`}
        erreur={
          tropCourt
            ? `Au moins ${LONGUEUR_MIN_MOT_DE_PASSE} caractères.`
            : identique
              ? 'Le nouveau mot de passe doit différer de l’ancien.'
              : undefined
        }
      >
        {(id) => (
          <Input
            id={id}
            type="password"
            value={nouveau}
            onChange={(e) => setNouveau(e.target.value)}
            autoComplete="new-password"
          />
        )}
      </Field>
      <Field
        label="Confirmation du nouveau mot de passe"
        erreur={different ? 'La confirmation ne correspond pas.' : undefined}
      >
        {(id) => (
          <Input
            id={id}
            type="password"
            value={confirmation}
            onChange={(e) => setConfirmation(e.target.value)}
            autoComplete="new-password"
          />
        )}
      </Field>
      {erreur && (
        <Alerte ton="danger" icone={OctagonAlert} role="alert">
          {erreur}
        </Alerte>
      )}
      <div className="flex flex-wrap items-center gap-3">
        <Button
          type="submit"
          variante="primaire"
          icone={<KeyRound className="size-4" aria-hidden />}
          disabled={!valide}
          chargement={envoi}
        >
          Changer le mot de passe
        </Button>
        {pied}
      </div>
    </form>
  );
}
