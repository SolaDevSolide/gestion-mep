import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { api } from '../api';
import { AuthContext } from '../auth';
import { utilisateur } from '../test/fabrique';
import type { ModeleRecette } from '../types';
import { TooltipProvider } from '../ui/Tooltip';
import BibliothequeRecettesPage from './BibliothequeRecettesPage';

const modele = (id: string, titre: string, systemeCatalogueId: string, instructions: string): ModeleRecette => ({
  id,
  titre,
  systemeCatalogueId,
  etapes: [{ ordre: 1, instructions, resultatAttendu: 'OK' }],
  version: 3,
  modifieLe: '2026-10-01T10:00:00Z',
  modifieParId: 'u2',
  modifiePar: 'Bob',
});

vi.mock('../api', () => ({
  api: {
    modelesRecette: vi.fn(),
    catalogueSystemes: vi.fn(async () => [
      { id: 'c-web', nom: 'WEB', description: '', cible: '', actif: true },
      { id: 'c-bdd', nom: 'BDD', description: '', cible: '', actif: true },
    ]),
    creerModeleRecette: vi.fn(),
    modifierModeleRecette: vi.fn(),
    supprimerModeleRecette: vi.fn(),
  },
}));

function rendre(estAdmin = false) {
  render(
    <MemoryRouter>
      <AuthContext.Provider value={{ utilisateur: utilisateur({ estAdmin }) } as never}>
        <TooltipProvider>
          <BibliothequeRecettesPage />
        </TooltipProvider>
      </AuthContext.Provider>
    </MemoryRouter>,
  );
}

describe('Bibliothèque de recettes', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(api.modelesRecette).mockResolvedValue([
      modele('r1', 'Connexion au portail', 'c-web', 'Ouvrir le portail'),
      modele('r2', 'Droits applicatifs', 'c-bdd', '```\npsql -c "SELECT 1"\n```'),
    ]);
  });

  it('présente les modèles en cartes, avec recherche et filtre par système', async () => {
    rendre();
    expect(await screen.findByText('Connexion au portail')).toBeInTheDocument();
    expect(screen.getAllByText('Modifié par Bob le', { exact: false })).toHaveLength(2);
    expect(screen.getByText('1. psql -c "SELECT 1"')).toBeInTheDocument();

    await userEvent.selectOptions(screen.getByLabelText('Filtrer par système'), 'c-bdd');
    expect(screen.queryByText('Connexion au portail')).toBeNull();
    expect(screen.getByText('Droits applicatifs')).toBeInTheDocument();

    await userEvent.selectOptions(screen.getByLabelText('Filtrer par système'), '');
    await userEvent.type(screen.getByLabelText('Rechercher un modèle'), 'portail');
    expect(screen.getByText('Connexion au portail')).toBeInTheDocument();
    expect(screen.queryByText('Droits applicatifs')).toBeNull();
  });

  it('crée un modèle rattaché à un système, avec ses étapes en blocs', async () => {
    vi.mocked(api.creerModeleRecette).mockResolvedValue(modele('r3', 'Santé API', 'c-web', 'x'));
    rendre();
    await screen.findByText('Connexion au portail');
    await userEvent.click(screen.getByRole('button', { name: 'Nouveau modèle' }));
    const dialogue = await screen.findByRole('dialog');
    await userEvent.type(within(dialogue).getByLabelText('Titre du modèle *'), 'Santé API');
    await userEvent.click(within(dialogue).getByRole('button', { name: 'Enregistrer' }));
    expect(within(dialogue).getByRole('alert')).toHaveTextContent('Choisissez le système du catalogue');
    await userEvent.selectOptions(within(dialogue).getByLabelText('Système du catalogue *'), 'c-web');
    await userEvent.click(within(dialogue).getByRole('button', { name: 'Ajouter une étape de test' }));
    await userEvent.type(within(dialogue).getByLabelText('Déroulé du test, étape 1, bloc 1 (texte)'), 'Appeler /sante');
    await userEvent.type(within(dialogue).getByLabelText('Résultat attendu, étape 1'), 'Réponse 200');
    await userEvent.click(within(dialogue).getByRole('button', { name: 'Enregistrer' }));
    await waitFor(() =>
      expect(api.creerModeleRecette).toHaveBeenCalledWith({
        titre: 'Santé API',
        systemeCatalogueId: 'c-web',
        etapes: [{ instructions: 'Appeler /sante', resultatAttendu: 'Réponse 200' }],
      }),
    );
  });

  it('modifie un modèle avec sa version', async () => {
    vi.mocked(api.modifierModeleRecette).mockResolvedValue(modele('r1', 'Connexion', 'c-web', 'x'));
    rendre();
    await userEvent.click(await screen.findByRole('button', { name: 'Modifier Connexion au portail' }));
    const dialogue = await screen.findByRole('dialog');
    await userEvent.click(within(dialogue).getByRole('button', { name: 'Enregistrer' }));
    await waitFor(() =>
      expect(api.modifierModeleRecette).toHaveBeenCalledWith('r1', {
        version: 3,
        titre: 'Connexion au portail',
        systemeCatalogueId: 'c-web',
        etapes: [{ instructions: 'Ouvrir le portail', resultatAttendu: 'OK' }],
      }),
    );
  });

  it('seul un administrateur peut supprimer un modèle', async () => {
    rendre(false);
    await screen.findByText('Connexion au portail');
    expect(screen.queryByRole('button', { name: /Supprimer Connexion/ })).toBeNull();
  });
});
