import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { ApiError } from '../api';
import { AuthContext } from '../auth';
import { utilisateur } from '../test/fabrique';
import { causeErreur, ErreurChargement } from './ErreurChargement';

const erreur = (status: number, message = `Erreur ${status}.`) =>
  new ApiError(status, message, [], 'GET /api/catalogue/systemes');

const SESSIONS = {
  admin: { utilisateur: utilisateur({ estAdmin: true }) } as never,
  autre: { utilisateur: utilisateur({ estAdmin: false }) } as never,
};

function rendre(contenu: React.ReactNode, estAdmin = false) {
  return render(
    <AuthContext.Provider value={estAdmin ? SESSIONS.admin : SESSIONS.autre}>
      <MemoryRouter>{contenu}</MemoryRouter>
    </AuthContext.Provider>,
  );
}

describe('causeErreur : message selon la réponse', () => {
  it('réseau coupé', () => {
    expect(causeErreur(erreur(0)).message).toBe('Le serveur est injoignable. Vérifiez votre connexion.');
  });

  it('serveur en cours de mise à jour (502, 503, 504)', () => {
    for (const s of [502, 503, 504]) {
      const c = causeErreur(erreur(s));
      expect(c.message).toBe(
        `Le serveur ne répond pas (${s}). Cela arrive pendant une mise à jour de Gestion MEP : réessayez dans un instant.`,
      );
      expect(c.reessayable).toBe(true);
    }
  });

  it('erreur interne : message de l’API s’il y en a un', () => {
    expect(causeErreur(erreur(500)).message).toBe('Erreur interne du serveur (500).');
    expect(causeErreur(erreur(500, 'Base de données indisponible.')).message).toBe(
      'Base de données indisponible (500).',
    );
  });

  it('accès refusé et objet supprimé : pas de « Réessayer »', () => {
    expect(causeErreur(erreur(403))).toMatchObject({
      message: 'Vous n’avez pas accès à ces informations.',
      reessayable: false,
    });
    expect(causeErreur(erreur(404), 'Cette MEP n’existe pas ou a été supprimée.')).toMatchObject({
      message: 'Cette MEP n’existe pas ou a été supprimée.',
      reessayable: false,
    });
  });
});

describe('ErreurChargement', () => {
  it('bloc : titre, cause et « Réessayer » branché sur le rechargement', async () => {
    const recharger = vi.fn();
    rendre(
      <ErreurChargement
        erreur={erreur(500)}
        titre="Impossible de charger le catalogue des systèmes"
        complement="Votre saisie n’est pas touchée."
        onReessayer={recharger}
      />,
    );
    const alerte = screen.getByRole('alert');
    expect(alerte).toHaveTextContent('Impossible de charger le catalogue des systèmes');
    expect(alerte).toHaveTextContent('Erreur interne du serveur (500). Votre saisie n’est pas touchée.');
    await userEvent.click(screen.getByRole('button', { name: 'Réessayer' }));
    expect(recharger).toHaveBeenCalledTimes(1);
  });

  it('ligne : une phrase courte dans la liste', () => {
    rendre(
      <>
        <ErreurChargement variante="ligne" erreur={erreur(500)} titre="Catalogue des systèmes non chargé" />
        <ErreurChargement variante="ligne" erreur={erreur(0)} titre="MEP non chargées" />
      </>,
    );
    const [a, b] = screen.getAllByRole('alert');
    expect(a).toHaveTextContent('Catalogue des systèmes non chargé (erreur 500).');
    expect(b).toHaveTextContent('MEP non chargées : le serveur est injoignable.');
  });

  it('page : « Réessayer » et « Retour à la liste »', () => {
    rendre(
      <ErreurChargement
        variante="page"
        erreur={erreur(502)}
        titre="Impossible d’ouvrir cette MEP"
        onReessayer={() => undefined}
        retour={{ libelle: 'Retour à la liste', vers: '/' }}
      />,
    );
    expect(screen.getByRole('heading', { name: 'Impossible d’ouvrir cette MEP' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Réessayer' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Retour à la liste' })).toHaveAttribute('href', '/');
  });

  it('403 : pas de bouton « Réessayer »', () => {
    rendre(<ErreurChargement erreur={erreur(403)} titre="Impossible de charger" onReessayer={() => undefined} />);
    expect(screen.queryByRole('button', { name: 'Réessayer' })).not.toBeInTheDocument();
  });

  it('401 : rien (le dialogue de reconnexion s’en charge)', () => {
    const { container } = rendre(<ErreurChargement erreur={erreur(401)} titre="Impossible de charger" />);
    expect(container).toBeEmptyDOMElement();
  });

  it('détails techniques réservés aux administrateurs', () => {
    const { unmount } = rendre(<ErreurChargement erreur={erreur(500)} titre="Impossible de charger" />);
    expect(screen.queryByText(/Détails techniques/)).not.toBeInTheDocument();
    unmount();
    rendre(<ErreurChargement erreur={erreur(500)} titre="Impossible de charger" />, true);
    expect(screen.getByText(/Détails techniques/)).toBeInTheDocument();
    expect(screen.getByText(/GET \/api\/catalogue\/systemes → 500/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Copier les détails' })).toBeInTheDocument();
  });
});
