import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { api } from '../../api';
import type * as moduleApi from '../../api';
import { FILTRE_VIDE } from '../../rechercheMeps';
import { AbonnementDialog, adresseAbonnement } from './AbonnementDialog';

vi.mock('../../api', async (importOriginal) => ({
  ApiError: (await importOriginal<typeof moduleApi>()).ApiError,
  api: {
    abonnementCalendrier: vi.fn(),
    regenererAbonnement: vi.fn(),
    revoquerAbonnement: vi.fn(async () => undefined),
  },
}));

const URL_ABO = 'https://mep.exemple.test/api/calendrier/abonnement/jeton-de-test.ics';

beforeEach(() => vi.clearAllMocks());

describe('AbonnementDialog', () => {
  it('ajoute les filtres et retire les gels dans l’adresse', () => {
    expect(adresseAbonnement(URL_ABO, null, true)).toBe(URL_ABO);
    expect(adresseAbonnement(URL_ABO, null, false)).toBe(`${URL_ABO}?gels=0`);
    expect(adresseAbonnement(URL_ABO, { ...FILTRE_VIDE, recherche: 'paie' }, false)).toMatch(
      /\?recherche=paie&gels=0$/,
    );
  });

  it('crée l’adresse et ne la montre qu’une fois', async () => {
    vi.mocked(api.abonnementCalendrier).mockResolvedValue({
      actif: false,
      creeLe: null,
      dernierAcces: null,
      url: null,
    });
    vi.mocked(api.regenererAbonnement).mockResolvedValue({
      actif: true,
      creeLe: '2026-11-02T08:00:00Z',
      dernierAcces: null,
      url: URL_ABO,
    });
    render(<AbonnementDialog open onOpenChange={vi.fn()} f={FILTRE_VIDE} />);
    expect(await screen.findByText(/Vous n’avez pas d’adresse d’abonnement/)).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Créer mon adresse' }));
    expect(await screen.findByText(URL_ABO)).toBeInTheDocument();
    expect(screen.getByText(/Copiez-la maintenant : elle ne sera plus affichée/)).toBeInTheDocument();
  });

  it('régénère ou révoque une adresse existante après confirmation', async () => {
    vi.mocked(api.abonnementCalendrier).mockResolvedValue({
      actif: true,
      creeLe: '2026-10-01T08:00:00Z',
      dernierAcces: null,
      url: null,
    });
    render(<AbonnementDialog open onOpenChange={vi.fn()} f={FILTRE_VIDE} />);
    expect(await screen.findByText(/pas encore lue/)).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Révoquer' }));
    expect(await screen.findByText('Révoquer l’adresse d’abonnement ?')).toBeInTheDocument();
    const boutons = screen.getAllByRole('button', { name: 'Révoquer' });
    await userEvent.click(boutons[boutons.length - 1]);
    expect(api.revoquerAbonnement).toHaveBeenCalled();
    expect(await screen.findByRole('button', { name: 'Créer mon adresse' })).toBeInTheDocument();
  });

  it('dit quand l’état ne se charge pas', async () => {
    vi.mocked(api.abonnementCalendrier).mockRejectedValue(new Error('Serveur injoignable'));
    render(<AbonnementDialog open onOpenChange={vi.fn()} f={FILTRE_VIDE} />);
    expect(await screen.findByText(/^État de l’abonnement non chargé/)).toBeInTheDocument();
  });
});
