import type {
  BilanSaisie, CritereGoNoGo, CritereGoNoGoSaisie, DecisionApprobation, DuplicationSaisie, ParametresGlobaux, PieceJointe, FicheRecetteSaisie, ProfilSaisie, SauvegardeSystemeSaisie,
  Evenement, EtapeSaisie, LivrableSaisie, Mep, MepIdentiteSaisie, MepResume, ParticipantSaisie,
  PlanRepliSaisie, ResolutionType, ResolutionTypeSaisie, ResultatControle, SauvegardeSaisie,
  ResponsableTechniqueSaisie, SystemeCatalogue, SystemeCatalogueSaisie, SystemeSaisie, TypePlan, Utilisateur,
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
  const formulaire = typeof FormData !== 'undefined' && body instanceof FormData;
  let res: Response;
  try {
    res = await fetch(`/api${path}`, {
      method,
      credentials: 'include',
      headers: body !== undefined && !formulaire ? { 'Content-Type': 'application/json' } : undefined,
      body: body === undefined ? undefined : formulaire ? (body as FormData) : JSON.stringify(body),
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
      message = res.status === 401 ? 'Identifiants incorrects.' : res.status === 413 ? 'Fichier trop volumineux.' : `Erreur ${res.status}.`;
    }
    throw new ApiError(res.status, message);
  }
  if (res.status === 204) return undefined as T;
  const text = await res.text();
  return (text ? JSON.parse(text) : undefined) as T;
}

function formulaireFichier(f: File): FormData {
  const fd = new FormData();
  fd.append('fichier', f, f.name);
  return fd;
}

export const urlPieceJointe = (pid: string) => `/api/pieces-jointes/${pid}/contenu`;

const get = <T>(p: string) => request<T>('GET', p);
const post = <T>(p: string, b?: unknown) => request<T>('POST', p, b ?? {});
const put = <T>(p: string, b: unknown) => request<T>('PUT', p, b);
const del = <T>(p: string) => request<T>('DELETE', p);

export const api = {
  connexion: (login: string, motDePasse: string) => post<Utilisateur>('/auth/connexion', { login, motDePasse }),
  deconnexion: () => post<void>('/auth/deconnexion'),
  moi: () => get<Utilisateur>('/auth/moi'),
  utilisateurs: () => get<Utilisateur[]>('/utilisateurs'),
  modifierMonProfil: (s: ProfilSaisie) => put<Utilisateur>('/utilisateurs/moi/profil', s),

  meps: (modele?: boolean) => get<MepResume[]>(`/meps${modele === undefined ? '' : `?modele=${modele}`}`),
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
  modifierSauvegardeSysteme: (id: string, sid: string, s: SauvegardeSystemeSaisie) => put<Mep>(`/meps/${id}/systemes/${sid}/sauvegarde`, s),
  ajouterLivrable: (id: string, s: LivrableSaisie) => post<Mep>(`/meps/${id}/livrables`, s),
  modifierLivrable: (id: string, lid: string, s: LivrableSaisie) => put<Mep>(`/meps/${id}/livrables/${lid}`, s),
  supprimerLivrable: (id: string, lid: string) => del<Mep>(`/meps/${id}/livrables/${lid}`),

  /** Création : `ligne` absente = nouvelle ligne en bas, `position` absente = en fin de ligne ; liens par défaut calculés par le serveur. */
  ajouterEtape: (id: string, plan: TypePlan = 'Deploiement', ligne?: number, position?: number) =>
    post<Mep>(`/meps/${id}/etapes`, { plan, ...(ligne === undefined ? {} : { ligne }), ...(position === undefined ? {} : { position }) }),
  /** Place une étape (ligne, position dans la ligne) ; `ligne` = nombre de lignes pour une nouvelle ligne en bas. */
  placerEtape: (id: string, eid: string, ligne: number, position: number, liensParDefaut = true) =>
    post<Mep>(`/meps/${id}/etapes/${eid}/placer`, { ligne, position, liensParDefaut }),
  autoriserNonRetour: (id: string, eid: string, revision: number) =>
    post<Mep>(`/meps/${id}/etapes/${eid}/autoriser-non-retour`, { revision }),
  modifierEtape: (id: string, eid: string, s: EtapeSaisie) => put<Mep>(`/meps/${id}/etapes/${eid}`, s),
  supprimerEtape: (id: string, eid: string) => del<Mep>(`/meps/${id}/etapes/${eid}`),
  demarrerEtape: (id: string, eid: string, revision: number) => post<Mep>(`/meps/${id}/etapes/${eid}/demarrer`, { revision }),
  terminerEtape: (id: string, eid: string, revision: number, resultat: 'Reussie' | 'Echec', preuve: string, resultatVerifie: boolean) =>
    post<Mep>(`/meps/${id}/etapes/${eid}/terminer`, { revision, resultat, preuve, resultatVerifie }),
  autoriserReprise: (id: string, eid: string, revision: number) =>
    post<Mep>(`/meps/${id}/etapes/${eid}/autoriser-reprise`, { revision }),

  prendreLaMain: (id: string, eid: string, revision: number, motif: string) =>
    post<Mep>(`/meps/${id}/etapes/${eid}/prendre-la-main`, { revision, motif }),
  modifierResponsablesTechniques: (id: string, version: number, responsables: ResponsableTechniqueSaisie[]) =>
    put<Mep>(`/meps/${id}/responsables-techniques`, { version, responsables }),
  appliquerResolution: (id: string, eid: string, resolutionTypeId: string, revision: number) =>
    post<Mep>(`/meps/${id}/etapes/${eid}/resolutions`, { resolutionTypeId, revision }),
  controlerCorrection: (id: string, rid: string, ordre: number, resultat: ResultatControle, observation: string) =>
    post<Mep>(`/meps/${id}/resolutions/${rid}/corrections/${ordre}`, { resultat, observation }),
  controlerVerification: (id: string, rid: string, resultat: ResultatControle, observation: string) =>
    post<Mep>(`/meps/${id}/resolutions/${rid}/verification`, { resultat, observation }),

  modifierPlanRepli: (id: string, s: PlanRepliSaisie) => put<Mep>(`/meps/${id}/plan-repli`, s),
  declencherRetourArriere: (id: string, motif: string) => post<Mep>(`/meps/${id}/retour-arriere`, { motif }),
  cloturer: (id: string, bilan?: BilanSaisie) => post<Mep>(`/meps/${id}/cloturer`, bilan ?? {}),
  go: (id: string, criteresCoches: string[]) => post<Mep>(`/meps/${id}/phase`, { phase: 'Execution', criteresCoches }),
  noGo: (id: string, motif: string) => post<Mep>(`/meps/${id}/no-go`, { motif }),
  passerEnRecette: (id: string) => post<Mep>(`/meps/${id}/phase`, { phase: 'Recette' }),
  dupliquer: (id: string, s: DuplicationSaisie) => post<Mep>(`/meps/${id}/dupliquer`, s),

  modifierApprobateurs: (id: string, version: number, utilisateurIds: string[]) => put<Mep>(`/meps/${id}/approbateurs`, { version, utilisateurIds }),
  decider: (id: string, version: number, decision: DecisionApprobation, commentaire: string) =>
    post<Mep>(`/meps/${id}/approbation`, { version, decision, commentaire }),

  ajouterFicheRecette: (id: string, systemeId: string | null, titre: string) => post<Mep>(`/meps/${id}/fiches-recette`, { systemeId, titre }),
  modifierFicheRecette: (id: string, fid: string, s: FicheRecetteSaisie) => put<Mep>(`/meps/${id}/fiches-recette/${fid}`, s),
  supprimerFicheRecette: (id: string, fid: string) => del<Mep>(`/meps/${id}/fiches-recette/${fid}`),
  resultatEtapeTest: (id: string, fid: string, tid: string, resultat: ResultatControle, observation: string) =>
    post<Mep>(`/meps/${id}/fiches-recette/${fid}/etapes/${tid}/resultat`, { resultat, observation }),
  refaireFicheRecette: (id: string, fid: string) => post<Mep>(`/meps/${id}/fiches-recette/${fid}/refaire`, {}),
  refaireEtapeTest: (id: string, fid: string, tid: string) => post<Mep>(`/meps/${id}/fiches-recette/${fid}/etapes/${tid}/refaire`, {}),

  // Pièces jointes : MEP, étape, résolution exécutée, résolution type.
  deposerPieceMep: (id: string, f: File) => post<Mep>(`/meps/${id}/pieces-jointes`, formulaireFichier(f)),
  deposerPieceEtape: (id: string, eid: string, f: File) => post<Mep>(`/meps/${id}/etapes/${eid}/pieces-jointes`, formulaireFichier(f)),
  supprimerPiece: (id: string, pid: string) => del<Mep>(`/meps/${id}/pieces-jointes/${pid}`),
  piecesResolution: (rid: string) => get<PieceJointe[]>(`/resolutions/${rid}/pieces-jointes`),
  deposerPieceResolution: (rid: string, f: File) => post<PieceJointe>(`/resolutions/${rid}/pieces-jointes`, formulaireFichier(f)),
  supprimerPieceResolution: (rid: string, pid: string) => del<void>(`/resolutions/${rid}/pieces-jointes/${pid}`),

  parametres: () => get<ParametresGlobaux>('/parametres'),
  modifierParametres: (s: ParametresGlobaux) => put<ParametresGlobaux>('/parametres', s),
  criteresGoNoGo: () => get<CritereGoNoGo[]>('/criteres-go-no-go'),
  creerCritere: (s: CritereGoNoGoSaisie) => post<CritereGoNoGo>('/criteres-go-no-go', s),
  modifierCritere: (id: string, s: CritereGoNoGoSaisie) => put<CritereGoNoGo>(`/criteres-go-no-go/${id}`, s),
  supprimerCritere: (id: string) => del<void>(`/criteres-go-no-go/${id}`),

  catalogueSystemes: () => get<SystemeCatalogue[]>('/catalogue/systemes'),
  creerSystemeCatalogue: (s: SystemeCatalogueSaisie) => post<SystemeCatalogue>('/catalogue/systemes', s),
  modifierSystemeCatalogue: (id: string, s: SystemeCatalogueSaisie) => put<SystemeCatalogue>(`/catalogue/systemes/${id}`, s),
  supprimerSystemeCatalogue: (id: string) => del<void>(`/catalogue/systemes/${id}`),
  ajouterSystemeDepuisCatalogue: (id: string, catalogueId: string, impact: string) =>
    post<Mep>(`/meps/${id}/systemes/depuis-catalogue`, { catalogueId, impact }),

  resolutions: (systemeCatalogueId?: string | null) =>
    get<ResolutionType[]>(`/resolutions${systemeCatalogueId ? `?systemeCatalogueId=${encodeURIComponent(systemeCatalogueId)}` : ''}`),
  creerResolution: (s: Omit<ResolutionTypeSaisie, 'version'>) => post<ResolutionType>('/resolutions', s),
  modifierResolution: (id: string, s: ResolutionTypeSaisie) => put<ResolutionType>(`/resolutions/${id}`, s),
  supprimerResolution: (id: string) => del<void>(`/resolutions/${id}`),

  procedureRelue: (id: string, relue: boolean) => post<Mep>(`/meps/${id}/procedure-relue`, { relue }),
  changerPhase: (id: string, phase: 'Preparation') => post<Mep>(`/meps/${id}/phase`, { phase }),
};
