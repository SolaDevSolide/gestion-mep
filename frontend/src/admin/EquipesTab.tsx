import { Loader2, OctagonAlert, Pencil, Plus, Trash2, UsersRound } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { api } from '../api';
import { useAsync } from '../hooks';
import { optionCompte } from '../personnes';
import type { Equipe, Utilisateur } from '../types';
import { Alerte } from '../ui/Alerte';
import { BoutonIcone, Button } from '../ui/Button';
import { Card, CardHeader } from '../ui/Card';
import { MultiSelect } from '../ui/Combobox';
import { ConfirmDialog, Dialog } from '../ui/Dialog';
import { EmptyState } from '../ui/EmptyState';
import { Field, Input, Textarea } from '../ui/Input';
import { Badge } from '../ui/StatusPill';
import { useToast } from '../ui/Toast';
import { Tooltip } from '../ui/Tooltip';

function EquipeDialog({
  existante,
  open,
  comptes,
  onOpenChange,
  onFait,
}: {
  existante: Equipe | null;
  open: boolean;
  comptes: Utilisateur[];
  onOpenChange: (o: boolean) => void;
  onFait: () => void;
}) {
  const toast = useToast();
  const [nom, setNom] = useState('');
  const [description, setDescription] = useState('');
  const [membres, setMembres] = useState<string[]>([]);
  const [erreur, setErreur] = useState('');
  const [envoi, setEnvoi] = useState(false);

  useEffect(() => {
    if (open) {
      setNom(existante?.nom ?? '');
      setDescription(existante?.description ?? '');
      setMembres(existante?.membreIds ?? []);
      setErreur('');
    }
  }, [open, existante]);

  // Comptes désactivés : proposés seulement s'ils sont déjà membres (une personne partie reste dans l'historique de l'équipe).
  const options = comptes.filter((u) => u.actif || membres.includes(u.id)).map(optionCompte);

  async function enregistrer() {
    setEnvoi(true);
    setErreur('');
    const saisie = { nom: nom.trim(), description: description.trim(), membreIds: membres };
    try {
      if (existante) await api.adminModifierEquipe(existante.id, saisie);
      else await api.adminCreerEquipe(saisie);
      toast.succes(existante ? 'Équipe modifiée.' : 'Équipe créée.');
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
      titre={existante ? 'Modifier l’équipe' : 'Nouvelle équipe'}
      description="Une personne peut appartenir à plusieurs équipes."
      pied={
        <>
          <Button variante="fantome" onClick={() => onOpenChange(false)}>
            Annuler
          </Button>
          <Button variante="primaire" chargement={envoi} disabled={!nom.trim()} onClick={() => void enregistrer()}>
            {existante ? 'Enregistrer' : 'Créer l’équipe'}
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
          {(id) => <Input id={id} value={nom} onChange={(e) => setNom(e.target.value)} autoFocus />}
        </Field>
        <Field label="Description">
          {(id) => <Textarea id={id} value={description} onChange={(e) => setDescription(e.target.value)} rows={2} />}
        </Field>
        <Field label="Membres" hint="Choisis parmi les comptes actifs.">
          {(id) => (
            <MultiSelect
              id={id}
              options={options}
              valeurs={membres}
              onChange={setMembres}
              placeholder="Aucun membre"
              placeholderRecherche="Rechercher un compte…"
              vide="Aucun compte."
              libelleInconnu="Compte inconnu"
            />
          )}
        </Field>
      </div>
    </Dialog>
  );
}

/** Onglet « Équipes » de la console : créer, modifier et supprimer les équipes, choisir leurs membres. */
export default function EquipesTab() {
  const toast = useToast();
  const { donnees: equipes, erreur, recharger } = useAsync(() => api.equipes(), []);
  const { donnees: comptes } = useAsync(() => api.adminUtilisateurs(), []);
  const [dialogue, setDialogue] = useState<{ existante: Equipe | null } | null>(null);
  const [suppr, setSuppr] = useState<Equipe | null>(null);
  const [erreurSuppr, setErreurSuppr] = useState('');
  const parId = useMemo(() => new Map((comptes ?? []).map((u) => [u.id, u])), [comptes]);

  async function supprimer(): Promise<boolean> {
    if (!suppr) return false;
    setErreurSuppr('');
    try {
      await api.adminSupprimerEquipe(suppr.id);
      toast.succes('Équipe supprimée.');
      recharger();
      return true;
    } catch (e) {
      setErreurSuppr(e instanceof Error ? e.message : 'Suppression impossible.');
      return false;
    }
  }

  return (
    <>
      <Card>
        <CardHeader
          titre="Équipes"
          description="Regroupez les personnes pour filtrer les comptes et les MEP par équipe."
          action={
            <Button
              variante="primaire"
              taille="sm"
              icone={<Plus className="size-4" aria-hidden />}
              onClick={() => setDialogue({ existante: null })}
            >
              Nouvelle équipe
            </Button>
          }
        />
        {erreur && (
          <div className="p-5">
            <Alerte ton="danger" icone={OctagonAlert} role="alert">
              {erreur}
            </Alerte>
          </div>
        )}
        {!erreur && equipes === null && (
          <p className="flex items-center justify-center gap-2 px-5 py-10 text-sm text-fg-subtle" role="status">
            <Loader2 className="size-4 animate-spin" aria-hidden /> Chargement…
          </p>
        )}
        {equipes && equipes.length === 0 && (
          <EmptyState
            icone={UsersRound}
            titre="Aucune équipe"
            description="Créez une équipe, puis choisissez ses membres."
          />
        )}
        {equipes && equipes.length > 0 && (
          <ul className="divide-y divide-line">
            {equipes.map((e) => (
              <li key={e.id} className="flex flex-wrap items-start gap-x-4 gap-y-2 px-5 py-3">
                <div className="min-w-0 flex-1 basis-60">
                  <p className="font-medium">{e.nom}</p>
                  {e.description && <p className="text-sm text-fg-muted">{e.description}</p>}
                  <div className="mt-1.5 flex flex-wrap gap-1.5">
                    {e.membreIds.length === 0 && <span className="text-xs text-fg-subtle">Aucun membre</span>}
                    {e.membreIds.map((id) => {
                      const u = parId.get(id);
                      return (
                        <Badge key={id} ton={u && !u.actif ? 'later' : undefined}>
                          {u ? u.nomPersonne : 'Compte inconnu'}
                          {u && u.comptePrincipalId && <span className="font-mono text-fg-subtle"> ({u.login})</span>}
                          {u && !u.actif && ' · désactivé'}
                        </Badge>
                      );
                    })}
                  </div>
                </div>
                <div className="flex shrink-0 items-center gap-1">
                  <span className="mr-2 text-xs text-fg-subtle tnum">
                    {e.membreIds.length} membre{e.membreIds.length > 1 ? 's' : ''}
                  </span>
                  <Tooltip content="Modifier">
                    <BoutonIcone label={`Modifier ${e.nom}`} onClick={() => setDialogue({ existante: e })}>
                      <Pencil className="size-4" aria-hidden />
                    </BoutonIcone>
                  </Tooltip>
                  <Tooltip content="Supprimer">
                    <BoutonIcone label={`Supprimer ${e.nom}`} onClick={() => setSuppr(e)}>
                      <Trash2 className="size-4" aria-hidden />
                    </BoutonIcone>
                  </Tooltip>
                </div>
              </li>
            ))}
          </ul>
        )}
      </Card>

      <EquipeDialog
        existante={dialogue?.existante ?? null}
        open={dialogue !== null}
        comptes={comptes ?? []}
        onOpenChange={(o) => {
          if (!o) setDialogue(null);
        }}
        onFait={recharger}
      />

      <ConfirmDialog
        open={suppr !== null}
        onOpenChange={(o) => {
          if (!o) {
            setSuppr(null);
            setErreurSuppr('');
          }
        }}
        titre={`Supprimer l’équipe ${suppr?.nom ?? ''} ?`}
        description="Les comptes, leurs données et les MEP ne changent pas : seul le regroupement disparaît."
        libelleConfirmer="Supprimer"
        variante="danger"
        onConfirmer={supprimer}
      >
        {erreurSuppr && (
          <Alerte ton="danger" icone={OctagonAlert} role="alert">
            {erreurSuppr}
          </Alerte>
        )}
      </ConfirmDialog>
    </>
  );
}
