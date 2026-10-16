import { act, render, screen } from '@testing-library/react';
import { NouvelleVersion } from './NouvelleVersion';

function servir(version: string, entree: string) {
  vi.stubGlobal(
    'fetch',
    vi.fn((url: string) =>
      Promise.resolve(
        url === '/api/version'
          ? new Response(JSON.stringify({ version }), { status: 200 })
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
});
