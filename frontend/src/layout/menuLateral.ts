import { useEffect, useSyncExternalStore } from 'react';

/**
 * Repli temporaire du menu latéral : un écran le demande tant qu'il a besoin de la largeur (étape ouverte en tiroir sur un
 * écran étroit), puis le menu revient tel que la personne l'avait laissé. Sa préférence (`mep.menu.replie`) n'est pas
 * modifiée. Un clic sur « Déplier le menu » pendant la demande la lève jusqu'à la fin de celle-ci.
 */
let demandes = 0;
let leve = false;
const abonnes = new Set<() => void>();
const prevenir = () => abonnes.forEach((f) => f());

function abonner(f: () => void) {
  abonnes.add(f);
  return () => {
    abonnes.delete(f);
  };
}

/** Le menu doit-il être replié en ce moment (en plus de la préférence de la personne) ? */
export function useMenuReplieTemporaire(): boolean {
  return useSyncExternalStore(
    abonner,
    () => demandes > 0 && !leve,
    () => false,
  );
}

/** « Déplier le menu » pendant une demande : le menu revient jusqu'à la fin de la demande en cours. */
export function leverRepliTemporaire() {
  leve = true;
  prevenir();
}

/** Demande le repli du menu latéral tant que `actif` est vrai. */
export function useRepliMenu(actif: boolean) {
  useEffect(() => {
    if (!actif) return undefined;
    demandes += 1;
    prevenir();
    return () => {
      demandes -= 1;
      if (demandes === 0) leve = false;
      prevenir();
    };
  }, [actif]);
}
