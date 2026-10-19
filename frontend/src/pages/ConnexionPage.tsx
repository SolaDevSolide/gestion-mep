import { CircleCheck, Loader2, LogIn, Rocket, ShieldCheck, Timer, Undo2 } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import { Navigate, useNavigate, useSearchParams } from 'react-router-dom';
import { api } from '../api';
import { useAuth } from '../auth';
import { useAsync } from '../hooks';
import type { CompteDemo } from '../types';
import { Alerte } from '../ui/Alerte';
import { Apparition } from '../ui/Apparition';
import { Squelette, SqueletteFormulaire } from '../ui/Squelette';
import { Avatar } from '../ui/Avatar';
import { Button } from '../ui/Button';
import { Field, Input } from '../ui/Input';
import { Badge } from '../ui/StatusPill';
import { OctagonAlert } from 'lucide-react';

/**
 * Connexion rapide du mode démonstration : un clic sur un compte ouvre sa session (le serveur ne l'accepte que pour les
 * comptes de démonstration, et seulement en mode démonstration).
 */
function ConnexionRapide({
  comptes,
  enCours,
  desactive,
  onChoisir,
}: {
  comptes: CompteDemo[];
  enCours: string | null;
  desactive: boolean;
  onChoisir: (login: string) => void;
}) {
  return (
    <section aria-labelledby="connexion-rapide" className="rounded-xl border border-warn-bd bg-warn-bg/40 p-4">
      <h2 id="connexion-rapide" className="text-sm font-semibold">
        Connexion rapide — comptes de démonstration
      </h2>
      <p className="mt-0.5 text-xs text-fg-muted">
        Un clic connecte directement. Mot de passe pour le formulaire : <code className="font-mono">demo</code>
      </p>
      <ul className="mt-3 space-y-2">
        {comptes.map((c) => (
          <li key={c.login}>
            <button
              type="button"
              disabled={desactive}
              onClick={() => onChoisir(c.login)}
              aria-label={`Se connecter en tant que ${c.nomAffiche} (${c.login})`}
              className="flex w-full items-start gap-3 rounded-lg border border-line bg-surface p-2.5 text-left shadow-sm transition-colors duration-150 hover:border-accent focus-visible:ring-2 focus-visible:ring-accent focus-visible:outline-none disabled:cursor-not-allowed disabled:opacity-60"
            >
              <Avatar nom={c.nomAffiche} taille="sm" className="mt-0.5" />
              <span className="min-w-0 flex-1">
                <span className="flex flex-wrap items-center gap-1.5">
                  <span className="text-sm font-medium text-fg">{c.nomAffiche}</span>
                  <span className="font-mono text-xs text-fg-subtle">{c.login}</span>
                  {c.estAdmin && <Badge ton="run">Administrateur</Badge>}
                </span>
                {c.role && <span className="block text-xs text-fg-muted">{c.role}</span>}
                <span className="mt-0.5 block text-xs text-fg-subtle">{c.description}</span>
              </span>
              {enCours === c.login ? (
                <Loader2 className="mt-1 size-4 shrink-0 animate-spin text-fg-subtle" aria-hidden />
              ) : (
                <LogIn className="mt-1 size-4 shrink-0 text-fg-subtle" aria-hidden />
              )}
            </button>
          </li>
        ))}
      </ul>
    </section>
  );
}

/** Messages des échecs de connexion unique : le serveur renvoie vers /connexion?erreur=CODE. */
const ERREURS_SSO: Record<string, string> = {
  'sso-inactif': 'La connexion Microsoft n’est plus activée. Utilisez votre identifiant et votre mot de passe.',
  'sso-annule': 'La connexion a été annulée.',
  'sso-echec': 'La connexion a échoué. Réessayez ou contactez un administrateur.',
  'sso-injoignable': 'Le fournisseur de connexion est injoignable depuis le serveur. Réessayez plus tard.',
  'compte-inconnu':
    'Aucun compte n’est relié à ce compte Microsoft, et la création automatique est désactivée. Demandez à un administrateur de créer votre compte, ou connectez-vous avec votre mot de passe puis liez votre compte depuis « Mon profil ».',
  'login-existant':
    'Un compte local porte déjà cet identifiant. Connectez-vous avec votre mot de passe, puis cliquez sur « Lier mon compte » dans « Mon profil ».',
  'nom-existant':
    'Un autre compte porte déjà le nom donné par Microsoft. Demandez à un administrateur de créer votre compte sous un nom qui vous distingue, puis liez-le à Microsoft depuis « Mon profil ».',
  'compte-desactive': 'Ce compte est désactivé. Contactez un administrateur.',
  'deja-lie': 'Ce compte Microsoft est déjà lié à un autre compte.',
  'sso-session': 'La demande de connexion a expiré. Recommencez.',
};
const ERREUR_SSO_INCONNUE = 'La connexion a échoué. Réessayez ou contactez un administrateur.';

