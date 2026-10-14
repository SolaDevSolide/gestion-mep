import { act, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { createMemoryRouter, Link, RouterProvider, useLocation } from 'react-router-dom';
import { useReportDirty } from '../hooks';
import { changeDeVue, GardeModifications } from './GardeModifications';

function Formulaire({ libelle }: { libelle: string }) {
  const [valeur, setValeur] = useState('');
  useReportDirty(valeur !== '', libelle);
  return <input aria-label={libelle} value={valeur} onChange={(e) => setValeur(e.target.value)} />;
}

function Page() {
  const { pathname, search } = useLocation();
  return (
    <>
      <p>Adresse : {pathname + search}</p>
      {pathname === '/meps/1' && search !== '?onglet=journal' && (
        <>
          <Formulaire libelle="Intervention" />
          <Formulaire libelle="Étape E2" />
        </>
      )}
      <Link to="/meps/1?onglet=journal">Journal</Link>
      <Link to="/meps/1?onglet=preparation&etape=e2">Ouvrir E2</Link>
      <Link to="/meps/2">Autre MEP</Link>
    </>
  );
}

function rendre() {
  const routeur = createMemoryRouter(
    [
      {
        path: '*',
        element: (
          <GardeModifications>
            <Page />
          </GardeModifications>
        ),
      },
    ],
    { initialEntries: ['/meps/1?onglet=preparation'] },
  );
  render(<RouterProvider router={routeur} />);
}

describe('Modifications non enregistrées', () => {
  it('sans saisie en cours, on change de vue librement', async () => {
    rendre();
    await userEvent.click(screen.getByRole('link', { name: 'Autre MEP' }));
    expect(screen.getByText('Adresse : /meps/2')).toBeInTheDocument();
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('quitter une vue avec une saisie demande confirmation et liste ce qui sera perdu ; Rester garde la saisie', async () => {
    rendre();
    await userEvent.type(screen.getByLabelText('Intervention'), 'Nouvel objet');
    await userEvent.type(screen.getByLabelText('Étape E2'), 'x');
    await userEvent.click(screen.getByRole('link', { name: 'Journal' }));

    const fenetre = await screen.findByRole('dialog', { name: /Quitter sans enregistrer \?/ });
    expect(fenetre).toHaveTextContent('Étape E2');
    expect(fenetre).toHaveTextContent('Intervention');
    expect(screen.getByRole('button', { name: 'Rester' })).toHaveFocus();

    await userEvent.click(screen.getByRole('button', { name: 'Rester' }));
    expect(screen.queryByRole('dialog')).toBeNull();
    expect(screen.getByText('Adresse : /meps/1?onglet=preparation')).toBeInTheDocument();
    expect(screen.getByLabelText('Intervention')).toHaveValue('Nouvel objet');
  });

  it('« Quitter sans enregistrer » poursuit la navigation (autre MEP)', async () => {
    rendre();
    await userEvent.type(screen.getByLabelText('Intervention'), 'Nouvel objet');
    await userEvent.click(screen.getByRole('link', { name: 'Autre MEP' }));
    await userEvent.click(await screen.findByRole('button', { name: 'Quitter sans enregistrer' }));
    expect(screen.getByText('Adresse : /meps/2')).toBeInTheDocument();
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('ouvrir une étape dans la même vue ne demande rien', async () => {
    rendre();
    await userEvent.type(screen.getByLabelText('Intervention'), 'Nouvel objet');
    await userEvent.click(screen.getByRole('link', { name: 'Ouvrir E2' }));
    expect(screen.queryByRole('dialog')).toBeNull();
    expect(screen.getByLabelText('Intervention')).toHaveValue('Nouvel objet');
  });

  it('fermer ou recharger le navigateur déclenche l’avertissement standard pendant une saisie', async () => {
    rendre();
    const avant = new Event('beforeunload', { cancelable: true });
    act(() => void window.dispatchEvent(avant));
    expect(avant.defaultPrevented).toBe(false);

    await userEvent.type(screen.getByLabelText('Intervention'), 'Nouvel objet');
    const pendant = new Event('beforeunload', { cancelable: true });
    act(() => void window.dispatchEvent(pendant));
    expect(pendant.defaultPrevented).toBe(true);
  });

  it('distingue un changement de vue d’un simple paramètre', () => {
    const a = { pathname: '/meps/1', search: '?onglet=plan&plan=etapes' };
    expect(changeDeVue(a, { pathname: '/meps/1', search: '?onglet=plan&plan=retour' })).toBe(true);
    expect(changeDeVue(a, { pathname: '/meps/1', search: '?onglet=journal' })).toBe(true);
    expect(changeDeVue(a, { pathname: '/meps/2', search: a.search })).toBe(true);
    expect(changeDeVue(a, { pathname: '/meps/1', search: '?onglet=plan&plan=etapes&etape=e3' })).toBe(false);
  });
});
