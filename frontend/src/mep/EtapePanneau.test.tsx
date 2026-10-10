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
    autoriserNonRetour: vi.fn(), deposerPiece: vi.fn(), supprimerPiece: vi.fn(),
  };
}

function rendre(e: Etape, opts: { m?: Partial<Mep>; participantId?: string | null; estPilote?: boolean; types?: ResolutionType[] } = {}) {
  const actions = actionsFactices();
  const m = mep({
    etapes: [e], ...opts.m,
    droits: { estPilote: opts.estPilote ?? false, estApprobateur: false, estResponsableTechnique: false, peutModifierPlan: false, peutEditerContenu: false, participantId: opts.participantId === undefined ? 'p1' : opts.participantId },
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

  it('prise de main : le motif est obligatoire et transmis', async () => {
    const { actions } = rendre(etape(), { participantId: 'p2' });
    await userEvent.click(screen.getByRole('button', { name: 'Prendre la main' }));
    expect(screen.getByText(/tracée au journal/)).toBeInTheDocument();
    const confirmer = () => { const b = screen.getAllByRole('button', { name: 'Prendre la main' }); return b[b.length - 1]; };
    expect(confirmer()).toBeDisabled();
    await userEvent.type(screen.getByLabelText(/Pourquoi prenez-vous la main/), 'Claire est injoignable');
    await userEvent.click(confirmer());
    expect(actions.prendreLaMain).toHaveBeenCalledWith(expect.objectContaining({ id: 'e1' }), 'Claire est injoignable');
  });

  it('badge « À vous » : visible sur mon étape à faire, masqué une fois terminée', () => {
    const { unmount } = render(<EtapePanneau mep={mep({ etapes: [etape()], droits: { ...mep().droits, participantId: 'p1' } })} etape={etape()} resolutionsTypes={[]} actions={actionsFactices()} onSelectionner={vi.fn()} />);
    expect(screen.getByText('À vous')).toBeInTheDocument();
    unmount();
    rendre(etape({ etat: 'Reussie' }, 'Terminee'));
    expect(screen.queryByText('À vous')).toBeNull();
  });

  it('badge « À vous » masqué dans une MEP clôturée', () => {
    rendre(etape({ etat: 'AFaire' }), { m: { phase: 'Cloturee' } });
    expect(screen.queryByText('À vous')).toBeNull();
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

describe('EtapePanneau : « À appeler en cas de problème »', () => {
  const rt = (participantId: string, systemeIds: string[], extra: Partial<Mep['responsablesTechniques'][number]> = {}) =>
    ({ participantId, systemeIds, perimetre: '', disponibilite: 'Joignable' as const, contactJourJ: '', ...extra });

  it('liste les responsables techniques du système de l’étape, avec contact et disponibilité', () => {
    const base = mep().systemes[0];
    rendre(etape({ systemeIds: ['s1'] }), {
      m: {
        systemes: [{ ...base, id: 's1', nom: 'API', cible: '' }, { ...base, id: 's2', nom: 'WEB', cible: '' }],
        responsablesTechniques: [rt('p2', ['s1'], { contactJourJ: '06 11 22 33 44' }), rt('p3', ['s2'], { disponibilite: 'Absent' })],
      },
    });
    const carte = screen.getByRole('region', { name: 'À appeler en cas de problème' });
    expect(carte).toHaveTextContent('Bob');
    expect(carte).toHaveTextContent('06 11 22 33 44');
    expect(carte).toHaveTextContent('Joignable');
    expect(carte).not.toHaveTextContent('Alice');
  });

  it('sans responsable technique pour le système, affiche tous les responsables (dont les absents)', () => {
    rendre(etape({ systemeIds: ['s1'] }), { m: { responsablesTechniques: [rt('p2', []), rt('p3', [], { disponibilite: 'Absent' })] } });
    const carte = screen.getByRole('region', { name: 'À appeler en cas de problème' });
    expect(carte).toHaveTextContent('Bob');
    expect(carte).toHaveTextContent('Alice');
    expect(carte).toHaveTextContent('Absent');
  });

  it('absent quand aucun responsable technique n’est renseigné', () => {
    rendre(etape());
    expect(screen.queryByRole('region', { name: 'À appeler en cas de problème' })).toBeNull();
  });

  it('étape sur plusieurs systèmes : responsables de chacun de ses systèmes', () => {
    const base = mep().systemes[0];
    rendre(etape({ systemeIds: ['s1', 's2'] }), {
      m: {
        systemes: [{ ...base, id: 's1', nom: 'API' }, { ...base, id: 's2', nom: 'WEB' }, { ...base, id: 's3', nom: 'BDD' }],
        responsablesTechniques: [rt('p2', ['s1']), rt('p1', ['s2']), rt('p3', ['s3'])],
      },
    });
    const carte = screen.getByRole('region', { name: 'À appeler en cas de problème' });
    expect(carte).toHaveTextContent('Bob');
    expect(carte).toHaveTextContent('Claire');
    expect(carte).not.toHaveTextContent('Alice');
  });
});

describe('EtapePanneau : instructions en blocs (markdown + Copier)', () => {
  const instructions = 'Prévenir le **support** avant de lancer.\n\n```\ncd /opt/commandes && ./deploy.sh 2.4.0\n```\n\n- [ ] vérifier le groupe `app-commandes`';

  it('rend le texte en markdown et les blocs de code avec un bouton Copier', () => {
    rendre(etape({ instructions, resultatAttendu: '`GET /sante` renvoie **200**' }));
    expect(screen.getByText('support', { selector: 'strong' })).toBeInTheDocument();
    expect(screen.getByText('cd /opt/commandes && ./deploy.sh 2.4.0', { selector: 'code' })).toBeInTheDocument();
    expect(screen.getByRole('checkbox')).toBeDisabled();
    expect(screen.getByText('GET /sante', { selector: 'code' })).toBeInTheDocument();
    expect(screen.getByText('200', { selector: 'strong' })).toBeInTheDocument();
  });

  it('Copier met la commande exacte dans le presse-papiers', async () => {
    const user = userEvent.setup();
    const ecrire = vi.spyOn(navigator.clipboard, 'writeText').mockResolvedValue();
    rendre(etape({ instructions }));
    await user.click(screen.getByRole('button', { name: /Copier la commande 1/ }));
    expect(ecrire).toHaveBeenCalledWith('cd /opt/commandes && ./deploy.sh 2.4.0');
    expect(await screen.findByRole('button', { name: 'Copié' })).toBeInTheDocument();
  });

  it('presse-papiers refusé : le texte est sélectionné et un message l’indique', async () => {
    const user = userEvent.setup();
    vi.spyOn(navigator.clipboard, 'writeText').mockRejectedValue(new Error('refus'));
    rendre(etape({ instructions }));
    await user.click(screen.getByRole('button', { name: /Copier la commande 1/ }));
    expect(await screen.findByText(/Copie refusée/)).toBeInTheDocument();
    expect(window.getSelection()?.toString()).toBe('cd /opt/commandes && ./deploy.sh 2.4.0');
  });

  it('n’interprète pas le HTML dangereux', () => {
    rendre(etape({ instructions: 'Texte <img src=x onerror="alert(1)"> <script>alert(2)</script>' }));
    expect(document.querySelector('script')).toBeNull();
    expect(document.querySelector('img')?.getAttribute('onerror') ?? null).toBeNull();
  });

  it('liste les préalables avec leur état', async () => {
    const prealable = etape({ id: 'e0', code: 'E0000', action: 'Sauvegarde BDD', etat: 'Reussie' }, 'Terminee');
    const e = etape({ dependances: ['e0'] });
    const onSelectionner = vi.fn();
    render(<EtapePanneau mep={mep({ etapes: [prealable, e] })} etape={e} resolutionsTypes={[]} actions={actionsFactices()} onSelectionner={onSelectionner} />);
    const bouton = screen.getByRole('button', { name: /E0000.*Sauvegarde BDD.*Réussie/ });
    await userEvent.click(bouton);
    expect(onSelectionner).toHaveBeenCalledWith('e0');
  });
});
