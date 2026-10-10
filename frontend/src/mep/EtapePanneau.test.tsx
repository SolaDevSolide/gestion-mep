import { render as rendu, screen, within } from '@testing-library/react';
import type { ComponentProps, ReactElement } from 'react';
import { MemoryRouter } from 'react-router-dom';
import userEvent from '@testing-library/user-event';
import { AuthContext } from '../auth';
import { EtapePanneau, type ActionsEtape } from './EtapePanneau';
import { etape, mep, resolution } from '../test/fabrique';
import type { Etape, Mep, ResolutionType } from '../types';

const typeRes: ResolutionType = {
  id: 't1',
  titre: 'API ne répond plus',
  symptomes: 'Erreur 502 sur /sante',
  diagnostic: '',
  systemeCatalogueIds: [],
  corrections: [],
  verificationInstruction: '',
  verificationAttendu: '',
  actif: true,
  version: 1,
  modifieeLe: '2026-10-09T10:00:00Z',
  modifieeParId: null,
  modifieeParNom: null,
};

// Le panneau contient des liens (onglet Retour arrière) : rendu dans un routeur.
const render = (ui: ReactElement) => rendu(ui, { wrapper: MemoryRouter });

function actionsFactices(): ActionsEtape {
  return {
    demarrer: vi.fn(),
    terminer: vi.fn(),
    commenter: vi.fn(),
    autoriserReprise: vi.fn(),
    prendreLaMain: vi.fn(),
    appliquerResolution: vi.fn(),
    controlerCorrection: vi.fn(),
    controlerVerification: vi.fn(),
    autoriserNonRetour: vi.fn(),
    deposerPiece: vi.fn(),
    supprimerPiece: vi.fn(),
  };
}

function rendre(
  e: Etape,
  opts: { m?: Partial<Mep>; participantId?: string | null; estPilote?: boolean; types?: ResolutionType[] } = {},
) {
  const actions = actionsFactices();
  const m = mep({
    etapes: [e],
    ...opts.m,
    droits: {
      estPilote: opts.estPilote ?? false,
      estApprobateur: false,
      estResponsableTechnique: false,
      peutModifierPlan: false,
      peutEditerContenu: false,
      participantId: opts.participantId === undefined ? 'p1' : opts.participantId,
    },
  });
  const onSelectionner = vi.fn();
  render(
    <EtapePanneau
      mep={m}
      etape={e}
      resolutionsTypes={opts.types ?? []}
      actions={actions}
      onSelectionner={onSelectionner}
    />,
  );
  return { actions, onSelectionner };
}

