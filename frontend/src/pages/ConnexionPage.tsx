import { Rocket, ShieldCheck, Timer, Undo2 } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import { Navigate, useNavigate } from 'react-router-dom';
import { useAuth } from '../auth';
import { Alerte } from '../ui/Alerte';
import { Button } from '../ui/Button';
import { Field, Input } from '../ui/Input';
import { OctagonAlert } from 'lucide-react';

const COMPTES_DEMO = [
  { login: 'alice', role: 'Pilote' },
  { login: 'bob', role: 'Opérateur' },
  { login: 'claire', role: 'Opérateur' },
];

export default function ConnexionPage() {
  const { utilisateur, chargement, connecter } = useAuth();
  const navigate = useNavigate();
  const [login, setLogin] = useState('');
  const [mdp, setMdp] = useState('');
  const [erreur, setErreur] = useState('');
  const [envoi, setEnvoi] = useState(false);

  if (!chargement && utilisateur) return <Navigate to="/" replace />;

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
    <div className="grid min-h-screen lg:grid-cols-[1.1fr_1fr]">
      <div className="relative hidden flex-col justify-between overflow-hidden bg-[#0d0e1a] p-12 text-white lg:flex">
        <div aria-hidden className="absolute -top-32 -left-24 size-[28rem] rounded-full bg-indigo-600/30 blur-3xl" />
        <div aria-hidden className="absolute -right-24 -bottom-40 size-[30rem] rounded-full bg-sky-500/20 blur-3xl" />
        <div className="relative flex items-center gap-3">
          <span className="flex size-10 items-center justify-center rounded-xl bg-indigo-500 shadow-lg">
            <Rocket className="size-5" aria-hidden />
          </span>
          <span className="text-lg font-semibold tracking-tight">Gestion MEP</span>
        </div>
        <div className="relative max-w-md">
          <h2 className="text-4xl leading-tight font-semibold tracking-tight">Mettre en production sans improviser.</h2>
          <p className="mt-4 text-base text-indigo-100/80">
            Un plan clair, des étapes tracées, un retour arrière toujours prêt. Même à trois heures du matin.
          </p>
          <ul className="mt-8 space-y-3 text-sm text-indigo-100/90">
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
        <p className="relative text-xs text-indigo-200/60">Application interne de gestion des mises en production</p>
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
          {import.meta.env.DEV && (
            <div className="mt-8 rounded-xl border border-dashed border-line-strong p-4">
              <p className="text-xs font-semibold tracking-wide text-fg-subtle uppercase">Comptes de démonstration</p>
              <p className="mt-0.5 text-xs text-fg-subtle">
                Mot de passe : <code className="font-mono">demo</code>
              </p>
              <div className="mt-3 flex flex-wrap gap-2">
                {COMPTES_DEMO.map((c) => (
                  <button
                    key={c.login}
                    type="button"
                    onClick={() => {
                      setLogin(c.login);
                      setMdp('demo');
                    }}
                    className="rounded-lg border border-line bg-surface px-2.5 py-1.5 text-xs font-medium text-fg-muted shadow-sm transition-colors duration-150 hover:border-accent hover:text-accent-text"
                  >
                    {c.login} <span className="text-fg-subtle">· {c.role}</span>
                  </button>
                ))}
              </div>
            </div>
          )}
        </div>
      </main>
    </div>
  );
}
