import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { EtapePanneau, type ActionsEtape } from './EtapePanneau';
import { etape, mep, resolution } from '../test/fabrique';
import type { Etape, Mep, ResolutionType } from '../types';

const typeRes: ResolutionType = {
  id: 't1', titre: 'API ne répond plus', symptomes: 'Erreur 502 sur /sante', diagnostic: '', systemeCatalogueId: null, corrections: [],
  verificationInstruction: '', verificationAttendu: '', actif: true, version: 1, modifieeLe: '2026-10-09T10:00:00Z',
};

function actionsFactices(): ActionsEtape {
  return {
    demarrer: vi.fn(), terminer: vi.fn(), autoriserReprise: vi.fn(), prendreLaMain: vi.fn(), appliquerResolution: vi.fn(),
    controlerCorrection: vi.fn(), controlerVerification: vi.fn(),
  };
}

function rendre(e: Etape, opts: { m?: Partial<Mep>; participantId?: string | null; estPilote?: boolean; types?: ResolutionType[] } = {}) {
  const actions = actionsFactices();
  const m = mep({
    etapes: [e], ...opts.m,
    droits: { estPilote: opts.estPilote ?? false, peutModifierPlan: false, participantId: opts.participantId === undefined ? 'p1' : opts.participantId },
  });
  const onSelectionner = vi.fn();
  render(<EtapePanneau mep={m} etape={e} resolutionsTypes={opts.types ?? []} actions={actions} onSelectionner={onSelectionner} />);
  return { actions, onSelectionner };
}

describe('EtapePanneau : boutons selon la disponibilité', () => {
  it('propose Démarrer si disponible et exécutant', async () => {
    const { actions } = rendre(etape());
    await userEvent.click(screen.getByRole('button', { name: /Démarrer/ }));
    expect(actions.demarrer).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole('button', { name: 'Réussie' })).toBeNull();
  });

  it('masque Démarrer si je ne suis pas l’exécutant, et propose Prendre la main', () => {
    rendre(etape(), { participantId: 'p2' });
    expect(screen.queryByRole('button', { name: /Démarrer/ })).toBeNull();
    expect(screen.getByRole('button', { name: 'Prendre la main' })).toBeInTheDocument();
  });

  it('masque Démarrer et indique le préalable bloquant avec un lien', async () => {
    const prealable = etape({ id: 'e0', code: 'E0000', action: 'Sauvegarde', etat: 'AFaire' }, 'Disponible');
    const e = etape({ dependances: ['e0'] }, 'Attend');
    const actions = actionsFactices();
    const onSelectionner = vi.fn();
    render(<EtapePanneau mep={mep({ etapes: [prealable, e] })} etape={e} resolutionsTypes={[]} actions={actions} onSelectionner={onSelectionner} />);
    expect(screen.queryByRole('button', { name: /Démarrer/ })).toBeNull();
    await userEvent.click(screen.getByRole('button', { name: 'E0000' }));
    expect(onSelectionner).toHaveBeenCalledWith('e0');
  });

  it('étape en cours : Réussie demande une confirmation, Échec exige une preuve', async () => {
    const { actions } = rendre(etape({ etat: 'EnCours' }, 'EnCours'));
    const echec = screen.getByRole('button', { name: 'Échec' });
    expect(echec).toBeDisabled();
    await userEvent.click(screen.getByRole('button', { name: 'Réussie' }));
    await userEvent.click(screen.getByRole('button', { name: /J’ai vérifié et obtenu le résultat attendu/ }));
    expect(actions.terminer).toHaveBeenCalledWith(expect.objectContaining({ id: 'e1' }), 'Reussie', '');
  });

  it('Échec avec preuve : confirmation puis appel', async () => {
    const { actions } = rendre(etape({ etat: 'EnCours' }, 'EnCours'));
    await userEvent.type(screen.getByLabelText('Preuve'), 'Erreur 502');
    await userEvent.click(screen.getByRole('button', { name: 'Échec' }));
    await userEvent.click(screen.getByRole('button', { name: 'Déclarer l’échec' }));
    expect(actions.terminer).toHaveBeenCalledWith(expect.objectContaining({ id: 'e1' }), 'Echec', 'Erreur 502');
  });

  it('étape réussie : aucune action, pas de prise de main', () => {
    rendre(etape({ etat: 'Reussie' }, 'Terminee'), { participantId: 'p2' });
    expect(screen.queryByRole('button', { name: 'Prendre la main' })).toBeNull();
    expect(screen.queryByRole('button', { name: /Démarrer/ })).toBeNull();
  });
});

