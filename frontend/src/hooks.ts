import { createContext, useContext, useEffect, useId, useState } from 'react';

/**
 * Brouillon local d'un formulaire : tant que l'utilisateur n'a rien modifié, on affiche la source
 * (qui suit donc les rechargements) ; dès qu'il saisit, son brouillon prime.
 */
export function useDraft<T>(source: T) {
  const [draft, setDraft] = useState<T | null>(null);
  const dirty = draft !== null && JSON.stringify(draft) !== JSON.stringify(source);
  const value = dirty ? (draft as T) : source;
  return {
    value,
    dirty,
    update: (patch: Partial<T>) => setDraft({ ...value, ...patch }),
    reset: () => setDraft(null),
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
  const set = (v: T) => {
    setValue(v);
    try { window.localStorage.setItem(key, JSON.stringify(v)); } catch { /* stockage indisponible */ }
  };
  return [value, set];
}
