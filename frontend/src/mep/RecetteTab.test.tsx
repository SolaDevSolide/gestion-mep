import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { api } from '../api';
import { MepContext } from '../mepContext';
import { mep } from '../test/fabrique';
import type { EtapeTest, FicheRecette, Mep } from '../types';
import { TooltipProvider } from '../ui/Tooltip';
import RecetteTab from './RecetteTab';

vi.mock('../api', () => ({
  api: {
    ajouterFicheRecette: vi.fn(),
    modifierFicheRecette: vi.fn(),
    supprimerFicheRecette: vi.fn(),
    resultatEtapeTest: vi.fn(),
    refaireFicheRecette: vi.fn(),
    refaireEtapeTest: vi.fn(),
  },
}));

function rendre(m: Mep) {
  const run = vi.fn(async (action?: () => Promise<unknown>) => {
    await action?.();
    return true;
  });
  render(
    <MepContext.Provider value={{ mep: m, run } as never}>
      <TooltipProvider>
        <RecetteTab />
      </TooltipProvider>
    </MepContext.Provider>,
  );
  return { run };
}

const test = (id: string, p: Partial<EtapeTest> = {}): EtapeTest => ({
  id,
  ordre: 1,
  instructions: 'Ouvrir la page',
  resultatAttendu: 'La page s’affiche',
  resultat: null,
  observation: '',
  par: null,
  le: null,
  ...p,
});
const fiche = (id: string, p: Partial<FicheRecette> = {}): FicheRecette => ({
  id,
  ordre: 1,
  systemeId: 's1',
  titre: 'Tables clients',
  etapes: [test('t1')],
  ...p,
});
const systemes = [
  { ...mep().systemes[0], id: 's1', nom: 'BDD', cible: 'srv-db' },
  { ...mep().systemes[0], id: 's2', nom: 'WEB', cible: '' },
];
const droits = mep().droits;
const droitsEdition = { ...droits, peutEditerContenu: true, peutModifierPlan: true };

