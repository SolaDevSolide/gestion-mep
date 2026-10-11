import {
  CircleCheck,
  CircleX,
  Copy,
  Link2Off,
  Loader2,
  OctagonAlert,
  Power,
  PowerOff,
  Save,
  ShieldCheck,
  TriangleAlert,
  Users,
} from 'lucide-react';
import { useState } from 'react';
import { api } from '../api';
import { formatDate, formatDateHeure } from '../format';
import { useAsync, useDraft, useNow, useReportDirty } from '../hooks';
import type { FournisseurSso, SsoIdentite, SsoReglages, SsoTest } from '../types';
import { Alerte } from '../ui/Alerte';
import { BoutonIcone, Button } from '../ui/Button';
import { Card, CardHeader } from '../ui/Card';
import { Checkbox } from '../ui/Checkbox';
import { CodeBlock } from '../ui/CodeBlock';
import { ConfirmDialog } from '../ui/Dialog';
import { EmptyState } from '../ui/EmptyState';
import { Field, Input } from '../ui/Input';
import { Segmented } from '../ui/Segmented';
import { StatusPill } from '../ui/StatusPill';
import { useToast } from '../ui/Toast';

const JOUR_MS = 86_400_000;
/** Délai avant l'expiration du secret à partir duquel on prévient. */
const PREAVIS_JOURS = 30;

const COMMANDES = `docker compose exec app gestion-mep sso afficher
docker compose exec -T app gestion-mep sso configurer --annuaire <ID annuaire> --client <ID application> --secret-stdin < secret.txt
docker compose exec app gestion-mep sso tester
docker compose exec app gestion-mep sso activer`;

interface Brouillon {
  fournisseur: FournisseurSso;
  annuaire: string;
  autorite: string;
  clientId: string;
  secret: string;
  secretExpireLe: string;
  creationAutomatique: boolean;
  roleAdmin: string;
  libelle: string;
}

const depuis = (r: SsoReglages): Brouillon => ({
  fournisseur: r.fournisseur,
  annuaire: r.annuaire,
  autorite: r.autorite,
  clientId: r.clientId,
  secret: '',
  secretExpireLe: r.secretExpireLe ?? '',
  creationAutomatique: r.creationAutomatique,
  roleAdmin: r.roleAdmin,
  libelle: r.libelle,
});

/** Jours restants avant la fin de la journée d'expiration (négatif si elle est passée). */
function joursRestants(expireLe: string, maintenant: number): number {
  const [a, m, j] = expireLe.split('-').map(Number);
  return Math.floor((new Date(a, m - 1, j + 1).getTime() - maintenant) / JOUR_MS);
}

