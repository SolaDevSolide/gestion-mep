import { OctagonAlert, Plus, X } from 'lucide-react';
import { useEffect, useState, type FormEvent } from 'react';
import { api } from '../api';
import { Alerte } from '../ui/Alerte';
import { Button } from '../ui/Button';
import { Dialog } from '../ui/Dialog';
import { Input } from '../ui/Input';
import { EnChargement, SqueletteLignes } from '../ui/Squelette';
import { useToast } from '../ui/Toast';

/**
 * Liste des catégories d'objets proposées par défaut à chaque système du catalogue (administrateurs). Retirer une
 * catégorie de la liste ne la retire pas des systèmes qui l'utilisent déjà.
 */
export function CategoriesDefautDialog({ open, onOpenChange }: { open: boolean; onOpenChange: (o: boolean) => void }) {
  const toast = useToast();
  const [liste, setListe] = useState<string[] | null>(null);
  const [nouvelle, setNouvelle] = useState('');
  const [erreur, setErreur] = useState('');
  const [envoi, setEnvoi] = useState(false);

  useEffect(() => {
    if (!open) return;
    setListe(null);
    setNouvelle('');
    setErreur('');
    api
      .categoriesParDefaut()
      .then(setListe)
      .catch((e: unknown) => setErreur(e instanceof Error ? e.message : 'Chargement impossible.'));
  }, [open]);

  function ajouter(e: FormEvent) {
    e.preventDefault();
    const nom = nouvelle.trim();
    if (!nom || !liste) return;
    if (liste.some((c) => c.toLowerCase() === nom.toLowerCase())) {
      setErreur(`« ${nom} » est déjà dans la liste.`);
      return;
    }
    setErreur('');
    setListe([...liste, nom]);
    setNouvelle('');
  }

  async function enregistrer() {
    if (!liste) return;
    setEnvoi(true);
    setErreur('');
    try {
      await api.definirCategoriesParDefaut(liste);
      toast.succes('Catégories par défaut enregistrées.');
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
      taille="lg"
      titre="Catégories d’objets par défaut"
      description="Proposées à chaque système du catalogue ; chaque système garde celles qui s’appliquent et y ajoute ses éléments."
      pied={
        <>
          <Button variante="fantome" onClick={() => onOpenChange(false)}>
            Annuler
          </Button>
          <Button variante="primaire" chargement={envoi} disabled={!liste} onClick={() => void enregistrer()}>
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
        <form onSubmit={ajouter} className="flex gap-2">
          <Input
            value={nouvelle}
            onChange={(e) => setNouvelle(e.target.value)}
            placeholder="Nouvelle catégorie, ex. Écrans"
            aria-label="Nouvelle catégorie"
            className="flex-1"
          />
          <Button type="submit" icone={<Plus className="size-4" aria-hidden />} disabled={!nouvelle.trim() || !liste}>
            Ajouter
          </Button>
        </form>
        {liste === null ? (
          <EnChargement libelle="Chargement des catégories…">
            <SqueletteLignes lignes={4} />
          </EnChargement>
        ) : liste.length === 0 ? (
          <p className="text-sm text-fg-subtle">Aucune catégorie par défaut.</p>
        ) : (
          <ul className="flex flex-wrap gap-1.5" aria-label="Catégories par défaut">
            {liste.map((c) => (
              <li
                key={c}
                className="inline-flex items-center gap-1 rounded-full border border-line bg-surface-2 py-0.5 pr-1 pl-2.5 text-sm"
              >
                {c}
                <button
                  type="button"
                  aria-label={`Retirer ${c}`}
                  onClick={() => setListe(liste.filter((x) => x !== c))}
                  className="rounded-full p-0.5 text-fg-subtle hover:bg-surface hover:text-fg"
                >
                  <X className="size-3.5" aria-hidden />
                </button>
              </li>
            ))}
          </ul>
        )}
        <p className="text-xs text-fg-subtle">
          Retirer une catégorie de cette liste ne la retire pas des systèmes qui l’utilisent déjà.
        </p>
      </div>
    </Dialog>
  );
}
