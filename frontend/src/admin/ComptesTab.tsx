import {
  CornerDownRight,
  KeyRound,
  Link2,
  OctagonAlert,
  Pencil,
  Search,
  ShieldCheck,
  ShieldAlert,
  UserCheck,
  UserPlus,
  UserX,
  Unlink,
  Users,
} from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { api } from '../api';
import { useAuth } from '../auth';
import { useAsync } from '../hooks';
import { comptesDuGroupe, filtrerParEquipe, groupeCorrespond, grouperParPersonne } from '../personnes';
import type { CompteAvecMotDePasse, CompteSaisie, Equipe, Utilisateur } from '../types';
import { Alerte } from '../ui/Alerte';
import { BoutonIcone, Button } from '../ui/Button';
import { Card, CardHeader } from '../ui/Card';
import { Checkbox } from '../ui/Checkbox';
import { CodeBlock } from '../ui/CodeBlock';
import { ConfirmDialog, Dialog } from '../ui/Dialog';
import { EmptyState } from '../ui/EmptyState';
import { Field, Input, Select } from '../ui/Input';
import { Badge, StatusPill } from '../ui/StatusPill';
import { useToast } from '../ui/Toast';
import { EnChargement, SqueletteTableau } from '../ui/Squelette';
import { Tooltip } from '../ui/Tooltip';

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

/**
 * Relie un autre compte de la même personne au compte principal choisi dans la liste : la personne s'affiche sous le nom
 * du compte principal ; rien n'est fusionné ni supprimé et la liaison peut être défaite.
 */
