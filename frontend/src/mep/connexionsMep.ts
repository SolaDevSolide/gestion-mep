// Connexions aux API externes vues d'une MEP (#111) : envois manuels proposés et échecs à montrer, relus quand le flux
// de la MEP annonce la fin d'un envoi (`event: appels`).
import { useEffect, useRef } from 'react';
import { api } from '../api';
import { useAsync } from '../hooks';

const ecouteurs = new Map<string, Set<() => void>>();

/** Un envoi de la MEP s'est terminé : les vues qui montrent ses connexions les relisent. */
export function signalerAppels(mepId: string) {
  for (const f of ecouteurs.get(mepId) ?? []) f();
}

/**
 * Envois manuels et échecs d'envoi d'une MEP (`GET /meps/{id}/connexions`). `signal` : relu quand il change (version de la
 * MEP) ; relu aussi à chaque fin d'envoi annoncée par le flux. Rien n'est lu pour un modèle.
 */
export function useConnexionsMep(mepId: string, estModele: boolean, signal: unknown) {
  const r = useAsync(() => (estModele ? Promise.resolve(null) : api.connexionsMep(mepId)), [mepId, estModele, signal]);
  const recharger = useRef(r.recharger);
  useEffect(() => {
    recharger.current = r.recharger;
  });
  useEffect(() => {
    const f = () => recharger.current();
    const liste = ecouteurs.get(mepId) ?? new Set<() => void>();
    liste.add(f);
    ecouteurs.set(mepId, liste);
    return () => {
      liste.delete(f);
      if (liste.size === 0) ecouteurs.delete(mepId);
    };
  }, [mepId]);
  return r;
}
