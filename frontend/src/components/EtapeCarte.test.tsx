import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { EtapeCarte, type ActionsEtape } from './EtapeCarte';
import type { Etape, StatutDisponibilite } from '../types';

function etape(p: Partial<Etape> = {}, statut: StatutDisponibilite = 'Disponible'): Etape {
  return {
    id: 'e1', code: 'E0001', ordre: 1, action: 'Redémarrer le service', systemeId: 's1', responsableId: 'p1',
    criticite: 'Moderee', etat: 'AFaire', instructions: 'systemctl restart api', resultatAttendu: 'Service actif',
    preuve: '', debutReel: null, finReelle: null, revision: 1, dependances: [],
    disponibilite: { statut, libelle: statut }, ...p,
  };
}

function rendre(e: Etape, opts: { participantId?: string | null; estPilote?: boolean } = {}) {
  const actions: ActionsEtape = { demarrer: vi.fn(), terminer: vi.fn(), autoriserReprise: vi.fn() };
  render(
    <EtapeCarte etape={e} etapes={[e]} participantId={opts.participantId === undefined ? 'p1' : opts.participantId}
      estPilote={opts.estPilote ?? false} actions={actions} ouverteParDefaut />,
  );
  return actions;
}

describe('EtapeCarte', () => {
  it('propose Démarrer si disponible et responsable', async () => {
    const actions = rendre(etape());
    await userEvent.click(screen.getByRole('button', { name: 'Démarrer' }));
    expect(actions.demarrer).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole('button', { name: 'Réussie' })).toBeNull();
  });

  it('masque Démarrer si je ne suis pas responsable', () => {
    rendre(etape(), { participantId: 'p2' });
    expect(screen.queryByRole('button', { name: 'Démarrer' })).toBeNull();
  });

  it('masque Démarrer si l’étape attend un préalable', () => {
    rendre(etape({}, 'Attend'));
    expect(screen.queryByRole('button', { name: 'Démarrer' })).toBeNull();
  });

  it('propose Réussie et Échec pour une étape en cours, avec confirmation', async () => {
    const actions = rendre(etape({ etat: 'EnCours' }, 'EnCours'));
    expect(screen.queryByRole('button', { name: 'Démarrer' })).toBeNull();
    expect(screen.getByRole('button', { name: 'Échec' })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Réussie' }));
    await userEvent.click(screen.getByRole('button', { name: /J’ai vérifié et obtenu le résultat attendu/ }));
    expect(actions.terminer).toHaveBeenCalledWith(expect.objectContaining({ id: 'e1' }), 'Reussie', '');
  });

  it('en échec : message d’arrêt, reprise réservée au pilote', () => {
    rendre(etape({ etat: 'Echec' }, 'ArretResolution'), { estPilote: true });
    expect(screen.getByText('Arrêter, prévenir le pilote, ne rien improviser.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Autoriser la reprise' })).toBeInTheDocument();
  });

  it('pas de reprise pour un non-pilote', () => {
    rendre(etape({ etat: 'Echec' }, 'ArretResolution'));
    expect(screen.queryByRole('button', { name: 'Autoriser la reprise' })).toBeNull();
  });
});
