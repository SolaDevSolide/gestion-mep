import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { api } from '../api';
import { MepContext } from '../mepContext';
import { etape, mep } from '../test/fabrique';
import type { Mep } from '../types';
import { ToastProvider } from '../ui/Toast';
import { TooltipProvider } from '../ui/Tooltip';
import { BadgesEtape } from './BadgesEtape';
import {
  AnnulerDialog,
  BandeauxCycleDeVie,
  ClotureEchecDialog,
  MenuPlusActions,
  nomComplet,
  ReporterDialog,
  retourArriereImpossible,
  SuppressionDialog,
} from './CycleDeVie';

vi.mock('../api', () => ({
  api: {
    annuler: vi.fn(),
    reporter: vi.fn(),
    supprimerMep: vi.fn(),
    cloturerEnEchec: vi.fn(),
    changerPilote: vi.fn(),
    utilisateurs: vi.fn(),
  },
}));

const droits = (d: Partial<Mep['droits']>): Mep['droits'] => ({ ...mep().droits, ...d });

/** `run` exécute l'appel comme le vrai contexte et renvoie true. */
function rendre(ui: React.ReactNode, m: Mep) {
  const run = vi.fn(async (f: () => Promise<unknown>) => {
    await f();
    return true;
  });
  render(
    <MemoryRouter initialEntries={['/meps/m1']}>
      <MepContext.Provider value={{ mep: m, run } as never}>
        <TooltipProvider>
          <ToastProvider>
            <Routes>
              <Route path="/meps/m1" element={ui} />
              <Route path="/" element={<p>Liste des MEP</p>} />
            </Routes>
          </ToastProvider>
        </TooltipProvider>
      </MepContext.Provider>
    </MemoryRouter>,
  );
  return { run };
}

async function ouvrirMenu() {
  await userEvent.click(screen.getByRole('button', { name: 'Plus d’actions' }));
  return screen.findByRole('menu');
}

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(api.utilisateurs).mockResolvedValue([]);
});

describe('aides du cycle de vie', () => {
  it('le nom à saisir pour supprimer est l’outil suivi de la version cible', () => {
    expect(nomComplet({ outil: ' Portail RH ', versionCible: '3.2 ' })).toBe('Portail RH 3.2');
    expect(nomComplet({ outil: 'Batch', versionCible: '' })).toBe('Batch');
  });

  it('dit pourquoi le retour arrière est impossible', () => {
    expect(retourArriereImpossible({ retourArriereActive: false, nonRetourFranchi: null })).toMatch(/pas de plan/);
    expect(retourArriereImpossible({ retourArriereActive: true, nonRetourFranchi: 'E0004' })).toMatch(/E0004/);
    expect(retourArriereImpossible({ retourArriereActive: true, nonRetourFranchi: null })).toBeNull();
  });
});

