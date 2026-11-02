import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { api } from '../../api';
import { MepContext } from '../../mepContext';
import { mep } from '../../test/fabrique';
import type { CalendrierMep, DerogationGel, Mep, OccurrenceGel } from '../../types';
import { BandeauGelConflits, BlocageGoGel, derogationDe, occurrencesGel, PastilleGel, raisonGoBloque } from './GelMep';

const occurrence: OccurrenceGel = {
  periodeGelId: 'g1',
  nom: 'Clôture comptable',
  motif: 'Arrêtés de fin de mois',
  debut: '2026-11-25T23:00:00Z',
  fin: '2026-11-30T23:00:00Z',
  fuseauHoraire: 'Europe/Paris',
  globale: true,
  derogation: null,
  decideurs: 'Claire Dubois',
};

const derogation = (p: Partial<DerogationGel> = {}): DerogationGel => ({
  id: 'd1',
  periodeGelId: 'g1',
  nomPeriode: 'Clôture comptable',
  occurrence: occurrence.debut,
  occurrenceFin: occurrence.fin,
  motif: 'Correctif urgent',
  statut: 'Demandee',
  demandeePar: 'Alice Martin',
  demandeeLe: '2026-11-20T09:00:00Z',
  decideePar: null,
  decideeLe: null,
  commentaire: '',
  peutDecider: false,
  ...p,
});

const calendrier = (p: Partial<CalendrierMep> = {}): CalendrierMep => ({
  debut: '2026-11-27T20:00:00Z',
  fin: '2026-11-27T22:30:00Z',
  journeeEntiere: false,
  prevu: true,
  enCours: false,
  gels: [occurrence],
  gelsAuGo: [],
  conflits: [],
  derogations: [],
  ...p,
});

function rendre(m: Partial<Mep>, contenu: React.ReactNode) {
  const run = vi.fn(async (f: () => Promise<unknown>) => {
    await f();
    return true;
  });
  render(
    <MemoryRouter>
      <MepContext.Provider value={{ mep: mep({ phase: 'Preparation', ...m }), run } as never}>
        {contenu}
      </MepContext.Provider>
    </MemoryRouter>,
  );
  return { run };
}

const pilote = (estPilote = true) => ({ droits: { ...mep().droits, estPilote } });

describe('gel d’une MEP : règles', () => {
  it('retrouve la dernière dérogation de l’occurrence et fusionne créneau et Go', () => {
    const cal = calendrier({
      gelsAuGo: [occurrence],
      derogations: [derogation({ id: 'ancienne', statut: 'Refusee' }), derogation({ id: 'neuve' })],
    });
    expect(derogationDe(cal, occurrence)?.id).toBe('neuve');
    expect(derogationDe(cal, { ...occurrence, periodeGelId: 'autre' })).toBeNull();
    expect(occurrencesGel(cal)).toEqual([{ o: occurrence, auGo: true, creneau: true }]);
  });

  it('grise le Go seulement si un Go maintenant tomberait dans un gel', () => {
    expect(raisonGoBloque(mep({ calendrier: calendrier() }))).toBeNull();
    expect(raisonGoBloque(mep({ calendrier: calendrier({ gelsAuGo: [occurrence] }) }))).toBe(
      'Bloqué : période de gel « Clôture comptable »',
    );
  });

  it('montre la pastille de l’en-tête pendant la préparation', () => {
    const { unmount } = render(<PastilleGel mep={mep({ phase: 'Preparation', calendrier: calendrier() })} />);
    expect(screen.getByText('Période de gel')).toBeInTheDocument();
    unmount();
    render(<PastilleGel mep={mep({ phase: 'Execution', calendrier: calendrier() })} />);
    expect(screen.queryByText('Période de gel')).not.toBeInTheDocument();
  });
});

