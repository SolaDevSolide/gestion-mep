import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { metaPhase } from '../statuts';
import { mep } from '../test/fabrique';
import type { Mep } from '../types';
import { TooltipProvider } from '../ui/Tooltip';
import { EnTeteMep } from './EnTeteMep';

function rendre(m: Partial<Mep>) {
  render(
    <MemoryRouter>
      <TooltipProvider>
        <EnTeteMep mep={mep(m)} onglets={null} />
      </TooltipProvider>
    </MemoryRouter>,
  );
}

describe('metaPhase', () => {
  it('donne la phase, puis l’issue une fois la MEP clôturée', () => {
    expect(metaPhase('Execution', 'Execution')).toMatchObject({ ton: 'run', libelle: 'Exécution' });
    expect(metaPhase('Cloturee', 'Reussie')).toMatchObject({ ton: 'done', libelle: 'Clôturée : réussie' });
    expect(metaPhase('Cloturee', 'Repliee')).toMatchObject({ ton: 'later', libelle: 'Clôturée : repliée' });
  });

  it('clôturée réussie après un échec d’étape : « réussie avec incidents », en attention (#91)', () => {
    expect(metaPhase('Cloturee', 'AvecIncidents')).toMatchObject({
      ton: 'warn',
      libelle: 'Clôturée : réussie avec incidents',
    });
  });
});

describe('EnTeteMep', () => {
  it('affiche « Clôturée : réussie » sans échec en route', () => {
    rendre({ phase: 'Cloturee', issue: 'Reussie', etat: 'Reussie' });
    expect(screen.getByText('Clôturée : réussie')).toBeInTheDocument();
  });

  it('affiche « Clôturée : réussie avec incidents » quand une étape a échoué puis été reprise', () => {
    rendre({ phase: 'Cloturee', issue: 'Reussie', etat: 'AvecIncidents', echecsSurvenus: 1 });
    expect(screen.getByText('Clôturée : réussie avec incidents')).toBeInTheDocument();
    expect(screen.queryByText('Clôturée : réussie')).toBeNull();
  });
});
