import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { api } from '../api';
import { mep } from '../test/fabrique';
import type { EnvoiMepNoosphere } from '../types';
import { MarqueNoosphere } from './MarqueNoosphere';

vi.mock('../api', () => ({ api: { envoisNoosphereMep: vi.fn() } }));

const envoi = (p: Partial<EnvoiMepNoosphere> = {}): EnvoiMepNoosphere => ({
  liaisonId: 'l1',
  nomInstance: 'Noosphere Alizé',
  envoiActif: true,
  demo: false,
  envoyeeLe: '2026-10-31T11:40:00Z',
  phase: 'preparation',
  datePrevue: true,
  applications: [
    {
      systemeId: 's1',
      systeme: 'Billing',
      applicationId: 'billing',
      url: 'https://docs.alize.example/applications/billing',
      domaine: 'Finance',
      equipe: 'Équipe Finance',
    },
  ],
  ...p,
});

describe('Marque « Envoyée à Noosphere »', () => {
  it('MEP envoyée : la marque ouvre le détail et le lien vers la fiche', async () => {
    vi.mocked(api.envoisNoosphereMep).mockResolvedValue([envoi()]);
    render(<MarqueNoosphere mep={mep()} />);
    await userEvent.click(await screen.findByRole('button', { name: 'Envoyée à Noosphere' }));
    expect(screen.getByText(/Billing est suivi par Noosphere/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Voir Billing dans Noosphere/ })).toHaveAttribute(
      'href',
      'https://docs.alize.example/applications/billing',
    );
  });

  it('sans date prévue : annoncée plus tard ; démonstration : lien inactif', async () => {
    vi.mocked(api.envoisNoosphereMep).mockResolvedValue([
      envoi({
        envoyeeLe: null,
        datePrevue: false,
        demo: true,
        applications: [{ ...envoi().applications[0], url: 'https://noosphere.demo.invalid/applications/billing' }],
      }),
    ]);
    render(<MarqueNoosphere mep={mep()} />);
    await userEvent.click(await screen.findByRole('button', { name: /dès qu’une date est prévue/ }));
    expect(screen.queryByRole('link')).not.toBeInTheDocument();
    expect(screen.getByText(/Voir Billing dans Noosphere/)).toHaveAttribute('aria-disabled', 'true');
  });

  it('MEP qui ne touche aucun système suivi : aucune marque', async () => {
    vi.mocked(api.envoisNoosphereMep).mockResolvedValue([]);
    const { container } = render(<MarqueNoosphere mep={mep()} />);
    await Promise.resolve();
    expect(container).toBeEmptyDOMElement();
  });
});
