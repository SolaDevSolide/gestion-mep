import { useEffect, useMemo, useRef, useState } from 'react';
import type { ModeleGraphe } from './modeleGraphe';
import { ranger, type Rangement, type Taille } from './rangement';

export interface Range {
  modele: ModeleGraphe;
  rangement: Rangement;
  signature: string;
}

/** Ce qui change le rangement : nœuds, parents, liens et tailles (arrondies), pas le contenu des cartes. */
export function signatureRangement(modele: ModeleGraphe, tailles: ReadonlyMap<string, Taille>): string {
  const noeuds = modele.noeuds.map((n) => {
    const t = tailles.get(n.id);
    const cercles = [...(n.entrees ?? []), ...(n.sorties ?? [])].map((c) => c.id).join(',');
    return `${n.id}<${n.parent ?? ''}#${t ? `${Math.round(t.largeur / 8)}x${Math.round(t.hauteur / 8)}` : ''}[${cercles}]`;
  });
  return `${noeuds.join(';')}|${modele.liens.map((l) => l.id).join(';')}`;
}

/**
 * Rangement ELK du modèle, avec gel (protection 1) : tant que `gele` est vrai (glisser, tracé de lien, saisie en cours),
 * les changements reçus attendent (`enAttente`) et le rangement affiché ne bouge pas. Un nouveau `jeton` (action de la
 * personne elle-même) force le rangement malgré le gel.
 */
export function useRangement(modele: ModeleGraphe, tailles: ReadonlyMap<string, Taille>, gele: boolean, jeton = 0) {
  const [range, setRange] = useState<Range | null>(null);
  const signature = useMemo(() => signatureRangement(modele, tailles), [modele, tailles]);
  const jetonApplique = useRef(jeton);
  const applique = range?.signature ?? null;

  const force = jeton !== jetonApplique.current;
  const attendre = gele && !force && applique !== null;

  useEffect(() => {
    if (attendre) return;
    if (signature === applique) {
      jetonApplique.current = jeton;
      return;
    }
    let annule = false;
    ranger(modele, tailles)
      .then((rangement) => {
        if (annule) return;
        jetonApplique.current = jeton;
        setRange({ modele, rangement, signature });
      })
      .catch(() => {
        /* rangement impossible : l'affichage précédent reste */
      });
    return () => {
      annule = true;
    };
  }, [modele, tailles, signature, attendre, applique, jeton]);

  return { range, enAttente: attendre && signature !== applique };
}
