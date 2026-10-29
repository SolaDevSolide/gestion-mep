// Brouillon de l'éditeur d'une connexion : passage connexion ↔ saisie, règles visibles avant l'enregistrement.
import { finOrigine } from '../../textes/gabarits';
import type {
  CatalogueConnexions,
  ChampConnexion,
  ConnexionApi,
  ConnexionApiSaisie,
  FiltresConnexion,
  ModeleConnexion,
  SecretEtat,
} from '../../types';

export interface EnteteBrouillon {
  nom: string;
  valeur: string;
  secret: boolean;
  /** Nouvelle valeur secrète ; vide : celle qui est enregistrée est gardée. */
  valeurSecrete: string;
  enregistre: SecretEtat | null;
}

export interface Brouillon {
  nom: string;
  description: string;
  modele: ModeleConnexion;
  methode: ConnexionApiSaisie['methode'];
  url: string;
  urlSecrete: boolean;
  adresseSecrete: string;
  authentification: ConnexionApiSaisie['authentification'];
  utilisateur: string;
  nomEntete: string;
  tokenUrl: string;
  clientId: string;
  scope: string;
  secret: string;
  entetes: EnteteBrouillon[];
  signature: boolean;
  secretSignature: string;
  format: ConnexionApiSaisie['format'];
  corps: string;
  champs: ChampConnexion[];
  delaiSecondes: number;
  tentatives: number;
  declencheurs: string[];
  filtres: FiltresConnexion;
  envoiManuel: boolean;
  libelleEnvoiManuel: string;
  active: boolean;
}

export const FILTRES_VIDES: FiltresConnexion = {
  equipeIds: [],
  systemeCatalogueIds: [],
  environnements: [],
  niveauxRisque: [],
  criticitesEtape: [],
  typesMep: [],
};

const nonNul = <T>(x: T | null): x is T => x !== null;

/** Filtres reçus (listes complètes, sans valeur nulle). */
export function filtresDe(f: ConnexionApi['filtres'] | null | undefined): FiltresConnexion {
  return {
    equipeIds: f?.equipeIds ?? [],
    systemeCatalogueIds: f?.systemeCatalogueIds ?? [],
    environnements: f?.environnements ?? [],
    niveauxRisque: (f?.niveauxRisque ?? []).filter(nonNul),
    criticitesEtape: (f?.criticitesEtape ?? []).filter(nonNul),
    typesMep: f?.typesMep ?? [],
  };
}

/** Brouillon d'une connexion enregistrée (aucun secret : les champs secrets partent vides). */
export function depuisConnexion(c: ConnexionApi): Brouillon {
  return {
    nom: c.nom,
    description: c.description,
    modele: c.modele ?? 'Libre',
    methode: c.methode ?? 'Post',
    url: c.url,
    urlSecrete: c.urlSecrete,
    adresseSecrete: '',
    authentification: c.authentification ?? 'Aucune',
    utilisateur: c.utilisateur,
    nomEntete: c.nomEntete,
    tokenUrl: c.tokenUrl,
    clientId: c.clientId,
    scope: c.scope,
    secret: '',
    entetes: c.entetes.map((e) => ({
      nom: e.nom,
      valeur: e.valeur,
      secret: e.secret,
      valeurSecrete: '',
      enregistre: e.valeurSecrete,
    })),
    signature: c.signature,
    secretSignature: '',
    format: c.format ?? 'Json',
    corps: c.corps,
    champs: c.champs.map((x) => ({ nom: x.nom ?? '', valeur: x.valeur ?? '' })),
    delaiSecondes: c.delaiSecondes,
    tentatives: c.tentatives,
    declencheurs: [...c.declencheurs],
    filtres: filtresDe(c.filtres),
    envoiManuel: c.envoiManuel,
    libelleEnvoiManuel: c.libelleEnvoiManuel,
    active: c.active,
  };
}

/** Copie d'une connexion (« Dupliquer ») : tout sauf les secrets, désactivée tant qu'ils ne sont pas saisis. */
export function copie(c: ConnexionApi): Brouillon {
  const b = depuisConnexion(c);
  return {
    ...b,
    nom: `${c.nom} (copie)`,
    url: c.urlSecrete ? '' : c.url,
    entetes: b.entetes.map((e) => ({ ...e, enregistre: null })),
  };
}

