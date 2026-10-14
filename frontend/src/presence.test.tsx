import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { useReportDirty } from './hooks';
import { NoeudEtape } from './mep/graphe/NoeudEtape';
import { libelleEditeurs, PresenceProvider, prenom, ZonePresence } from './presence';
import { etape, mep } from './test/fabrique';
import type { Presence } from './types';

const bob: Presence = { utilisateurId: 'u-bob', nom: 'Bob Martin', elements: ['etape:e1'] };
const claire: Presence = { utilisateurId: 'u-claire', nom: 'Claire Petit', elements: ['etape:e1'] };
const dave: Presence = { utilisateurId: 'u-dave', nom: 'Dave', elements: [] };

describe('Présence', () => {
  it('nomme les personnes par leur prénom', () => {
    expect(prenom('Bob Martin')).toBe('Bob');
    expect(libelleEditeurs([bob])).toBe('Bob modifie');
    expect(libelleEditeurs([bob, claire])).toBe('Bob et Claire modifient');
    expect(libelleEditeurs([bob, claire, dave])).toBe('Bob, Claire et Dave modifient');
  });

  it('encadre la fiche d’une étape que quelqu’un d’autre modifie', () => {
    const props = {
      mep: mep(),
      mode: 'edition' as const,
      edition: true,
      descriptions: new Map<string, string>(),
      selectionnee: false,
      glissee: false,
      cible: null,
      onClavier: vi.fn(),
      onActiver: vi.fn(),
    };
    render(
      <PresenceProvider value={{ autres: [bob, dave], declarer: vi.fn() }}>
        <NoeudEtape {...props} etape={etape({ id: 'e1', code: 'E1' })} />
        <NoeudEtape {...props} etape={etape({ id: 'e2', code: 'E2' })} />
      </PresenceProvider>,
    );
    expect(screen.getAllByText('Bob modifie')).toHaveLength(1);
    expect(screen.getByText('Bob modifie').closest('[data-noeud]')).toHaveAttribute('data-noeud', 'e1');
  });

  it('une zone se déclare tant que le focus y est ou qu’une saisie y attend d’être enregistrée', async () => {
    function Champ() {
      const [v, setV] = useState('');
      useReportDirty(v !== '');
      return <input aria-label="Objet" value={v} onChange={(e) => setV(e.target.value)} />;
    }
    const declarer = vi.fn();
    render(
      <PresenceProvider value={{ autres: [], declarer }}>
        <ZonePresence cle="preparation:intervention">
          <Champ />
        </ZonePresence>
        <button type="button">Ailleurs</button>
      </PresenceProvider>,
    );
    const dernier = () => declarer.mock.calls.at(-1)?.[1];
    expect(dernier()).toBeNull();
    await userEvent.click(screen.getByLabelText('Objet'));
    expect(dernier()).toBe('preparation:intervention');
    await userEvent.click(screen.getByRole('button', { name: 'Ailleurs' }));
    expect(dernier()).toBeNull();

    await userEvent.type(screen.getByLabelText('Objet'), 'x');
    await userEvent.click(screen.getByRole('button', { name: 'Ailleurs' }));
    expect(dernier()).toBe('preparation:intervention'); // saisie non enregistrée : toujours en cours
    await userEvent.clear(screen.getByLabelText('Objet'));
    await userEvent.click(screen.getByRole('button', { name: 'Ailleurs' }));
    expect(dernier()).toBeNull();
  });
});
