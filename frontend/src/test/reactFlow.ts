/**
 * React Flow dans jsdom : pas de mise en page, donc ni ResizeObserver, ni DOMMatrix, ni tailles mesurées. Ces
 * simulations donnent une taille aux nœuds pour qu'ils deviennent visibles (et accessibles) dans les tests du graphe.
 * Une carte glissable s'ouvre par `fireEvent.click` : user-event appuie d'abord sans `view`, que d3-drag exige.
 */
export function simulerReactFlow() {
  class ResizeObserverSimule {
    constructor(private readonly rappel: ResizeObserverCallback) {}
    observe(cible: Element) {
      const el = cible as HTMLElement;
      const rect = { width: el.offsetWidth, height: el.offsetHeight, x: 0, y: 0, top: 0, left: 0, right: 0, bottom: 0 };
      this.rappel(
        [{ target: cible, contentRect: rect } as unknown as ResizeObserverEntry],
        this as unknown as ResizeObserver,
      );
    }
    unobserve() {}
    disconnect() {}
  }
  class DOMMatrixReadOnlySimulee {
    m22: number;
    constructor(transformation?: string) {
      const echelle = transformation?.match(/scale\(([0-9.]+)\)/)?.[1];
      this.m22 = echelle === undefined ? 1 : Number(echelle);
    }
  }
  globalThis.ResizeObserver = ResizeObserverSimule as unknown as typeof ResizeObserver;
  globalThis.DOMMatrixReadOnly = DOMMatrixReadOnlySimulee as unknown as typeof DOMMatrixReadOnly;
  Object.defineProperties(HTMLElement.prototype, {
    offsetHeight: {
      configurable: true,
      get(this: HTMLElement) {
        return Number.parseFloat(this.style.height) || 100;
      },
    },
    offsetWidth: {
      configurable: true,
      get(this: HTMLElement) {
        return Number.parseFloat(this.style.width) || 200;
      },
    },
  });
  (SVGElement.prototype as unknown as { getBBox: () => DOMRect }).getBBox = () =>
    ({ x: 0, y: 0, width: 0, height: 0 }) as DOMRect;
  document.elementsFromPoint ??= () => [];
}