/** Brouillon d'une nouvelle connexion à partir d'un modèle de départ. */
export function depuisModele(catalogue: CatalogueConnexions, modele: ModeleConnexion, nom: string): Brouillon {
  const m = catalogue.modeles.find((x) => x.modele === modele) ?? catalogue.modeles[catalogue.modeles.length - 1];
  return {
    nom,
    description: '',
    modele: m?.modele ?? 'Libre',
    methode: m?.methode ?? 'Post',
    url: '',
    urlSecrete: m?.urlSecrete ?? false,
    adresseSecrete: '',
    authentification: m?.authentification ?? 'Aucune',
    utilisateur: '',
    nomEntete: '',
    tokenUrl: '',
    clientId: '',
    scope: '',
    secret: '',
    entetes: [],
    signature: m?.signature ?? false,
    secretSignature: '',
    format: m?.format ?? 'Json',
    corps: m?.corps ?? '',
    champs: [],
    delaiSecondes: 10,
    tentatives: 3,
    declencheurs: [...(m?.declencheurs ?? [])],
    filtres: { ...FILTRES_VIDES },
    envoiManuel: m?.envoiManuel ?? false,
    libelleEnvoiManuel: m?.libelleEnvoiManuel ?? '',
    active: true,
  };
}

/** Saisie envoyée au serveur : les secrets vides ne partent pas (le serveur garde ceux qui sont enregistrés). */
export function versSaisie(b: Brouillon, version?: number): ConnexionApiSaisie {
  const secret = b.secret.trim();
  const adresseSecrete = b.adresseSecrete.trim();
  const secretSignature = b.secretSignature.trim();
  return {
    ...(version !== undefined ? { version } : {}),
    nom: b.nom.trim(),
    description: b.description.trim(),
    modele: b.modele,
    methode: b.methode,
    url: b.url.trim(),
    urlSecrete: b.urlSecrete,
    ...(b.urlSecrete && adresseSecrete ? { adresseSecrete } : {}),
    authentification: b.authentification,
    utilisateur: b.utilisateur.trim(),
    nomEntete: b.nomEntete.trim(),
    tokenUrl: b.tokenUrl.trim(),
    clientId: b.clientId.trim(),
    scope: b.scope.trim(),
    ...(secret ? { secret } : {}),
    entetes: b.entetes.map((e) => ({
      nom: e.nom.trim(),
      valeur: e.secret ? '' : e.valeur,
      secret: e.secret,
      ...(e.secret && e.valeurSecrete.trim() ? { valeurSecrete: e.valeurSecrete.trim() } : {}),
    })),
    signature: b.signature,
    ...(b.signature && secretSignature ? { secretSignature } : {}),
    format: b.format,
    corps: b.format === 'Json' || b.format === 'Texte' ? b.corps : '',
    champs: b.format === 'Formulaire' ? b.champs : [],
    delaiSecondes: b.delaiSecondes,
    tentatives: b.tentatives,
    declencheurs: b.declencheurs,
    filtres: b.filtres,
    envoiManuel: b.envoiManuel,
    libelleEnvoiManuel: b.libelleEnvoiManuel.trim(),
    active: b.active,
  };
}

/** « https://hote[:port] » d'une adresse, en minuscules ; vide si elle n'est pas lisible (même règle que le serveur). */
export function origine(url: string): string {
  const debut = url.slice(0, finOrigine(url)).trim();
  try {
    const u = new URL(debut);
    if (u.protocol !== 'https:' && u.protocol !== 'http:') return '';
    return `${u.protocol}//${u.host}`.toLowerCase();
  } catch {
    return '';
  }
}

/** Hôte d'une origine (« outil.exemple.invalid »). */
export function hote(o: string): string {
  try {
    return new URL(o).hostname;
  } catch {
    return o;
  }
}

/** Origine à laquelle les secrets sont liés (adresse, et point de jeton en OAuth2), comme le serveur. */
export function origineSecrets(
  b: Pick<Brouillon, 'url' | 'urlSecrete' | 'adresseSecrete' | 'authentification' | 'tokenUrl'>,
  urlEnregistree: string,
) {
  const adresse = b.urlSecrete ? b.adresseSecrete.trim() || urlEnregistree : b.url;
  const o = origine(adresse);
  return b.authentification === 'OAuth2' ? `${o} ${origine(b.tokenUrl)}` : o;
}

/** Une connexion enregistrée a-t-elle au moins un secret ? */
export function aDesSecrets(c: ConnexionApi): boolean {
  return (
    c.adresseSecrete?.enregistre === true ||
    c.secret?.enregistre === true ||
    c.secretSignature?.enregistre === true ||
    c.entetes.some((e) => e.valeurSecrete?.enregistre === true)
  );
}
