import { createContext, useContext, useEffect, useId, useState } from 'react';

/**
 * Brouillon local d'un formulaire : tant que l'utilisateur n'a rien modifié, on affiche la source
 * (qui suit donc les rechargements) ; dès qu'il saisit, son brouillon prime.
 *
 * Le brouillon mémorise aussi la source au moment où la saisie a commencé (`base`). Si la source
 * change ensuite (MEP rechargée par une autre action, avec la modification d'un collègue),
 * `conflit` passe à vrai : enregistrer écraserait ce changement sans que l'utilisateur l'ait vu.
 * La source ne doit donc contenir que les champs du formulaire (pas la version de la MEP).
 */
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
  };
}

/** Registre des formulaires en cours de saisie (pour ne pas écraser une saisie lors d'un rechargement). */
export interface DirtyRegistry {
  set: (id: string, dirty: boolean) => void;
}
export const DirtyContext = createContext<DirtyRegistry>({ set: () => undefined });

export function useReportDirty(dirty: boolean) {
  const registry = useContext(DirtyContext);
  const id = useId();
  useEffect(() => {
    registry.set(id, dirty);
    return () => registry.set(id, false);
  }, [registry, id, dirty]);
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

/** Charge des données asynchrones avec rechargement manuel. */
export function useAsync<T>(charger: () => Promise<T>, deps: unknown[]) {
  const [donnees, setDonnees] = useState<T | null>(null);
  const [erreur, setErreur] = useState('');
  const [tick, setTick] = useState(0);
  useEffect(() => {
    let annule = false;
    charger()
      .then((d) => {
        if (!annule) {
          setDonnees(d);
          setErreur('');
        }
      })
      .catch((e: unknown) => {
        if (!annule) setErreur(e instanceof Error ? e.message : 'Chargement impossible.');
      });
    return () => {
      annule = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [...deps, tick]);
  return { donnees, erreur, recharger: () => setTick((t) => t + 1), setDonnees };
}
