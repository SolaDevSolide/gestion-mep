import { render, screen } from '@testing-library/react';
import { ApercuAppli, ApercuExcel } from './ApercuCharte';
import { lireFichier, type Charte } from './charte';

const horizon = lireFichier({
  couleurs: { primaire: '#0F766E', secondaire: '#1B2A41', tertiaire: '#C026D3' },
  menuLateral: 'secondaire',
}).charte as Charte;

describe('Aperçu de la charte', () => {
  it('montre l’application avec les jetons de la charte, dans le thème demandé', () => {
    render(<ApercuAppli charte={horizon} mode="sombre" />);
    const apercu = screen.getByRole('img', { name: 'Aperçu de l’application, thème sombre' });
    expect(apercu).toHaveClass('dark');
    expect(apercu.style.getPropertyValue('--accent')).toBe('#129187');
    expect(apercu.style.getPropertyValue('--nav-bg')).toBe('#162132');
  });

  it('montre un classeur avec la palette des exports : en-têtes en primaire, titre en secondaire', () => {
    render(<ApercuExcel charte={horizon} />);
    expect(screen.getByText('Réf.')).toHaveStyle({ backgroundColor: '#0f766e', color: '#ffffff' });
    expect(screen.getByText('Paiement mobile 4.2 · Étapes', { selector: 'td' })).toHaveStyle({ color: '#1b2a41' });
  });
});