describe('menu « Plus d’actions »', () => {
  it('n’apparaît pas pour qui n’a aucune de ces actions', () => {
    rendre(<MenuPlusActions />, mep());
    expect(screen.queryByRole('button', { name: 'Plus d’actions' })).toBeNull();
  });

  it('pilote en préparation : reporter et annuler, sans la partie administration', async () => {
    rendre(
      <MenuPlusActions />,
      mep({ phase: 'Preparation', droits: droits({ estPilote: true, peutReporter: true, peutAnnuler: true }) }),
    );
    const menu = await ouvrirMenu();
    expect(within(menu).getByRole('menuitem', { name: /Reporter la MEP/ })).toBeInTheDocument();
    expect(within(menu).getByRole('menuitem', { name: /Annuler la MEP/ })).not.toHaveAttribute('data-disabled');
    expect(within(menu).queryByText('Administration')).toBeNull();
    expect(within(menu).queryByRole('menuitem', { name: /Supprimer/ })).toBeNull();
  });

  it('une MEP reportée propose « Modifier le report »', async () => {
    rendre(
      <MenuPlusActions />,
      mep({
        report: { motif: 'Gel', dateVisee: null, le: '2026-10-18T08:00:00Z', par: 'Alice' },
        droits: droits({ estPilote: true, peutReporter: true, peutAnnuler: true }),
      }),
    );
    const menu = await ouvrirMenu();
    expect(within(menu).getByRole('menuitem', { name: /Modifier le report/ })).toBeInTheDocument();
  });

  it('pilote après le Go : annuler grisé (revenir d’abord en préparation) et passer le pilotage', async () => {
    rendre(
      <MenuPlusActions />,
      mep({ phase: 'Execution', droits: droits({ estPilote: true, peutChangerPilote: true }) }),
    );
    const menu = await ouvrirMenu();
    const annuler = within(menu).getByRole('menuitem', { name: /Annuler la MEP/ });
    expect(annuler).toHaveAttribute('data-disabled');
    expect(annuler).toHaveTextContent('Revenir d’abord en préparation');
    expect(within(menu).getByRole('menuitem', { name: /Passer le pilotage/ })).toBeInTheDocument();
    expect(within(menu).queryByRole('menuitem', { name: /Pilotage de secours/ })).toBeNull();
  });

  it('administrateur qui ne pilote pas après le Go : pilotage de secours, suppression grisée', async () => {
    rendre(
      <MenuPlusActions />,
      mep({ phase: 'Execution', droits: droits({ estAdmin: true, peutChangerPilote: true }) }),
    );
    const menu = await ouvrirMenu();
    expect(within(menu).getByRole('menuitem', { name: /Pilotage de secours/ })).toBeInTheDocument();
    expect(within(menu).queryByRole('menuitem', { name: /Passer le pilotage/ })).toBeNull();
    expect(within(menu).getByText('Administration')).toBeInTheDocument();
    const supprimer = within(menu).getByRole('menuitem', { name: /Supprimer définitivement/ });
    expect(supprimer).toHaveAttribute('data-disabled');
    expect(supprimer).toHaveTextContent('Impossible après un Go : annuler plutôt');
  });

  it('administrateur sur une MEP jamais lancée : la suppression ouvre sa confirmation', async () => {
    rendre(<MenuPlusActions />, mep({ droits: droits({ estAdmin: true, peutAnnuler: true, peutSupprimer: true }) }));
    const menu = await ouvrirMenu();
    await userEvent.click(within(menu).getByRole('menuitem', { name: /Supprimer définitivement/ }));
    expect(await screen.findByRole('dialog', { name: 'Supprimer définitivement la MEP ?' })).toBeInTheDocument();
  });
});

describe('annuler la MEP', () => {
  it('avant le Go : seul le motif est demandé', async () => {
    vi.mocked(api.annuler).mockResolvedValue(mep());
    rendre(<AnnulerDialog open onOpenChange={vi.fn()} />, mep());
    expect(screen.queryByLabelText(/État laissé en production/)).toBeNull();
    const bouton = screen.getByRole('button', { name: 'Annuler la MEP' });
    expect(bouton).toBeDisabled();
    await userEvent.type(screen.getByLabelText(/Motif/), ' Projet suspendu ');
    await userEvent.click(bouton);
    expect(api.annuler).toHaveBeenCalledWith('m1', { motif: 'Projet suspendu' });
  });

  it('après un Go : liste les étapes exécutées et exige l’état laissé en production', async () => {
    vi.mocked(api.annuler).mockResolvedValue(mep());
    const m = mep({
      premierGo: '2026-10-19T11:10:00Z',
      etapes: [
        etape({ id: 'e1', code: 'E0001', action: 'Arrêter le service', etat: 'Reussie' }),
        etape({ id: 'e2', code: 'E0002', action: 'Migrer la base', etat: 'AFaire' }),
      ],
    });
    rendre(<AnnulerDialog open onOpenChange={vi.fn()} />, m);
    expect(screen.getByText(/1 étape a été exécutée en production/)).toBeInTheDocument();
    expect(screen.getByText('Arrêter le service')).toBeInTheDocument();
    expect(screen.queryByText('Migrer la base')).toBeNull();
    const bouton = screen.getByRole('button', { name: 'Annuler la MEP' });
    await userEvent.type(screen.getByLabelText(/^Motif/), 'Projet suspendu');
    expect(bouton).toBeDisabled();
    await userEvent.type(screen.getByLabelText(/État laissé en production/), 'Index en place');
    await userEvent.click(bouton);
    expect(api.annuler).toHaveBeenCalledWith('m1', { motif: 'Projet suspendu', etatProduction: 'Index en place' });
  });
});

describe('reporter la MEP', () => {
  it('motif obligatoire, date visée facultative', async () => {
    vi.mocked(api.reporter).mockResolvedValue(mep());
    rendre(<ReporterDialog open onOpenChange={vi.fn()} />, mep());
    const bouton = screen.getByRole('button', { name: 'Reporter la MEP' });
    expect(bouton).toBeDisabled();
    await userEvent.type(screen.getByLabelText(/Motif/), 'Gel des changements');
    await userEvent.click(bouton);
    expect(api.reporter).toHaveBeenCalledWith('m1', { motif: 'Gel des changements', dateVisee: null });
  });
});

