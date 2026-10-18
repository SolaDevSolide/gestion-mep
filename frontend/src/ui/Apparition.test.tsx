import { render, screen } from '@testing-library/react';
import { Apparition } from './Apparition';

describe('Apparition', () => {
  it('pendant le chargement : un squelette annoncé, pas de contenu partiel', () => {
    render(
      <Apparition pret={false} libelle="Chargement des étapes…" squelette={<p>Squelette</p>}>
        <p>Contenu</p>
      </Apparition>,
    );
    expect(screen.queryByText('Contenu')).not.toBeInTheDocument();
    expect(screen.getByRole('status', { name: 'Chargement des étapes…' })).toHaveAttribute('aria-busy', 'true');
    expect(screen.getByText('Squelette')).toBeInTheDocument();
  });

  it('affiche le contenu complet dès qu’il est prêt, en fondu', () => {
    const { rerender } = render(
      <Apparition pret={false}>
        <p>Contenu</p>
      </Apparition>,
    );
    rerender(
      <Apparition pret>
        <p>Contenu</p>
      </Apparition>,
    );
    expect(screen.getByText('Contenu').parentElement).toHaveClass('apparition');
    expect(screen.queryByRole('status')).not.toBeInTheDocument();
  });
});
