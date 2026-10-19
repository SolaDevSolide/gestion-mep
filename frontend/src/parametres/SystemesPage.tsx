import { CheckCircle2, CircleOff, Layers, ListTree, Lock, Pencil, Plus, Trash2 } from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api';
import { useAuth } from '../auth';
import { useAsync } from '../hooks';
import { formatDate } from '../format';
import { lienHistoriqueSysteme } from '../mep/SystemeCliquable';
import { metaEtatMep } from '../statuts';
import type { ActiviteSysteme, SystemeCatalogue } from '../types';
import { ErreurChargement } from '../ui/ErreurChargement';
import { BoutonIcone, Button } from '../ui/Button';
import { Card } from '../ui/Card';
import { ConfirmDialog } from '../ui/Dialog';
import { EmptyState } from '../ui/EmptyState';
import { PastilleEnvironnement } from '../ui/PastilleEnvironnement';
import { StatusPill } from '../ui/StatusPill';
import { Tooltip } from '../ui/Tooltip';
import { useToast } from '../ui/Toast';
import { CategoriesDefautDialog } from './CategoriesDefautDialog';
import { cn } from '../ui/cn';
import { TON } from '../ui/tons';
import { Squelette, SqueletteRangees } from '../ui/Squelette';
import { SystemeDialog } from './SystemeDialog';

/** Dernière MEP d'un système (#59) : son jour, son outil et son état ; un lien vers l'historique du système. */
function DerniereMep({ a, systeme }: { a: ActiviteSysteme | undefined; systeme: SystemeCatalogue }) {
  if (!a?.derniereMepId || !a.derniereJour || !a.derniereEtat)
    return <span className="text-fg-subtle">Aucune MEP</span>;
  const meta = metaEtatMep(a.derniereEtat);
  return (
    <Link
      to={lienHistoriqueSysteme(systeme.id)}
      className="group inline-flex flex-col hover:text-accent-text"
      aria-label={`Historique des MEP de ${systeme.nom} : dernière le ${formatDate(a.derniereJour)}, ${a.derniereOutil ?? ''}, ${meta.libelle}`}
    >
      <span className="inline-flex items-center gap-1.5 whitespace-nowrap tnum group-hover:underline">
        <span className={cn('size-2 shrink-0 rounded-full', TON[meta.ton].bgSolide)} title={meta.libelle} aria-hidden />
        {formatDate(a.derniereJour)}
      </span>
      <span className="max-w-56 truncate text-xs text-fg-subtle">
        {a.derniereOutil}
        {a.derniereVersion?.trim() && ` → ${a.derniereVersion}`} · {a.nbMep} MEP
      </span>
    </Link>
  );
}

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
  const { donnees, erreur, echec, chargement, recharger } = useAsync(() => api.catalogueSystemes(), []);
  // Activité des systèmes : colonne « Dernière MEP » ; en erreur, le catalogue reste consultable et la colonne le dit.
  const lue = useAsync(async () => new Map((await api.activiteSystemes()).map((a) => [a.systemeId, a])), []);
  const activite = lue.donnees;
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

      {echec !== null && (
        <ErreurChargement
          className="mt-6"
          erreur={echec}
          titre="Impossible de charger le catalogue des systèmes"
          onReessayer={recharger}
          enCours={chargement}
        />
      )}
      {lue.echec !== null && donnees !== null && (
        <ErreurChargement
          variante="ligne"
          className="mt-6"
          erreur={lue.echec}
          titre="Colonne « Dernière MEP » non chargée"
          onReessayer={lue.recharger}
          enCours={lue.chargement}
        />
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
                  <th scope="col" className="px-4 py-3 font-semibold whitespace-nowrap">
                    Dernière MEP
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
              <tbody
                className={cn('divide-y divide-line', donnees !== null && 'apparition')}
                aria-busy={(donnees === null && !erreur) || undefined}
                aria-label={donnees === null && !erreur ? 'Chargement du catalogue…' : undefined}
              >
                {donnees === null && !erreur && <SqueletteRangees lignes={6} colonnes={admin ? 8 : 7} />}
                {(donnees ?? []).map((s) => (
                  <tr key={s.id} className="transition-colors duration-150 hover:bg-surface-2/50">
                    <th scope="row" className="px-4 py-3 font-medium">
                      <Link to={`/parametres/systemes/${s.id}`} className="hover:text-accent-text hover:underline">
                        {s.nom}
                      </Link>
                    </th>
                    <td className="max-w-xs px-4 py-3 text-fg-muted">{s.description || '—'}</td>
                    <td className="px-4 py-3 font-mono text-ui text-fg-muted">{s.cible || '—'}</td>
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
                      {activite ? (
                        <DerniereMep a={activite.get(s.id)} systeme={s} />
                      ) : lue.echec === null ? (
                        <Squelette className="h-4 w-20" />
                      ) : null}
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
