import { CheckCircle2, CircleOff, Layers, ListTree, Lock, OctagonAlert, Pencil, Plus, Trash2 } from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api';
import { useAuth } from '../auth';
import { useAsync } from '../hooks';
import type { SystemeCatalogue } from '../types';
import { Alerte } from '../ui/Alerte';
import { BoutonIcone, Button } from '../ui/Button';
import { Card } from '../ui/Card';
import { ConfirmDialog } from '../ui/Dialog';
import { EmptyState } from '../ui/EmptyState';
import { PastilleEnvironnement } from '../ui/PastilleEnvironnement';
import { StatusPill } from '../ui/StatusPill';
import { Tooltip } from '../ui/Tooltip';
import { useToast } from '../ui/Toast';
import { CategoriesDefautDialog } from './CategoriesDefautDialog';
import { SystemeDialog } from './SystemeDialog';

/** « 25 dans 3 catégories » : objets connus de la fiche. */
function resumeFiche(s: Pick<SystemeCatalogue, 'nbObjets' | 'categories'>): string {
  const c = s.categories.length;
  if (c === 0) return 'Aucune catégorie';
  return `${s.nbObjets} dans ${c} catégorie${c > 1 ? 's' : ''}`;
}

export default function SystemesPage() {
  const { utilisateur } = useAuth();
  const toast = useToast();
  const admin = utilisateur?.estAdmin ?? false;
  const { donnees, erreur, recharger } = useAsync(() => api.catalogueSystemes(), []);
  const [edition, setEdition] = useState<{ existant: SystemeCatalogue | null } | null>(null);
  const [suppr, setSuppr] = useState<SystemeCatalogue | null>(null);
  const [categories, setCategories] = useState(false);

  return (
    <main className="mx-auto w-full max-w-6xl px-6 py-8">
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Catalogue des systèmes</h1>
          <p className="mt-1 text-sm text-fg-subtle">
            Les systèmes réutilisables d’une MEP à l’autre.{' '}
            {!admin && (
              <span className="inline-flex items-center gap-1">
                <Lock className="size-3.5" aria-hidden />
                Lecture seule : seuls les administrateurs peuvent modifier le catalogue.
              </span>
            )}
          </p>
        </div>
        {admin && (
          <div className="flex flex-wrap gap-2">
            <Button icone={<ListTree className="size-4" aria-hidden />} onClick={() => setCategories(true)}>
              Catégories par défaut
            </Button>
            <Button
              variante="primaire"
              icone={<Plus className="size-4" aria-hidden />}
              onClick={() => setEdition({ existant: null })}
            >
              Nouveau système
            </Button>
          </div>
        )}
      </div>

      {erreur && (
        <Alerte ton="danger" icone={OctagonAlert} role="alert" className="mt-6">
          {erreur}
        </Alerte>
      )}

      <Card className="mt-6 overflow-hidden">
        {donnees !== null && donnees.length === 0 ? (
          <EmptyState
            icone={Layers}
            titre="Le catalogue est vide"
            description="Ajoutez les systèmes que vous touchez régulièrement."
            action={
              admin ? (
                <Button
                  variante="primaire"
                  icone={<Plus className="size-4" aria-hidden />}
                  onClick={() => setEdition({ existant: null })}
                >
                  Nouveau système
                </Button>
              ) : undefined
            }
          />
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="border-b border-line bg-surface-2/60 text-xs tracking-wide text-fg-subtle uppercase">
                <tr>
                  <th scope="col" className="px-4 py-3 font-semibold">
                    Nom
                  </th>
                  <th scope="col" className="px-4 py-3 font-semibold">
                    Description
                  </th>
                  <th scope="col" className="px-4 py-3 font-semibold">
                    Cible
                  </th>
                  <th scope="col" className="px-4 py-3 font-semibold">
                    Environnements
                  </th>
                  <th scope="col" className="px-4 py-3 font-semibold">
                    Objets
                  </th>
                  <th scope="col" className="px-4 py-3 font-semibold">
                    État
                  </th>
                  {admin && (
                    <th scope="col" className="px-4 py-3">
                      <span className="sr-only">Actions</span>
                    </th>
                  )}
                </tr>
              </thead>
              <tbody className="divide-y divide-line">
                {(donnees ?? []).map((s) => (
                  <tr key={s.id} className="transition-colors duration-150 hover:bg-surface-2/50">
                    <th scope="row" className="px-4 py-3 font-medium">
                      <Link to={`/parametres/systemes/${s.id}`} className="hover:text-accent-text hover:underline">
                        {s.nom}
                      </Link>
                    </th>
                    <td className="max-w-xs px-4 py-3 text-fg-muted">{s.description || '—'}</td>
                    <td className="px-4 py-3 font-mono text-[13px] text-fg-muted">{s.cible || '—'}</td>
                    <td className="px-4 py-3">
                      {s.environnements.length === 0 ? (
                        <span className="text-fg-subtle">—</span>
                      ) : (
                        <span className="flex max-w-64 flex-wrap gap-1">
                          {s.environnements.map((e) => (
                            <PastilleEnvironnement key={e.nom} nom={e.nom} production={e.production} />
                          ))}
                        </span>
                      )}
                    </td>
                    <td className="px-4 py-3 whitespace-nowrap text-fg-muted">
                      <Link to={`/parametres/systemes/${s.id}`} className="hover:text-accent-text hover:underline">
                        {resumeFiche(s)}
                      </Link>
                    </td>
                    <td className="px-4 py-3">
                      <StatusPill taille="sm" ton={s.actif ? 'ok' : 'wait'} icone={s.actif ? CheckCircle2 : CircleOff}>
                        {s.actif ? 'Actif' : 'Inactif'}
                      </StatusPill>
                    </td>
                    {admin && (
                      <td className="px-4 py-3 text-right whitespace-nowrap">
                        <Tooltip content="Modifier">
                          <BoutonIcone
                            label={`Modifier ${s.nom}`}
                            taille="sm"
                            onClick={() => setEdition({ existant: s })}
                          >
                            <Pencil className="size-4" aria-hidden />
                          </BoutonIcone>
                        </Tooltip>
                        <Tooltip content="Supprimer">
                          <BoutonIcone label={`Supprimer ${s.nom}`} taille="sm" onClick={() => setSuppr(s)}>
                            <Trash2 className="size-4" aria-hidden />
                          </BoutonIcone>
                        </Tooltip>
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      <CategoriesDefautDialog open={categories} onOpenChange={setCategories} />
      <SystemeDialog
        existant={edition?.existant ?? null}
        open={edition !== null}
        onOpenChange={(o) => {
          if (!o) setEdition(null);
        }}
        onFait={recharger}
      />
      <ConfirmDialog
        open={suppr !== null}
        onOpenChange={(o) => {
          if (!o) setSuppr(null);
        }}
        titre={`Supprimer ${suppr?.nom ?? ''} ?`}
        description="S’il est utilisé par une MEP, il sera seulement désactivé."
        libelleConfirmer="Supprimer"
        variante="danger"
        onConfirmer={async () => {
          if (!suppr) return false;
          try {
            await api.supprimerSystemeCatalogue(suppr.id);
            toast.succes('Système supprimé ou désactivé.');
            recharger();
            return true;
          } catch (e) {
            toast.erreur(e instanceof Error ? e.message : 'Suppression impossible.');
            return false;
          }
        }}
      />
    </main>
  );
}