describe('supprimer définitivement', () => {
  it('exige le motif et le nom exact, puis revient à la liste', async () => {
    vi.mocked(api.supprimerMep).mockResolvedValue(undefined);
    const m = mep({
      outil: 'Portail RH',
      versionCible: '3.2',
      droits: droits({ estAdmin: true, peutSupprimer: true }),
    });
    rendre(<SuppressionDialog open onOpenChange={vi.fn()} />, m);
    const bouton = screen.getByRole('button', { name: 'Supprimer définitivement' });
    await userEvent.type(screen.getByLabelText(/Motif/), 'Créée par erreur');
    await userEvent.type(screen.getByLabelText(/saisissez le nom/), 'Portail RH');
    expect(bouton).toBeDisabled();
    await userEvent.type(screen.getByLabelText(/saisissez le nom/), ' 3.2');
    expect(bouton).toBeEnabled();
    await userEvent.click(bouton);
    expect(api.supprimerMep).toHaveBeenCalledWith('m1', { motif: 'Créée par erreur', nom: 'Portail RH 3.2' });
    expect(await screen.findByText('Liste des MEP')).toBeInTheDocument();
  });
});

describe('clôturer en échec', () => {
  it('exige l’état du service et le bilan ; la référence d’incident est facultative', async () => {
    vi.mocked(api.cloturerEnEchec).mockResolvedValue(mep());
    rendre(<ClotureEchecDialog open onOpenChange={vi.fn()} />, mep({ phase: 'RetourArriere' }));
    const bouton = screen.getByRole('button', { name: 'Clôturer en échec' });
    await userEvent.type(screen.getByLabelText(/État du service/), 'Lecture seule');
    await userEvent.type(screen.getByLabelText(/Ce qui a marché/), 'Sauvegarde');
    expect(bouton).toBeDisabled();
    await userEvent.type(screen.getByLabelText(/Incidents/), 'Restauration impossible');
    await userEvent.click(bouton);
    expect(api.cloturerEnEchec).toHaveBeenCalledWith('m1', {
      echec: true,
      etatService: 'Lecture seule',
      referenceIncident: '',
      ceQuiAMarche: 'Sauvegarde',
      incidents: 'Restauration impossible',
      actions: '',
    });
  });
});

describe('bandeaux visibles de tous', () => {
  it('annulée : qui, quand, motif et état laissé en production', () => {
    rendre(
      <BandeauxCycleDeVie />,
      mep({
        phase: 'Annulee',
        annulation: {
          motif: 'Projet suspendu',
          etatProduction: 'Index en place',
          le: '2026-10-19T14:02:00Z',
          par: 'Alice',
        },
      }),
    );
    expect(screen.getByText(/^Annulée par Alice le 19\/10\/2026/)).toBeInTheDocument();
    expect(screen.getByText('Motif : Projet suspendu')).toBeInTheDocument();
    expect(screen.getByText('État laissé en production : Index en place')).toBeInTheDocument();
  });

  it('reportée : la date visée, ou « sans date visée »', () => {
    rendre(
      <BandeauxCycleDeVie />,
      mep({ phase: 'Preparation', report: { motif: 'Gel', dateVisee: null, le: '2026-10-18T08:00:00Z', par: 'Bob' } }),
    );
    expect(screen.getByText(/Reportée par Bob .* sans date visée/)).toBeInTheDocument();
    expect(screen.getByText('Motif : Gel')).toBeInTheDocument();
  });

  it('pilotage de secours : qui a pris le pilotage, à la place de qui, et pourquoi', () => {
    rendre(
      <BandeauxCycleDeVie />,
      mep({
        phase: 'Execution',
        pilotageSecours: {
          motif: 'Pilote injoignable',
          le: '2026-10-19T00:12:00Z',
          par: 'Claire',
          nouveauPilote: 'Claire',
          ancienPilote: 'Bob',
        },
      }),
    );
    expect(screen.getByText(/^Pilotage de secours depuis/)).toBeInTheDocument();
    expect(screen.getByText(/Claire \(administrateur\) a pris le pilotage à la place de Bob/)).toBeInTheDocument();
    expect(screen.getByText('Motif : Pilote injoignable')).toBeInTheDocument();
  });
});

describe('étiquettes d’étape', () => {
  it('signale une étape corrective « Ajoutée après le Go »', () => {
    render(<BadgesEtape etape={{ type: 'Standard', pointDeNonRetour: false, ajouteeApresGo: true }} />);
    expect(screen.getByText('Ajoutée après le Go')).toBeInTheDocument();
  });
});
