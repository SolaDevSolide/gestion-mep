import { createContext, useContext } from 'react';
import type { Etape, Mep } from '../../types';
import type { PlaceEtape } from './apiGraphe';
import type { ModeGraphe } from './CarteEtape';
import type { CibleDepot } from './connexions';
import type { Section } from './sections';

/** État partagé par les nœuds du graphe libre (les nœuds React Flow ne reçoivent que des identifiants). */
export interface ValeurGraphe {
  mep: Mep;
  mode: ModeGraphe;
  /** Édition : glisser, poignées de liens, « + » de création rapide. */
  edition: boolean;
  descriptions: Map<string, string>;
  etapes: ReadonlyMap<string, Etape>;
  sections: ReadonlyMap<string, Section>;
  /** Étapes de chaque section (pour le nombre et l'avancement). */
  etapesParSection: ReadonlyMap<string, Etape[]>;
  selection: string | null;
  /** Cible survolée pendant un glisser. */
  cible: CibleDepot | null;
  /** Carte en cours de glisser. */
  glisse: string | null;
  /** Une copie d'étape modèle attend d'être posée : les « + » posent la copie. */
  placement: boolean;
  /** Abscisses des cercles sur le bord des sections repliées. */
  cercles: ReadonlyMap<string, number>;
  /** Vue d'édition d'une section. */
  sectionVue: string | null;
  estEstompee?: (e: Etape) => boolean;
  onActiver: (id: string) => void;
  onAjouter?: (place: PlaceEtape, ancre: string | null) => void;
  onBasculer: (sectionId: string) => void;
  onEditerSection?: (sectionId: string) => void;
  onOuvrirCercle?: (cercleId: string) => void;
}

export const ContexteGraphe = createContext<ValeurGraphe | null>(null);

export function useGraphe(): ValeurGraphe {
  const v = useContext(ContexteGraphe);
  if (!v) throw new Error('useGraphe hors du graphe');
  return v;
}