describe('EtapePanneau : démarrer et terminer', () => {
  it('Démarrer est proposé à tout participant relié à un compte, sans responsable désigné', async () => {
    const { actions } = rendre(etape({ executantId: null }), { participantId: 'p2' });
    await userEvent.click(screen.getByRole('button', { name: /Démarrer/ }));
    expect(actions.demarrer).toHaveBeenCalledTimes(1);
    expect(screen.getByText('Pas encore démarrée')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Réussie' })).toBeNull();
  });

  it('pas de Démarrer sans participant relié, sauf pour le pilote', () => {
    rendre(etape(), { participantId: null });
    expect(screen.queryByRole('button', { name: /Démarrer/ })).toBeNull();
  });

  it('le pilote peut démarrer même sans être participant relié', () => {
    rendre(etape(), { participantId: null, estPilote: true });
    expect(screen.getByRole('button', { name: /Démarrer/ })).toBeInTheDocument();
  });

  it('masque Démarrer et indique le préalable bloquant avec un lien', async () => {
    const prealable = etape({ id: 'e0', code: 'E0000', action: 'Sauvegarde', etat: 'AFaire' }, 'Disponible');
    const e = etape({ dependances: ['e0'] }, 'Attend');
    const onSelectionner = vi.fn();
    render(
      <EtapePanneau
        mep={mep({ etapes: [prealable, e] })}
        etape={e}
        resolutionsTypes={[]}
        actions={actionsFactices()}
        onSelectionner={onSelectionner}
      />,
    );
    expect(screen.queryByRole('button', { name: /Démarrer/ })).toBeNull();
    await userEvent.click(screen.getByRole('button', { name: 'E0000' }));
    expect(onSelectionner).toHaveBeenCalledWith('e0');
  });

  it('étape en cours (exécutant) : Réussie demande une confirmation, Échec exige une preuve', async () => {
    const { actions } = rendre(etape({ etat: 'EnCours', executantId: 'p1' }, 'EnCours'));
    expect(screen.getByRole('button', { name: 'Échec' })).toBeDisabled();
    await userEvent.click(screen.getByRole('button', { name: 'Réussie' }));
    await userEvent.click(screen.getByRole('button', { name: /J’ai vérifié et obtenu le résultat attendu/ }));
    expect(actions.terminer).toHaveBeenCalledWith(expect.objectContaining({ id: 'e1' }), 'Reussie', '', undefined);
  });

  it('Échec avec preuve : confirmation puis appel', async () => {
    const { actions } = rendre(etape({ etat: 'EnCours', executantId: 'p1' }, 'EnCours'));
    await userEvent.type(screen.getByLabelText(/Preuve/), 'Erreur 502');
    await userEvent.click(screen.getByRole('button', { name: 'Échec' }));
    await userEvent.click(screen.getByRole('button', { name: 'Déclarer l’échec' }));
    expect(actions.terminer).toHaveBeenCalledWith(
      expect.objectContaining({ id: 'e1' }),
      'Echec',
      'Erreur 502',
      undefined,
    );
  });

  it('le pilote peut terminer une étape exécutée par un autre', () => {
    rendre(etape({ etat: 'EnCours', executantId: 'p2' }, 'EnCours'), { participantId: 'p3', estPilote: true });
    expect(screen.getByRole('button', { name: 'Réussie' })).toBeInTheDocument();
  });

  it('étape réussie : aucune action, pas de prise de main', () => {
    rendre(etape({ etat: 'Reussie', executantId: 'p1' }, 'Terminee'), { participantId: 'p2' });
    expect(screen.queryByRole('button', { name: 'Prendre la main' })).toBeNull();
    expect(screen.queryByRole('button', { name: /Démarrer/ })).toBeNull();
  });

  it('durée estimée omise quand elle n’est pas renseignée', () => {
    rendre(etape({ dureeEstimeeMinutes: null }));
    expect(screen.queryByText(/\d+ min/)).toBeNull();
  });
});

describe('EtapePanneau : exécutant et prise de main', () => {
  it('affiche l’exécutant une fois l’étape démarrée', () => {
    rendre(etape({ etat: 'EnCours', executantId: 'p2' }, 'EnCours'), { participantId: 'p2' });
    expect(screen.getByText('Bob')).toBeInTheDocument();
  });

  it('étape en cours démarrée par un autre : pas de Réussie, prise de main avec motif obligatoire', async () => {
    const { actions } = rendre(etape({ etat: 'EnCours', executantId: 'p1' }, 'EnCours'), { participantId: 'p2' });
    expect(screen.queryByRole('button', { name: 'Réussie' })).toBeNull();
    await userEvent.click(screen.getByRole('button', { name: 'Prendre la main' }));
    expect(screen.getByText(/Tracé au journal/)).toBeInTheDocument();
    const confirmer = () => {
      const b = screen.getAllByRole('button', { name: 'Prendre la main' });
      return b[b.length - 1];
    };
    expect(confirmer()).toBeDisabled();
    await userEvent.type(screen.getByLabelText(/Pourquoi prenez-vous la main/), 'Claire est injoignable');
    await userEvent.click(confirmer());
    expect(actions.prendreLaMain).toHaveBeenCalledWith(expect.objectContaining({ id: 'e1' }), 'Claire est injoignable');
  });

  it('prise de main possible sur une étape en échec, pas sur une étape à faire', () => {
    const { unmount } = render(
      <EtapePanneau
        mep={mep({ droits: { ...mep().droits, participantId: 'p2' } })}
        etape={etape({ etat: 'Echec', executantId: 'p1' }, 'ArretResolution')}
        resolutionsTypes={[]}
        actions={actionsFactices()}
        onSelectionner={vi.fn()}
      />,
    );
    expect(screen.getByRole('button', { name: 'Prendre la main' })).toBeInTheDocument();
    unmount();
    rendre(etape(), { participantId: 'p2' });
    expect(screen.queryByRole('button', { name: 'Prendre la main' })).toBeNull();
  });

  it('prise de main : réservée aux participants et aux administrateurs', () => {
    const e = etape({ etat: 'Echec', executantId: 'p1' }, 'ArretResolution');
    const { unmount } = render(
      <EtapePanneau
        mep={mep({ droits: { ...mep().droits, participantId: null } })}
        etape={e}
        resolutionsTypes={[]}
        actions={actionsFactices()}
        onSelectionner={vi.fn()}
      />,
    );
    expect(screen.queryByRole('button', { name: 'Prendre la main' })).toBeNull();
    unmount();
    const admin = {
      utilisateur: { id: 'u9', login: 'eve', nomAffiche: 'Eve', estAdmin: true },
    } as unknown as ComponentProps<typeof AuthContext.Provider>['value'];
    render(
      <AuthContext.Provider value={admin}>
        <EtapePanneau
          mep={mep({ droits: { ...mep().droits, participantId: null } })}
          etape={e}
          resolutionsTypes={[]}
          actions={actionsFactices()}
          onSelectionner={vi.fn()}
        />
      </AuthContext.Provider>,
    );
    expect(screen.getByRole('button', { name: 'Prendre la main' })).toBeInTheDocument();
  });

  it('badge « À vous » : sur l’étape que j’exécute, masqué une fois terminée', () => {
    const { unmount } = render(
      <EtapePanneau
        mep={mep({ droits: { ...mep().droits, participantId: 'p1' } })}
        etape={etape({ etat: 'EnCours', executantId: 'p1' }, 'EnCours')}
        resolutionsTypes={[]}
        actions={actionsFactices()}
        onSelectionner={vi.fn()}
      />,
    );
    expect(screen.getByText('À vous')).toBeInTheDocument();
    unmount();
    rendre(etape({ etat: 'Reussie', executantId: 'p1' }, 'Terminee'));
    expect(screen.queryByText('À vous')).toBeNull();
  });

  it('badge « À vous » masqué dans une MEP clôturée', () => {
    rendre(etape({ etat: 'EnCours', executantId: 'p1' }), { m: { phase: 'Cloturee' } });
    expect(screen.queryByText('À vous')).toBeNull();
  });

  it('pas de prise de main hors de la phase d’exécution du plan', () => {
    rendre(etape({ etat: 'EnCours', executantId: 'p1' }), { participantId: 'p2', m: { phase: 'Preparation' } });
    expect(screen.queryByRole('button', { name: 'Prendre la main' })).toBeNull();
  });
});

describe('EtapePanneau : échec et résolutions', () => {
  const echec = () =>
    etape({ etat: 'Echec', preuve: 'Erreur 502', resolutionIds: ['t1'], executantId: 'p1' }, 'ArretResolution');

  it('affiche le message d’arrêt et les résolutions liées, applicables par l’exécutant', async () => {
    const { actions } = rendre(echec(), { types: [typeRes] });
    expect(screen.getByText('Arrêter, prévenir le pilote, ne rien improviser.')).toBeInTheDocument();
    expect(screen.getByText('Problème connu ?')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Appliquer cette résolution' }));
    expect(actions.appliquerResolution).toHaveBeenCalledWith(expect.objectContaining({ id: 'e1' }), 't1');
  });

  it('sans résolution liée : arrêter et prévenir le pilote, reprise réservée au pilote', () => {
    rendre(etape({ etat: 'Echec', executantId: 'p1' }, 'ArretResolution'), { estPilote: true });
    expect(screen.getByText('Aucune résolution prévue : arrêter et prévenir le pilote.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Autoriser la reprise' })).toBeInTheDocument();
  });

  it('pas de reprise pour un non-pilote', () => {
    rendre(etape({ etat: 'Echec', executantId: 'p1' }, 'ArretResolution'));
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
    rendre(etape({ etat: 'AFaire' }, 'Disponible'), {
      m: { resolutions: [resolution({ statut: 'Reussie', termineeLe: '2026-10-09T21:30:00Z' })] },
    });
    expect(screen.getByText('Résolutions déjà appliquées')).toBeInTheDocument();
    expect(screen.getByText('Réussie')).toBeInTheDocument();
  });
});

describe('EtapePanneau : « À appeler en cas de problème »', () => {
  const rt = (
    participantId: string,
    systemeIds: string[],
    extra: Partial<Mep['responsablesTechniques'][number]> = {},
  ) => ({ participantId, systemeIds, perimetre: '', disponibilite: 'Joignable' as const, contactJourJ: '', ...extra });

  it('liste les responsables techniques du système de l’étape, avec contact et disponibilité', () => {
    const base = mep().systemes[0];
    rendre(etape({ systemeIds: ['s1'] }), {
      m: {
        systemes: [
          { ...base, id: 's1', nom: 'API', cible: '' },
          { ...base, id: 's2', nom: 'WEB', cible: '' },
        ],
        responsablesTechniques: [
          rt('p2', ['s1'], { contactJourJ: '06 11 22 33 44' }),
          rt('p3', ['s2'], { disponibilite: 'Absent' }),
        ],
      },
    });
    const carte = screen.getByRole('region', { name: 'Responsables techniques' });
    expect(carte).toHaveTextContent('Bob');
    expect(carte).toHaveTextContent('06 11 22 33 44');
    expect(carte).toHaveTextContent('Joignable');
    expect(carte).not.toHaveTextContent('Alice');
  });

  it('sans responsable technique pour le système, affiche tous les responsables (dont les absents)', () => {
    rendre(etape({ systemeIds: ['s1'] }), {
      m: { responsablesTechniques: [rt('p2', []), rt('p3', [], { disponibilite: 'Absent' })] },
    });
    const carte = screen.getByRole('region', { name: 'À appeler en cas de problème' });
    expect(carte).toHaveTextContent('Bob');
    expect(carte).toHaveTextContent('Alice');
    expect(carte).toHaveTextContent('Absent');
  });

  it('sans responsable technique : seulement le pilote et les suppléants', () => {
    rendre(etape(), { m: { suppleantIds: ['p2'] } });
    expect(screen.queryByRole('region', { name: 'Responsables techniques' })).toBeNull();
    const pilotage = screen.getByRole('region', { name: 'Pilotage' });
    expect(pilotage).toHaveTextContent('AlicePilote');
    expect(pilotage).toHaveTextContent('BobSuppléant');
  });

  it('étape sur plusieurs systèmes : responsables de chacun de ses systèmes', () => {
    const base = mep().systemes[0];
    rendre(etape({ systemeIds: ['s1', 's2'] }), {
      m: {
        systemes: [
          { ...base, id: 's1', nom: 'API' },
          { ...base, id: 's2', nom: 'WEB' },
          { ...base, id: 's3', nom: 'BDD' },
        ],
        responsablesTechniques: [rt('p2', ['s1']), rt('p1', ['s2']), rt('p3', ['s3'])],
      },
    });
    const carte = screen.getByRole('region', { name: 'Responsables techniques' });
    expect(carte).toHaveTextContent('Bob');
    expect(carte).toHaveTextContent('Claire');
    expect(carte).not.toHaveTextContent('Alice');
  });
});

describe('EtapePanneau : instructions en blocs (markdown + Copier)', () => {
  const instructions =
    'Prévenir le **support** avant de lancer.\n\n```\ncd /opt/commandes && ./deploy.sh 2.4.0\n```\n\n- [ ] vérifier le groupe `app-commandes`';

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
    render(
      <EtapePanneau
        mep={mep({ etapes: [prealable, e] })}
        etape={e}
        resolutionsTypes={[]}
        actions={actionsFactices()}
        onSelectionner={onSelectionner}
      />,
    );
    const bouton = screen.getByRole('button', { name: /E0000.*Sauvegarde BDD.*Réussie/ });
    await userEvent.click(bouton);
    expect(onSelectionner).toHaveBeenCalledWith('e0');
  });
});

