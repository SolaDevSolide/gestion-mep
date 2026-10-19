import { act, render, screen } from '@testing-library/react';
import { NouvelleVersion } from './NouvelleVersion';

function servir(version: string, entree: string, charte?: string) {
  vi.stubGlobal(
    'fetch',
    vi.fn((url: string) =>
      Promise.resolve(
        url === '/api/version'
          ? new Response(JSON.stringify({ version, charte }), { status: 200 })
          : new Response(`<script type="module" crossorigin src="${entree}"></script>`, { status: 200 }),
      ),
    ),
  );
}

async function vider() {
  await act(() => new Promise((r) => setTimeout(r, 0)));
}

describe('NouvelleVersion', () => {
  let script: HTMLScriptElement;
  beforeEach(() => {
    script = document.createElement('script');
    script.type = 'module';
    script.src = '/assets/index-ancien.js';
    document.head.append(script);
  });
  afterEach(() => {
    script.remove();
    vi.unstubAllGlobals();
  });

  it('se tait quand l’interface servie est celle de l’onglet', async () => {
    servir('0.0.0-dev', '/assets/index-ancien.js');
    render(<NouvelleVersion />);
    await vider();
    expect(screen.queryByText(/nouvelle version/)).toBeNull();
  });

  it('prévient quand une nouvelle construction est servie, même avec le même numéro de version', async () => {
    servir('0.0.0-dev', '/assets/index-nouveau.js');
    render(<NouvelleVersion />);
    await vider();
    expect(screen.getByText(/nouvelle version de l’application/)).toBeInTheDocument();
  });

  it('applique la charte visuelle modifiée entre-temps, sans recharger la page', async () => {
    const lien = document.createElement('link');
    lien.id = 'charte';
    lien.rel = 'stylesheet';
    lien.href = '/charte.css';
    lien.dataset.empreinte = 'e3b0c44298fc';
    document.head.append(lien);
    servir('0.0.0-dev', '/assets/index-ancien.js', '40f78de39f99');
    render(<NouvelleVersion />);
    await vider();
    expect(document.head.querySelector('link[href="/charte.css?v=40f78de39f99"]')).not.toBeNull();
    expect(screen.queryByText(/nouvelle version/)).toBeNull();
    document.head.querySelectorAll('link').forEach((l) => l.remove());
  });

  it('prévient aussitôt quand un fichier chargé à la demande a disparu du serveur (vite:preloadError)', async () => {
    servir('0.0.0-dev', '/assets/index-ancien.js');
    render(<NouvelleVersion />);
    await vider();
    expect(screen.queryByText(/nouvelle version/)).toBeNull();
    act(() => {
      window.dispatchEvent(new Event('vite:preloadError'));
    });
    expect(screen.getByText(/nouvelle version de l’application/)).toBeInTheDocument();
  });
});
