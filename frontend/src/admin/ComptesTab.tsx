import {
  KeyRound,
  Loader2,
  OctagonAlert,
  Pencil,
  Search,
  ShieldCheck,
  ShieldAlert,
  UserCheck,
  UserPlus,
  UserX,
  Users,
} from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { api } from '../api';
import { useAuth } from '../auth';
import { useAsync } from '../hooks';
import type { CompteAvecMotDePasse, CompteSaisie, Utilisateur } from '../types';
import { Alerte } from '../ui/Alerte';
import { BoutonIcone, Button } from '../ui/Button';
import { Card, CardHeader } from '../ui/Card';
import { Checkbox } from '../ui/Checkbox';
import { CodeBlock } from '../ui/CodeBlock';
import { ConfirmDialog, Dialog } from '../ui/Dialog';
import { EmptyState } from '../ui/EmptyState';
import { Field, Input } from '../ui/Input';
import { Badge, StatusPill } from '../ui/StatusPill';
import { useToast } from '../ui/Toast';
import { Tooltip } from '../ui/Tooltip';

const norm = (s: string) => s.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();

/** Boîte affichant une seule fois le mot de passe provisoire. */
function MotDePasseProvisoire({ compte }: { compte: CompteAvecMotDePasse }) {
  return (
    <div className="space-y-3">
      <Alerte ton="warn" icone={ShieldAlert}>
        Ce mot de passe provisoire n’est affiché qu’une seule fois. Transmettez-le à {compte.utilisateur.nomAffiche} par
        un canal sûr (en main propre ou par message chiffré, jamais par e-mail en clair) : il devra le changer dès sa
        première connexion.
      </Alerte>
      <CodeBlock titre="Mot de passe provisoire" code={compte.motDePasseProvisoire} hauteurMax="max-h-24" />
    </div>
  );
}

function CompteDialog({
  existant,
  open,
  onOpenChange,
  onFait,
}: {
  existant: Utilisateur | null;
  open: boolean;
  onOpenChange: (o: boolean) => void;
  onFait: (u: Utilisateur) => void;
}) {
  const toast = useToast();
  const [login, setLogin] = useState('');
  const [nomAffiche, setNomAffiche] = useState('');
  const [role, setRole] = useState('');
  const [contact, setContact] = useState('');
  const [estAdmin, setEstAdmin] = useState(false);
  const [erreur, setErreur] = useState('');
  const [envoi, setEnvoi] = useState(false);
  const [cree, setCree] = useState<CompteAvecMotDePasse | null>(null);

  useEffect(() => {
    if (open) {
      setLogin(existant?.login ?? '');
      setNomAffiche(existant?.nomAffiche ?? '');
      setRole(existant?.role ?? '');
      setContact(existant?.contact ?? '');
      setEstAdmin(existant?.estAdmin ?? false);
      setErreur('');
      setCree(null);
    }
  }, [open, existant]);

  const valide = nomAffiche.trim() !== '' && (existant !== null || login.trim() !== '');

  async function enregistrer() {
    setEnvoi(true);
    setErreur('');
    const saisie: CompteSaisie = {
      nomAffiche: nomAffiche.trim(),
      estAdmin,
      role: role.trim(),
      contact: contact.trim(),
    };
    try {
      if (existant) {
        const u = await api.adminModifierUtilisateur(existant.id, saisie);
        toast.succes('Compte modifié.');
        onFait(u);
        onOpenChange(false);
      } else {
        const r = await api.adminCreerUtilisateur({ login: login.trim(), ...saisie });
        setCree(r);
        onFait(r.utilisateur);
      }
    } catch (e) {
      setErreur(e instanceof Error ? e.message : 'Enregistrement impossible.');
    } finally {
      setEnvoi(false);
    }
  }

  if (cree) {
    return (
      <Dialog
        open={open}
        onOpenChange={onOpenChange}
        titre="Compte créé"
        description={`Le compte « ${cree.utilisateur.login} » est prêt.`}
        pied={
          <Button variante="primaire" onClick={() => onOpenChange(false)}>
            J’ai transmis le mot de passe
          </Button>
        }
      >
        <MotDePasseProvisoire compte={cree} />
      </Dialog>
    );
  }

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      titre={existant ? 'Modifier le compte' : 'Nouveau compte'}
      description={
        existant ? `Identifiant : ${existant.login}` : 'Un mot de passe provisoire sera généré à la création.'
      }
      pied={
        <>
          <Button variante="fantome" onClick={() => onOpenChange(false)}>
            Annuler
          </Button>
          <Button variante="primaire" chargement={envoi} disabled={!valide} onClick={() => void enregistrer()}>
            {existant ? 'Enregistrer' : 'Créer le compte'}
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
        {!existant && (
          <Field label="Identifiant *">
            {(id) => (
              <Input id={id} value={login} onChange={(e) => setLogin(e.target.value)} autoComplete="off" autoFocus />
            )}
          </Field>
        )}
        <Field label="Nom affiché *">
          {(id) => <Input id={id} value={nomAffiche} onChange={(e) => setNomAffiche(e.target.value)} />}
        </Field>
        <div className="grid gap-4 md:grid-cols-2">
          <Field label="Rôle" hint="ex. Pilote, DBA.">
            {(id) => <Input id={id} value={role} onChange={(e) => setRole(e.target.value)} />}
          </Field>
          <Field label="Contact" hint="e-mail ou téléphone.">
            {(id) => <Input id={id} value={contact} onChange={(e) => setContact(e.target.value)} />}
          </Field>
        </div>
        <Checkbox
          label="Administrateur"
          description="Accès à la console d’administration et aux paramètres globaux."
          checked={estAdmin}
          onChange={setEstAdmin}
        />
      </div>
    </Dialog>
  );
}

