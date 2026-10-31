import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import type * as moduleApi from '../../api';
import { api } from '../../api';
import type { ApplicationLiaison, CatalogueLiaison, LiaisonNoosphere } from '../../types';
import { ToastProvider } from '../../ui/Toast';
import LiaisonsTab from './LiaisonsTab';

vi.mock('../../api', async (original) => ({
  ApiError: (await original<typeof moduleApi>()).ApiError,
  api: {
    liaisons: vi.fn(),
    liaison: vi.fn(),
    statutLiaison: vi.fn(),
    appairerLiaison: vi.fn(),
    confirmerLiaison: vi.fn(),
    catalogueLiaison: vi.fn(),
    suiviLiaison: vi.fn(),
    optionsLiaison: vi.fn(),
    synchroniserLiaison: vi.fn(),
    renouvelerLiaison: vi.fn(),
    couperLiaison: vi.fn(),
    echangesLiaisons: vi.fn(async () => []),
    application: vi.fn(async () => ({ version: '1.0', lienSignalement: null, miseAJour: null, installation: 'linux' })),
  },
}));

function liaison(p: Partial<LiaisonNoosphere> = {}): LiaisonNoosphere {
  return {
    id: 'l1',
    produit: 'noosphere',
    nomInstance: 'Noosphere Alizé',
    url: 'https://docs.alize.example',
    versionNoosphere: '0.6.0',
    etat: 'Active',
    confirmeeIci: true,
    confirmeeLe: '2026-10-22T10:00:00Z',
    confirmeePar: 'Alice',
    confirmeeParNoosphere: true,
    capacites: ['catalog:read', 'notices:send', 'deployments:receive'],
    empreinte: 'maple quartz 3381 heron',
    appaireeLe: '2026-10-22T09:58:00Z',
    appaireePar: 'Alice',
    clesRenouveleesLe: null,
    dernierEchangeLe: '2026-10-31T11:44:00Z',
    coupeeLe: null,
    coupeePar: '',
    envoyerMeps: true,
    suiviAutomatique: true,
    domainesSuivis: ['finance'],
    derniereSynchro: '2026-10-31T11:44:00Z',
    derniereErreur: '',
    systemesSuivis: 2,
    demo: false,
    ...p,
  };
}

function app(p: Partial<ApplicationLiaison> & { id: string; nom: string }): ApplicationLiaison {
  return {
    description: '',
    domaineId: 'finance',
    domaine: 'Finance',
    equipe: 'Équipe Finance',
    type: 'app',
    cycle: 'live',
    url: `https://docs.alize.example/applications/${p.id}`,
    suivie: false,
    systemeId: null,
    systeme: null,
    suggestions: [],
    ...p,
  };
}

const catalogue: CatalogueLiaison = {
  luLe: '2026-10-31T11:44:00Z',
  nomInstance: 'Noosphere Alizé',
  domaines: [
    { id: 'finance', nom: 'Finance', suivi: true, nbApplications: 2 },
    { id: 'socle', nom: 'Socle', suivi: false, nbApplications: 3 },
  ],
  applications: [
    app({ id: 'billing', nom: 'Billing', suivie: true, systemeId: 's1', systeme: 'Billing' }),
    app({
      id: 'archive',
      nom: 'Invoice archive',
      cycle: 'retired',
      suivie: true,
      systemeId: 's2',
      systeme: 'Invoice archive',
    }),
    app({
      id: 'crm',
      nom: 'CRM',
      domaineId: 'socle',
      domaine: 'Socle',
      equipe: 'Équipe CRM',
      suggestions: [{ id: 's3', nom: 'CRM', memeNom: true }],
    }),
    app({
      id: 'payments',
      nom: 'Payments',
      domaineId: 'socle',
      domaine: 'Socle',
      suggestions: [{ id: 's4', nom: 'PAYMENT', memeNom: false }],
    }),
    app({ id: 'bus', nom: 'Event bus', domaineId: 'socle', domaine: 'Socle' }),
  ],
};

function rendre() {
  render(
    <MemoryRouter>
      <ToastProvider>
        <LiaisonsTab />
      </ToastProvider>
    </MemoryRouter>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(api.catalogueLiaison).mockResolvedValue(catalogue);
  vi.mocked(api.echangesLiaisons).mockResolvedValue([]);
});

