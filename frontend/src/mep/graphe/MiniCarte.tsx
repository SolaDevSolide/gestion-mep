import { MiniMap } from '@xyflow/react';
import { useState } from 'react';
import { PREFIXE_MESURE } from './mesuresGraphe';

/** Taille de la mini-carte, en bas à gauche du graphe (la vue d'arrivée lui laisse sa place). */
export const MINI_CARTE = { largeur: 168, hauteur: 112 };

/** Préférence de mini-carte : affichée par défaut, sauf dans la vue d'ensemble (#61), qui a sa propre préférence. */
const cleMiniCarte = (ensemble: boolean) => (ensemble ? 'graphe.miniCarte.ensemble' : 'graphe.miniCarte');

function lireMiniCarte(ensemble: boolean): boolean {
  try {
    const v = window.localStorage.getItem(cleMiniCarte(ensemble));
    return ensemble ? v === 'oui' : v !== 'non';
  } catch {
    return !ensemble;
  }
}

/** Mini-carte affichée ou non, mémorisée dans le navigateur (préférence à part pour la vue d'ensemble). */
export function useMiniCarte(ensemble: boolean): [boolean, () => void] {
  const [miniCarte, setMiniCarte] = useState(() => lireMiniCarte(ensemble));
  const basculer = () =>
    setMiniCarte((v) => {
      try {
        window.localStorage.setItem(cleMiniCarte(ensemble), v ? 'non' : 'oui');
      } catch {
        /* préférence non mémorisée */
      }
      return !v;
    });
  return [miniCarte, basculer];
}

/** Mini-carte du graphe (à placer dans `ReactFlow`) : cadres, cercles et cartes ; les nœuds de mesure n'y paraissent pas. */
export function MiniCarte() {
  return (
    <MiniMap
      pannable
      zoomable
      position="bottom-left"
      ariaLabel="Mini-carte du graphe"
      style={{ width: MINI_CARTE.largeur, height: MINI_CARTE.hauteur }}
      className="!m-3 overflow-hidden rounded-lg border border-line !bg-surface-2 shadow-md"
      nodeColor={(n) =>
        n.id.startsWith(PREFIXE_MESURE)
          ? 'transparent'
          : n.type === 'cadre'
            ? 'var(--tertiaire-soft)'
            : n.type === 'cercle'
              ? 'var(--tertiaire)'
              : 'var(--line-strong)'
      }
      maskColor="color-mix(in srgb, var(--bg) 60%, transparent)"
      nodeBorderRadius={6}
    />
  );
}