describe('EtapePanneau : exécutant et prise de main', () => {
  it('affiche l’exécutant effectif à la place du responsable prévu', () => {
    rendre(etape({ executantId: 'p2' }, 'Disponible'), { participantId: 'p2' });
    expect(screen.getByText('Exécutée par Bob à la place de Claire')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Démarrer/ })).toBeInTheDocument();
  });

  it('le responsable prévu ne peut plus démarrer après une prise de main', () => {
    rendre(etape({ executantId: 'p2' }), { participantId: 'p1' });
    expect(screen.queryByRole('button', { name: /Démarrer/ })).toBeNull();
    expect(screen.getByRole('button', { name: 'Prendre la main' })).toBeInTheDocument();
  });

  it('prise de main : dialogue d’explication puis appel', async () => {
    const { actions } = rendre(etape(), { participantId: 'p2' });
    await userEvent.click(screen.getByRole('button', { name: 'Prendre la main' }));
    expect(screen.getByText(/tracée au journal/)).toBeInTheDocument();
    const boutons = screen.getAllByRole('button', { name: 'Prendre la main' });
    await userEvent.click(boutons[boutons.length - 1]);
    expect(actions.prendreLaMain).toHaveBeenCalledWith(expect.objectContaining({ id: 'e1' }));
  });

  it('pas de prise de main hors de la phase d’exécution du plan', () => {
    rendre(etape(), { participantId: 'p2', m: { phase: 'Preparation' } });
    expect(screen.queryByRole('button', { name: 'Prendre la main' })).toBeNull();
  });
});

describe('EtapePanneau : échec et résolutions', () => {
  const echec = () => etape({ etat: 'Echec', preuve: 'Erreur 502', resolutionIds: ['t1'] }, 'ArretResolution');

  it('affiche le message d’arrêt et les résolutions liées, applicables par l’exécutant', async () => {
    const { actions } = rendre(echec(), { types: [typeRes] });
    expect(screen.getByText('Arrêter, prévenir le pilote, ne rien improviser.')).toBeInTheDocument();
    expect(screen.getByText('Problème connu ?')).toBeInTheDocument();
    expect(screen.getByText('API ne répond plus')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Appliquer cette résolution' }));
    expect(actions.appliquerResolution).toHaveBeenCalledWith(expect.objectContaining({ id: 'e1' }), 't1');
  });

  it('sans résolution liée : arrêter et prévenir le pilote, reprise réservée au pilote', () => {
    rendre(etape({ etat: 'Echec' }, 'ArretResolution'), { estPilote: true });
    expect(screen.getByText('Aucune résolution prévue : arrêter et prévenir le pilote.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Autoriser la reprise' })).toBeInTheDocument();
  });

  it('pas de reprise pour un non-pilote', () => {
    rendre(etape({ etat: 'Echec' }, 'ArretResolution'));
    expect(screen.queryByRole('button', { name: 'Autoriser la reprise' })).toBeNull();
  });

  it('non exécutant : le bouton Appliquer est désactivé', () => {
    rendre(echec(), { types: [typeRes], participantId: 'p2' });
    expect(screen.getByRole('button', { name: 'Appliquer cette résolution' })).toBeDisabled();
  });

  it('une résolution en cours remplace les cartes par le déroulé guidé', () => {
    rendre(echec(), { types: [typeRes], m: { resolutions: [resolution()] } });
    expect(screen.getByText('Résolution en cours')).toBeInTheDocument();
    expect(screen.queryByText('Problème connu ?')).toBeNull();
  });

  it('affiche l’historique des résolutions terminées', () => {
    rendre(etape({ etat: 'AFaire' }, 'Disponible'), { m: { resolutions: [resolution({ statut: 'Reussie', termineeLe: '2026-10-09T21:30:00Z' })] } });
    expect(screen.getByText('Résolutions déjà appliquées')).toBeInTheDocument();
    expect(screen.getByText('Réussie')).toBeInTheDocument();
  });
});
