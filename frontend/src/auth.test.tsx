import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { api, ApiError } from './api';
import { AuthProvider, useAuth } from './auth';
import { useDraft, useReportDirty } from './hooks';
import { GardeModifications } from './layout/GardeModifications';
import { SessionExpiree } from './layout/SessionExpiree';
import { utilisateur } from './test/fabrique';
import { ToastProvider } from './ui/Toast';

/** Serveur simulé : la session peut expirer, la connexion la rouvre. */
let session = true;
let appelsListe = 0;

function json(statut: number, corps: unknown) {
  return new Response(corps === undefined ? null : JSON.stringify(corps), {
    status: statut,
    headers: { 'Content-Type': 'application/json' },
  });
}

function serveur(url: string, init?: RequestInit): Response {
  const chemin = url.replace(/^\/api/, '');
  if (chemin === '/auth/moi') return session ? json(200, utilisateur()) : json(401, { message: 'Non connecté.' });
  if (chemin === '/auth/options') return json(200, { ssoActif: false, ssoLibelle: null });
  if (chemin === '/auth/connexion') {
    const { login } = JSON.parse(typeof init?.body === 'string' ? init.body : '{}') as { login: string };
    session = true;
    return json(200, utilisateur(login === 'claire' ? {} : { id: 'u2', login, nomAffiche: 'Bob' }));
  }
  if (chemin === '/utilisateurs') {
    appelsListe++;
    return session ? json(200, []) : json(401, { message: 'Session expirée.' });
  }
  return json(404, { message: 'Inconnu' });
}

/** Une saisie en cours (comme la section Intervention) et une requête lancée à la demande. */
function Page() {
  const { utilisateur: moi } = useAuth();
  const d = useDraft({ titre: '' });
  useReportDirty(d.dirty, 'Intervention', d);
  const [resultat, setResultat] = useState('');
  return (
    <>
      <p>Connecté : {moi?.login ?? 'personne'}</p>
      <label>
        Titre
        <input value={d.value.titre} onChange={(e) => d.update({ titre: e.target.value })} />
      </label>
      <button
        type="button"
        onClick={() =>
          void api.utilisateurs().then(
            (l) => setResultat(`reçu ${l.length}`),
            (e: unknown) => setResultat(`échec ${e instanceof ApiError ? e.status : '?'}`),
          )
        }
      >
        Lire les comptes
      </button>
      <button
        type="button"
        onClick={() =>
          void Promise.all([api.utilisateurs(), api.utilisateurs()]).then(
            (l) => setResultat(`reçu ${l.length} réponses`),
            () => setResultat('échec'),
          )
        }
      >
        Lire deux fois
      </button>
      <p>{resultat}</p>
    </>
  );
}

function rendre() {
  const routeur = createMemoryRouter([
    {
      path: '*',
      element: (
        <AuthProvider>
          <GardeModifications>
            <Page />
            <SessionExpiree />
          </GardeModifications>
        </AuthProvider>
      ),
    },
  ]);
  render(
    <ToastProvider>
      <RouterProvider router={routeur} />
    </ToastProvider>,
  );
}

async function expirerPendantLaSaisie(bouton = 'Lire les comptes') {
  rendre();
  expect(await screen.findByText('Connecté : claire')).toBeInTheDocument();
  await userEvent.type(screen.getByLabelText('Titre'), 'Bascule');
  session = false;
  await userEvent.click(screen.getByRole('button', { name: bouton }));
  return screen.findByRole('dialog', { name: 'Session expirée' });
}

describe('Session expirée pendant le travail', () => {
  beforeEach(() => {
    session = true;
    appelsListe = 0;
    vi.stubGlobal(
      'fetch',
      vi.fn((url: string, init?: RequestInit) => Promise.resolve(serveur(url, init))),
    );
  });
  afterEach(() => vi.unstubAllGlobals());

  it('ouvre le dialogue sur la page, garde la saisie et relance une fois la requête après la reconnexion', async () => {
    const dialogue = await expirerPendantLaSaisie();
    expect(dialogue).toHaveTextContent('Votre saisie est conservée (Intervention)');
    // La page reste montée derrière, avec sa saisie ; la requête attend.
    expect(screen.getByLabelText('Titre', { selector: 'input' })).toHaveValue('Bascule');
    expect(screen.queryByText(/échec/)).toBeNull();
    expect(within(dialogue).getByLabelText('Identifiant')).toHaveValue('claire');
    // Pas de fermeture sans se reconnecter.
    expect(within(dialogue).queryByRole('button', { name: 'Fermer' })).toBeNull();

    await userEvent.type(within(dialogue).getByLabelText('Mot de passe'), 'demo');
    await userEvent.click(within(dialogue).getByRole('button', { name: 'Se reconnecter' }));

    expect(await screen.findByText('reçu 0')).toBeInTheDocument();
    expect(appelsListe).toBe(2);
    expect(await screen.findByText('Reconnecté. Votre saisie est intacte.')).toBeInTheDocument();
    expect(screen.queryByRole('dialog', { name: 'Session expirée' })).toBeNull();
    expect(screen.getByLabelText('Titre', { selector: 'input' })).toHaveValue('Bascule');
  });

  it('plusieurs requêtes en échec attendent la même reconnexion', async () => {
    const dialogue = await expirerPendantLaSaisie('Lire deux fois');
    expect(screen.getAllByRole('dialog', { name: 'Session expirée' })).toHaveLength(1);
    await userEvent.type(within(dialogue).getByLabelText('Mot de passe'), 'demo');
    await userEvent.click(within(dialogue).getByRole('button', { name: 'Se reconnecter' }));
    expect(await screen.findByText('reçu 2 réponses')).toBeInTheDocument();
    // Deux échecs, deux relances : une seule fois chacune.
    expect(appelsListe).toBe(4);
  });

  it('changer de compte demande confirmation avant d’abandonner la saisie', async () => {
    const dialogue = await expirerPendantLaSaisie();
    await userEvent.click(within(dialogue).getByRole('button', { name: 'Changer de compte' }));
    const confirmation = await screen.findByRole('dialog', { name: 'Abandonner la saisie de Claire ?' });
    expect(confirmation).toHaveTextContent('Intervention');
    await userEvent.click(within(confirmation).getByRole('button', { name: 'Abandonner la saisie' }));
    expect(await screen.findByText('échec 401')).toBeInTheDocument();
    expect(screen.getByText('Connecté : personne')).toBeInTheDocument();
    expect(appelsListe).toBe(1);
  });

  it('un autre identifiant ouvre la session de ce compte, après confirmation', async () => {
    const dialogue = await expirerPendantLaSaisie();
    const identifiant = within(dialogue).getByLabelText('Identifiant');
    await userEvent.clear(identifiant);
    await userEvent.type(identifiant, 'bob');
    await userEvent.type(within(dialogue).getByLabelText('Mot de passe'), 'demo');
    await userEvent.click(within(dialogue).getByRole('button', { name: 'Se reconnecter' }));
    const confirmation = await screen.findByRole('dialog', { name: 'Abandonner la saisie de Claire ?' });
    await userEvent.click(within(confirmation).getByRole('button', { name: 'Abandonner la saisie' }));
    expect(await screen.findByText('Connecté : bob')).toBeInTheDocument();
    expect(screen.getByText('échec 401')).toBeInTheDocument();
  });

  it('sans session au démarrage : pas de dialogue, la lecture échoue simplement', async () => {
    session = false;
    rendre();
    expect(await screen.findByText('Connecté : personne')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Lire les comptes' }));
    expect(await screen.findByText('échec 401')).toBeInTheDocument();
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Session expirée' })).toBeNull());
  });
});
