import type { CharteFichier } from './charte/charte';
import type {
  ActiviteSysteme,
  AnnulationSaisie,
  BilanSaisie,
  ClotureEchecSaisie,
  EtapeCorrectiveSaisie,
  NoGoSaisie,
  PilotageSaisie,
  ReportSaisie,
  SuppressionMepSaisie,
  HistoriqueSysteme,
  CritereGoNoGo,
  CritereGoNoGoSaisie,
  DecisionApprobation,
  DuplicationSaisie,
  InfosApplication,
  MiseAJour,
  ParametresGlobaux,
  ApercuValidation,
  ModeleRecette,
  ModeleRecetteSaisie,
  ModeleEtape,
  ModeleEtapeSaisie,
  Reglage,
  EtatCharteServeur,
  VerificationCharte,
  PieceJointe,
  FicheRecetteSaisie,
  ProfilSaisie,
  SauvegardeSystemeSaisie,
  Evenement,
  TestsUnitaires,
  Presence,
  EtapeSaisie,
  ObjetsSystemeSaisie,
  ImportObjets,
  ObjetCatalogue,
  ObjetCatalogueSaisie,
  CategorieSaisie,
  EnvironnementCatalogue,
  Mep,
  MepIdentiteSaisie,
  MepResume,
  ParticipantSaisie,
  PlanRepliSaisie,
  ResolutionType,
  ResolutionTypeSaisie,
  ResultatControle,
  SauvegardeSaisie,
  TypeMep,
  TypeMepSaisie,
  VersionResolutionType,
  ResponsableTechniqueSaisie,
  SystemeCatalogue,
  SystemeCatalogueSaisie,
  SystemeSaisie,
  TypePlan,
  Utilisateur,
  CompteAnnuaire,
  Equipe,
  EquipeSaisie,
  EquipeCommunicationSaisie,
  CommunicationSaisie,
  ModeleTexte,
  ModeleTexteSaisie,
  VariableTexte,
  VariablesMep,
  LiaisonCompteSaisie,
  ChangementMotDePasseSaisie,
  CompteAvecMotDePasse,
  CompteSaisie,
  EvenementAdmin,
  NouveauCompteSaisie,
  OptionsConnexion,
  EtatDemo,
  ReinitialisationDemo,
  EtatSimulation,
  SsoIdentite,
  SsoLiaison,
  SsoProfil,
  SsoReglages,
  SsoSaisie,
  SsoTest,
  RapportFin,
  Suivi,
} from './types';

export class ApiError extends Error {
  status: number;
  /** Liste détaillée des anomalies renvoyée par le serveur (champ `erreurs`), vide sinon. */
  erreurs: string[];
  constructor(status: number, message: string, erreurs: string[] = []) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.erreurs = erreurs;
  }
}

let onUnauthorized: () => void = () => {
  if (window.location.pathname !== '/connexion') window.location.assign('/connexion');
};
export function setUnauthorizedHandler(fn: () => void) {
  onUnauthorized = fn;
}

// ---------- Requêtes en cours : l'indicateur discret de chargement (`ActiviteReseau`) les suit.
let enCours = 0;
const ecouteursActivite = new Set<(n: number) => void>();
function compter(d: number) {
  enCours += d;
  for (const f of ecouteursActivite) f(enCours);
}
/** Suit le nombre de requêtes en cours (hors requêtes silencieuses) ; renvoie de quoi arrêter le suivi. */
export function suivreActivite(f: (n: number) => void): () => void {
  ecouteursActivite.add(f);
  f(enCours);
  return () => ecouteursActivite.delete(f);
}

export interface OptionsRequete {
  /** Requête de fond (présence, verrou, sondage périodique) : pas d'indicateur de chargement. */
  silencieux?: boolean;
}

/** Requête vers l'API (`/api` + chemin) : erreurs en `ApiError`, 401 renvoie à la connexion. */
export async function request<T>(
  method: string,
  path: string,
  body?: unknown,
  options: OptionsRequete = {},
): Promise<T> {
  if (options.silencieux) return envoyer<T>(method, path, body);
  compter(1);
  try {
    return await envoyer<T>(method, path, body);
  } finally {
    compter(-1);
  }
}