type Action = { type: 'desactiver' | 'reactiver' | 'reinitialiser'; compte: Utilisateur };

export default function ComptesTab() {
  const { utilisateur: moi, majUtilisateur } = useAuth();
  const toast = useToast();
  const { donnees, erreur, recharger } = useAsync(() => api.adminUtilisateurs(), []);
  const [recherche, setRecherche] = useState('');
  const [dialogue, setDialogue] = useState<{ existant: Utilisateur | null } | null>(null);
  const [action, setAction] = useState<Action | null>(null);
  const [erreurAction, setErreurAction] = useState('');
  const [reinit, setReinit] = useState<CompteAvecMotDePasse | null>(null);

  const comptes = useMemo(() => {
    const q = norm(recherche.trim());
    return (donnees ?? []).filter((u) => !q || norm(`${u.nomAffiche} ${u.login} ${u.role}`).includes(q));
  }, [donnees, recherche]);

  // Change l'état de session si l'administrateur modifie son propre compte.
  const synchroniserMoi = (u: Utilisateur) => {
    if (moi && u.id === moi.id) majUtilisateur(u);
  };

  async function confirmer(): Promise<boolean> {
    if (!action) return false;
    setErreurAction('');
    try {
      if (action.type === 'reinitialiser') {
        const r = await api.adminReinitialiserMotDePasse(action.compte.id);
        setReinit(r);
      } else {
        const u =
          action.type === 'desactiver'
            ? await api.adminDesactiver(action.compte.id)
            : await api.adminReactiver(action.compte.id);
        synchroniserMoi(u);
        toast.succes(action.type === 'desactiver' ? 'Compte désactivé.' : 'Compte réactivé.');
      }
      recharger();
      return true;
    } catch (e) {
      setErreurAction(e instanceof Error ? e.message : 'Action impossible.');
      return false;
    }
  }

  const fermerAction = (o: boolean) => {
    if (!o) {
      setAction(null);
      setErreurAction('');
    }
  };
  const nom = action?.compte.nomAffiche ?? '';

  return (
    <>
      <Card>
        <CardHeader
          titre="Comptes"
          description="Tous les comptes, y compris les comptes désactivés."
          action={
            <Button
              variante="primaire"
              taille="sm"
              icone={<UserPlus className="size-4" aria-hidden />}
              onClick={() => setDialogue({ existant: null })}
            >
              Nouveau compte
            </Button>
          }
        />
        <div className="border-b border-line px-5 py-3">
          <div className="relative max-w-sm">
            <Search
              className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-fg-subtle"
              aria-hidden
            />
            <Input
              aria-label="Rechercher un compte"
              placeholder="Rechercher par nom, identifiant ou rôle…"
              className="pl-9"
              value={recherche}
              onChange={(e) => setRecherche(e.target.value)}
            />
          </div>
        </div>
        {erreur && (
          <div className="p-5">
            <Alerte ton="danger" icone={OctagonAlert} role="alert">
              {erreur}
            </Alerte>
          </div>
        )}
        {!erreur && donnees === null && (
          <p className="flex items-center justify-center gap-2 px-5 py-10 text-sm text-fg-subtle" role="status">
            <Loader2 className="size-4 animate-spin" aria-hidden /> Chargement…
          </p>
        )}
        {donnees && comptes.length === 0 && (
          <EmptyState
            icone={Users}
            titre="Aucun compte"
            description={recherche ? 'Aucun compte ne correspond à la recherche.' : 'Créez le premier compte.'}
          />
        )}
        {comptes.length > 0 && (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="border-b border-line text-xs tracking-wide text-fg-subtle uppercase">
                <tr>
                  <th scope="col" className="px-5 py-2.5 font-medium">
                    Compte
                  </th>
                  <th scope="col" className="px-3 py-2.5 font-medium">
                    Rôle
                  </th>
                  <th scope="col" className="px-3 py-2.5 font-medium">
                    Administrateur
                  </th>
                  <th scope="col" className="px-3 py-2.5 font-medium">
                    État
                  </th>
                  <th scope="col" className="px-5 py-2.5 text-right font-medium">
                    Actions
                  </th>
                </tr>
              </thead>
              <tbody className="divide-y divide-line">
                {comptes.map((u) => (
                  <tr key={u.id} className={u.actif ? undefined : 'bg-surface-2/50 text-fg-muted'}>
                    <td className="px-5 py-3">
                      <div className="font-medium text-fg">{u.nomAffiche}</div>
                      <div className="font-mono text-xs text-fg-subtle">{u.login}</div>
                    </td>
                    <td className="px-3 py-3">{u.role || '—'}</td>
                    <td className="px-3 py-3">
                      {u.estAdmin ? (
                        <StatusPill ton="run" icone={ShieldCheck} taille="sm">
                          Admin
                        </StatusPill>
                      ) : (
                        <span className="text-fg-subtle">—</span>
                      )}
                    </td>
                    <td className="px-3 py-3">
                      <div className="flex flex-wrap gap-1.5">
                        {u.actif ? (
                          <StatusPill ton="ok" icone={UserCheck} taille="sm">
                            Actif
                          </StatusPill>
                        ) : (
                          <StatusPill ton="later" icone={UserX} taille="sm">
                            Désactivé
                          </StatusPill>
                        )}
                        {u.doitChangerMotDePasse && <Badge ton="warn">Mot de passe à changer</Badge>}
                      </div>
                    </td>
                    <td className="px-5 py-3">
                      <div className="flex justify-end gap-1">
                        <Tooltip content="Modifier">
                          <BoutonIcone label={`Modifier ${u.nomAffiche}`} onClick={() => setDialogue({ existant: u })}>
                            <Pencil className="size-4" aria-hidden />
                          </BoutonIcone>
                        </Tooltip>
                        <Tooltip content="Réinitialiser le mot de passe">
                          <BoutonIcone
                            label={`Réinitialiser le mot de passe de ${u.nomAffiche}`}
                            onClick={() => setAction({ type: 'reinitialiser', compte: u })}
                          >
                            <KeyRound className="size-4" aria-hidden />
                          </BoutonIcone>
                        </Tooltip>
                        {u.actif ? (
                          <Tooltip content="Désactiver">
                            <BoutonIcone
                              label={`Désactiver ${u.nomAffiche}`}
                              onClick={() => setAction({ type: 'desactiver', compte: u })}
                            >
                              <UserX className="size-4" aria-hidden />
                            </BoutonIcone>
                          </Tooltip>
                        ) : (
                          <Tooltip content="Réactiver">
                            <BoutonIcone
                              label={`Réactiver ${u.nomAffiche}`}
                              onClick={() => setAction({ type: 'reactiver', compte: u })}
                            >
                              <UserCheck className="size-4" aria-hidden />
                            </BoutonIcone>
                          </Tooltip>
                        )}
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      <CompteDialog
        existant={dialogue?.existant ?? null}
        open={dialogue !== null}
        onOpenChange={(o) => {
          if (!o) setDialogue(null);
        }}
        onFait={(u) => {
          synchroniserMoi(u);
          recharger();
        }}
      />

      <ConfirmDialog
        open={action !== null}
        onOpenChange={fermerAction}
        titre={
          action?.type === 'desactiver'
            ? `Désactiver ${nom} ?`
            : action?.type === 'reactiver'
              ? `Réactiver ${nom} ?`
              : `Réinitialiser le mot de passe de ${nom} ?`
        }
        description={
          action?.type === 'desactiver'
            ? 'Le compte ne pourra plus se connecter et ses sessions en cours seront coupées.'
            : action?.type === 'reactiver'
              ? 'Le compte pourra de nouveau se connecter.'
              : 'Un nouveau mot de passe provisoire sera généré. Les sessions en cours du compte seront coupées et il devra le changer à sa prochaine connexion.'
        }
        libelleConfirmer={
          action?.type === 'desactiver' ? 'Désactiver' : action?.type === 'reactiver' ? 'Réactiver' : 'Réinitialiser'
        }
        variante={action?.type === 'reactiver' ? 'primaire' : 'danger'}
        onConfirmer={confirmer}
      >
        {erreurAction && (
          <Alerte ton="danger" icone={OctagonAlert} role="alert">
            {erreurAction}
          </Alerte>
        )}
      </ConfirmDialog>

      <Dialog
        open={reinit !== null}
        onOpenChange={(o) => {
          if (!o) setReinit(null);
        }}
        titre="Mot de passe réinitialisé"
        description={reinit ? `Compte : ${reinit.utilisateur.login}` : undefined}
        pied={
          <Button variante="primaire" onClick={() => setReinit(null)}>
            J’ai transmis le mot de passe
          </Button>
        }
      >
        {reinit && <MotDePasseProvisoire compte={reinit} />}
      </Dialog>
    </>
  );
}
