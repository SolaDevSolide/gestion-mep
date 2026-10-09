import { useState, type FormEvent } from 'react';
import { Navigate, useNavigate } from 'react-router-dom';
import { useAuth } from '../auth';

export default function ConnexionPage() {
  const { utilisateur, connecter } = useAuth();
  const navigate = useNavigate();
  const [login, setLogin] = useState('');
  const [mdp, setMdp] = useState('');
  const [erreur, setErreur] = useState('');
  const [envoi, setEnvoi] = useState(false);

  if (utilisateur) return <Navigate to="/" replace />;

  async function soumettre(e: FormEvent) {
    e.preventDefault();
    setErreur('');
    setEnvoi(true);
    try {
      await connecter(login.trim(), mdp);
      navigate('/', { replace: true });
    } catch (err) {
      setErreur(err instanceof Error ? err.message : 'Connexion impossible.');
    } finally {
      setEnvoi(false);
    }
  }

  return (
    <main className="connexion">
      <form className="carte connexion-form" onSubmit={soumettre}>
        <h1>Gestion des MEP</h1>
        <p className="muted">Connectez-vous pour préparer ou exécuter une mise en production.</p>
        <label className="champ">
          <span>Identifiant</span>
          <input value={login} onChange={(e) => setLogin(e.target.value)} autoComplete="username" autoFocus required />
        </label>
        <label className="champ">
          <span>Mot de passe</span>
          <input type="password" value={mdp} onChange={(e) => setMdp(e.target.value)} autoComplete="current-password" required />
        </label>
        {erreur && <p className="alerte alerte-erreur" role="alert">{erreur}</p>}
        <button type="submit" className="btn btn-primaire" disabled={envoi}>
          {envoi ? 'Connexion…' : 'Se connecter'}
        </button>
        {import.meta.env.DEV && (
          <p className="petit muted">Comptes de démo : alice, bob, claire (mot de passe : demo).</p>
        )}
      </form>
    </main>
  );
}
