import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { AuthContext } from '../auth';
import { utilisateur } from '../test/fabrique';
import type { ResolutionType } from '../types';
import { TooltipProvider } from '../ui/Tooltip';
import ResolutionsPage from './ResolutionsPage';

const res = (
  id: string,
  titre: string,
  systemeCatalogueIds: string[],
  symptomes: string,
  utilisations: number,
  creeeLe = '2026-01-01T00:00:00Z',
): ResolutionType => ({
  id,
  titre,
  symptomes,
  diagnostic: '',
  systemeCatalogueIds,
  corrections: [],
  verificationInstruction: '',
  verificationAttendu: '',
  actif: true,
  version: 1,
  modifieeLe: '2026-10-10T10:00:00Z',
  modifieeParId: null,
  modifieeParNom: null,
  utilisations,
  creeeLe,
});

vi.mock('../api', () => ({
  api: {
    resolutions: vi.fn(async () => [
      res('r1', 'API : le service ne démarre pas', ['c-api'], 'GET /sante renvoie 503', 2),
      res('r2', 'API + BDD : connexion refusée', ['c-api', 'c-bdd'], 'Erreur de connexion', 7),
      res('r3', 'Redémarrage général', [], 'Le service ne répond plus', 2, '2025-06-01T00:00:00Z'),
    ]),
    catalogueSystemes: vi.fn(async () => [
      { id: 'c-bdd', nom: 'BDD', description: '', cible: '', actif: true },
      { id: 'c-api', nom: 'API', description: '', cible: '', actif: true },
    ]),
  },
}));

function rendre(url = '/parametres/resolutions') {
  render(
    <MemoryRouter initialEntries={[url]}>
      <AuthContext.Provider value={{ utilisateur: utilisateur({ estAdmin: false }) } as never}>
        <TooltipProvider>
          <Routes>
            <Route path="/parametres/resolutions" element={<ResolutionsPage />} />
          </Routes>
        </TooltipProvider>
      </AuthContext.Provider>
    </MemoryRouter>,
  );
}

const titres = () => screen.getAllByRole('heading', { level: 2 }).map((h) => h.textContent);

describe('Résolutions types', () => {
  it('s’ouvre sur le choix du système ; une résolution multi-systèmes compte dans chacun', async () => {
    rendre();
    const api = await screen.findByRole('link', { name: 'API : 2 résolutions' });
    expect(api).toHaveTextContent('dont 1 multi-systèmes');
    expect(screen.getByRole('link', { name: 'BDD : 1 résolution' })).toBeInTheDocument();
    expect(screen.getByText(/1 résolution commune à tous les systèmes/)).toBeInTheDocument();
  });

  it('la bibliothèque d’un système montre aussi les résolutions communes, triées par utilisations', async () => {
    rendre('/parametres/resolutions?systeme=BDD');
    await screen.findByText('API + BDD : connexion refusée');
    expect(titres()).toEqual(['API + BDD : connexion refusée', 'Redémarrage général']);
    expect(screen.getByText('Tous les systèmes')).toBeInTheDocument();
  });

  it('la recherche porte sur le titre et les symptômes', async () => {
    rendre();
    await screen.findByRole('link', { name: 'API : 2 résolutions' });
    await userEvent.type(screen.getByLabelText('Rechercher dans la bibliothèque'), 'api 503');
    expect(titres()).toEqual(['API : le service ne démarre pas']);
    await userEvent.clear(screen.getByLabelText('Rechercher dans la bibliothèque'));
    await userEvent.type(screen.getByLabelText('Rechercher dans la bibliothèque'), 'service');
    // Même nombre d'utilisations : la plus ancienne d'abord.
    expect(titres()).toEqual(['Redémarrage général', 'API : le service ne démarre pas']);
    expect(within(screen.getByRole('status')).getByText('2 résolutions')).toBeInTheDocument();
  });
});
