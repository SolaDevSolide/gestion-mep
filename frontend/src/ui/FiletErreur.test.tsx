import { render, screen } from '@testing-library/react';
import { FiletErreur } from './FiletErreur';

function Casse(): never {
  throw new Error('rendu impossible');
}

describe('FiletErreur', () => {
  beforeEach(() => {
    vi.spyOn(console, 'error').mockImplementation(() => {});
  });
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('affiche l’erreur au lieu d’une page blanche, sans démonter ce qui l’entoure', () => {
    render(
      <div>
        <nav>Menu</nav>
        <FiletErreur cle="/meps/1?onglet=etapes">
          <Casse />
        </FiletErreur>
      </div>,
    );
    expect(screen.getByText('Menu')).toBeInTheDocument();
    expect(screen.getByRole('alert')).toHaveTextContent('Cette page n’a pas pu s’afficher');
    expect(screen.getByRole('alert')).toHaveTextContent('rendu impossible');
    expect(screen.getByRole('button', { name: /Recharger la page/ })).toBeInTheDocument();
  });

  it('réessaie l’affichage quand on change de page ou d’onglet', () => {
    const { rerender } = render(
      <FiletErreur cle="/meps/1?onglet=etapes">
        <Casse />
      </FiletErreur>,
    );
    expect(screen.getByRole('alert')).toBeInTheDocument();
    rerender(
      <FiletErreur cle="/meps/1?onglet=journal">
        <p>Journal</p>
      </FiletErreur>,
    );
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(screen.getByText('Journal')).toBeInTheDocument();
  });
});