/** Logo à quatre carrés, en couleurs fixes (marque du fournisseur). */
function IconeMicrosoft() {
  return (
    <svg viewBox="0 0 21 21" className="size-4" aria-hidden>
      <rect x="1" y="1" width="9" height="9" fill="#f25022" />
      <rect x="11" y="1" width="9" height="9" fill="#7fba00" />
      <rect x="1" y="11" width="9" height="9" fill="#00a4ef" />
      <rect x="11" y="11" width="9" height="9" fill="#ffb900" />
    </svg>
  );
}

/** Place réservée du formulaire (identifiant, mot de passe, bouton) pendant la lecture des options de connexion. */
function SqueletteConnexion() {
  return (
    <div className="space-y-4">
      <SqueletteFormulaire champs={2} />
      <Squelette className="h-12 w-full rounded-xl" />
    </div>
  );
}

export default function ConnexionPage() {
  const { utilisateur, chargement, connecter, majUtilisateur } = useAuth();
  const navigate = useNavigate();
  const [login, setLogin] = useState('');
  const [mdp, setMdp] = useState('');
  const [erreur, setErreur] = useState('');
  const [envoi, setEnvoi] = useState(false);
  const [rapide, setRapide] = useState<string | null>(null);
  const [params] = useSearchParams();
  // Échec de chargement : simplement pas de bouton de connexion unique.
  const optionsLues = useAsync(() => api.optionsConnexion(), []);
  const options = optionsLues.donnees;
  // Hors mode démonstration (route absente) : null, aucune connexion rapide.
  const demoLue = useAsync(() => api.demo(), []);
  const demo = demoLue.donnees;
  // Le formulaire apparaît une fois ces deux lectures faites : le bouton Microsoft et les comptes de démonstration ne
  // décalent plus le formulaire en arrivant.
  const optionsPretes = !optionsLues.chargement && !demoLue.chargement;
  const codeSso = params.get('erreur');
  const erreurSso = codeSso ? (ERREURS_SSO[codeSso] ?? ERREUR_SSO_INCONNUE) : '';
  const reinitialisee = params.get('demo') === 'reinitialisee';

  if (!chargement && utilisateur) return <Navigate to="/" replace />;

  async function connexionRapide(compte: string) {
    setErreur('');
    setRapide(compte);
    try {
      majUtilisateur(await api.connexionDemo(compte));
      void navigate('/', { replace: true });
    } catch (err) {
      setErreur(err instanceof Error ? err.message : 'Connexion impossible.');
    } finally {
      setRapide(null);
    }
  }

  async function valider(e: FormEvent) {
    e.preventDefault();
    setErreur('');
    setEnvoi(true);
    try {
      await connecter(login.trim(), mdp);
      void navigate('/', { replace: true });
    } catch (err) {
      setErreur(err instanceof Error ? err.message : 'Connexion impossible.');
    } finally {
      setEnvoi(false);
    }
  }

  return (
    <div className="grid min-h-[calc(100vh-var(--bandeau-demo,0px))] lg:grid-cols-[1.1fr_1fr]">
      <div className="relative hidden flex-col justify-between overflow-hidden bg-secondaire p-12 text-secondaire-fg lg:flex">
        <div
          aria-hidden
          className="absolute -top-32 -left-24 size-[28rem] rounded-full bg-connexion-halo-1/30 blur-3xl"
        />
        <div
          aria-hidden
          className="absolute -right-24 -bottom-40 size-[30rem] rounded-full bg-connexion-halo-2/20 blur-3xl"
        />
        <div className="relative flex items-center gap-3">
          <span className="flex size-10 items-center justify-center rounded-xl bg-connexion-logo text-connexion-logo-fg shadow-lg">
            <Rocket className="size-5" aria-hidden />
          </span>
          <span className="text-lg font-semibold tracking-tight">Gestion MEP</span>
        </div>
        <div className="relative max-w-md">
          <h2 className="text-4xl leading-tight font-semibold tracking-tight">Mettre en production sans improviser.</h2>
          <p className="mt-4 text-base text-connexion-texte/80">
            Un plan clair, des étapes tracées, un retour arrière toujours prêt. Même à trois heures du matin.
          </p>
          <ul className="mt-8 space-y-3 text-sm text-connexion-texte/90">
            <li className="flex items-center gap-3">
              <ShieldCheck className="size-5 text-emerald-300" aria-hidden /> Chaque action est tracée dans le journal
            </li>
            <li className="flex items-center gap-3">
              <Timer className="size-5 text-amber-300" aria-hidden /> Compte à rebours jusqu’à l’échéance de retour
              arrière
            </li>
            <li className="flex items-center gap-3">
              <Undo2 className="size-5 text-rose-300" aria-hidden /> Résolutions types et plan de repli intégrés
            </li>
          </ul>
        </div>
        <p className="relative text-xs text-connexion-discret/60">
          Application interne de gestion des mises en production
        </p>
      </div>

      <main className="flex items-center justify-center bg-bg p-6">
        <div className="w-full max-w-sm">
          <div className="mb-8 flex items-center gap-3 lg:hidden">
            <span className="flex size-10 items-center justify-center rounded-xl bg-accent text-accent-fg shadow-sm">
              <Rocket className="size-5" aria-hidden />
            </span>
            <span className="text-lg font-semibold tracking-tight">Gestion MEP</span>
          </div>
          <h1 className="text-2xl font-semibold tracking-tight">Connexion</h1>
          <p className="mt-1 mb-6 text-sm text-fg-subtle">Identifiez-vous pour accéder aux mises en production.</p>
          <Apparition
            pret={optionsPretes}
            libelle="Chargement des options de connexion…"
            squelette={<SqueletteConnexion />}
          >
            {erreurSso && (
              <Alerte ton="danger" icone={OctagonAlert} role="alert" className="mb-4">
                {erreurSso}
              </Alerte>
            )}
            {reinitialisee && (
              <Alerte ton="ok" icone={CircleCheck} className="mb-4">
                La démonstration a été réinitialisée : reconnectez-vous avec un compte de démonstration.
              </Alerte>
            )}
            {demo && demo.comptes.length > 0 && (
              <>
                <ConnexionRapide
                  comptes={demo.comptes}
                  enCours={rapide}
                  desactive={rapide !== null || envoi}
                  onChoisir={(c) => void connexionRapide(c)}
                />
                <div className="my-5 flex items-center gap-3 text-xs text-fg-subtle" aria-hidden>
                  <span className="h-px flex-1 bg-line" /> ou avec un identifiant{' '}
                  <span className="h-px flex-1 bg-line" />
                </div>
              </>
            )}
            {options?.ssoActif && (
              <>
                <Button
                  variante="secondaire"
                  taille="lg"
                  className="w-full"
                  icone={<IconeMicrosoft />}
                  onClick={() => window.location.assign('/api/auth/sso/connexion')}
                >
                  {options.ssoLibelle || 'Se connecter avec Microsoft'}
                </Button>
                <div className="my-5 flex items-center gap-3 text-xs text-fg-subtle" aria-hidden>
                  <span className="h-px flex-1 bg-line" /> ou <span className="h-px flex-1 bg-line" />
                </div>
              </>
            )}
            <form onSubmit={(e) => void valider(e)} className="space-y-4">
              <Field label="Identifiant">
                {(id) => (
                  <Input
                    id={id}
                    value={login}
                    onChange={(e) => setLogin(e.target.value)}
                    autoComplete="username"
                    autoFocus
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
                    required
                  />
                )}
              </Field>
              {erreur && (
                <Alerte ton="danger" icone={OctagonAlert} role="alert">
                  {erreur}
                </Alerte>
              )}
              <Button type="submit" variante="primaire" taille="lg" className="w-full" chargement={envoi}>
                Se connecter
              </Button>
            </form>
          </Apparition>
        </div>
      </main>
    </div>
  );
}
