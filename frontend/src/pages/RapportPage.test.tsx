import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import type * as moduleApi from '../api';
import { api, ApiError } from '../api';
import { telecharger } from '../telechargement';
import { etape, mep } from '../test/fabrique';
import type { RapportFin } from '../types';
import RapportPage from './RapportPage';

vi.mock('../api', async (importOriginal) => {
  const vrai = await importOriginal<typeof moduleApi>();
  return {
    ApiError: vrai.ApiError,
    api: {
      rapport: vi.fn(),
      catalogueSystemes: vi.fn(),
      resolutions: vi.fn(),
      urlExportRapport: vrai.api.urlExportRapport,
    },
  };
});
vi.mock('../telechargement', () => ({ telecharger: vi.fn() }));
// Le graphe (React Flow) a sa propre couverture : ici, seulement ce qu'il reçoit.
vi.mock('../mep/graphe/GrapheLibre', () => ({
  GrapheLibre: ({ etapes }: { etapes: unknown[] }) => <p>Plan coloré de {etapes.length} étapes</p>,
}));

function rapport(): RapportFin {
  const m = mep({
    phase: 'Cloturee',
    issue: 'Reussie',
    // E0003 a échoué puis a été reprise (#91).
    etat: 'AvecIncidents',
    echecsSurvenus: 1,
    outil: 'Gestion des stocks',
    versionActuelle: '6.2',
    versionCible: '6.3',
    participants: [{ id: 'p1', nom: 'Bob', role: 'Dev', contact: '', utilisateurId: 'u2', utilisateurNom: 'Bob' }],
    etapes: [
      etape({
        id: 'e3',
        code: 'E0003',
        action: 'Déploiement du service',
        etat: 'Reussie',
        executantId: 'p1',
        debutReel: '2026-10-10T18:40:00Z',
        finReelle: '2026-10-10T18:44:00Z',
        dureeEstimeeMinutes: 15,
      }),
    ],
    bilan: { ceQuiAMarche: 'Tout', incidents: 'E0003 en échec, résolu.', actions: '', redigePar: 'Alice', le: null },
    cloture: { le: '2026-10-10T20:00:00Z', par: 'Alice' },
  });
  return {
    mepId: m.id,
    figeLe: '2026-10-16T09:00:00Z',
    calculeApres: true,
    synthese: {
      etapes: 1,
      etapesReussies: 1,
      go: '2026-10-10T18:00:00Z',
      finDeploiement: '2026-10-10T19:17:00Z',
      dureeReelleMinutes: 77,
      dureePrevueMinutes: 75,
      echecs: 1,
      echecsRepris: 1,
      resolutions: 1,
      recetteTests: 4,
      recetteConformes: 4,
      recetteEchecs: 0,
      retourArriere: false,
    },
    pointsBloquants: [
      {
        plan: 'Deploiement',
        code: 'E0003',
        action: 'Déploiement du service',
        echecLe: '2026-10-10T18:32:00Z',
        par: 'Bob',
        motif: '/sante répond 503',
        resolution: 'API : réponse de test absente',
        statutResolution: 'Reussie',
        dureeResolutionMinutes: 5,
        repriseLe: '2026-10-10T18:40:00Z',
        repriseReussieLe: '2026-10-10T18:44:00Z',
        arretMinutes: 8,
      },
    ],
    testsUnitaires: { faits: 0, nonFaits: 0, verifications: 1, tests: [] },
    mep: m,
    journal: [
      {
        id: 1,
        quand: '2026-10-10T20:00:00Z',
        auteur: 'Alice',
        auteurLogin: 'alice',
        type: 'mep.cloturee',
        detail: 'MEP clôturée',
      },
    ],
  } as RapportFin;
}

function rendre() {
  render(
    <MemoryRouter initialEntries={['/meps/m1/rapport']}>
      <Routes>
        <Route path="/meps/:id/rapport" element={<RapportPage />} />
      </Routes>
    </MemoryRouter>,
  );
}

describe('RapportPage (#61)', () => {
  beforeEach(() => {
    vi.mocked(api.catalogueSystemes).mockResolvedValue([]);
    vi.mocked(api.resolutions).mockResolvedValue([]);
  });

  it('montre le rapport figé : synthèse, points bloquants, exécutants, bilan et annexes', async () => {
    vi.mocked(api.rapport).mockResolvedValue(rapport());
    rendre();
    expect(await screen.findByRole('heading', { name: /Gestion des stocks/ })).toBeInTheDocument();
    expect(screen.getByText('Clôturée : réussie avec incidents')).toBeInTheDocument();
    expect(screen.getByText('1 h 17')).toBeInTheDocument();
    expect(screen.getByText('prévue 1 h 15')).toBeInTheDocument();
    expect(screen.getByText('Plan coloré de 1 étapes')).toBeInTheDocument();
    expect(
      screen.getByText(/Résolution type « API : réponse de test absente » \(réussie\) en 5 min/),
    ).toHaveTextContent('Arrêt : 8 min.');
    expect(screen.getByRole('heading', { name: '5. Qui a fait chaque étape' })).toBeInTheDocument();
    expect(screen.getByText('4 min / 15 min')).toBeInTheDocument();
    expect(screen.getByText('E0003 en échec, résolu.')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Annexe B. Journal complet' })).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: /Retour arrière/ })).not.toBeInTheDocument();
    expect(screen.getByText(/établi à sa première consultation/)).toBeInTheDocument();
    // Système touché (#59) : une puce qui ouvre son menu, avec l'alerte de l'échec survenu dessus.
    const puce = screen.getByRole('button', { name: 'API : historique et MEP de ce système' });
    expect(within(puce).getByLabelText('1 échec d’étape sur ce système')).toBeInTheDocument();
  });

  it('exporte en Excel et passe par l’impression du navigateur pour le PDF', async () => {
    vi.mocked(api.rapport).mockResolvedValue(rapport());
    const imprimer = vi.spyOn(window, 'print').mockImplementation(() => undefined);
    rendre();
    await userEvent.click(await screen.findByRole('button', { name: 'Exporter en Excel' }));
    expect(telecharger).toHaveBeenCalledWith('/api/meps/m1/rapport/excel');
    await userEvent.click(screen.getByRole('button', { name: 'Télécharger en PDF' }));
    await userEvent.click(screen.getByRole('button', { name: 'Imprimer' }));
    expect(imprimer).toHaveBeenCalledTimes(2);
  });

  it('avant la clôture, dit que le rapport n’existe pas encore', async () => {
    vi.mocked(api.rapport).mockRejectedValue(new ApiError(422, 'Le rapport de fin existe une fois la MEP clôturée.'));
    rendre();
    expect(await screen.findByRole('alert')).toHaveTextContent('Le rapport de fin existe une fois la MEP clôturée.');
    expect(screen.queryByRole('button', { name: 'Exporter en Excel' })).not.toBeInTheDocument();
  });
});
