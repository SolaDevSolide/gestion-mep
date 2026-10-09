import { createContext, useContext } from 'react';
import type { Mep } from './types';

export interface MepContextValue {
  mep: Mep;
  /** Exécute une action API renvoyant la MEP à jour ; gère 409/422 et affiche le message. */
  run: (action: () => Promise<Mep>, succes?: string) => Promise<boolean>;
  notifier: (type: 'succes' | 'erreur', texte: string) => void;
}

export const MepContext = createContext<MepContextValue | null>(null);

export function useMep(): MepContextValue {
  const ctx = useContext(MepContext);
  if (!ctx) throw new Error('useMep hors MepContext');
  return ctx;
}
