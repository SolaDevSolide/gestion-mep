import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
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
  /**
   * Session expirée pendant le travail (401) : le dialogue « Session expirée » s'ouvre par-dessus la page, qui reste
   * montée avec ses saisies ; les requêtes en échec attendent la reconnexion.
   */
  sessionExpiree: boolean;
  /** Rouvre la session du même compte : les requêtes en attente repartent une fois. */
  reconnecter: (login: string, mdp: string) => Promise<void>;
  /**
   * Abandonne la session expirée (changement de compte) : les requêtes en attente échouent. `autre` : compte connecté
   * à la place (autre identifiant saisi dans le dialogue), null pour revenir à l'écran de connexion.
   */
  abandonnerSession: (autre: Utilisateur | null) => void;
}

export const AuthContext = createContext<AuthValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [utilisateur, setUtilisateur] = useState<Utilisateur | null>(null);
  const [chargement, setChargement] = useState(true);
  const [sessionExpiree, setSessionExpiree] = useState(false);
  const navigate = useNavigate();
  const utilisateurRef = useRef(utilisateur);
  useEffect(() => {
    utilisateurRef.current = utilisateur;
  }, [utilisateur]);
  // Reconnexion attendue par les requêtes reçues en 401 (une seule pour toutes).
  const attente = useRef<{ promesse: Promise<boolean>; resoudre: (ok: boolean) => void } | null>(null);

  useEffect(() => {
    setUnauthorizedHandler(() => {
      // Personne n'était connecté (lecture de la session au démarrage) : rien à reprendre, l'écran de connexion suffit.
      if (!utilisateurRef.current) return Promise.resolve(false);
      if (!attente.current) {
        let resoudre: (ok: boolean) => void = () => undefined;
        const promesse = new Promise<boolean>((r) => {
          resoudre = r;
        });
        attente.current = { promesse, resoudre };
        setSessionExpiree(true);
      }
      return attente.current.promesse;
    });
  }, []);

  const terminerAttente = useCallback((ok: boolean) => {
    attente.current?.resoudre(ok);
    attente.current = null;
    setSessionExpiree(false);
  }, []);

  useEffect(() => {
    api
      .moi()
      .then(setUtilisateur)
      .catch(() => setUtilisateur(null))
      .finally(() => setChargement(false));
  }, []);

  const connecter = useCallback(async (login: string, mdp: string) => {
    setUtilisateur(await api.connexion(login, mdp));
  }, []);

  const reconnecter = useCallback(
    async (login: string, mdp: string) => {
      const u = await api.connexion(login, mdp);
      setUtilisateur(u);
      terminerAttente(true);
    },
    [terminerAttente],
  );

  const abandonnerSession = useCallback(
    (autre: Utilisateur | null) => {
      setUtilisateur(autre);
      terminerAttente(false);
    },
    [terminerAttente],
  );

  const deconnecter = useCallback(async () => {
    try {
      await api.deconnexion();
    } finally {
      setUtilisateur(null);
      void navigate('/connexion', { replace: true });
    }
  }, [navigate]);

  const value = useMemo(
    () => ({
      utilisateur,
      chargement,
      connecter,
      deconnecter,
      majUtilisateur: setUtilisateur,
      sessionExpiree,
      reconnecter,
      abandonnerSession,
    }),
    [utilisateur, chargement, connecter, deconnecter, sessionExpiree, reconnecter, abandonnerSession],
  );
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth hors AuthProvider');
  return ctx;
}
