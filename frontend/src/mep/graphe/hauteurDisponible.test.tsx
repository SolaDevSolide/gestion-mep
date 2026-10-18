import { act, render } from '@testing-library/react';
import { useLayoutEffect, useRef } from 'react';
import { useHauteurDisponible } from './hauteurDisponible';

/**
 * Mise en page simulée (jsdom n'en a pas) : fenêtre de 900 px, graphe à 300 px du haut, une ligne de 25 px sous le graphe
 * (« Lecture seule… » de la vue d'ensemble). Comme sous Windows, la barre de défilement de la page prend de la place : elle
 * apparaît quand la page déborde et disparaît sinon, ce qui redimensionne le conteneur du graphe (ResizeObserver).
 */
const FENETRE = 900;
const HAUT = 300;
const LIGNE = 25;
const etat: { hauteur: number | null } = { hauteur: null };
let rappelsImage = new Map<number, FrameRequestCallback>();
let numeroImage = 0;
let observateurs: ResizeObserverCallback[] = [];
let ligneMesurable = true;

function Graphe() {
  const cadre = useRef<HTMLDivElement>(null);
  const hauteur = useHauteurDisponible(cadre, true);
  useLayoutEffect(() => {
    etat.hauteur = hauteur;
  });
  return (
    <div data-testid="conteneur">
      <div ref={cadre} data-testid="cadre" style={{ height: hauteur ?? 520 }} />
      <p data-testid="dessous">Lecture seule : cliquer une section pour la déplier.</p>
    </div>
  );
}

const hauteurPage = () => Math.max(FENETRE, HAUT + (etat.hauteur ?? 520) + LIGNE);
const rect = (top: number, height: number) =>
  ({
    top,
    bottom: top + height,
    height,
    left: 0,
    right: 1000,
    width: 1000,
    x: 0,
    y: top,
    toJSON: () => ({}),
  }) as DOMRect;

beforeEach(() => {
  etat.hauteur = null;
  rappelsImage = new Map();
  observateurs = [];
  vi.spyOn(window, 'innerHeight', 'get').mockReturnValue(FENETRE);
  vi.spyOn(document.documentElement, 'scrollHeight', 'get').mockImplementation(hauteurPage);
  vi.spyOn(window, 'requestAnimationFrame').mockImplementation((f) => {
    rappelsImage.set(++numeroImage, f);
    return numeroImage;
  });
  vi.spyOn(window, 'cancelAnimationFrame').mockImplementation((n) => {
    rappelsImage.delete(n);
  });
  vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(function (this: HTMLElement) {
    const h = etat.hauteur ?? 520;
    if (this.dataset.testid === 'cadre') return rect(HAUT, h);
    if (this.dataset.testid === 'dessous') return ligneMesurable ? rect(HAUT + h, LIGNE) : rect(0, 0);
    if (this.dataset.testid === 'conteneur') return rect(HAUT, h + LIGNE);
    return rect(0, hauteurPage());
  });
  vi.stubGlobal(
    'ResizeObserver',
    class {
      constructor(rappel: ResizeObserverCallback) {
        observateurs.push(rappel);
      }
      observe() {}
      disconnect() {}
    },
  );
});
afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

function simuler(): (number | null)[] {
  render(<Graphe />);
  const vues: (number | null)[] = [];
  let barre = false;
  // Une image : la barre de défilement suit le débordement de la page (le conteneur change alors de largeur), puis les
  // rappels d'image en attente.
  const barreSuitLaPage = () => {
    const deborde = hauteurPage() > FENETRE;
    if (deborde === barre) return;
    barre = deborde;
    act(() => {
      for (const o of observateurs) o([], {} as ResizeObserver);
    });
  };
  for (let i = 0; i < 12; i++) {
    barreSuitLaPage();
    act(() => {
      const r = [...rappelsImage.values()];
      rappelsImage = new Map();
      for (const f of r) f(0);
    });
    barreSuitLaPage();
    vues.push(etat.hauteur);
  }
  return vues;
}

describe('useHauteurDisponible', () => {
  beforeEach(() => {
    ligneMesurable = true;
  });

  it('tient compte de ce qui suit le graphe et ne bascule pas avec la barre de défilement (graphe dédoublé)', () => {
    const vues = simuler();
    // Hauteur stable (pas d'aller-retour d'une image à l'autre), et la page tient dans la fenêtre.
    expect(new Set(vues.slice(-6)).size).toBe(1);
    expect(etat.hauteur).toBe(FENETRE - HAUT - LIGNE - 1);
    expect(hauteurPage()).toBeLessThanOrEqual(FENETRE);
  });

  it('garde la correction de ce qui dépasse quand le contenu dessous ne se mesure pas', () => {
    ligneMesurable = false;
    const vues = simuler();
    expect(new Set(vues.slice(-6)).size).toBe(1);
    expect(hauteurPage()).toBeLessThanOrEqual(FENETRE);
  });
});
