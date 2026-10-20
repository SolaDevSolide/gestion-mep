import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { api, ApiError } from '../api';
import type { Mep } from '../types';
import { etape, mep } from '../test/fabrique';
import type { ResolutionType } from '../types';
import NouvelleResolutionPage from './NouvelleResolutionPage';

const base = mep().systemes[0];
const laMep = mep({
  phase: 'Preparation',
  systemes: [
    { ...base, id: 's1', nom: 'API', catalogueId: 'c-api' },
    { ...base, id: 's2', nom: 'WEB', catalogueId: 'c-web' },
  ],
  etapes: [
    etape({
      id: 'e6',
      code: 'E0006',
      action: 'Publication du portail',
      systemeIds: ['s2', 's1'],
      resolutionIds: ['r-existante'],
      revision: 4,
    }),
  ],
});
const creee: ResolutionType = {
  id: 'r-neuve',
  titre: 'WEB : purge du cache CDN refusée',
  symptomes: '',
  diagnostic: '',
  systemeCatalogueIds: ['c-web'],
  corrections: [],
  verificationInstruction: '',
  verificationAttendu: '',
  actif: true,
  version: 1,
  modifieeLe: '2026-10-10T10:00:00Z',
  modifieeParId: null,
  modifieeParNom: null,
  utilisations: 0,
  creeeLe: '2026-01-01T00:00:00Z',
};

vi.mock('../api', () => ({
  api: {
    catalogueSystemes: vi.fn(async () => [
      { id: 'c-api', nom: 'API', description: 'Service commandes', cible: '', actif: true },
      { id: 'c-web', nom: 'WEB', description: 'Portail public', cible: '', actif: true },
    ]),
    mep: vi.fn(async () => laMep),
    creerResolution: vi.fn(async () => creee),
    modifierEtape: vi.fn(async () => laMep),
  },
  ApiError: class ErreurApi extends Error {
    status: number;
    erreurs: string[] = [];
    requete = '';
    date = new Date();
    constructor(status: number, message: string) {
      super(message);
      this.status = status;
    }
  },
}));

function Destination() {
  const l = useLocation();
  return (
    <p>
      Destination : {l.pathname}
      {l.search}
    </p>
  );
}

function rendre(url: string) {
  render(
    <MemoryRouter initialEntries={[url]}>
      <Routes>
        <Route path="/parametres/resolutions/nouvelle" element={<NouvelleResolutionPage />} />
        <Route path="*" element={<Destination />} />
      </Routes>
    </MemoryRouter>,
  );
}

describe('NouvelleResolutionPage', () => {
  beforeEach(() => vi.clearAllMocks());

  it('depuis une étape : système pré-sélectionné, aperçu en direct, enregistrer et lier', async () => {
    rendre('/parametres/resolutions/nouvelle?mep=m1&etape=e6');
    const bouton = await screen.findByRole('button', { name: 'Enregistrer et lier à E0006' });
    expect(screen.getByRole('link', { name: /Retour à E0006 · Publication du portail/ })).toBeInTheDocument();
    await waitFor(() => expect(screen.getByRole('button', { name: 'WEB', pressed: true })).toBeInTheDocument());
    expect(screen.getByRole('button', { name: 'API', pressed: true })).toBeInTheDocument();

    await userEvent.type(screen.getByLabelText('Titre *'), 'WEB : purge du cache CDN refusée');
    await userEvent.type(screen.getByLabelText('Diagnostic, bloc 1 (code)'), 'curl -I https://cdn');
    const apercu = screen.getByRole('region', { name: 'Aperçu en exécution' });
    expect(apercu).toHaveTextContent('WEB : purge du cache CDN refusée');
    expect(apercu).toHaveTextContent('curl -I https://cdn');

    await userEvent.click(bouton);
    await waitFor(() => expect(api.modifierEtape).toHaveBeenCalled());
    expect(api.creerResolution).toHaveBeenCalledWith(
      expect.objectContaining({
        titre: 'WEB : purge du cache CDN refusée',
        systemeCatalogueIds: ['c-api', 'c-web'],
        diagnostic: '```\ncurl -I https://cdn\n```',
      }),
    );
    expect(api.modifierEtape).toHaveBeenCalledWith(
      'm1',
      'e6',
      expect.objectContaining({ revision: 4, resolutionIds: ['r-existante', 'r-neuve'] }),
    );
    expect(await screen.findByText('Destination : /meps/m1?onglet=plan&plan=etapes&etape=e6')).toBeInTheDocument();
  });

  it('titre obligatoire', async () => {
    rendre('/parametres/resolutions/nouvelle?mep=m1&etape=e6');
    await userEvent.click(await screen.findByRole('button', { name: 'Enregistrer et lier à E0006' }));
    expect(screen.getByRole('alert')).toHaveTextContent('Donnez un titre');
    expect(api.creerResolution).not.toHaveBeenCalled();
  });

  it('sans paramètres : simple création, retour aux résolutions types', async () => {
    rendre('/parametres/resolutions/nouvelle');
    await userEvent.type(screen.getByLabelText('Titre *'), 'Nouvelle fiche');
    await userEvent.click(screen.getByRole('button', { name: 'Enregistrer' }));
    expect(await screen.findByText('Destination : /parametres/resolutions')).toBeInTheDocument();
    expect(api.modifierEtape).not.toHaveBeenCalled();
  });
});

