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
  /** Édition : glisser, poignées de liens, « + » de création rapide (hors de l'intérieur des sections dépliées du plan). */
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
  /**
   * Mise en avant : lien survolé (avec ses deux cartes) ou carte sélectionnée (avec ses liens) ; les autres liens s'estompent.
   * null : rien de mis en avant.
   */
  enAvant: { liens: ReadonlySet<string>; noeuds: ReadonlySet<string> } | null;
  /** Mouvement en cours (repli, dépli) : les liens de React Flow sont masqués, le graphe dessine ceux du mouvement. */
  enMouvement: boolean;
  /** Sections dont le repli ou le dépli se prépare (mesure et rangement de leur intérieur) : leur bouton le signale. */
  sectionsEnCours: ReadonlySet<string>;
  /** Réorganisation en cours : le nœud glissé change de place dans son niveau (gauche-droite). */
  reordre: string | null;
  estEstompee?: (e: Etape) => boolean;
  onActiver: (id: string) => void;
  /** « + » d'une carte : création à cette place (menu « Étape vierge / Depuis un modèle » ouvert sous `element`). */
  onAjouter?: (place: PlaceEtape, ancre: string | null, element: Element | null) => void;
  onBasculer: (sectionId: string) => void;
  onEditerSection?: (sectionId: string) => void;
  onOuvrirCercle?: (cercleId: string) => void;
  /** Vue d'ensemble (#61) : lecture seule, cartes et sections colorées par état ; étapes bloquées et leurs causes. */
  ensemble?: { bloquees: ReadonlyMap<string, Etape[]> } | null;
}

export const ContexteGraphe = createContext<ValeurGraphe | null>(null);

export function useGraphe(): ValeurGraphe {
  const v = useContext(ContexteGraphe);
  if (!v) throw new Error('useGraphe hors du graphe');
  return v;
}
