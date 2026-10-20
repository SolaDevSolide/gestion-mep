import { createContext, useContext, useEffect, useId, useRef, useState } from 'react';
import { reprendreBrouillon } from './brouillons';

/**
 * Brouillon local d'un formulaire : tant que l'utilisateur n'a rien modifié, on affiche la source
 * (qui suit donc les rechargements) ; dès qu'il saisit, son brouillon prime.
 *
 * Le brouillon mémorise aussi la source au moment où la saisie a commencé (`base`). Si la source
 * change ensuite (MEP rechargée par une autre action, avec la modification d'un collègue),
 * `conflit` passe à vrai : enregistrer écraserait ce changement sans que l'utilisateur l'ait vu.
 * La source ne doit donc contenir que les champs du formulaire (pas la version de la MEP).
 */
/** Copie d'une saisie relue du stockage de session : sa forme seule est vérifiable. */
function estCopie<T>(v: unknown): v is { draft: T; base: T } {
  return typeof v === 'object' && v !== null && 'draft' in v && 'base' in v;
}

export function useDraft<T>(source: T) {
  const [etat, setEtat] = useState<{ draft: T; base: T } | null>(null);
  const dirty = etat !== null && JSON.stringify(etat.draft) !== JSON.stringify(source);
  const value = dirty ? etat.draft : source;
  const base = dirty ? etat.base : source;
  const conflit = dirty && JSON.stringify(etat.base) !== JSON.stringify(source);
  return {
    value,
    dirty,
    conflit,
    update: (patch: Partial<T>) => setEtat({ draft: { ...value, ...patch }, base }),
    /** Remplace tout le brouillon (valeur non objet, ex. une liste). */
    setValeur: (v: T) => setEtat({ draft: v, base }),
    reset: () => setEtat(null),
    /** Garde la saisie en connaissance de cause : la version à jour devient la nouvelle base. */
    garderSaisie: () => setEtat((e) => (e ? { draft: e.draft, base: source } : e)),
    /** Saisie et base, copiables avant une redirection (reconnexion Microsoft) ; null sans saisie. */
    lireBrouillon: (): unknown => (dirty ? etat : null),
    /**
     * Reprend une saisie copiée avant une redirection. Si la source a changé depuis, `conflit` le signale comme pour
     * une modification d'un collègue.
     */
    restaurerBrouillon: (v: unknown) => {
      if (estCopie<T>(v)) setEtat({ draft: v.draft, base: v.base });
    },
  };
}

/** Saisie qu'un formulaire sait copier puis reprendre (`useDraft`) : passée à `useReportDirty`. */
export interface BrouillonSauvegardable {
  lireBrouillon: () => unknown;
  restaurerBrouillon: (v: unknown) => void;
}

/**
 * Registre des formulaires en cours de saisie : sert à prévenir avant de quitter une vue avec des modifications non
 * enregistrées (`GardeModifications`), en nommant ce qui serait perdu (`libelle`).
 */
export interface DirtyRegistry {
  /** `lire` : copie de la saisie, gardée avant une redirection (reconnexion Microsoft). */
  set: (id: string, dirty: boolean, libelle?: string, lire?: () => unknown) => void;
  /** Libellés des saisies non enregistrées (« Étape E0002 », « Intervention »). */
  libelles: () => string[];
  /** Saisies non enregistrées qui savent se copier, par libellé. */
  brouillons: () => { libelle: string; valeur: unknown }[];
  /** Oublie les saisies en cours : plus d'avertissement en quittant (saisie abandonnée en connaissance de cause). */
  abandonner: () => void;
}
export const DirtyContext = createContext<DirtyRegistry>({
  set: () => undefined,
  libelles: () => [],
  brouillons: () => [],
  abandonner: () => undefined,
});

/**
 * Signale une saisie non enregistrée ; `libelle` la nomme dans la fenêtre « Quitter sans enregistrer ? ». Avec
 * `brouillon` (le résultat de `useDraft`), la saisie est copiée avant une reconnexion Microsoft et reprise au retour sur
 * la même page.
 */
