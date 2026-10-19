import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { MepContext } from '../mepContext';
import { metaEtatMep, metaPhase } from '../statuts';
import { mep } from '../test/fabrique';
import type { Mep } from '../types';
import { TooltipProvider } from '../ui/Tooltip';
import { EnTeteMep } from './EnTeteMep';

function rendre(m: Partial<Mep>) {
  const valeur = mep(m);
  render(
    <MemoryRouter>
      <MepContext.Provider value={{ mep: valeur, run: vi.fn() } as never}>
        <TooltipProvider>
          <EnTeteMep mep={valeur} onglets={null} />
        </TooltipProvider>
      </MepContext.Provider>
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

describe('nouveaux états du cycle de vie (#93)', () => {
  it('reportée : la date visée dans la pastille, ou « sans date »', () => {
    expect(metaPhase('Preparation', 'Reportee', '2026-10-28')).toMatchObject({
      ton: 'wait',
      libelle: 'Reportée · 28/10/2026',
    });
    expect(metaPhase('Preparation', 'Reportee', null).libelle).toMatch(/^Reportée/);
  });

  it('annulée et clôturée en échec ont leur pastille', () => {
    expect(metaPhase('Annulee', 'Annulee')).toMatchObject({ ton: 'later', libelle: 'Annulée' });
    expect(metaPhase('Cloturee', 'Echec')).toMatchObject({ ton: 'danger', libelle: 'Clôturée : échec' });
    expect(metaEtatMep('Echec')).toMatchObject({ ton: 'danger', libelle: 'Échec (service dégradé)' });
    expect(metaEtatMep('Annulee').libelle).toBe('Annulée');
    expect(metaEtatMep('Reportee').libelle).toBe('Reportée');
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