describe('Onglet Liaisons : appairer', () => {
  it('sans liaison : adresse et code, puis l’empreinte à comparer', async () => {
    vi.mocked(api.liaisons).mockResolvedValue([]);
    const enAttente = liaison({ etat: 'EnAttente', confirmeeIci: false, confirmeeParNoosphere: null });
    vi.mocked(api.appairerLiaison).mockResolvedValue(enAttente);
    vi.mocked(api.statutLiaison).mockResolvedValue(enAttente);
    rendre();
    expect(await screen.findByText('Aucune liaison')).toBeInTheDocument();
    expect(screen.getByText('Ce qui sera échangé')).toBeInTheDocument();
    const bouton = screen.getByRole('button', { name: 'Appairer' });
    expect(bouton).toBeDisabled();
    await userEvent.type(screen.getByLabelText('Adresse de Noosphere'), 'https://docs.alize.example');
    await userEvent.type(screen.getByLabelText('Code d’appairage'), '7K4Q-29XD-M3PA');
    await userEvent.click(bouton);
    expect(api.appairerLiaison).toHaveBeenCalledWith({ url: 'https://docs.alize.example', code: '7K4Q-29XD-M3PA' });
    expect(await screen.findByText('Comparer l’empreinte')).toBeInTheDocument();
    expect(screen.getByLabelText('Empreinte : maple quartz 3381 heron')).toBeInTheDocument();
    // Aucun état du pair n'est relu : rien n'était en service à l'ouverture.
    expect(api.statutLiaison).not.toHaveBeenCalled();
  });

  it('code refusé : le motif s’affiche au-dessus du formulaire', async () => {
    vi.mocked(api.liaisons).mockResolvedValue([]);
    vi.mocked(api.appairerLiaison).mockRejectedValue(new Error('Code inconnu ou expiré.'));
    rendre();
    await userEvent.type(await screen.findByLabelText('Adresse de Noosphere'), 'https://n.example');
    await userEvent.type(screen.getByLabelText('Code d’appairage'), 'AAAA');
    await userEvent.click(screen.getByRole('button', { name: 'Appairer' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Code inconnu ou expiré.');
  });

  it('liaisons coupées en historique sous le formulaire', async () => {
    vi.mocked(api.liaisons).mockResolvedValue([
      liaison({ etat: 'Coupee', coupeeLe: '2026-10-30T08:00:00Z', coupeePar: 'Bob' }),
    ]);
    rendre();
    expect(await screen.findByText('Liaisons coupées')).toBeInTheDocument();
    expect(screen.getByText(/par Bob/)).toBeInTheDocument();
    expect(api.statutLiaison).not.toHaveBeenCalled();
  });
});

describe('Onglet Liaisons : empreinte', () => {
  it('relit l’état de Noosphere à l’ouverture, puis confirme les mots identiques', async () => {
    const enAttente = liaison({ etat: 'EnAttente', confirmeeIci: false, confirmeeParNoosphere: null });
    vi.mocked(api.liaisons).mockResolvedValueOnce([enAttente]);
    vi.mocked(api.statutLiaison).mockResolvedValue({ ...enAttente, confirmeeParNoosphere: true });
    rendre();
    expect(await screen.findByText('Comparer l’empreinte')).toBeInTheDocument();
    expect(api.statutLiaison).toHaveBeenCalledWith('l1');
    // L'état relu (et non celui de la base) : Noosphere a déjà confirmé.
    expect(screen.getByText('Confirmée')).toBeInTheDocument();

    vi.mocked(api.confirmerLiaison).mockResolvedValue(liaison());
    vi.mocked(api.liaisons).mockResolvedValue([liaison()]);
    vi.mocked(api.statutLiaison).mockResolvedValue(liaison());
    await userEvent.click(screen.getByRole('button', { name: 'Les mots sont identiques : confirmer' }));
    expect(api.confirmerLiaison).toHaveBeenCalledWith('l1', 'maple quartz 3381 heron');
    expect(await screen.findByText('Confirmée des deux côtés')).toBeInTheDocument();
  });

  it('« Ils diffèrent » coupe la liaison et revient au formulaire', async () => {
    const enAttente = liaison({ etat: 'EnAttente', confirmeeIci: false, confirmeeParNoosphere: false });
    vi.mocked(api.liaisons).mockResolvedValue([enAttente]);
    vi.mocked(api.statutLiaison).mockResolvedValue(enAttente);
    vi.mocked(api.couperLiaison).mockResolvedValue({ ...enAttente, etat: 'Coupee', coupeeLe: '2026-10-31T12:00:00Z' });
    rendre();
    await userEvent.click(await screen.findByRole('button', { name: 'Ils diffèrent : annuler' }));
    expect(api.couperLiaison).toHaveBeenCalledWith('l1');
    expect(await screen.findByText('Aucune liaison')).toBeInTheDocument();
  });
});

describe('Onglet Liaisons : liaison active', () => {
  it('affiche la liaison relue, le journal et le résumé des applications suivies', async () => {
    vi.mocked(api.liaisons).mockResolvedValue([liaison({ confirmeeParNoosphere: null })]);
    vi.mocked(api.statutLiaison).mockResolvedValue(liaison());
    vi.mocked(api.echangesLiaisons).mockResolvedValue([
      {
        id: 'x1',
        liaisonId: 'l1',
        quand: '2026-10-31T10:02:00Z',
        sens: 'entrant',
        operation: 'notices',
        resultat: 'refuse',
        code: 401,
        detail: 'signature invalide (bad-signature)',
      },
    ]);
    rendre();
    expect(await screen.findByText('Confirmée des deux côtés')).toBeInTheDocument();
    expect(screen.getByLabelText('Empreinte : maple quartz 3381 heron')).toBeInTheDocument();
    expect(screen.getByText('Recevoir les MEP')).toBeInTheDocument();
    expect(await screen.findByText('Requête refusée')).toBeInTheDocument();
    expect(screen.getByText('Refusée')).toBeInTheDocument();
    expect(api.echangesLiaisons).toHaveBeenCalledWith({ liaison: 'l1', limite: 20, refus: false });
    // Résumé : Finance suivie en entier, une application retirée.
    expect(screen.getByText('Finance · 2')).toBeInTheDocument();
    expect(screen.getByText(/Invoice archive : système inactif/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Voir le catalogue' })).toHaveAttribute(
      'href',
      '/parametres/systemes?source=noosphere',
    );
  });

  it('Noosphere injoignable à l’ouverture : la liaison s’affiche avec un avertissement', async () => {
    vi.mocked(api.liaisons).mockResolvedValue([liaison()]);
    vi.mocked(api.statutLiaison).mockRejectedValue(new Error('Noosphere injoignable.'));
    rendre();
    expect(await screen.findByText('État de Noosphere non relu')).toBeInTheDocument();
    expect(screen.getByText(/Noosphere injoignable\./)).toBeInTheDocument();
  });

  it('démonstration : rien n’est relu chez Noosphere, la liaison est marquée', async () => {
    vi.mocked(api.liaisons).mockResolvedValue([liaison({ demo: true, url: 'https://noosphere.demo.invalid' })]);
    rendre();
    expect(await screen.findByText('Démonstration')).toBeInTheDocument();
    expect(api.statutLiaison).not.toHaveBeenCalled();
  });

  it('couper demande une confirmation dans la page', async () => {
    vi.mocked(api.liaisons).mockResolvedValue([liaison()]);
    vi.mocked(api.statutLiaison).mockResolvedValue(liaison());
    rendre();
    await userEvent.click(await screen.findByRole('button', { name: 'Couper la liaison' }));
    const alerte = screen.getByRole('alert');
    expect(alerte).toHaveTextContent('Couper la liaison avec Noosphere Alizé ?');
    expect(alerte).toHaveTextContent('2 systèmes importés restent dans le catalogue');
    await userEvent.click(within(alerte).getByRole('button', { name: 'Annuler' }));
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(api.couperLiaison).not.toHaveBeenCalled();

    vi.mocked(api.couperLiaison).mockResolvedValue(liaison({ etat: 'Coupee' }));
    vi.mocked(api.liaisons).mockResolvedValue([liaison({ etat: 'Coupee', coupeeLe: '2026-10-31T12:00:00Z' })]);
    await userEvent.click(screen.getByRole('button', { name: 'Couper la liaison' }));
    await userEvent.click(within(screen.getByRole('alert')).getByRole('button', { name: 'Couper la liaison' }));
    expect(api.couperLiaison).toHaveBeenCalledWith('l1');
    expect(await screen.findByText('Aucune liaison')).toBeInTheDocument();
  });

  it('« Envoyer les MEP à Noosphere » se coupe d’un clic', async () => {
    vi.mocked(api.liaisons).mockResolvedValue([liaison()]);
    vi.mocked(api.statutLiaison).mockResolvedValue(liaison());
    vi.mocked(api.optionsLiaison).mockResolvedValue(liaison({ envoyerMeps: false }));
    rendre();
    await userEvent.click(await screen.findByRole('checkbox', { name: /Envoyer les MEP à Noosphere/ }));
    expect(api.optionsLiaison).toHaveBeenCalledWith('l1', { envoyerMeps: false, suiviAutomatique: null });
    await waitFor(() =>
      expect(screen.getByRole('checkbox', { name: /Envoyer les MEP à Noosphere/ })).not.toBeChecked(),
    );
  });

  it('« Synchroniser maintenant » relit le catalogue', async () => {
    vi.mocked(api.liaisons).mockResolvedValue([liaison()]);
    vi.mocked(api.statutLiaison).mockResolvedValue(liaison());
    vi.mocked(api.synchroniserLiaison).mockResolvedValue({
      inchange: true,
      resume: 'Catalogue inchangé.',
      crees: [],
      rattaches: [],
      misAJour: [],
      desactives: [],
      reactives: [],
      avertissements: [],
      liaison: liaison(),
    });
    rendre();
    await userEvent.click(await screen.findByRole('button', { name: 'Synchroniser maintenant' }));
    expect(api.synchroniserLiaison).toHaveBeenCalledWith('l1');
    expect(await screen.findByText('Catalogue inchangé.')).toBeInTheDocument();
  });
});

describe('Onglet Liaisons : applications suivies et rapprochement', () => {
  it('suivre un domaine, puis rapprocher : le nom proche attend un choix, rien n’est appliqué avant de valider', async () => {
    vi.mocked(api.liaisons).mockResolvedValue([liaison()]);
    vi.mocked(api.statutLiaison).mockResolvedValue(liaison());
    vi.mocked(api.suiviLiaison).mockResolvedValue(catalogue);
    vi.mocked(api.liaison).mockResolvedValue(liaison({ systemesSuivis: 5 }));
    rendre();
    await userEvent.click(await screen.findByRole('button', { name: 'Modifier' }));
    expect(screen.getByText('Applications suivies', { selector: 'h2' })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('checkbox', { name: 'Suivre tout le domaine Socle' }));
    expect(screen.getByText(/5 applications suivies/)).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Continuer : rapprocher du catalogue' }));

    expect(screen.getByText('Rapprocher du catalogue')).toBeInTheDocument();
    // Même nom : proposé, à confirmer en validant ; nom proche : un choix est attendu ; aucun proche : création.
    expect(screen.getByText('Rattachée à CRM')).toBeInTheDocument();
    expect(screen.getByText('nom proche')).toBeInTheDocument();
    const valider = screen.getByRole('button', { name: 'Valider et synchroniser' });
    expect(valider).toBeDisabled();
    expect(screen.getByText(/1 rattachement à choisir/)).toBeInTheDocument();
    expect(api.suiviLiaison).not.toHaveBeenCalled();

    const payments = screen.getByRole('group', { name: 'Choix pour Payments' });
    await userEvent.click(within(payments).getByRole('button', { name: 'Créer un système' }));
    // Refuser le rattachement au même nom : un nouveau système sera créé.
    const crm = screen.getByRole('group', { name: 'Choix pour CRM' });
    await userEvent.click(within(crm).getByRole('button', { name: 'Changer' }));
    await userEvent.click(within(crm).getByRole('button', { name: 'Créer un système' }));
    await userEvent.click(valider);
    expect(api.suiviLiaison).toHaveBeenCalledWith('l1', {
      domainesSuivis: ['finance', 'socle'],
      applications: [
        { applicationId: 'crm', suivre: true, systemeId: null, creer: true },
        { applicationId: 'payments', suivre: true, systemeId: null, creer: true },
        { applicationId: 'bus', suivre: true, systemeId: null, creer: true },
      ],
    });
    expect(await screen.findByText('Journal des échanges')).toBeInTheDocument();
  });

  it('décocher une application d’un domaine suivi la rend locale, sans étape de rapprochement', async () => {
    vi.mocked(api.liaisons).mockResolvedValue([liaison()]);
    vi.mocked(api.statutLiaison).mockResolvedValue(liaison());
    vi.mocked(api.suiviLiaison).mockResolvedValue(catalogue);
    vi.mocked(api.liaison).mockResolvedValue(liaison());
    rendre();
    await userEvent.click(await screen.findByRole('button', { name: 'Modifier' }));
    await userEvent.click(screen.getByRole('button', { name: /^Finance/ }));
    await userEvent.click(screen.getByRole('checkbox', { name: /Invoice archive/ }));
    expect(screen.getByText(/1 sur 2, choisie une par une/)).toBeInTheDocument();
    expect(screen.getByText(/ne sera plus suivi : le système reste au catalogue/)).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Enregistrer et synchroniser' }));
    expect(api.suiviLiaison).toHaveBeenCalledWith('l1', {
      domainesSuivis: [],
      applications: [{ applicationId: 'archive', suivre: false, systemeId: null }],
    });
  });

  it('catalogue lu sans rien de suivi : le choix des applications s’ouvre de lui-même', async () => {
    const neuve = liaison({ systemesSuivis: 0, domainesSuivis: [] });
    vi.mocked(api.liaisons).mockResolvedValue([neuve]);
    vi.mocked(api.statutLiaison).mockResolvedValue(neuve);
    vi.mocked(api.catalogueLiaison).mockResolvedValue({
      ...catalogue,
      domaines: catalogue.domaines.map((d) => ({ ...d, suivi: false })),
      applications: catalogue.applications.map((a) => ({ ...a, suivie: false, systemeId: null, systeme: null })),
    });
    rendre();
    expect(await screen.findByText('Liaison confirmée')).toBeInTheDocument();
    expect(screen.getByRole('checkbox', { name: /Suivre automatiquement/ })).toBeChecked();
    expect(screen.queryByRole('button', { name: 'Annuler' })).not.toBeInTheDocument();
  });
});