describe('BandeauGelConflits', () => {
  it('signale le gel avec ses actions au pilote, et le conflit sans rien bloquer', async () => {
    const allerIntervention = vi.fn();
    rendre(
      {
        ...pilote(),
        calendrier: calendrier({
          conflits: [
            {
              mepId: 'x',
              outil: 'Portail adhérents',
              versionCible: '5.2.1',
              etat: 'Preparation',
              debut: '2026-11-27T20:30:00Z',
              fin: '2026-11-27T22:00:00Z',
              systemes: ['WEB'],
            },
          ],
        }),
      },
      <BandeauGelConflits allerIntervention={allerIntervention} />,
    );
    expect(screen.getByText('Cette MEP tombe pendant la période de gel « Clôture comptable ».')).toBeInTheDocument();
    expect(screen.getByText(/Le Go sera bloqué sauf dérogation, accordée par Claire Dubois/)).toBeInTheDocument();
    expect(screen.getByText('Libre à partir du mardi 1er décembre')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Choisir une autre date' }));
    expect(allerIntervention).toHaveBeenCalled();
    expect(screen.getByText(/Conflit possible : WEB est aussi touché/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Portail adhérents 5.2.1' })).toHaveAttribute('href', '/meps/x');
    expect(screen.getByText(/Signalé seulement : rien n’est bloqué/)).toBeInTheDocument();
  });

  it('demande une dérogation avec un motif obligatoire', async () => {
    const appel = vi.spyOn(api, 'demanderDerogation').mockResolvedValue(undefined as never);
    rendre({ ...pilote(), calendrier: calendrier() }, <BandeauGelConflits allerIntervention={vi.fn()} />);
    await userEvent.click(screen.getByRole('button', { name: 'Demander une dérogation' }));
    const envoyer = screen.getByRole('button', { name: 'Demander la dérogation' });
    expect(envoyer).toBeDisabled();
    await userEvent.type(screen.getByLabelText('Motif de la dérogation (obligatoire)'), 'Correctif urgent');
    await userEvent.click(envoyer);
    expect(appel).toHaveBeenCalledWith(expect.any(String), 'g1', occurrence.debut, 'Correctif urgent');
  });

  it('montre la demande en attente, et la décision à l’approbateur (refus expliqué)', async () => {
    const appel = vi.spyOn(api, 'deciderDerogation').mockResolvedValue(undefined as never);
    rendre(
      { ...pilote(false), calendrier: calendrier({ derogations: [derogation({ peutDecider: true })] }) },
      <BandeauGelConflits allerIntervention={vi.fn()} />,
    );
    expect(screen.getByText(/Dérogation demandée par Alice Martin/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Demander une dérogation' })).not.toBeInTheDocument();
    const refuser = screen.getByRole('button', { name: 'Refuser' });
    expect(refuser).toBeDisabled();
    await userEvent.type(screen.getByLabelText(/Commentaire/), 'Pas pendant la clôture');
    await userEvent.click(refuser);
    expect(appel).toHaveBeenCalledWith(expect.any(String), 'd1', false, 'Pas pendant la clôture');
  });

  it('dit que le Go est permis une fois la dérogation accordée', () => {
    rendre(
      {
        ...pilote(),
        calendrier: calendrier({
          derogations: [
            derogation({ statut: 'Accordee', decideePar: 'Claire Dubois', decideeLe: '2026-11-21T10:00:00Z' }),
          ],
        }),
      },
      <BandeauGelConflits allerIntervention={vi.fn()} />,
    );
    expect(screen.getByText(/Le Go est permis par la dérogation/)).toBeInTheDocument();
    expect(screen.getByText(/Dérogation accordée par Claire Dubois/)).toBeInTheDocument();
  });

  it('ne montre rien sans gel ni conflit', () => {
    const { container } = render(
      <MemoryRouter>
        <MepContext.Provider value={{ mep: mep({ calendrier: calendrier({ gels: [] }) }), run: vi.fn() } as never}>
          <BandeauGelConflits allerIntervention={vi.fn()} />
        </MepContext.Provider>
      </MemoryRouter>,
    );
    expect(container).toBeEmptyDOMElement();
  });
});

describe('dérogation tombée', () => {
  it('dit que la date a changé et propose de la redemander', () => {
    rendre(
      { ...pilote(), calendrier: calendrier({ derogations: [derogation({ statut: 'Caduque' })] }) },
      <BandeauGelConflits allerIntervention={vi.fn()} />,
    );
    expect(screen.getByText('Dérogation tombée : la date de la MEP a changé')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Demander une dérogation' })).toBeInTheDocument();
  });
});

describe('BlocageGoGel', () => {
  it('explique le blocage et propose la demande dans le dialogue Go', async () => {
    const appel = vi.spyOn(api, 'demanderDerogation').mockResolvedValue(undefined as never);
    rendre({ calendrier: calendrier({ gelsAuGo: [occurrence] }) }, <BlocageGoGel />);
    expect(screen.getByText('Go bloqué : période de gel « Clôture comptable »')).toBeInTheDocument();
    expect(screen.getByText(/Peut accorder la dérogation : Claire Dubois/)).toBeInTheDocument();
    await userEvent.type(screen.getByLabelText('Motif de la dérogation (obligatoire)'), 'Urgence');
    await userEvent.click(screen.getByRole('button', { name: 'Demander la dérogation' }));
    expect(appel).toHaveBeenCalledWith(expect.any(String), 'g1', occurrence.debut, 'Urgence');
  });

  it('montre l’attente au lieu d’une nouvelle demande', () => {
    rendre({ calendrier: calendrier({ gelsAuGo: [occurrence], derogations: [derogation()] }) }, <BlocageGoGel />);
    expect(screen.getByText(/En attente de la décision d’un approbateur/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Demander la dérogation' })).not.toBeInTheDocument();
  });
});
