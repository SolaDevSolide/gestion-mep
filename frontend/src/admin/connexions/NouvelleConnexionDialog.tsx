import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import type { CatalogueConnexions, ModeleConnexion } from '../../types';
import { Button } from '../../ui/Button';
import { cn } from '../../ui/cn';
import { Dialog } from '../../ui/Dialog';
import { Field, Input } from '../../ui/Input';
import { Badge } from '../../ui/StatusPill';

/**
 * « Nouvelle connexion » : choix d'un modèle de départ (méthode, format, authentification, message et déclencheurs
 * suggérés, tout restant modifiable) et du nom, puis l'éditeur.
 */
export function NouvelleConnexionDialog({
  open,
  onOpenChange,
  catalogue,
}: {
  open: boolean;
  onOpenChange: (o: boolean) => void;
  catalogue: CatalogueConnexions;
}) {
  const navigate = useNavigate();
  const [modele, setModele] = useState<ModeleConnexion>('Teams');
  const [nom, setNom] = useState('');

  function continuer() {
    const p = new URLSearchParams({ modele });
    if (nom.trim()) p.set('nom', nom.trim());
    onOpenChange(false);
    void navigate(`/admin/api/nouvelle?${p.toString()}`);
  }

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      taille="lg"
      titre="Nouvelle connexion"
      description="Choisissez un point de départ. Vous réglerez l’adresse et le message à l’étape suivante."
      pied={
        <>
          <Button variante="fantome" onClick={() => onOpenChange(false)}>
            Annuler
          </Button>
          <Button variante="primaire" onClick={continuer}>
            Continuer
          </Button>
        </>
      }
    >
      <form
        onSubmit={(e) => {
          e.preventDefault();
          continuer();
        }}
        className="grid gap-4"
      >
        <div role="radiogroup" aria-label="Modèle de départ" className="grid gap-2.5 sm:grid-cols-2">
          {catalogue.modeles.map((m) => (
            <button
              key={m.modele}
              type="button"
              role="radio"
              aria-checked={modele === m.modele}
              onClick={() => setModele(m.modele)}
              className={cn(
                'rounded-lg border px-3.5 py-3 text-left transition-colors duration-150',
                modele === m.modele ? 'border-accent bg-accent-soft' : 'border-line hover:border-line-strong',
              )}
            >
              <span className="block text-sm font-semibold text-fg">{m.libelle}</span>
              <span className="mt-0.5 block text-ui text-fg-muted">{m.description}</span>
              {m.etiquettes.length > 0 && (
                <span className="mt-2 flex flex-wrap gap-1">
                  {m.etiquettes.map((e) => (
                    <Badge key={e}>{e}</Badge>
                  ))}
                </span>
              )}
            </button>
          ))}
        </div>
        <Field label="Nom de la connexion" hint="Modifiable ensuite. Par exemple « Canal Teams Exploitation ».">
          {(i) => <Input id={i} value={nom} maxLength={100} onChange={(e) => setNom(e.target.value)} />}
        </Field>
      </form>
    </Dialog>
  );
}
