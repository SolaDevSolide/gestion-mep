import { Lock, LogIn, OctagonAlert } from 'lucide-react';
import { useContext, useEffect, useState, type FormEvent } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { api } from '../api';
import { useAuth } from '../auth';
import { copierBrouillons, retenirRetour } from '../brouillons';
import { DirtyContext } from '../hooks';
import type { OptionsConnexion } from '../types';
import { Alerte } from '../ui/Alerte';
import { IconeMicrosoft } from '../ui/IconeMicrosoft';
import { Button } from '../ui/Button';
import { ConfirmDialog, Dialog } from '../ui/Dialog';
import { Field, Input } from '../ui/Input';
import { useToast } from '../ui/Toast';

const memeLogin = (a: string, b: string) => a.trim().toLocaleLowerCase('fr') === b.trim().toLocaleLowerCase('fr');

/**
 * « Session expirée » : ouvert par-dessus la page sur un 401, sans la démonter. Les saisies restent en mémoire ; la
 * reconnexion (même compte) relance les requêtes en attente. Microsoft : redirection, après avoir copié les saisies
 * dans l'onglet pour les reprendre au retour. Changer de compte abandonne la saisie, après confirmation.
 */
export function SessionExpiree() {
  const { utilisateur, sessionExpiree, reconnecter, abandonnerSession } = useAuth();
  const registre = useContext(DirtyContext);
  const toast = useToast();
  const navigate = useNavigate();
  const location = useLocation();
  const [login, setLogin] = useState('');
  const [mdp, setMdp] = useState('');
  const [erreur, setErreur] = useState('');
  const [envoi, setEnvoi] = useState(false);
  const [options, setOptions] = useState<OptionsConnexion | null>(null);
  /** Changement de compte à confirmer : vers l'écran de connexion, ou avec l'identifiant saisi. */
  const [aConfirmer, setAConfirmer] = useState<'ecran' | 'saisi' | null>(null);
  const [saisies, setSaisies] = useState<string[]>([]);

  useEffect(() => {
    if (!sessionExpiree) return;
    setLogin(utilisateur?.login ?? '');
    setMdp('');
    setErreur('');
    setSaisies(registre.libelles());
    // Bouton Microsoft si la connexion unique est active (lecture sans session, possible ici).
    api
      .optionsConnexion()
      .then(setOptions)
      .catch(() => setOptions(null));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [sessionExpiree]);

  const nom = utilisateur?.nomAffiche ?? utilisateur?.login ?? '';

  async function seReconnecter() {
    setEnvoi(true);
    setErreur('');
    try {
      await reconnecter(login.trim(), mdp);
      toast.succes(saisies.length > 0 ? 'Reconnecté. Votre saisie est intacte.' : 'Reconnecté.');
    } catch (e) {
      setErreur(e instanceof Error ? e.message : 'Connexion impossible.');
    } finally {
      setEnvoi(false);
    }
  }

  /** Autre identifiant saisi : la saisie en cours est abandonnée, la session de cet autre compte s'ouvre sur l'accueil. */
  async function connecterAutreCompte() {
    setEnvoi(true);
    setErreur('');
    try {
      const u = await api.connexion(login.trim(), mdp);
      registre.abandonner();
      abandonnerSession(u);
      void navigate('/', { replace: true });
    } catch (e) {
      setErreur(e instanceof Error ? e.message : 'Connexion impossible.');
    } finally {
      setEnvoi(false);
    }
  }

  /** Retour à l'écran de connexion : la page d'origine y est retenue (rouverte après la connexion). */
  function changerDeCompte() {
    registre.abandonner();
    abandonnerSession(null);
  }

  function valider(e: FormEvent) {
    e.preventDefault();
    if (!utilisateur || memeLogin(login, utilisateur.login)) void seReconnecter();
    else if (registre.libelles().length > 0) setAConfirmer('saisi');
    else void connecterAutreCompte();
  }

  function microsoft() {
    copierBrouillons(location.pathname, registre.brouillons());
    retenirRetour(location.pathname + location.search + location.hash);
    // Les saisies sont copiées : plus d'avertissement du navigateur en quittant la page.
    registre.abandonner();
    window.location.assign('/api/auth/sso/connexion');
  }

  return (
    <>
      <Dialog
        open={sessionExpiree}
        onOpenChange={() => undefined}
        fermable={false}
        taille="sm"
        titre={
          <span className="flex items-center gap-2">
            <Lock className="size-5" aria-hidden />
            Session expirée
          </span>
        }
        description={
          saisies.length > 0
            ? `Votre saisie est conservée (${saisies.join(', ')}). Reconnectez-vous pour continuer.`
            : 'Reconnectez-vous pour continuer là où vous en étiez.'
        }
      >
        <form onSubmit={valider} className="space-y-4">
          <Field label="Identifiant">
            {(id) => (
              <Input
                id={id}
                value={login}
                onChange={(e) => setLogin(e.target.value)}
                autoComplete="username"
                required
              />
            )}
          </Field>
          <Field label="Mot de passe">
            {(id) => (
              <Input
                id={id}
                type="password"
                value={mdp}
                onChange={(e) => setMdp(e.target.value)}
                autoComplete="current-password"
                autoFocus
                required
              />
            )}
          </Field>
          {erreur && (
            <Alerte ton="danger" icone={OctagonAlert} role="alert">
              {erreur}
            </Alerte>
          )}
          <Button
            type="submit"
            variante="primaire"
            className="w-full"
            icone={<LogIn className="size-4" aria-hidden />}
            chargement={envoi}
          >
            Se reconnecter
          </Button>
          {options?.ssoActif && (
            <Button className="w-full" icone={<IconeMicrosoft />} disabled={envoi} onClick={microsoft}>
              {options.ssoLibelle || 'Se connecter avec Microsoft'}
            </Button>
          )}
          <p className="text-xs text-fg-subtle">
            Autre compte ?{' '}
            <button
              type="button"
              className="font-medium text-accent-text underline"
              onClick={() => (registre.libelles().length > 0 ? setAConfirmer('ecran') : changerDeCompte())}
            >
              Changer de compte
            </button>{' '}
            (la saisie en cours sera abandonnée, avec confirmation).
          </p>
        </form>
      </Dialog>
      <ConfirmDialog
        open={aConfirmer !== null}
        onOpenChange={(o) => {
          if (!o) setAConfirmer(null);
        }}
        titre={`Abandonner la saisie de ${nom} ?`}
        description={`Ces modifications ne sont pas enregistrées et seront perdues : ${saisies.join(', ')}.`}
        libelleConfirmer="Abandonner la saisie"
        variante="danger"
        onConfirmer={async () => {
          if (aConfirmer === 'saisi') await connecterAutreCompte();
          else changerDeCompte();
          return true;
        }}
      />
    </>
  );
}