describe('EtapePanneau : fin de résolution', () => {
  it('résolution réussie : état explicite et bouton Redémarrer l’étape', async () => {
    const r = resolution({ statut: 'Reussie', termineeLe: '2026-10-09T21:30:00Z', verificationResultat: 'Conforme' });
    const { actions } = rendre(etape({ etat: 'AFaire', executantId: 'p1' }, 'Disponible'), { m: { resolutions: [r] } });
    expect(screen.getByText('Résolution réussie : l’étape est de nouveau à faire.')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Redémarrer l’étape' }));
    expect(actions.demarrer).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole('button', { name: /^Démarrer$/ })).toBeNull();
  });

  it('résolution en échec : retour arrière déclenché, lien vers l’onglet Retour arrière', () => {
    const r = resolution({ statut: 'Echouee', termineeLe: '2026-10-09T21:30:00Z' });
    rendre(etape({ etat: 'Echec', executantId: 'p1' }, 'RetourArriere'), {
      m: { resolutions: [r], phase: 'RetourArriere' },
    });
    expect(screen.getByText('Résolution en échec : retour arrière déclenché.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Ouvrir l’onglet Retour arrière' })).toHaveAttribute(
      'href',
      '/?onglet=retour',
    );
    expect(screen.queryByText('Problème connu ?')).toBeNull();
  });

  it('le pilote (non exécutant) peut dérouler la résolution', async () => {
    const { actions } = rendre(
      etape({ etat: 'Echec', executantId: 'p1', resolutionIds: ['t1'] }, 'ResolutionEnCours'),
      {
        participantId: 'p3',
        estPilote: true,
        types: [typeRes],
        m: { resolutions: [resolution()] },
      },
    );
    await userEvent.click(
      within(screen.getByRole('group', { name: 'Correction 1 sur 2 : à faire maintenant' })).getByRole('button', {
        name: 'Conforme',
      }),
    );
    expect(actions.controlerCorrection).toHaveBeenCalledWith(expect.objectContaining({ id: 'r1' }), 1, 'Conforme', '');
  });

  it('étape en échec : instructions de l’étape repliées sous le déroulé', () => {
    rendre(etape({ etat: 'Echec', executantId: 'p1' }, 'ArretResolution'), { m: { resolutions: [resolution()] } });
    expect(screen.getByText('Instructions de l’étape').closest('details')).not.toHaveAttribute('open');
  });
});

