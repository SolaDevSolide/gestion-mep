import { Library, ListOrdered, OctagonAlert, Search } from 'lucide-react';
import { useMemo, useState } from 'react';
import { api } from '../../api';
import { useAsync } from '../../hooks';
import { useMep } from '../../mepContext';
import { correspond, termes } from '../../recherche';
import type { ModeleRecette, Systeme } from '../../types';
import { Alerte } from '../../ui/Alerte';
import { Button } from '../../ui/Button';
import { Dialog } from '../../ui/Dialog';
import { Input, Select } from '../../ui/Input';
import { Badge } from '../../ui/StatusPill';
import { EnChargement, Squelette } from '../../ui/Squelette';
import { cn } from '../../ui/cn';
import { premiereLigne } from '../../blocs';

/**
 * « Depuis la bibliothèque » : choisir un modèle de recette et l'insérer (copie) dans la section du système.
 * Les modèles du même système du catalogue sont proposés d'abord ; « Tous les systèmes » élargit la liste.
 */
export function BibliothequeDialog({ systeme, onFermer }: { systeme: Systeme | null; onFermer: () => void }) {
  const { mep, run } = useMep();
  const modeles = useAsync(() => (systeme ? api.modelesRecette() : Promise.resolve([] as ModeleRecette[])), [systeme]);
  const catalogue = useAsync(() => (systeme ? api.catalogueSystemes() : Promise.resolve([])), [systeme]);
  const [tous, setTous] = useState(false);
  const [recherche, setRecherche] = useState('');
  const [envoi, setEnvoi] = useState<string | null>(null);
  const catalogueId = systeme?.catalogueId ?? null;
  const parSysteme = !tous && catalogueId !== null;
  const noms = useMemo(() => new Map((catalogue.donnees ?? []).map((s) => [s.id, s.nom])), [catalogue.donnees]);

  const filtres = useMemo(() => {
    const ts = termes(recherche);
    return (modeles.donnees ?? []).filter(
      (m) =>
        (!parSysteme || m.systemeCatalogueId === catalogueId) &&
        correspond({ titre: m.titre, textes: m.etapes.map((e) => e.instructions) }, ts),
    );
  }, [modeles.donnees, recherche, parSysteme, catalogueId]);

  async function inserer(m: ModeleRecette) {
    if (!systeme) return;
    setEnvoi(m.id);
    try {
      const ok = await run(
        () => api.insererModeleRecette(mep.id, m.id, systeme.id),
        `Fiche « ${m.titre} » ajoutée depuis la bibliothèque.`,
      );
      if (ok) onFermer();
    } finally {
      setEnvoi(null);
    }
  }

  return (
    <Dialog
      open={systeme !== null}
      onOpenChange={(o) => {
        if (!o) onFermer();
      }}
      titre="Depuis la bibliothèque"
      description={
        systeme ? `La fiche choisie est copiée dans ${systeme.nom} : vous pourrez l’adapter à cette MEP.` : undefined
      }
      taille="lg"
    >
      <div className="flex flex-wrap items-center gap-3">
        <div className="relative min-w-52 flex-1">
          <Search
            className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-fg-subtle"
            aria-hidden
          />
          <Input
            aria-label="Rechercher un modèle"
            placeholder="Rechercher un modèle…"
            className="pl-9"
            value={recherche}
            onChange={(e) => setRecherche(e.target.value)}
          />
        </div>
        {catalogueId && (
          <div className="w-52">
            <Select
              aria-label="Systèmes des modèles"
              value={tous ? 'tous' : 'systeme'}
              onChange={(e) => setTous(e.target.value === 'tous')}
            >
              <option value="systeme">{noms.get(catalogueId) ?? systeme?.nom} seulement</option>
              <option value="tous">Tous les systèmes</option>
            </Select>
          </div>
        )}
      </div>

      {modeles.erreur && (
        <Alerte ton="danger" icone={OctagonAlert} role="alert" className="mt-4">
          {modeles.erreur}
        </Alerte>
      )}
      {modeles.donnees !== null && filtres.length === 0 && (
        <p className="mt-4 rounded-lg border border-dashed border-line-strong px-4 py-6 text-center text-sm text-fg-subtle">
          {modeles.donnees.length === 0
            ? 'La bibliothèque est vide : enregistrez une fiche comme modèle depuis sa page.'
            : 'Aucun modèle ne correspond.'}
        </p>
      )}
      {modeles.donnees === null && !modeles.erreur && (
        <EnChargement libelle="Chargement de la bibliothèque…" className="mt-4 space-y-2">
          {[0, 1, 2, 3].map((i) => (
            <div key={i} className="grid gap-2 rounded-lg border border-line px-3 py-2.5">
              <Squelette className="h-4 w-1/2" />
              <Squelette className="h-3 w-3/4" />
              <Squelette className="h-4 w-40" />
            </div>
          ))}
        </EnChargement>
      )}
      <ul
        className={cn('mt-4 max-h-[55vh] space-y-2 overflow-y-auto', modeles.donnees !== null && 'apparition')}
        aria-label="Modèles de recette"
      >
        {filtres.map((m) => (
          <li key={m.id} className="flex flex-wrap items-center gap-3 rounded-lg border border-line px-3 py-2.5">
            <div className="min-w-0 flex-1 basis-60">
              <p className="flex items-center gap-2 font-semibold">
                <Library className="size-4 shrink-0 text-accent" aria-hidden />
                <span className="truncate">{m.titre}</span>
              </p>
              <p className="mt-0.5 truncate text-xs text-fg-muted">{premiereLigne(m.etapes[0]?.instructions ?? '')}</p>
              <div className="mt-1.5 flex flex-wrap gap-2">
                <Badge>{noms.get(m.systemeCatalogueId) ?? 'Système'}</Badge>
                <Badge>
                  <ListOrdered className="size-3" aria-hidden />
                  {m.etapes.length} étape{m.etapes.length > 1 ? 's' : ''}
                </Badge>
              </div>
            </div>
            <Button
              taille="sm"
              variante="primaire"
              chargement={envoi === m.id}
              disabled={envoi !== null}
              aria-label={`Insérer ${m.titre}`}
              onClick={() => void inserer(m)}
            >
              Insérer
            </Button>
          </li>
        ))}
      </ul>
    </Dialog>
  );
}
