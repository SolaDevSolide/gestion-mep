import { Layers, Lock, OctagonAlert, Pencil, Plus, Trash2 } from 'lucide-react';
import { useMemo, useState } from 'react';
import { api } from '../api';
import { useAuth } from '../auth';
import { useAsync } from '../hooks';
import type { SystemeCatalogue } from '../types';
import { Alerte } from '../ui/Alerte';
import { BoutonIcone, Button } from '../ui/Button';
import { Card } from '../ui/Card';
import { Checkbox } from '../ui/Checkbox';
import { ConfirmDialog, Dialog } from '../ui/Dialog';
import { EmptyState } from '../ui/EmptyState';
import { Field, Input, Textarea } from '../ui/Input';
import { StatusPill } from '../ui/StatusPill';
import { Tooltip } from '../ui/Tooltip';
import { useToast } from '../ui/Toast';
import { CheckCircle2, CircleOff } from 'lucide-react';

const VIDE = { nom: '', description: '', cible: '', actif: true };

function SystemeDialog({
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

export default function SystemesPage() {
  const { utilisateur } = useAuth();
  const toast = useToast();
  const admin = utilisateur?.estAdmin ?? false;
  const { donnees, erreur, recharger } = useAsync(() => api.catalogueSystemes(), []);
  const [edition, setEdition] = useState<{ existant: SystemeCatalogue | null } | null>(null);
  const [suppr, setSuppr] = useState<SystemeCatalogue | null>(null);

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
          <Button
            variante="primaire"
            icone={<Plus className="size-4" aria-hidden />}
            onClick={() => setEdition({ existant: null })}
          >
            Nouveau système
          </Button>
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
                      {s.nom}
                    </th>
                    <td className="max-w-xs px-4 py-3 text-fg-muted">{s.description || '—'}</td>
                    <td className="px-4 py-3 font-mono text-[13px] text-fg-muted">{s.cible || '—'}</td>
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
