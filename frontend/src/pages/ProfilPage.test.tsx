import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { api } from '../api';
import { AuthContext } from '../auth';
import { utilisateur } from '../test/fabrique';
import type { SystemeCatalogue, Utilisateur } from '../types';
import ProfilPage from './ProfilPage';

vi.mock('../api', () => ({ api: { catalogueSystemes: vi.fn(), modifierMonProfil: vi.fn() } }));

const catalogue: SystemeCatalogue[] = [
  { id: 'c-api', nom: 'API', description: '', cible: '', actif: true },
  { id: 'c-bdd', nom: 'BDD', description: '', cible: '', actif: true },
];

function rendre(u: Utilisateur) {
  const majUtilisateur = vi.fn();
  vi.mocked(api.catalogueSystemes).mockResolvedValue(catalogue);
  render(
    <AuthContext.Provider value={{ utilisateur: u, chargement: false, connecter: vi.fn(), deconnecter: vi.fn(), majUtilisateur }}>
      <ProfilPage />
    </AuthContext.Provider>,
  );
  return { majUtilisateur };
}

describe('Page Mon profil', () => {
  beforeEach(() => vi.clearAllMocks());

  it('affiche le profil du compte connecté et ses expertises', async () => {
    rendre(utilisateur({ nomAffiche: 'Bob', role: 'Développeur API', contact: '06 11', expertises: [{ systemeCatalogueId: 'c-api', detail: 'Expert facturation' }] }));
    expect(screen.getByLabelText(/Nom affiché/)).toHaveValue('Bob');
    expect(screen.getByLabelText('Rôle')).toHaveValue('Développeur API');
    expect(screen.getByLabelText('Contact')).toHaveValue('06 11');
    expect(await screen.findByLabelText('Détail')).toHaveValue('Expert facturation');
  });

  it('enregistre le rôle, le contact et les expertises ajoutées ou retirées', async () => {
    const maj = utilisateur({ nomAffiche: 'Bob', role: 'DBA', contact: 'bob@x.fr', expertises: [{ systemeCatalogueId: 'c-bdd', detail: 'Index' }] });
    vi.mocked(api.modifierMonProfil).mockResolvedValue(maj);
    const { majUtilisateur } = rendre(utilisateur({ nomAffiche: 'Bob', expertises: [{ systemeCatalogueId: 'c-api', detail: 'Expert' }] }));
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
      nomAffiche: 'Bob', role: 'DBA', contact: 'bob@x.fr', expertises: [{ systemeCatalogueId: expect.stringMatching(/^c-/), detail: 'Index' }],
    });
    await waitFor(() => expect(majUtilisateur).toHaveBeenCalledWith(maj));
  });

  it('ne propose pas deux fois le même système', async () => {
    rendre(utilisateur({ expertises: [{ systemeCatalogueId: 'c-api', detail: '' }] }));
    const select = await screen.findByLabelText('Système');
    expect(within(select).getAllByRole('option').map((o) => o.textContent)).toEqual(['API', 'BDD']);
    await userEvent.click(screen.getByRole('button', { name: 'Ajouter une expertise' }));
    // Plus de système libre : le bouton d'ajout est désactivé.
    expect(screen.getByRole('button', { name: 'Ajouter une expertise' })).toBeDisabled();
  });

  it('Enregistrer reste désactivé tant que rien n’a changé', async () => {
    rendre(utilisateur());
    await screen.findByRole('button', { name: 'Ajouter une expertise' });
    expect(screen.getByRole('button', { name: 'Enregistrer' })).toBeDisabled();
  });
});
