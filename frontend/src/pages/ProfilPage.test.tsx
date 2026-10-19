import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { api } from '../api';
import { AuthContext } from '../auth';
import { SESSION_ACTIVE, utilisateur } from '../test/fabrique';
import type { SsoProfil, SystemeCatalogue, Utilisateur } from '../types';
import ProfilPage from './ProfilPage';

vi.mock('../api', () => ({
  api: {
    catalogueSystemes: vi.fn(),
    modifierMonProfil: vi.fn(),
    ssoEtat: vi.fn(),
    ssoLier: vi.fn(),
    ssoDelierMoi: vi.fn(),
  },
}));

const catalogue: SystemeCatalogue[] = [
  { id: 'c-api', nom: 'API', description: '', cible: '', actif: true, environnements: [], categories: [], nbObjets: 0 },
  { id: 'c-bdd', nom: 'BDD', description: '', cible: '', actif: true, environnements: [], categories: [], nbObjets: 0 },
];

function rendre(u: Utilisateur, sso?: Partial<SsoProfil>, url = '/profil') {
  const majUtilisateur = vi.fn();
  vi.mocked(api.catalogueSystemes).mockResolvedValue(catalogue);
  vi.mocked(api.ssoEtat).mockResolvedValue({
    disponible: false,
    libelle: null,
    identifiant: null,
    peutDelier: false,
    ...sso,
  });
  render(
    <AuthContext.Provider
      value={{
        utilisateur: u,
        chargement: false,
        connecter: vi.fn(),
        deconnecter: vi.fn(),
        majUtilisateur,
        ...SESSION_ACTIVE,
      }}
    >
      <MemoryRouter initialEntries={[url]}>
        <ProfilPage />
      </MemoryRouter>
    </AuthContext.Provider>,
  );
  return { majUtilisateur };
}

describe('Page Mon profil', () => {
  beforeEach(() => vi.clearAllMocks());

  it('affiche le profil du compte connecté et ses expertises', async () => {
    rendre(
      utilisateur({
        nomAffiche: 'Bob',
        role: 'Développeur API',
        contact: '06 11',
        expertises: [{ systemeCatalogueId: 'c-api', detail: 'Expert facturation' }],
      }),
    );
    // La page apparaît d'un bloc, une fois le catalogue et l'état de la connexion unique lus.
    expect(await screen.findByLabelText(/Nom affiché/)).toHaveValue('Bob');
    expect(screen.getByLabelText('Rôle')).toHaveValue('Développeur API');
    expect(screen.getByLabelText('Contact')).toHaveValue('06 11');
    expect(await screen.findByLabelText('Détail')).toHaveValue('Expert facturation');
  });

  it('enregistre le rôle, le contact et les expertises ajoutées ou retirées', async () => {
    const maj = utilisateur({
      nomAffiche: 'Bob',
      role: 'DBA',
      contact: 'bob@x.fr',
      expertises: [{ systemeCatalogueId: 'c-bdd', detail: 'Index' }],
    });
    vi.mocked(api.modifierMonProfil).mockResolvedValue(maj);
    const { majUtilisateur } = rendre(
      utilisateur({ nomAffiche: 'Bob', expertises: [{ systemeCatalogueId: 'c-api', detail: 'Expert' }] }),
    );
    await screen.findByLabelText('Détail');
    await userEvent.type(screen.getByLabelText('Rôle'), 'DBA');
    await userEvent.type(screen.getByLabelText('Contact'), 'bob@x.fr');
    await userEvent.click(screen.getByRole('button', { name: 'Supprimer l’expertise API' }));
    await userEvent.click(screen.getByRole('button', { name: 'Ajouter une expertise' }));
    const detail = screen.getByLabelText('Détail');
    await userEvent.type(detail, ' Index ');
    await userEvent.click(screen.getByRole('button', { name: 'Enregistrer' }));
    await waitFor(() => expect(api.modifierMonProfil).toHaveBeenCalled());
    expect(api.modifierMonProfil).toHaveBeenCalledWith({
      nomAffiche: 'Bob',
      role: 'DBA',
      contact: 'bob@x.fr',
      expertises: [{ systemeCatalogueId: expect.stringMatching(/^c-/), detail: 'Index' }],
    });
    await waitFor(() => expect(majUtilisateur).toHaveBeenCalledWith(maj));
  });

  it('ne propose pas deux fois le même système', async () => {
    rendre(utilisateur({ expertises: [{ systemeCatalogueId: 'c-api', detail: '' }] }));
    const select = await screen.findByLabelText('Système');
    expect(
      within(select)
        .getAllByRole('option')
        .map((o) => o.textContent),
    ).toEqual(['API', 'BDD']);
    await userEvent.click(screen.getByRole('button', { name: 'Ajouter une expertise' }));
    // Plus de système libre : le bouton d'ajout est désactivé.
    expect(screen.getByRole('button', { name: 'Ajouter une expertise' })).toBeDisabled();
  });

  it('Enregistrer reste désactivé tant que rien n’a changé', async () => {
    rendre(utilisateur());
    await screen.findByRole('button', { name: 'Ajouter une expertise' });
    expect(screen.getByRole('button', { name: 'Enregistrer' })).toBeDisabled();
  });

  it('propose de lier son compte quand la connexion unique est disponible', async () => {
    rendre(utilisateur(), { disponible: true, libelle: 'Se connecter avec Microsoft' });
    expect(await screen.findByRole('button', { name: 'Lier mon compte' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Délier' })).toBeNull();
  });

  it('masque la carte quand la connexion unique est indisponible et le compte non lié', async () => {
    rendre(utilisateur());
    await screen.findByRole('button', { name: 'Ajouter une expertise' });
    await waitFor(() => expect(api.ssoEtat).toHaveBeenCalled());
    expect(screen.queryByText('Connexion unique')).toBeNull();
  });

  it('affiche l’identifiant lié et délie après confirmation', async () => {
    vi.mocked(api.ssoDelierMoi).mockResolvedValue(undefined);
    rendre(utilisateur(), { disponible: true, identifiant: 'alice@x.fr', peutDelier: true });
    expect(await screen.findByText('alice@x.fr')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Délier' }));
    await userEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Délier' }));
    await waitFor(() => expect(api.ssoDelierMoi).toHaveBeenCalled());
  });

  it('affiche le message du retour de la connexion unique', async () => {
    rendre(utilisateur(), undefined, '/profil?sso=deja-lie');
    expect(await screen.findByText('Ce compte Microsoft est déjà lié à un autre compte.')).toBeInTheDocument();
  });
});