export function useReportDirty(dirty: boolean, libelle?: string, brouillon?: BrouillonSauvegardable) {
  const registry = useContext(DirtyContext);
  const id = useId();
  const ref = useRef(brouillon);
  useEffect(() => {
    ref.current = brouillon;
  });
  const sauvegardable = brouillon !== undefined;
  useEffect(() => {
    registry.set(id, dirty, libelle, sauvegardable ? () => ref.current?.lireBrouillon() : undefined);
    return () => registry.set(id, false);
  }, [registry, id, dirty, libelle, sauvegardable]);
  // Retour d'une reconnexion Microsoft : la saisie copiée pour ce formulaire (même page, même libellé) est reprise.
  useEffect(() => {
    if (!libelle || !ref.current) return;
    const v = reprendreBrouillon(window.location.pathname, libelle);
    if (v !== undefined) ref.current.restaurerBrouillon(v);
  }, [libelle]);
}

export function useLocalStorageState<T>(key: string, initial: T): [T, (v: T) => void] {
  const [value, setValue] = useState<T>(() => {
    try {
      const raw = window.localStorage.getItem(key);
      return raw === null ? initial : (JSON.parse(raw) as T);
    } catch {
      return initial;
    }
  });
  // Changement de clé (autre MEP) : relecture.
  const [cle, setCle] = useState(key);
  if (cle !== key) {
    setCle(key);
    try {
      const raw = window.localStorage.getItem(key);
      setValue(raw === null ? initial : (JSON.parse(raw) as T));
    } catch {
      setValue(initial);
    }
  }
  const set = (v: T) => {
    setValue(v);
    try {
      window.localStorage.setItem(key, JSON.stringify(v));
    } catch {
      /* stockage indisponible */
    }
  };
  return [value, set];
}

/** Horloge : se met à jour toutes les `ms` millisecondes. */
export function useNow(ms = 1000): number {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const t = window.setInterval(() => setNow(Date.now()), ms);
    return () => window.clearInterval(t);
  }, [ms]);
  return now;
}

/**
 * Charge des données asynchrones avec rechargement manuel. Pendant un rechargement (`recharger`, autres `deps`), les
 * données précédentes restent affichées : `chargement` est vrai jusqu'à la réponse (`rechargement` : vrai s'il y avait
 * déjà des données, pour un indicateur discret plutôt qu'un squelette).
 */
export function useAsync<T>(charger: () => Promise<T>, deps: unknown[]) {
  const [donnees, setDonnees] = useState<T | null>(null);
  const [erreur, setErreur] = useState('');
  // Erreur reçue telle quelle (`ApiError` : code, requête) : `ErreurChargement` en tire son message et ses détails.
  const [echec, setEchec] = useState<unknown>(null);
  const [tick, setTick] = useState(0);
  const cle = [...deps, tick];
  // Clé de la dernière réponse reçue (réussie ou non) : différente de `cle` tant qu'une demande est en cours.
  const [cleRecue, setCleRecue] = useState<unknown[] | null>(null);
  useEffect(() => {
    let annule = false;
    const demandee = cle;
    charger()
      .then((d) => {
        if (!annule) {
          setDonnees(d);
          setErreur('');
          setEchec(null);
          setCleRecue(demandee);
        }
      })
      .catch((e: unknown) => {
        if (!annule) {
          setErreur(e instanceof Error ? e.message : 'Chargement impossible.');
          setEchec(e ?? new Error('Chargement impossible.'));
          setCleRecue(demandee);
        }
      });
    return () => {
      annule = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [...deps, tick]);
  const chargement = !(cleRecue?.length === cle.length && cleRecue.every((v, i) => Object.is(v, cle[i])));
  return {
    donnees,
    erreur,
    /** Erreur de la dernière réponse (null si elle a réussi), pour `ErreurChargement`. */
    echec,
    chargement,
    rechargement: chargement && donnees !== null,
    recharger: () => setTick((t) => t + 1),
    setDonnees,
  };
}
