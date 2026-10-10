import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';
import { api, setUnauthorizedHandler } from './api';
import type { Utilisateur } from './types';

interface AuthValue {
  utilisateur: Utilisateur | null;
  chargement: boolean;
  connecter: (login: string, mdp: string) => Promise<void>;
  deconnecter: () => Promise<void>;
  /** Remplace l’utilisateur connecté (après la modification de son profil). */
  majUtilisateur: (u: Utilisateur) => void;
}

export const AuthContext = createContext<AuthValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [utilisateur, setUtilisateur] = useState<Utilisateur | null>(null);
  const [chargement, setChargement] = useState(true);
  const navigate = useNavigate();

  useEffect(() => {
    setUnauthorizedHandler(() => {
      setUtilisateur(null);
      void navigate('/connexion', { replace: true });
    });
  }, [navigate]);

  useEffect(() => {
    api.moi()
      .then(setUtilisateur)
      .catch(() => setUtilisateur(null))
      .finally(() => setChargement(false));
  }, []);

  const connecter = useCallback(async (login: string, mdp: string) => {
    setUtilisateur(await api.connexion(login, mdp));
  }, []);

  const deconnecter = useCallback(async () => {
    try { await api.deconnexion(); } finally {
      setUtilisateur(null);
      void navigate('/connexion', { replace: true });
    }
  }, [navigate]);

  const value = useMemo(() => ({ utilisateur, chargement, connecter, deconnecter, majUtilisateur: setUtilisateur }), [utilisateur, chargement, connecter, deconnecter]);
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth hors AuthProvider');
  return ctx;
}
