import type {
  Evenement, EtapeSaisie, LivrableSaisie, Mep, MepIdentiteSaisie, MepResume, ParticipantSaisie,
  Phase, SauvegardeSaisie, SystemeSaisie, Utilisateur,
} from './types';

export class ApiError extends Error {
  status: number;
  constructor(status: number, message: string) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
  }
}

let onUnauthorized: () => void = () => {
  if (window.location.pathname !== '/connexion') window.location.assign('/connexion');
};
export function setUnauthorizedHandler(fn: () => void) {
  onUnauthorized = fn;
}

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  let res: Response;
  try {
    res = await fetch(`/api${path}`, {
      method,
      credentials: 'include',
      headers: body !== undefined ? { 'Content-Type': 'application/json' } : undefined,
      body: body !== undefined ? JSON.stringify(body) : undefined,
    });
  } catch {
    throw new ApiError(0, 'Le serveur est injoignable. Vérifiez votre connexion.');
  }
  if (res.status === 401 && path !== '/auth/connexion') {
    onUnauthorized();
    throw new ApiError(401, 'Session expirée, veuillez vous reconnecter.');
  }
  if (!res.ok) {
    let message = '';
    try {
      const data = await res.json();
      message = typeof data?.message === 'string' ? data.message : '';
    } catch {
      /* corps non JSON */
    }
    if (!message) {
      message = res.status === 401 ? 'Identifiants incorrects.' : `Erreur ${res.status}.`;
    }
    throw new ApiError(res.status, message);
  }
  if (res.status === 204) return undefined as T;
  const text = await res.text();
  return (text ? JSON.parse(text) : undefined) as T;
}

const get = <T>(p: string) => request<T>('GET', p);
const post = <T>(p: string, b?: unknown) => request<T>('POST', p, b ?? {});
const put = <T>(p: string, b: unknown) => request<T>('PUT', p, b);
const del = <T>(p: string) => request<T>('DELETE', p);

export const api = {
  connexion: (login: string, motDePasse: string) => post<Utilisateur>('/auth/connexion', { login, motDePasse }),
  deconnexion: () => post<void>('/auth/deconnexion'),
  moi: () => get<Utilisateur>('/auth/moi'),
  utilisateurs: () => get<Utilisateur[]>('/utilisateurs'),

  meps: () => get<MepResume[]>('/meps'),
  creerMep: (outil: string, objet: string) => post<Mep>('/meps', { outil, objet }),
  mep: (id: string) => get<Mep>(`/meps/${id}`),
  modifierIdentite: (id: string, s: MepIdentiteSaisie) => put<Mep>(`/meps/${id}`, s),
  modifierSauvegarde: (id: string, s: SauvegardeSaisie) => put<Mep>(`/meps/${id}/sauvegarde`, s),
  journal: (id: string) => get<Evenement[]>(`/meps/${id}/journal`),

  ajouterParticipant: (id: string, s: ParticipantSaisie) => post<Mep>(`/meps/${id}/participants`, s),
  modifierParticipant: (id: string, pid: string, s: ParticipantSaisie) => put<Mep>(`/meps/${id}/participants/${pid}`, s),
  supprimerParticipant: (id: string, pid: string) => del<Mep>(`/meps/${id}/participants/${pid}`),
  ajouterSysteme: (id: string, s: SystemeSaisie) => post<Mep>(`/meps/${id}/systemes`, s),
  modifierSysteme: (id: string, sid: string, s: SystemeSaisie) => put<Mep>(`/meps/${id}/systemes/${sid}`, s),
  supprimerSysteme: (id: string, sid: string) => del<Mep>(`/meps/${id}/systemes/${sid}`),
  ajouterLivrable: (id: string, s: LivrableSaisie) => post<Mep>(`/meps/${id}/livrables`, s),
  modifierLivrable: (id: string, lid: string, s: LivrableSaisie) => put<Mep>(`/meps/${id}/livrables/${lid}`, s),
  supprimerLivrable: (id: string, lid: string) => del<Mep>(`/meps/${id}/livrables/${lid}`),

  ajouterEtape: (id: string) => post<Mep>(`/meps/${id}/etapes`, {}),
  modifierEtape: (id: string, eid: string, s: EtapeSaisie) => put<Mep>(`/meps/${id}/etapes/${eid}`, s),
  supprimerEtape: (id: string, eid: string) => del<Mep>(`/meps/${id}/etapes/${eid}`),
  deplacerEtape: (id: string, eid: string, sens: 'haut' | 'bas') => post<Mep>(`/meps/${id}/etapes/${eid}/deplacer`, { sens }),
  demarrerEtape: (id: string, eid: string, revision: number) => post<Mep>(`/meps/${id}/etapes/${eid}/demarrer`, { revision }),
  terminerEtape: (id: string, eid: string, revision: number, resultat: 'Reussie' | 'Echec', preuve: string, resultatVerifie: boolean) =>
    post<Mep>(`/meps/${id}/etapes/${eid}/terminer`, { revision, resultat, preuve, resultatVerifie }),
  autoriserReprise: (id: string, eid: string, revision: number) =>
    post<Mep>(`/meps/${id}/etapes/${eid}/autoriser-reprise`, { revision }),

  procedureRelue: (id: string, relue: boolean) => post<Mep>(`/meps/${id}/procedure-relue`, { relue }),
  changerPhase: (id: string, phase: Phase) => post<Mep>(`/meps/${id}/phase`, { phase }),
};
