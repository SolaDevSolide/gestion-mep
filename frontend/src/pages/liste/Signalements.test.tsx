import { render, screen } from '@testing-library/react';
import type { ConflitCalendrier, OccurrenceGel } from '../../types';
import { Signalements } from './Signalements';

const gel: OccurrenceGel = {
  periodeGelId: 'g1',
  nom: 'Clôture comptable',
  motif: '',
  debut: '2026-11-25T23:00:00Z',
  fin: '2026-11-30T23:00:00Z',
  fuseauHoraire: 'Europe/Paris',
  globale: true,
  derogation: null,
};

const conflit = (p: Partial<ConflitCalendrier> = {}): ConflitCalendrier => ({
  mepId: 'x',
  outil: 'Portail adhérents',
  versionCible: '5.2.1',
  etat: 'Preparation',
  debut: '2026-11-27T20:30:00Z',
  fin: '2026-11-27T22:00:00Z',
  systemes: ['WEB'],
  ...p,
});

describe('Signalements', () => {
  it('ne montre rien sans gel ni conflit', () => {
    const { container } = render(<Signalements s={undefined} phase="Preparation" />);
    expect(container).toBeEmptyDOMElement();
  });

  it('montre le gel et le conflit d’une MEP', () => {
    render(<Signalements s={{ mepId: 'm', gels: [gel], conflits: [conflit()] }} phase="Preparation" />);
    expect(screen.getByText('Gel : Clôture comptable · Go bloqué sans dérogation')).toBeInTheDocument();
    expect(screen.getByText('Conflit sur WEB avec Portail adhérents 5.2.1 (21:30 → 23:00)')).toBeInTheDocument();
  });

  it('résume plusieurs conflits, le détail restant lisible', () => {
    render(
      <Signalements
        s={{ mepId: 'm', gels: [], conflits: [conflit(), conflit({ mepId: 'y', outil: 'Paie', systemes: ['BDD'] })] }}
        phase="Preparation"
      />,
    );
    expect(screen.getByText('Conflit avec 2 MEP sur WEB, BDD')).toBeInTheDocument();
    expect(screen.getByText(/Conflit sur BDD avec Paie 5.2.1/)).toBeInTheDocument();
  });
});