describe('NouvelleResolutionPage : systèmes multiples et titre proposé', () => {
  beforeEach(() => vi.clearAllMocks());

  it('titre pré-rempli par la recherche, plusieurs systèmes sélectionnables', async () => {
    rendre('/parametres/resolutions/nouvelle?titre=LDAP%20muet');
    expect(screen.getByLabelText('Titre *')).toHaveValue('LDAP muet');
    await userEvent.click(await screen.findByRole('button', { name: 'API', pressed: false }));
    await userEvent.click(screen.getByRole('button', { name: 'WEB', pressed: false }));
    await userEvent.click(screen.getByRole('button', { name: 'Enregistrer' }));
    expect(api.creerResolution).toHaveBeenCalledWith(
      expect.objectContaining({ titre: 'LDAP muet', systemeCatalogueIds: ['c-api', 'c-web'] }),
    );
  });
});

describe('NouvelleResolutionPage : pas d’enregistrement avant l’étape à lier', () => {
  beforeEach(() => vi.clearAllMocks());

  /** Réponse de la MEP retenue : elle arrive quand le test le décide. */
  function mepRetenue() {
    let resoudre: (m: Mep) => void = () => undefined;
    vi.mocked(api.mep).mockReturnValueOnce(
      new Promise<Mep>((r) => {
        resoudre = r;
      }),
    );
    return (m: Mep) => resoudre(m);
  }

  it('« Enregistrer et lier » attend l’étape, avec son motif ; la saisie reste possible', async () => {
    const livrer = mepRetenue();
    rendre('/parametres/resolutions/nouvelle?mep=m1&etape=e6');
    const bouton = screen.getByRole('button', { name: 'Enregistrer et lier' });
    expect(bouton).toHaveAttribute('aria-disabled', 'true');
    expect(bouton).toHaveAccessibleDescription('Chargement de l’étape à lier… La saisie reste possible.');
    // Ni fil d'Ariane vers « Résolutions types », ni titre qui changera à l'arrivée de la MEP.
    expect(screen.queryByRole('link', { name: /Résolutions types/ })).toBeNull();

    await userEvent.type(screen.getByLabelText('Titre *'), 'Purge refusée{Enter}');
    expect(screen.getByLabelText('Titre *')).toHaveValue('Purge refusée');
    await userEvent.click(bouton);
    expect(api.creerResolution).not.toHaveBeenCalled();

    livrer(laMep);
    const pret = await screen.findByRole('button', { name: 'Enregistrer et lier à E0006' });
    expect(pret).not.toHaveAttribute('aria-disabled');
    expect(screen.getByLabelText('Titre *')).toHaveValue('Purge refusée');
    await userEvent.click(pret);
    await waitFor(() => expect(api.modifierEtape).toHaveBeenCalled());
  });

  it('MEP non chargée : l’erreur se dit en ligne, « Réessayer » relance, sinon « Enregistrer sans lier »', async () => {
    vi.mocked(api.mep).mockRejectedValueOnce(new ApiError(502, 'Erreur 502.'));
    rendre('/parametres/resolutions/nouvelle?mep=m1&etape=e6');
    const alerte = (await screen.findByText(/Étape à lier non chargée : le serveur ne répond pas \(502\)/)).closest(
      '[role="alert"]',
    ) as HTMLElement;
    expect(screen.getByRole('link', { name: /Résolutions types/ })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Enregistrer sans lier' })).not.toHaveAttribute('aria-disabled');

    await userEvent.click(within(alerte).getByRole('button', { name: 'Réessayer' }));
    expect(await screen.findByRole('button', { name: 'Enregistrer et lier à E0006' })).toBeInTheDocument();
    expect(api.mep).toHaveBeenCalledTimes(2);
  });

  it('« Enregistrer sans lier » crée la fiche sans toucher à l’étape', async () => {
    vi.mocked(api.mep).mockRejectedValueOnce(new ApiError(502, 'Erreur 502.'));
    rendre('/parametres/resolutions/nouvelle?mep=m1&etape=e6');
    await userEvent.type(screen.getByLabelText('Titre *'), 'Purge refusée');
    await userEvent.click(await screen.findByRole('button', { name: 'Enregistrer sans lier' }));
    expect(await screen.findByText('Destination : /parametres/resolutions')).toBeInTheDocument();
    expect(api.creerResolution).toHaveBeenCalled();
    expect(api.modifierEtape).not.toHaveBeenCalled();
  });
});
