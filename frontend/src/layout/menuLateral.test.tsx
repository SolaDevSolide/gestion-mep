import { act, render, screen } from '@testing-library/react';
import { leverRepliTemporaire, useMenuReplieTemporaire, useRepliMenu } from './menuLateral';

function Menu() {
  return <p>{useMenuReplieTemporaire() ? 'replié' : 'déplié'}</p>;
}
function Ecran({ actif }: { actif: boolean }) {
  useRepliMenu(actif);
  return null;
}

describe('repli temporaire du menu latéral', () => {
  it('replié tant qu’un écran le demande, puis revient', () => {
    const { rerender } = render(
      <>
        <Menu />
        <Ecran actif={false} />
      </>,
    );
    expect(screen.getByText('déplié')).toBeInTheDocument();
    rerender(
      <>
        <Menu />
        <Ecran actif />
      </>,
    );
    expect(screen.getByText('replié')).toBeInTheDocument();
    rerender(
      <>
        <Menu />
        <Ecran actif={false} />
      </>,
    );
    expect(screen.getByText('déplié')).toBeInTheDocument();
  });

  it('« Déplier le menu » lève la demande en cours, pas la suivante', () => {
    const { rerender } = render(
      <>
        <Menu />
        <Ecran actif />
      </>,
    );
    act(() => leverRepliTemporaire());
    expect(screen.getByText('déplié')).toBeInTheDocument();
    rerender(
      <>
        <Menu />
        <Ecran actif={false} />
      </>,
    );
    rerender(
      <>
        <Menu />
        <Ecran actif />
      </>,
    );
    expect(screen.getByText('replié')).toBeInTheDocument();
  });
});