function RelierDialog({
  principal,
  comptes,
  moiId,
  onOpenChange,
  onFait,
}: {
  principal: Utilisateur | null;
  comptes: Utilisateur[];
  moiId: string | undefined;
  onOpenChange: (o: boolean) => void;
  onFait: (u: Utilisateur) => void;
}) {
  const toast = useToast();
  const [compteId, setCompteId] = useState('');
  const [desactiver, setDesactiver] = useState(false);
  const [erreur, setErreur] = useState('');
  const [envoi, setEnvoi] = useState(false);

  useEffect(() => {
    if (principal) {
      setCompteId('');
      setDesactiver(false);
      setErreur('');
    }
  }, [principal]);

  // Un seul niveau de liaison : ni un compte déjà relié, ni le compte principal d'autres comptes.
  const candidats = comptes.filter(
    (u) => u.id !== principal?.id && u.comptePrincipalId === null && !comptes.some((x) => x.comptePrincipalId === u.id),
  );
  const choisi = candidats.find((u) => u.id === compteId);

  async function relier() {
    if (!principal || !choisi) return;
    setEnvoi(true);
    setErreur('');
    try {
      const u = await api.adminRelier(choisi.id, { comptePrincipalId: principal.id, desactiver });
      toast.succes('Comptes reliés.');
      onFait(u);
      onOpenChange(false);
    } catch (e) {
      setErreur(e instanceof Error ? e.message : 'Liaison impossible.');
    } finally {
      setEnvoi(false);
    }
  }

  return (
    <Dialog
      open={principal !== null}
      onOpenChange={onOpenChange}
      titre={`Relier un compte à ${principal?.nomAffiche ?? ''}`}
      description={`Pour une personne qui a plusieurs comptes. Les deux comptes s’affichent sous le nom « ${principal?.nomAffiche ?? ''} » ; chacun garde son historique, rien n’est supprimé et la liaison peut être défaite.`}
      pied={
        <>
          <Button variante="fantome" onClick={() => onOpenChange(false)}>
            Annuler
          </Button>
          <Button variante="primaire" chargement={envoi} disabled={!choisi} onClick={() => void relier()}>
            Relier
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
        <Field label="Autre compte de la même personne">
          {(id) => (
            <Select id={id} value={compteId} onChange={(e) => setCompteId(e.target.value)}>
              <option value="">— choisir un compte —</option>
              {candidats.map((u) => (
                <option key={u.id} value={u.id}>
                  {u.nomAffiche} ({u.login}){u.actif ? '' : ' — désactivé'}
                </option>
              ))}
            </Select>
          )}
        </Field>
        <Checkbox
          label="Désactiver ce compte"
          description={
            choisi?.id === moiId
              ? 'Impossible pour votre propre compte.'
              : 'Il ne pourra plus se connecter ; son historique reste visible. Sinon, il reste utilisable.'
          }
          checked={desactiver}
          disabled={!choisi || !choisi.actif || choisi.id === moiId}
          onChange={setDesactiver}
        />
      </div>
    </Dialog>
  );
}

type Action = { type: 'desactiver' | 'reactiver' | 'reinitialiser' | 'delier'; compte: Utilisateur };

export default function ComptesTab() {
  const { utilisateur: moi, majUtilisateur } = useAuth();
  const toast = useToast();
  const { donnees, erreur, recharger } = useAsync(() => api.adminUtilisateurs(), []);
  const { donnees: equipes } = useAsync(() => api.equipes(), []);
  const [recherche, setRecherche] = useState('');
  const [equipe, setEquipe] = useState('');
  const [relierA, setRelierA] = useState<Utilisateur | null>(null);
  const [dialogue, setDialogue] = useState<{ existant: Utilisateur | null } | null>(null);
  const [action, setAction] = useState<Action | null>(null);
  const [erreurAction, setErreurAction] = useState('');
  const [reinit, setReinit] = useState<CompteAvecMotDePasse | null>(null);

  // Une personne = un compte principal et ses comptes reliés, affichés ensemble ; la recherche et le filtre par équipe
  // portent sur la personne entière.
  const comptes = useMemo(
    () =>
      grouperParPersonne(filtrerParEquipe(donnees ?? [], equipe))
        .filter((g) => groupeCorrespond(g, recherche))
        .flatMap(comptesDuGroupe),
    [donnees, recherche, equipe],
  );
  const parId = useMemo(() => new Map((donnees ?? []).map((u) => [u.id, u])), [donnees]);
  const nomsEquipes = useMemo(() => new Map((equipes ?? []).map((e: Equipe) => [e.id, e.nom])), [equipes]);

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
      } else if (action.type === 'delier') {
        synchroniserMoi(await api.adminDelier(action.compte.id));
        toast.succes('Liaison défaite.');
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
          description="Tous les comptes, y compris les comptes désactivés. Les comptes reliés d’une même personne sont regroupés sous son compte principal."
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
        <div className="flex flex-wrap items-center gap-3 border-b border-line px-5 py-3">
          <div className="relative max-w-sm min-w-60 flex-1">
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
          {equipes && equipes.length > 0 && (
            <div className="w-56">
              <Select aria-label="Filtrer par équipe" value={equipe} onChange={(e) => setEquipe(e.target.value)}>
                <option value="">Toutes les équipes</option>
                {equipes.map((e) => (
                  <option key={e.id} value={e.id}>
                    {e.nom}
                  </option>
                ))}
              </Select>
            </div>
          )}
        </div>
        {erreur && (
          <div className="p-5">
            <Alerte ton="danger" icone={OctagonAlert} role="alert">
              {erreur}
            </Alerte>
          </div>
        )}
        {!erreur && donnees === null && (
          <EnChargement libelle="Chargement des comptes…">
            <SqueletteTableau lignes={4} colonnes={5} className="rounded-none border-0" />
          </EnChargement>
        )}
        {donnees && comptes.length === 0 && (
          <EmptyState
            icone={Users}
            titre="Aucun compte"
            description={
              recherche || equipe ? 'Aucun compte ne correspond à la recherche.' : 'Créez le premier compte.'
            }
          />
        )}
        {comptes.length > 0 && (
          <div className="apparition overflow-x-auto">
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
                    Équipes
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
                {comptes.map((u) => {
                  const principal = u.comptePrincipalId ? parId.get(u.comptePrincipalId) : undefined;
                  return (
                    <tr key={u.id} className={u.actif ? undefined : 'bg-surface-2/50 text-fg-muted'}>
                      <td className={principal ? 'py-3 pr-5 pl-10' : 'px-5 py-3'}>
                        <div className="flex items-start gap-1.5">
                          {principal && (
                            <CornerDownRight className="mt-0.5 size-4 shrink-0 text-fg-subtle" aria-hidden />
                          )}
                          <div className="min-w-0">
                            <div className="font-medium text-fg">{u.nomAffiche}</div>
                            <div className="font-mono text-xs text-fg-subtle">{u.login}</div>
                            {principal && (
                              <div className="text-xs text-fg-subtle">
                                Relié à {principal.nomAffiche} : affiché sous ce nom
                              </div>
                            )}
                          </div>
                        </div>
                      </td>
                      <td className="px-3 py-3">{u.role || '—'}</td>
                      <td className="px-3 py-3">
                        {u.equipeIds.length === 0 ? (
                          <span className="text-fg-subtle">—</span>
                        ) : (
                          <div className="flex flex-wrap gap-1">
                            {u.equipeIds.map((id) => (
                              <Badge key={id}>{nomsEquipes.get(id) ?? 'Équipe'}</Badge>
                            ))}
                          </div>
                        )}
                      </td>
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
                            <BoutonIcone
                              label={`Modifier ${u.nomAffiche}`}
                              onClick={() => setDialogue({ existant: u })}
                            >
                              <Pencil className="size-4" aria-hidden />
                            </BoutonIcone>
                          </Tooltip>
                          {principal ? (
                            <Tooltip content="Défaire la liaison">
                              <BoutonIcone
                                label={`Défaire la liaison de ${u.nomAffiche}`}
                                onClick={() => setAction({ type: 'delier', compte: u })}
                              >
                                <Unlink className="size-4" aria-hidden />
                              </BoutonIcone>
                            </Tooltip>
                          ) : (
                            <Tooltip content="Relier un autre compte de la même personne">
                              <BoutonIcone label={`Relier un compte à ${u.nomAffiche}`} onClick={() => setRelierA(u)}>
                                <Link2 className="size-4" aria-hidden />
                              </BoutonIcone>
                            </Tooltip>
                          )}
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
                  );
                })}
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

      <RelierDialog
        principal={relierA}
        comptes={donnees ?? []}
        moiId={moi?.id}
        onOpenChange={(o) => {
          if (!o) setRelierA(null);
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
              : action?.type === 'delier'
                ? `Défaire la liaison de ${nom} ?`
                : `Réinitialiser le mot de passe de ${nom} ?`
        }
        description={
          action?.type === 'desactiver'
            ? 'Le compte ne pourra plus se connecter et ses sessions en cours seront coupées.'
            : action?.type === 'reactiver'
              ? 'Le compte pourra de nouveau se connecter.'
              : action?.type === 'delier'
                ? `Le compte ${action.compte.login} s’affichera de nouveau sous son propre nom. Son état, ses équipes et son historique ne changent pas.`
                : 'Un nouveau mot de passe provisoire sera généré. Les sessions en cours du compte seront coupées et il devra le changer à sa prochaine connexion.'
        }
        libelleConfirmer={
          action?.type === 'desactiver'
            ? 'Désactiver'
            : action?.type === 'reactiver'
              ? 'Réactiver'
              : action?.type === 'delier'
                ? 'Défaire la liaison'
                : 'Réinitialiser'
        }
        variante={action?.type === 'reactiver' || action?.type === 'delier' ? 'primaire' : 'danger'}
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