async function envoyer<T>(method: string, path: string, body?: unknown): Promise<T> {
  const formulaire = typeof FormData !== 'undefined' && body instanceof FormData;
  let res: Response;
  try {
    res = await fetch(`/api${path}`, {
      method,
      credentials: 'include',
      // X-Requested-With : exigé par l'API sur toute requête qui modifie (protection CSRF, un formulaire d'un autre site ne peut pas l'ajouter).
      headers:
        body !== undefined && !formulaire
          ? { 'Content-Type': 'application/json', 'X-Requested-With': 'fetch' }
          : { 'X-Requested-With': 'fetch' },
      body: body === undefined ? undefined : formulaire ? (body as FormData) : JSON.stringify(body),
    });
  } catch {
    throw new ApiError(0, 'Le serveur est injoignable. Vérifiez votre connexion.');
  }
  if (res.status === 401 && path !== '/auth/connexion' && path !== '/demo/connexion') {
    onUnauthorized();
    throw new ApiError(401, 'Session expirée, veuillez vous reconnecter.');
  }
  if (!res.ok) {
    let message = '';
    let erreurs: string[] = [];
    try {
      const data = await res.json();
      // Corps « problem details » (RFC 9457) : `message` (repris de `detail`), et `erreurs` quand il y a plusieurs motifs.
      message = typeof data?.message === 'string' ? data.message : typeof data?.detail === 'string' ? data.detail : '';
      if (Array.isArray(data?.erreurs))
        erreurs = data.erreurs.filter((e: unknown): e is string => typeof e === 'string');
    } catch {
      /* corps non JSON */
    }
    if (!message) {
      message =
        res.status === 401
          ? 'Identifiants incorrects.'
          : res.status === 413
            ? 'Fichier trop volumineux.'
            : `Erreur ${res.status}.`;
    }
    throw new ApiError(res.status, message, erreurs);
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

/**
 * Mode démonstration : comptes de la connexion rapide, ou null si la démonstration n'est pas active (route absente, 404)
 * ou injoignable. Lu sans passer par `request` : une réponse 401 ne doit pas renvoyer vers l'écran de connexion.
 */
function estDemoActive(v: unknown): v is EtatDemo {
  return (
    typeof v === 'object' &&
    v !== null &&
    'actif' in v &&
    v.actif === true &&
    'comptes' in v &&
    Array.isArray(v.comptes)
  );
}

async function lireDemo(): Promise<EtatDemo | null> {
  try {
    const r = await fetch('/api/demo', { credentials: 'include', headers: { 'X-Requested-With': 'fetch' } });
    if (!r.ok) return null;
    const etat: unknown = await r.json();
    return estDemoActive(etat) ? etat : null;
  } catch {
    return null;
  }
}

let demoLu: Promise<EtatDemo | null> | null = null;

export const urlPieceJointe = (pid: string) => `/api/pieces-jointes/${pid}/contenu`;

/** Paramètres d'adresse (« ?a=1&b=2 »), sans les valeurs vides ; « » s'il n'en reste aucun. */
function requete(valeurs: Record<string, string | undefined>): string {
  const p = new URLSearchParams();
  for (const [cle, valeur] of Object.entries(valeurs)) if (valeur) p.set(cle, valeur);
  const texte = p.toString();
  return texte ? `?${texte}` : '';
}

const get = <T>(p: string, options?: OptionsRequete) => request<T>('GET', p, undefined, options);
const post = <T>(p: string, b?: unknown) => request<T>('POST', p, b ?? {});
const put = <T>(p: string, b: unknown, options?: OptionsRequete) => request<T>('PUT', p, b, options);
const del = <T>(p: string, b?: unknown) => request<T>('DELETE', p, b);

export const api = {
  connexion: (login: string, motDePasse: string) => post<Utilisateur>('/auth/connexion', { login, motDePasse }),
  deconnexion: () => post<void>('/auth/deconnexion'),
  moi: () => get<Utilisateur>('/auth/moi'),
  optionsConnexion: () => get<OptionsConnexion>('/auth/options'),
  // Mode démonstration (routes absentes hors démonstration).
  /** Lu une fois par page (le bandeau et le premier affichage l'attendent ensemble : pas de bandeau qui surgit). */
  demo: () => (demoLu ??= lireDemo()),
  connexionDemo: (login: string) => post<Utilisateur>('/demo/connexion', { login }),
  reinitialiserDemo: () => post<ReinitialisationDemo>('/demo/reinitialiser'),
  simulationDemo: () => get<EtatSimulation>('/demo/simulation', { silencieux: true }),
  simulerCollegues: (actif: boolean) => post<EtatSimulation>('/demo/simulation', { actif }),
  ssoEtat: () => get<SsoProfil>('/auth/sso/etat'),
  ssoLier: () => post<SsoLiaison>('/auth/sso/lier'),
  ssoDelierMoi: () => del<void>('/auth/sso/lien'),
  utilisateurs: () => get<CompteAnnuaire[]>('/utilisateurs'),
  modifierMonProfil: (s: ProfilSaisie) => put<Utilisateur>('/utilisateurs/moi/profil', s),
  changerMotDePasse: (s: ChangementMotDePasseSaisie) => post<Utilisateur>('/auth/mot-de-passe', s),

  // Console d'administration (réservée aux administrateurs).
  adminUtilisateurs: () => get<Utilisateur[]>('/admin/utilisateurs'),
  adminCreerUtilisateur: (s: NouveauCompteSaisie) => post<CompteAvecMotDePasse>('/admin/utilisateurs', s),
  adminModifierUtilisateur: (id: string, s: CompteSaisie) => put<Utilisateur>(`/admin/utilisateurs/${id}`, s),
  adminDesactiver: (id: string) => post<Utilisateur>(`/admin/utilisateurs/${id}/desactiver`),
  adminReactiver: (id: string) => post<Utilisateur>(`/admin/utilisateurs/${id}/reactiver`),
  adminReinitialiserMotDePasse: (id: string) =>
    post<CompteAvecMotDePasse>(`/admin/utilisateurs/${id}/reinitialiser-mot-de-passe`),
  adminRelier: (id: string, s: LiaisonCompteSaisie) => post<Utilisateur>(`/admin/utilisateurs/${id}/relier`, s),
  adminDelier: (id: string) => post<Utilisateur>(`/admin/utilisateurs/${id}/delier`),
  equipes: () => get<Equipe[]>('/equipes'),
  adminCreerEquipe: (s: EquipeSaisie) => post<Equipe>('/admin/equipes', s),
  adminModifierEquipe: (id: string, s: EquipeSaisie) => put<Equipe>(`/admin/equipes/${id}`, s),
  adminSupprimerEquipe: (id: string) => del<void>(`/admin/equipes/${id}`),
  ssoReglages: () => get<SsoReglages>('/admin/sso'),
  ssoEnregistrer: (s: SsoSaisie) => put<SsoReglages>('/admin/sso', s),
  ssoActiver: () => post<SsoReglages>('/admin/sso/activer'),
  ssoDesactiver: () => post<SsoReglages>('/admin/sso/desactiver'),
  ssoTester: () => post<SsoTest>('/admin/sso/tester'),
  ssoIdentites: () => get<SsoIdentite[]>('/admin/sso/identites'),
  ssoDelier: (id: string) => del<void>(`/admin/sso/identites/${id}`),
  adminJournal: (limite = 200) => get<EvenementAdmin[]>(`/admin/journal?limite=${limite}`),

  meps: (modele?: boolean) => get<MepResume[]>(`/meps${modele === undefined ? '' : `?modele=${modele}`}`),
  creerMep: (outil: string, objet: string, modele = false, type: TypeMep = 'Complete') =>
    post<Mep>('/meps', { outil, objet, modele, type }),
  /** Type de la MEP, en préparation : passer en simplifiée efface durées, criticités, résolutions liées et versions. */
  changerTypeMep: (id: string, s: TypeMepSaisie) => put<Mep>(`/meps/${id}/type`, s),
  /** `silencieux` : relecture périodique, sans indicateur de chargement. */
  mep: (id: string, silencieux = false) => get<Mep>(`/meps/${id}`, { silencieux }),
  modifierIdentite: (id: string, s: MepIdentiteSaisie) => put<Mep>(`/meps/${id}`, s),
  modifierSauvegarde: (id: string, s: SauvegardeSaisie) => put<Mep>(`/meps/${id}/sauvegarde`, s),
  modifierCommunication: (id: string, s: CommunicationSaisie) => put<Mep>(`/meps/${id}/communication`, s),
  /** Valeurs des variables des modèles de texte ; `equipe` absente : équipe proposée (celle du pilote). */
  variablesTexte: (id: string, equipe?: string) =>
    get<VariablesMep>(`/meps/${id}/variables-texte${equipe ? `?equipe=${encodeURIComponent(equipe)}` : ''}`),
  journal: (id: string) => get<Evenement[]>(`/meps/${id}/journal`),
  rapport: (id: string) => get<RapportFin>(`/meps/${id}/rapport`),
  suivi: () => get<Suivi>('/suivi'),
  /** Tests unitaires faits, non faits et leurs résultats constatés. */
  testsUnitaires: (id: string) => get<TestsUnitaires>(`/meps/${id}/tests-unitaires`),
  presences: (id: string) => get<Presence[]>(`/meps/${id}/presence`),
  /** Éléments sur lesquels travaille cet onglet (`session` : celle passée au flux). 404 si le flux n'est pas ouvert. */
  declarerPresence: (id: string, session: string, elements: string[]) =>
    put<void>(`/meps/${id}/presence`, { session, elements }, { silencieux: true }),

  ajouterParticipant: (id: string, s: ParticipantSaisie) => post<Mep>(`/meps/${id}/participants`, s),
  modifierParticipant: (id: string, pid: string, s: ParticipantSaisie) =>
    put<Mep>(`/meps/${id}/participants/${pid}`, s),
  supprimerParticipant: (id: string, pid: string) => del<Mep>(`/meps/${id}/participants/${pid}`),
  ajouterSysteme: (id: string, s: SystemeSaisie) => post<Mep>(`/meps/${id}/systemes`, s),
  modifierSysteme: (id: string, sid: string, s: SystemeSaisie) => put<Mep>(`/meps/${id}/systemes/${sid}`, s),
  supprimerSysteme: (id: string, sid: string) => del<Mep>(`/meps/${id}/systemes/${sid}`),
  modifierSauvegardeSysteme: (id: string, sid: string, s: SauvegardeSystemeSaisie) =>
    put<Mep>(`/meps/${id}/systemes/${sid}/sauvegarde`, s),
  // Environnements et objets impactés d'un système de la MEP (choisis dans la fiche du catalogue).
  environnementsSysteme: (id: string, sid: string, noms: string[]) =>
    put<Mep>(`/meps/${id}/systemes/${sid}/environnements`, { noms }),
  objetsSysteme: (id: string, sid: string, s: ObjetsSystemeSaisie) => put<Mep>(`/meps/${id}/systemes/${sid}/objets`, s),
  supprimerObjetImpacte: (id: string, oid: string) => del<Mep>(`/meps/${id}/objets/${oid}`),
  // Objets impactés en Excel : modèle vide ou export (téléchargement direct), puis import.
  urlObjetsExcel: (id: string, modele: boolean) => `/api/meps/${id}/objets/excel${modele ? '?modele=true' : ''}`,
  importerObjets: (id: string, f: File) => post<ImportObjets>(`/meps/${id}/objets/excel`, formulaireFichier(f)),

  /** Création : `ligne` absente = nouvelle ligne en bas, `position` absente = en fin de ligne ; liens par défaut calculés par le serveur. */
  ajouterEtape: (id: string, plan: TypePlan = 'Deploiement', ligne?: number, position?: number) =>
    post<Mep>(`/meps/${id}/etapes`, {
      plan,
      ...(ligne === undefined ? {} : { ligne }),
      ...(position === undefined ? {} : { position }),
    }),
  /**
   * Place une étape (ligne, position dans la ligne) ; `ligne` = nombre de lignes pour une nouvelle ligne en bas.
   * `nouvelleLigne` : l'étape ouvre une ligne à elle, insérée juste au-dessus de `ligne` (glisser entre deux lignes).
   */
  placerEtape: (id: string, eid: string, ligne: number, position: number, nouvelleLigne = false) =>
    post<Mep>(`/meps/${id}/etapes/${eid}/placer`, {
      ligne,
      position,
      liensParDefaut: true,
      ...(nouvelleLigne ? { nouvelleLigne } : {}),
    }),
  autoriserNonRetour: (id: string, eid: string, revision: number) =>
    post<Mep>(`/meps/${id}/etapes/${eid}/autoriser-non-retour`, { revision }),
  modifierEtape: (id: string, eid: string, s: EtapeSaisie) => put<Mep>(`/meps/${id}/etapes/${eid}`, s),
  supprimerEtape: (id: string, eid: string) => del<Mep>(`/meps/${id}/etapes/${eid}`),
  demarrerEtape: (id: string, eid: string, revision: number) =>
    post<Mep>(`/meps/${id}/etapes/${eid}/demarrer`, { revision }),
  terminerEtape: (
    id: string,
    eid: string,
    revision: number,
    resultat: 'Reussie' | 'Echec',
    preuve: string,
    resultatVerifie: boolean,
    commentaire?: string,
    resultatConstate?: string,
  ) =>
    post<Mep>(`/meps/${id}/etapes/${eid}/terminer`, {
      revision,
      resultat,
      preuve,
      resultatVerifie,
      ...(commentaire === undefined ? {} : { commentaire }),
      ...(resultatConstate === undefined ? {} : { resultatConstate }),
    }),
  /** Commentaire facultatif d'une étape (exécutant, pilote / suppléant, admin ; pendant l'exécution, la recette ou le retour arrière). */
  commenterEtape: (id: string, eid: string, revision: number, commentaire: string) =>
    put<Mep>(`/meps/${id}/etapes/${eid}/commentaire`, { revision, commentaire }),
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
  cloturerEnEchec: (id: string, s: ClotureEchecSaisie) => post<Mep>(`/meps/${id}/cloturer`, s),
  go: (id: string, criteresCoches: string[]) => post<Mep>(`/meps/${id}/phase`, { phase: 'Execution', criteresCoches }),
  noGo: (id: string, s: NoGoSaisie) => post<Mep>(`/meps/${id}/no-go`, s),
  passerEnRecette: (id: string) => post<Mep>(`/meps/${id}/phase`, { phase: 'Recette' }),
  dupliquer: (id: string, s: DuplicationSaisie) => post<Mep>(`/meps/${id}/dupliquer`, s),

  modifierApprobateurs: (id: string, version: number, utilisateurIds: string[]) =>
    put<Mep>(`/meps/${id}/approbateurs`, { version, utilisateurIds }),
  decider: (id: string, version: number, decision: DecisionApprobation, commentaire: string) =>
    post<Mep>(`/meps/${id}/approbation`, { version, decision, commentaire }),

  ajouterFicheRecette: (id: string, systemeId: string, titre: string) =>
    post<Mep>(`/meps/${id}/fiches-recette`, { systemeId, titre }),
  insererModeleRecette: (id: string, modeleId: string, systemeId: string) =>
    post<Mep>(`/meps/${id}/fiches-recette/depuis-modele`, { modeleId, systemeId }),
  modifierFicheRecette: (id: string, fid: string, s: FicheRecetteSaisie) =>
    put<Mep>(`/meps/${id}/fiches-recette/${fid}`, s),
  supprimerFicheRecette: (id: string, fid: string) => del<Mep>(`/meps/${id}/fiches-recette/${fid}`),
  resultatEtapeTest: (id: string, fid: string, tid: string, resultat: ResultatControle, observation: string) =>
    post<Mep>(`/meps/${id}/fiches-recette/${fid}/etapes/${tid}/resultat`, { resultat, observation }),
  refaireFicheRecette: (id: string, fid: string) => post<Mep>(`/meps/${id}/fiches-recette/${fid}/refaire`, {}),
  refaireEtapeTest: (id: string, fid: string, tid: string) =>
    post<Mep>(`/meps/${id}/fiches-recette/${fid}/etapes/${tid}/refaire`, {}),

  // Exports Excel des vues (téléchargement direct, en lecture seule) : préparation, étapes, retour arrière, journal avec
  // ses filtres. La liste des MEP filtrée : `urlExportListe` (rechercheMeps.ts).
  urlExportPreparation: (id: string) => `/api/meps/${id}/preparation/excel`,
  urlExportEtapes: (id: string) => `/api/meps/${id}/etapes/excel`,
  urlExportRetourArriere: (id: string) => `/api/meps/${id}/retour-arriere/excel`,
  urlExportJournal: (id: string, f: { type?: string; recherche?: string } = {}) =>
    `/api/meps/${id}/journal/excel${requete({ type: f.type === 'tous' ? '' : f.type, recherche: f.recherche?.trim() })}`,

  // Recette en Excel : export (téléchargement direct), aperçu puis report de la validation des métiers.
  urlExportRecette: (id: string) => `/api/meps/${id}/recette/excel`,
  urlExportRapport: (id: string) => `/api/meps/${id}/rapport/excel`,
  apercuValidation: (id: string, f: File) =>
    post<ApercuValidation>(`/meps/${id}/recette/excel/apercu`, formulaireFichier(f)),
  importerValidation: (id: string, f: File) => post<Mep>(`/meps/${id}/recette/excel/import`, formulaireFichier(f)),

  // Bibliothèque de recettes.
  modelesRecette: () => get<ModeleRecette[]>('/modeles-recette'),
  creerModeleRecette: (s: Omit<ModeleRecetteSaisie, 'version'>) => post<ModeleRecette>('/modeles-recette', s),
  modifierModeleRecette: (mid: string, s: ModeleRecetteSaisie) => put<ModeleRecette>(`/modeles-recette/${mid}`, s),
  supprimerModeleRecette: (mid: string) => del<void>(`/modeles-recette/${mid}`),
  modeleDepuisFiche: (mepId: string, ficheId: string, titre: string, systemeCatalogueId: string) =>
    post<ModeleRecette>('/modeles-recette/depuis-fiche', { mepId, ficheId, titre, systemeCatalogueId }),

  // Modèles de texte des équipes et informations communes d'une équipe.
  variablesModelesTexte: () => get<VariableTexte[]>('/modeles-texte/variables'),
  modelesTexte: (equipe?: string) =>
    get<ModeleTexte[]>(`/modeles-texte${equipe ? `?equipe=${encodeURIComponent(equipe)}` : ''}`),
  modeleTexte: (mid: string) => get<ModeleTexte>(`/modeles-texte/${mid}`),
  creerModeleTexte: (s: ModeleTexteSaisie) => post<ModeleTexte>('/modeles-texte', s),
  modifierModeleTexte: (mid: string, s: ModeleTexteSaisie) => put<ModeleTexte>(`/modeles-texte/${mid}`, s),
  retablirModeleTexte: (mid: string) => post<ModeleTexte>(`/modeles-texte/${mid}/retablir`),
  supprimerModeleTexte: (mid: string) => del<void>(`/modeles-texte/${mid}`),
  modifierCommunicationEquipe: (eid: string, s: EquipeCommunicationSaisie) =>
    put<Equipe>(`/equipes/${eid}/communication`, s),

  // Bibliothèque d'étapes modèles (triées par nombre d'utilisations, puis ordre de création).
  etapesModeles: () => get<ModeleEtape[]>('/etapes-modeles'),
  creerEtapeModele: (s: Omit<ModeleEtapeSaisie, 'version'>) => post<ModeleEtape>('/etapes-modeles', s),
  modifierEtapeModele: (mid: string, s: ModeleEtapeSaisie) => put<ModeleEtape>(`/etapes-modeles/${mid}`, s),
  supprimerEtapeModele: (mid: string) => del<void>(`/etapes-modeles/${mid}`),
  /** « Transformer en étape modèle » : copie d'une étape de MEP dans la bibliothèque, sous le nom donné. */
  etapeModeleDepuisEtape: (mepId: string, etapeId: string, action: string) =>
    post<ModeleEtape>('/etapes-modeles/depuis-etape', { mepId, etapeId, action }),
  /**
   * Insère une copie dans le plan : `ligne` absente = nouvelle ligne en bas, `position` absente = en fin de ligne ;
   * `nouvelleLigne` : la copie ouvre une ligne à elle, insérée juste au-dessus de `ligne`.
   */
  insererEtapeModele: (
    id: string,
    modeleId: string,
    plan: TypePlan,
    ligne?: number,
    position?: number,
    nouvelleLigne = false,
  ) =>
    post<Mep>(`/meps/${id}/etapes/depuis-modele`, {
      modeleId,
      plan,
      ...(ligne === undefined ? {} : { ligne }),
      ...(position === undefined ? {} : { position }),
      ...(nouvelleLigne ? { nouvelleLigne } : {}),
    }),

  // Pièces jointes : MEP, étape, résolution exécutée, résolution type.
  deposerPieceMep: (id: string, f: File) => post<Mep>(`/meps/${id}/pieces-jointes`, formulaireFichier(f)),
  deposerPieceEtape: (id: string, eid: string, f: File) =>
    post<Mep>(`/meps/${id}/etapes/${eid}/pieces-jointes`, formulaireFichier(f)),
  supprimerPiece: (id: string, pid: string) => del<Mep>(`/meps/${id}/pieces-jointes/${pid}`),
  piecesResolution: (rid: string) => get<PieceJointe[]>(`/resolutions/${rid}/pieces-jointes`),
  deposerPieceResolution: (rid: string, f: File) =>
    post<PieceJointe>(`/resolutions/${rid}/pieces-jointes`, formulaireFichier(f)),
  supprimerPieceResolution: (rid: string, pid: string) => del<void>(`/resolutions/${rid}/pieces-jointes/${pid}`),

  application: () => get<InfosApplication>('/application'),
  verifierMisesAJour: () => post<MiseAJour>('/admin/mises-a-jour/verifier', {}),
  installerMiseAJour: (version: string) => post<MiseAJour>('/admin/mises-a-jour/installer', { version }),

  parametres: () => get<ParametresGlobaux>('/parametres'),
  modifierParametres: (s: ParametresGlobaux) => put<ParametresGlobaux>('/parametres', s),
  reglages: () => get<Reglage[]>('/admin/reglages'),
  definirReglage: (nom: string, valeur: string) =>
    put<Reglage>(`/admin/reglages/${encodeURIComponent(nom)}`, { valeur }),
  retablirReglage: (nom: string) => del<Reglage>(`/admin/reglages/${encodeURIComponent(nom)}`),
  charte: () => get<EtatCharteServeur>('/admin/charte'),
  enregistrerCharte: (c: CharteFichier) => put<EtatCharteServeur>('/admin/charte', c),
  retablirCharte: () => del<EtatCharteServeur>('/admin/charte'),
  verifierCharte: (contenu: string) => post<VerificationCharte>('/admin/charte/verifier', { contenu }),
  criteresGoNoGo: () => get<CritereGoNoGo[]>('/criteres-go-no-go'),
  creerCritere: (s: CritereGoNoGoSaisie) => post<CritereGoNoGo>('/criteres-go-no-go', s),
  modifierCritere: (id: string, s: CritereGoNoGoSaisie) => put<CritereGoNoGo>(`/criteres-go-no-go/${id}`, s),
  supprimerCritere: (id: string) => del<void>(`/criteres-go-no-go/${id}`),

  catalogueSystemes: () => get<SystemeCatalogue[]>('/catalogue/systemes'),
  // Recherche avancée (#59) : activité de chaque système (colonne « Dernière MEP ») et historique des MEP d'un système.
  activiteSystemes: () => get<ActiviteSysteme[]>('/catalogue/systemes/activite'),
  historiqueSysteme: (id: string) => get<HistoriqueSysteme>(`/catalogue/systemes/${id}/historique`),
  urlExportHistorique: (id: string) => `/api/catalogue/systemes/${id}/historique/excel`,
  creerSystemeCatalogue: (s: SystemeCatalogueSaisie) => post<SystemeCatalogue>('/catalogue/systemes', s),
  modifierSystemeCatalogue: (id: string, s: SystemeCatalogueSaisie) =>
    put<SystemeCatalogue>(`/catalogue/systemes/${id}`, s),
  supprimerSystemeCatalogue: (id: string) => del<void>(`/catalogue/systemes/${id}`),
  // Fiche d'un système du catalogue : environnements, catégories, objets (modification réservée aux administrateurs).
  objetsCatalogue: (id: string) => get<ObjetCatalogue[]>(`/catalogue/systemes/${id}/objets`),
  environnementsCatalogue: (id: string, environnements: EnvironnementCatalogue[]) =>
    put<SystemeCatalogue>(`/catalogue/systemes/${id}/environnements`, { environnements }),
  categoriesCatalogue: (id: string, categories: CategorieSaisie[]) =>
    put<SystemeCatalogue>(`/catalogue/systemes/${id}/categories`, { categories }),
  ajouterObjetCatalogue: (id: string, s: ObjetCatalogueSaisie) =>
    post<ObjetCatalogue>(`/catalogue/systemes/${id}/objets`, s),
  modifierObjetCatalogue: (id: string, oid: string, s: ObjetCatalogueSaisie) =>
    put<ObjetCatalogue>(`/catalogue/systemes/${id}/objets/${oid}`, s),
  supprimerObjetCatalogue: (id: string, oid: string) => del<void>(`/catalogue/systemes/${id}/objets/${oid}`),
  categoriesParDefaut: () => get<string[]>('/catalogue/categories'),
  definirCategoriesParDefaut: (categories: string[]) => put<string[]>('/catalogue/categories', { categories }),
  ajouterSystemeDepuisCatalogue: (id: string, catalogueId: string, impact: string) =>
    post<Mep>(`/meps/${id}/systemes/depuis-catalogue`, { catalogueId, impact }),

  resolutions: (systemeCatalogueId?: string | null) =>
    get<ResolutionType[]>(
      `/resolutions${systemeCatalogueId ? `?systemeCatalogueId=${encodeURIComponent(systemeCatalogueId)}` : ''}`,
    ),
  creerResolution: (s: Omit<ResolutionTypeSaisie, 'version'>) => post<ResolutionType>('/resolutions', s),
  modifierResolution: (id: string, s: ResolutionTypeSaisie) => put<ResolutionType>(`/resolutions/${id}`, s),
  versionsResolution: (id: string) => get<VersionResolutionType[]>(`/resolutions/${id}/versions`),
  supprimerResolution: (id: string) => del<void>(`/resolutions/${id}`),

  procedureRelue: (id: string, relue: boolean) => post<Mep>(`/meps/${id}/procedure-relue`, { relue }),
  /** Retour en préparation (depuis l'exécution) ou en exécution (depuis la recette, « corriger en avant ») : motif obligatoire. */
  changerPhase: (id: string, phase: 'Preparation' | 'Execution', motif: string) =>
    post<Mep>(`/meps/${id}/phase`, { phase, motif }),

  // Sorties du cycle de vie (#93).
  annuler: (id: string, s: AnnulationSaisie) => post<Mep>(`/meps/${id}/annuler`, s),
  reporter: (id: string, s: ReportSaisie) => post<Mep>(`/meps/${id}/reporter`, s),
  reprendrePreparation: (id: string) => post<Mep>(`/meps/${id}/reprendre`),
  ajouterEtapeCorrective: (id: string, s: EtapeCorrectiveSaisie) => post<Mep>(`/meps/${id}/etapes-correctives`, s),
  changerPilote: (id: string, s: PilotageSaisie) => post<Mep>(`/meps/${id}/pilotage`, s),
  /** Suppression définitive (administrateur, MEP jamais lancée) : motif et nom de la MEP saisi pour confirmer. */
  supprimerMep: (id: string, s: SuppressionMepSaisie) => del<void>(`/meps/${id}`, s),
};