describe('Onglet Recette', () => {
  beforeEach(() => vi.clearAllMocks());

  it('présente une section par système de la MEP, avec ses fiches', () => {
    rendre(mep({ phase: 'Preparation', systemes, fichesRecette: [fiche('f1')] }));
    const bdd = screen.getByRole('region', { name: 'BDD' });
    expect(within(bdd).getByText('Tables clients')).toBeInTheDocument();
    const web = screen.getByRole('region', { name: 'WEB' });
    expect(within(web).getByText('Aucune recette pour le système WEB.')).toBeInTheDocument();
  });

  it('affiche le contact des responsables techniques du système', () => {
    rendre(
      mep({
        phase: 'Recette',
        systemes,
        fichesRecette: [fiche('f1')],
        responsablesTechniques: [
          {
            participantId: 'p2',
            systemeIds: ['s1'],
            perimetre: '',
            disponibilite: 'Joignable',
            contactJourJ: '06 22 22',
          },
        ],
      }),
    );
    const contacts = within(screen.getByRole('region', { name: 'BDD' })).getByLabelText(
      'Responsables techniques de BDD',
    );
    expect(contacts).toHaveTextContent('Bob');
    expect(contacts).toHaveTextContent('06 22 22');
  });

  it('range les fiches sans système dans « Sans système »', () => {
    rendre(
      mep({ phase: 'Recette', systemes, fichesRecette: [fiche('f1', { systemeId: null, titre: 'Recette générale' })] }),
    );
    expect(
      within(screen.getByRole('region', { name: 'Sans système' })).getByText('Recette générale'),
    ).toBeInTheDocument();
  });

  it('en préparation, un éditeur ajoute une fiche pour le système choisi', async () => {
    const { run } = rendre(mep({ phase: 'Preparation', systemes, droits: droitsEdition }));
    const web = screen.getByRole('region', { name: 'WEB' });
    await userEvent.click(within(web).getByRole('button', { name: 'Ajouter une recette' }));
    await userEvent.type(await screen.findByLabelText(/Titre de la fiche/), 'Page d’accueil');
    await userEvent.click(screen.getByRole('button', { name: 'Ajouter' }));
    await waitFor(() => expect(run).toHaveBeenCalled());
    expect(api.ajouterFicheRecette).toHaveBeenCalledWith('m1', 's2', 'Page d’accueil');
  });

  it('en préparation, enregistre toute la fiche avec ses étapes (les id connus sont conservés)', async () => {
    const { run } = rendre(
      mep({ phase: 'Preparation', systemes, fichesRecette: [fiche('f1')], droits: droitsEdition }),
    );
    await userEvent.click(screen.getByRole('button', { name: /Tables clients/ }));
    await userEvent.click(screen.getByRole('button', { name: 'Ajouter une étape de test' }));
    await userEvent.type(screen.getByLabelText('Instructions de l’étape 2, bloc 1 (texte)'), 'Cliquer sur Clients');
    await userEvent.type(screen.getByLabelText('Résultat attendu de l’étape 2, bloc 1 (texte)'), 'La liste s’affiche');
    await userEvent.click(screen.getByRole('button', { name: 'Enregistrer la fiche' }));
    await waitFor(() => expect(run).toHaveBeenCalled());
    expect(api.modifierFicheRecette).toHaveBeenCalledWith('m1', 'f1', {
      systemeId: 's1',
      titre: 'Tables clients',
      etapes: [
        { id: 't1', instructions: 'Ouvrir la page', resultatAttendu: 'La page s’affiche' },
        { id: undefined, instructions: 'Cliquer sur Clients', resultatAttendu: 'La liste s’affiche' },
      ],
    });
  });

  it('en préparation sans droit d’édition : pas de bouton d’ajout', () => {
    rendre(mep({ phase: 'Preparation', systemes, fichesRecette: [fiche('f1')] }));
    expect(screen.queryByRole('button', { name: 'Ajouter une recette' })).toBeNull();
  });

  describe('déroulé en phase Recette', () => {
    const enRecette = (f: FicheRecette[], d = {}) =>
      mep({ phase: 'Recette', systemes, fichesRecette: f, droits: { ...droits, participantId: 'p1', ...d } });

    it('Conforme / Échec exigent une observation, puis appellent l’API de l’étape', async () => {
      const { run } = rendre(enRecette([fiche('f1')]));
      const conforme = screen.getByRole('button', { name: 'Conforme' });
      expect(conforme).toBeDisabled();
      await userEvent.type(screen.getByLabelText(/Observation de l’étape 1/), 'Version 4.2 visible');
      await userEvent.click(conforme);
      await waitFor(() => expect(run).toHaveBeenCalled());
      expect(api.resultatEtapeTest).toHaveBeenCalledWith('m1', 'f1', 't1', 'Conforme', 'Version 4.2 visible');
    });

    it('une étape validée montre son résultat ; seul le pilote peut la refaire (étape ou fiche)', async () => {
      const f = fiche('f1', {
        etapes: [
          test('t1', { resultat: 'Echec', observation: 'Erreur 500', par: 'Claire', le: '2026-10-09T21:00:00Z' }),
        ],
      });
      const { run } = rendre(enRecette([f], { estPilote: true }));
      expect(screen.getByText('Erreur 500')).toBeInTheDocument();
      await userEvent.click(screen.getByRole('button', { name: /Refaire l’étape 1/ }));
      await waitFor(() => expect(run).toHaveBeenCalledTimes(1));
      expect(api.refaireEtapeTest).toHaveBeenCalledWith('m1', 'f1', 't1');
      await userEvent.click(screen.getByRole('button', { name: /Refaire toute la fiche/ }));
      expect(api.refaireFicheRecette).toHaveBeenCalledWith('m1', 'f1');
    });

    it('un non-pilote ne voit pas « Refaire »', () => {
      const f = fiche('f1', { etapes: [test('t1', { resultat: 'Conforme', observation: 'ok' })] });
      rendre(enRecette([f]));
      expect(screen.queryByRole('button', { name: /Refaire/ })).toBeNull();
    });

    it('résume la progression et signale les échecs', () => {
      const f = fiche('f1', {
        etapes: [
          test('t1', { resultat: 'Conforme', observation: 'ok' }),
          test('t2', { ordre: 2, resultat: 'Echec', observation: 'ko' }),
          test('t3', { ordre: 3 }),
        ],
      });
      rendre(enRecette([f]));
      expect(screen.getAllByText(/1\/3 conforme/).length).toBeGreaterThan(0);
      expect(screen.getByText('Une étape de test est en échec')).toBeInTheDocument();
    });
  });
});
