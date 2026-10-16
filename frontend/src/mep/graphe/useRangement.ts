import { useEffect, useMemo, useRef, useState } from 'react';
import { mesuresManquantes } from './mesuresGraphe';
import type { ModeleGraphe } from './modeleGraphe';
import type { Rangement, Taille } from './rangement';
import { rangerBordure } from './rangementBordure';

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

/** Délai de secours : une mesure qui n'arrive jamais (cas anormal) n'empêche pas le rangement. */
export const SECOURS_MESURES_MS = 4000;

/**
 * Rangement ELK du modèle (cercles et titres des sections dépliées sur leurs bordures, `rangerBordure`), avec gel (protection 1) : tant que `gele` est vrai (glisser, tracé de lien, saisie en cours),
 * les changements reçus attendent (`enAttente`) et le rangement affiché ne bouge pas. Un nouveau `jeton` (action de la
 * personne elle-même) force le rangement malgré le gel.
 *
 * `attendreMesures` (graphe déjà affiché) : un changement qui fait paraître des nœuds pas encore mesurés (section qu'on
 * déplie ou replie, étape ajoutée) n'est rangé qu'une fois leurs tailles connues, en un seul rangement définitif ; le
 * graphe affiché ne bouge pas d'ici là. Le passage d'un état à l'autre dépend des mesures, pas d'un délai.
 */
export function useRangement(
  modele: ModeleGraphe,
  tailles: ReadonlyMap<string, Taille>,
  gele: boolean,
  jeton = 0,
  attendreMesures = false,
) {
  const [range, setRange] = useState<Range | null>(null);
  const signature = useMemo(() => signatureRangement(modele, tailles), [modele, tailles]);
  const manquantes = useMemo(() => mesuresManquantes(modele, tailles).length, [modele, tailles]);
  const jetonApplique = useRef(jeton);
  const applique = range?.signature ?? null;

  const force = jeton !== jetonApplique.current;
  const attendre = gele && !force && applique !== null;
  const mesuresAttendues = attendreMesures && applique !== null && manquantes > 0;
  // Secours (cas anormal seulement) : la mesure ne vient pas, le rangement se fait quand même.
  const [secours, setSecours] = useState<string | null>(null);
  useEffect(() => {
    if (!mesuresAttendues) return undefined;
    const t = window.setTimeout(() => setSecours(signature), SECOURS_MESURES_MS);
    return () => window.clearTimeout(t);
  }, [mesuresAttendues, signature]);
  const differe = mesuresAttendues && secours !== signature;

  useEffect(() => {
    if (attendre || differe) return;
    if (signature === applique) {
      jetonApplique.current = jeton;
      return;
    }
    let annule = false;
    rangerBordure(modele, tailles)
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
  }, [modele, tailles, signature, attendre, differe, applique, jeton]);

  return { range, enAttente: attendre && signature !== applique, mesuresAttendues: differe };
}