/** Console : réglages de la connexion unique (Microsoft Entra ID ou OpenID Connect) et comptes liés. */
export default function SsoTab() {
  const toast = useToast();
  const { donnees, erreur, setDonnees } = useAsync(() => api.ssoReglages(), []);
  const identites = useAsync(() => api.ssoIdentites(), []);
  const maintenant = useNow(60_000);
  const d = useDraft<Brouillon | null>(donnees ? depuis(donnees) : null);
  useReportDirty(d.dirty);
  const [envoi, setEnvoi] = useState(false);
  const [test, setTest] = useState<SsoTest | null>(null);
  const [testEnCours, setTestEnCours] = useState(false);
  const [erreurAction, setErreurAction] = useState('');
  const [desactiver, setDesactiver] = useState(false);
  const [aDelier, setADelier] = useState<SsoIdentite | null>(null);

  if (erreur) {
    return (
      <Alerte ton="danger" icone={OctagonAlert} role="alert">
        {erreur}
      </Alerte>
    );
  }
  const v = d.value;
  if (!donnees || !v) {
    return (
      <p className="flex items-center justify-center gap-2 px-5 py-10 text-sm text-fg-subtle">
        <Loader2 className="size-4 animate-spin" aria-hidden /> Chargement…
      </p>
    );
  }
  const microsoft = v.fournisseur === 'Microsoft';
  // Le secret appartient à une application d'un émetteur : en changer efface l'ancien secret (le serveur ne l'envoie
  // jamais à un autre fournisseur), il faut donc saisir celui de la nouvelle application.
  const emetteurChange =
    donnees.secretEnregistre &&
    (v.fournisseur !== donnees.fournisseur ||
      v.clientId.trim() !== donnees.clientId ||
      (microsoft ? v.annuaire.trim().toLowerCase() !== donnees.annuaire : v.autorite.trim() !== donnees.autorite));
  const incomplet =
    !v.clientId.trim() ||
    !(microsoft ? v.annuaire.trim() : v.autorite.trim()) ||
    ((!donnees.secretEnregistre || emetteurChange) && !v.secret.trim());
  const jours = donnees.secretExpireLe ? joursRestants(donnees.secretExpireLe, maintenant) : null;

  async function enregistrer(b: Brouillon) {
    setEnvoi(true);
    setErreurAction('');
    try {
      const secret = b.secret.trim();
      const r = await api.ssoEnregistrer({
        fournisseur: b.fournisseur,
        annuaire: b.annuaire.trim(),
        autorite: b.autorite.trim(),
        clientId: b.clientId.trim(),
        // Secret vide : non envoyé, le serveur garde celui qui est enregistré.
        ...(secret ? { secret } : {}),
        ...(b.secretExpireLe ? { secretExpireLe: b.secretExpireLe } : {}),
        creationAutomatique: b.creationAutomatique,
        roleAdmin: b.roleAdmin.trim(),
        libelle: b.libelle.trim(),
      });
      setDonnees(r);
      d.reset();
      setTest(null);
      toast.succes('Réglages enregistrés.');
    } catch (e) {
      setErreurAction(e instanceof Error ? e.message : 'Enregistrement impossible.');
    } finally {
      setEnvoi(false);
    }
  }

  async function tester() {
    setTestEnCours(true);
    setErreurAction('');
    try {
      setTest(await api.ssoTester());
    } catch (e) {
      setErreurAction(e instanceof Error ? e.message : 'Test impossible.');
    } finally {
      setTestEnCours(false);
    }
  }

  async function activer() {
    setErreurAction('');
    try {
      setDonnees(await api.ssoActiver());
      toast.succes('Connexion unique activée.');
    } catch (e) {
      setErreurAction(e instanceof Error ? e.message : 'Activation impossible.');
    }
  }

  async function confirmerDesactivation(): Promise<boolean> {
    try {
      setDonnees(await api.ssoDesactiver());
      toast.succes('Connexion unique désactivée.');
      return true;
    } catch (e) {
      toast.erreur(e instanceof Error ? e.message : 'Désactivation impossible.');
      return false;
    }
  }

  async function confirmerDelier(): Promise<boolean> {
    if (!aDelier) return false;
    try {
      await api.ssoDelier(aDelier.id);
      identites.recharger();
      toast.succes('Compte délié.');
      return true;
    } catch (e) {
      toast.erreur(e instanceof Error ? e.message : 'Dissociation impossible.');
      return false;
    }
  }

  async function copierUrl(url: string) {
    try {
      await navigator.clipboard.writeText(url);
      toast.succes('Adresse copiée.');
    } catch {
      toast.erreur('Copie impossible : sélectionnez l’adresse et copiez-la à la main.');
    }
  }

  return (
    <div className="space-y-6">
      <Card>
        <CardHeader
          titre="Connexion unique"
          description="Permet de se connecter avec un compte professionnel au lieu d’un mot de passe. Facultatif."
          action={
            <div className="flex items-center gap-3">
              {donnees.active ? (
                <StatusPill ton="ok" icone={CircleCheck}>
                  Active
                </StatusPill>
              ) : (
                <StatusPill ton="later" icone={PowerOff}>
                  Désactivée
                </StatusPill>
              )}
              {donnees.active ? (
                <Button
                  taille="sm"
                  icone={<PowerOff className="size-4" aria-hidden />}
                  onClick={() => setDesactiver(true)}
                >
                  Désactiver
                </Button>
              ) : (
                <Button
                  taille="sm"
                  variante="primaire"
                  icone={<Power className="size-4" aria-hidden />}
                  disabled={donnees.anomalies.length > 0}
                  onClick={() => void activer()}
                >
                  Activer
                </Button>
              )}
            </div>
          }
        />
        <div className="space-y-3 px-5 py-5">
          {donnees.anomalies.length > 0 && (
            <Alerte ton="warn" icone={TriangleAlert} titre="À corriger avant d’activer">
              <ul className="list-disc pl-5">
                {donnees.anomalies.map((a) => (
                  <li key={a}>{a}</li>
                ))}
              </ul>
            </Alerte>
          )}
          {donnees.secretEnregistre && !donnees.secretLisible && (
            <Alerte ton="danger" icone={OctagonAlert} role="alert">
              Le secret enregistré est illisible (clés de l’application changées, par exemple après une restauration) :
              saisissez-le de nouveau.
            </Alerte>
          )}
          {jours !== null && jours < 0 && (
            <Alerte ton="danger" icone={OctagonAlert} role="alert">
              Le secret client a expiré le {formatDate(donnees.secretExpireLe, true)} : la connexion unique ne
              fonctionne plus. Créez un nouveau secret chez le fournisseur et saisissez-le ci-dessous.
            </Alerte>
          )}
          {jours !== null && jours >= 0 && jours <= PREAVIS_JOURS && (
            <Alerte ton="warn" icone={TriangleAlert}>
              Le secret client expire le {formatDate(donnees.secretExpireLe, true)} (dans {jours + 1} jour
              {jours + 1 > 1 ? 's' : ''}). Pensez à en créer un nouveau avant cette date.
            </Alerte>
          )}
          {donnees.modifieeLe && (
            <p className="text-xs text-fg-subtle">Dernière modification : {formatDateHeure(donnees.modifieeLe)}</p>
          )}
        </div>
      </Card>

      <Card>
        <CardHeader
          titre={microsoft ? 'À faire dans Microsoft Entra' : 'À faire chez le fournisseur'}
          description="À transmettre à l’équipe informatique."
        />
        <div className="space-y-4 px-5 py-5 text-sm">
          {microsoft ? (
            <ol className="list-decimal space-y-2 pl-5">
              <li>
                Dans le portail Entra, ouvrez <strong>Inscriptions d’applications</strong> ›{' '}
                <strong>Nouvelle inscription</strong>, avec les comptes de cet annuaire uniquement.
              </li>
              <li>
                Ajoutez l’<strong>URI de redirection</strong> de type <strong>Web</strong> ci-dessous.
              </li>
              <li>
                Dans <strong>Certificats et secrets</strong> › <strong>Nouveau secret client</strong>, copiez la{' '}
                <strong>Valeur</strong> (pas l’ID) et notez la date d’expiration.
              </li>
              <li>
                Dans <strong>Vue d’ensemble</strong>, copiez l’<strong>ID d’annuaire (locataire)</strong> et l’
                <strong>ID d’application (client)</strong>.
              </li>
              <li>
                Facultatif : dans <strong>Rôles d’application</strong>, créez un rôle (par exemple{' '}
                <code className="font-mono">MEP.Admin</code>) puis attribuez-le dans{' '}
                <strong>Applications d’entreprise</strong>.
              </li>
            </ol>
          ) : (
            <p>
              Déclarez l’application chez le fournisseur avec l’adresse de retour ci-dessous, puis récupérez l’adresse
              de l’émetteur, l’ID client et le secret.
            </p>
          )}
          {donnees.urlRetour ? (
            <div className="flex items-end gap-2">
              <Field label="Adresse de retour (URI de redirection)" className="flex-1">
                {(id) => <Input id={id} readOnly value={donnees.urlRetour ?? ''} className="font-mono text-[13px]" />}
              </Field>
              <Button
                icone={<Copy className="size-4" aria-hidden />}
                onClick={() => void copierUrl(donnees.urlRetour ?? '')}
              >
                Copier
              </Button>
            </div>
          ) : (
            <Alerte ton="warn" icone={TriangleAlert}>
              L’adresse publique de l’application n’est pas configurée sur le serveur : l’adresse de retour est
              inconnue.
            </Alerte>
          )}
        </div>
      </Card>

      <Card>
        <CardHeader titre="Réglages" />
        <div className="grid gap-4 px-5 py-5 md:grid-cols-2">
          <div className="md:col-span-2">
            <Segmented<FournisseurSso>
              label="Fournisseur"
              valeur={v.fournisseur}
              onChange={(f) => d.update({ fournisseur: f })}
              options={[
                { valeur: 'Microsoft', libelle: 'Microsoft Entra ID' },
                { valeur: 'OpenIdConnect', libelle: 'Autre fournisseur OpenID Connect' },
              ]}
            />
          </div>
          {microsoft ? (
            <Field label="ID de l’annuaire (locataire)">
              {(id) => (
                <Input
                  id={id}
                  value={v.annuaire}
                  onChange={(e) => d.update({ annuaire: e.target.value })}
                  className="font-mono text-[13px]"
                  autoComplete="off"
                />
              )}
            </Field>
          ) : (
            <Field label="Adresse de l’émetteur" hint="ex. https://sso.exemple.fr/realms/entreprise">
              {(id) => (
                <Input
                  id={id}
                  value={v.autorite}
                  onChange={(e) => d.update({ autorite: e.target.value })}
                  autoComplete="off"
                />
              )}
            </Field>
          )}
          <Field label="ID d’application (client)">
            {(id) => (
              <Input
                id={id}
                value={v.clientId}
                onChange={(e) => d.update({ clientId: e.target.value })}
                className="font-mono text-[13px]"
                autoComplete="off"
              />
            )}
          </Field>
          <Field
            label="Secret client"
            hint={
              emetteurChange
                ? 'L’annuaire ou l’application a changé : saisissez le secret de la nouvelle application, l’ancien sera effacé.'
                : undefined
            }
          >
            {(id) => (
              <Input
                id={id}
                type="password"
                value={v.secret}
                onChange={(e) => d.update({ secret: e.target.value })}
                placeholder={
                  donnees.secretEnregistre && !emetteurChange ? 'Enregistré — laisser vide pour le conserver' : ''
                }
                autoComplete="new-password"
              />
            )}
          </Field>
          <Field label="Expiration du secret" hint="Facultatif : sert à vous prévenir 30 jours avant.">
            {(id) => (
              <Input
                id={id}
                type="date"
                value={v.secretExpireLe}
                onChange={(e) => d.update({ secretExpireLe: e.target.value })}
              />
            )}
          </Field>
          <Field
            label="Rôle d’application administrateur"
            hint="Facultatif. Vide : le rôle administrateur se gère dans l’onglet Comptes. Renseigné, il est relu à chaque connexion."
          >
            {(id) => (
              <Input
                id={id}
                value={v.roleAdmin}
                onChange={(e) => d.update({ roleAdmin: e.target.value })}
                placeholder="MEP.Admin"
                autoComplete="off"
              />
            )}
          </Field>
          <Field label="Texte du bouton" hint="Facultatif.">
            {(id) => (
              <Input
                id={id}
                value={v.libelle}
                onChange={(e) => d.update({ libelle: e.target.value })}
                placeholder={donnees.libelleEffectif}
              />
            )}
          </Field>
          <Checkbox
            className="md:col-span-2"
            label="Créer le compte à la première connexion"
            description="Sinon, seuls les comptes déjà liés peuvent entrer."
            checked={v.creationAutomatique}
            onChange={(c) => d.update({ creationAutomatique: c })}
          />
          {erreurAction && (
            <Alerte ton="danger" icone={OctagonAlert} role="alert" className="md:col-span-2">
              {erreurAction}
            </Alerte>
          )}
          {test && (
            <div className="md:col-span-2">
              <Alerte
                ton={test.ok ? 'ok' : 'danger'}
                icone={test.ok ? CircleCheck : OctagonAlert}
                titre={test.ok ? 'Le test est réussi' : 'Le test a échoué'}
              >
                <ul className="mt-1 space-y-1">
                  {test.verifications.map((t) => (
                    <li key={t.libelle} className="flex items-start gap-2">
                      {t.ok ? (
                        <CircleCheck className="mt-0.5 size-4 shrink-0" aria-label="Réussi" />
                      ) : (
                        <CircleX className="mt-0.5 size-4 shrink-0" aria-label="Échec" />
                      )}
                      <span>
                        <span className="font-medium">{t.libelle}</span>
                        {t.detail && <span> : {t.detail}</span>}
                      </span>
                    </li>
                  ))}
                </ul>
              </Alerte>
            </div>
          )}
        </div>
        <div className="flex flex-wrap items-center gap-3 border-t border-line bg-surface-2/50 px-5 py-3">
          <Button
            variante="primaire"
            icone={<Save className="size-4" aria-hidden />}
            disabled={!d.dirty || incomplet}
            chargement={envoi}
            onClick={() => void enregistrer(v)}
          >
            Enregistrer
          </Button>
          <Button
            icone={<ShieldCheck className="size-4" aria-hidden />}
            chargement={testEnCours}
            disabled={d.dirty}
            title={d.dirty ? 'Enregistrez d’abord vos modifications.' : undefined}
            onClick={() => void tester()}
          >
            Tester
          </Button>
          {d.dirty && (
            <Button variante="fantome" onClick={d.reset}>
              Annuler les modifications
            </Button>
          )}
        </div>
      </Card>

      <Card>
        <CardHeader titre="Comptes liés" description="Comptes de l’application reliés à un compte du fournisseur." />
        {identites.erreur && (
          <div className="p-5">
            <Alerte ton="danger" icone={OctagonAlert} role="alert">
              {identites.erreur}
            </Alerte>
          </div>
        )}
        {!identites.erreur && identites.donnees === null && (
          <p className="flex items-center justify-center gap-2 px-5 py-10 text-sm text-fg-subtle">
            <Loader2 className="size-4 animate-spin" aria-hidden /> Chargement…
          </p>
        )}
        {identites.donnees?.length === 0 && (
          <EmptyState icone={Users} titre="Aucun compte lié" description="Les liaisons apparaîtront ici." />
        )}
        {identites.donnees && identites.donnees.length > 0 && (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="border-b border-line text-xs tracking-wide text-fg-subtle uppercase">
                <tr>
                  <th scope="col" className="px-5 py-2.5 font-medium">
                    Nom
                  </th>
                  <th scope="col" className="px-3 py-2.5 font-medium">
                    Identifiant
                  </th>
                  <th scope="col" className="px-3 py-2.5 font-medium">
                    Identifiant chez le fournisseur
                  </th>
                  <th scope="col" className="px-3 py-2.5 font-medium">
                    Dernière connexion
                  </th>
                  <th scope="col" className="px-5 py-2.5 text-right font-medium">
                    Actions
                  </th>
                </tr>
              </thead>
              <tbody className="divide-y divide-line">
                {identites.donnees.map((i) => (
                  <tr key={i.id}>
                    <td className="px-5 py-3 font-medium text-fg">{i.nomAffiche}</td>
                    <td className="px-3 py-3 font-mono text-xs">{i.login}</td>
                    <td className="px-3 py-3 font-mono text-xs break-all">{i.identifiant}</td>
                    <td className="px-3 py-3">
                      {i.derniereConnexion ? formatDateHeure(i.derniereConnexion) : 'jamais'}
                    </td>
                    <td className="px-5 py-3 text-right">
                      <BoutonIcone label={`Délier le compte ${i.login}`} onClick={() => setADelier(i)}>
                        <Link2Off className="size-4" aria-hidden />
                      </BoutonIcone>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      <Card>
        <CardHeader titre="En ligne de commande" description="Les mêmes réglages sont possibles depuis le serveur." />
        <div className="px-5 py-5">
          <CodeBlock code={COMMANDES} titre="Connexion unique" />
        </div>
      </Card>

      <ConfirmDialog
        open={desactiver}
        onOpenChange={setDesactiver}
        titre="Désactiver la connexion unique ?"
        description="Les personnes se connecteront avec leur mot de passe ; les liaisons sont conservées."
        libelleConfirmer="Désactiver"
        variante="danger"
        onConfirmer={confirmerDesactivation}
      />
      <ConfirmDialog
        open={aDelier !== null}
        onOpenChange={(o) => {
          if (!o) setADelier(null);
        }}
        titre={`Délier le compte ${aDelier?.login ?? ''} ?`}
        description="La personne devra relier son compte pour utiliser de nouveau la connexion unique. Un compte créé par la connexion unique n’a pas de mot de passe : il faudra le réinitialiser dans l’onglet Comptes pour qu’elle puisse se connecter autrement."
        libelleConfirmer="Délier"
        variante="danger"
        onConfirmer={confirmerDelier}
      />
    </div>
  );
}