describe('EtapePanneau : commentaire', () => {
  it('l’exécutant enregistre un commentaire facultatif', async () => {
    const { actions } = rendre(etape({ etat: 'EnCours', executantId: 'p1' }, 'EnCours'));
    await userEvent.type(screen.getByLabelText(/Commentaire/), 'Service redémarré deux fois');
    await userEvent.click(screen.getByRole('button', { name: 'Enregistrer le commentaire' }));
    expect(actions.commenter).toHaveBeenCalledWith(
      expect.objectContaining({ id: 'e1' }),
      'Service redémarré deux fois',
    );
  });

  it('un commentaire non enregistré part avec « Réussie »', async () => {
    const { actions } = rendre(etape({ etat: 'EnCours', executantId: 'p1' }, 'EnCours'));
    await userEvent.type(screen.getByLabelText(/Commentaire/), 'RAS');
    await userEvent.click(screen.getByRole('button', { name: 'Réussie' }));
    await userEvent.click(screen.getByRole('button', { name: /J’ai vérifié/ }));
    expect(actions.terminer).toHaveBeenCalledWith(expect.objectContaining({ id: 'e1' }), 'Reussie', '', 'RAS');
  });

  it('lecture seule pour les autres participants', () => {
    rendre(etape({ etat: 'EnCours', executantId: 'p1', commentaire: 'Lenteur constatée' }, 'EnCours'), {
      participantId: 'p2',
    });
    expect(screen.queryByLabelText(/Commentaire/)).toBeNull();
    expect(screen.getByText('Lenteur constatée')).toBeInTheDocument();
  });
});
