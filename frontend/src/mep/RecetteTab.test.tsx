import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { api, ApiError } from '../api';
import { MepContext } from '../mepContext';
import { mep } from '../test/fabrique';
import type { EtapeTest, FicheRecette, Mep, ModeleRecette } from '../types';
import { TooltipProvider } from '../ui/Tooltip';
import RecetteTab from './RecetteTab';

vi.mock('../api', () => {
  class Erreur extends Error {
    status: number;
    erreurs: string[];
    constructor(status: number, message: string, erreurs: string[] = []) {
      super(message);
      this.status = status;
      this.erreurs = erreurs;
    }
  }
  return {
    ApiError: Erreur,
    api: {
      ajouterFicheRecette: vi.fn(),
      modifierFicheRecette: vi.fn(),
      supprimerFicheRecette: vi.fn(),
      resultatEtapeTest: vi.fn(),
      refaireFicheRecette: vi.fn(),
      refaireEtapeTest: vi.fn(),
      urlExportRecette: (id: string) => `/api/meps/${id}/recette/excel`,
      apercuValidation: vi.fn(),
      importerValidation: vi.fn(),
      modelesRecette: vi.fn(),
      insererModeleRecette: vi.fn(),
      catalogueSystemes: vi.fn(),
      modeleDepuisFiche: vi.fn(),
    },
  };
});

/** Adresse courante, pour vérifier la navigation vers la page d'une fiche. */
function Adresse() {
  const l = useLocation();
  return <output data-testid="adresse">{l.search}</output>;
}

function rendre(m: Mep, adresse = '?onglet=plan&plan=recette') {
  const run = vi.fn(async (action?: () => Promise<unknown>) => {
    await action?.();
    return true;
  });
  render(
    <MemoryRouter initialEntries={[`/meps/m1${adresse}`]}>
      <MepContext.Provider value={{ mep: m, run } as never}>
        <TooltipProvider>
          <RecetteTab />
          <Adresse />
        </TooltipProvider>
      </MepContext.Provider>
    </MemoryRouter>,
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
  valideePar: '',
  valideeLe: null,
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
  { ...mep().systemes[0], id: 's1', nom: 'BDD', cible: 'srv-db', catalogueId: 'c-bdd' },
  { ...mep().systemes[0], id: 's2', nom: 'WEB', cible: '', catalogueId: 'c-web' },
];
const droits = mep().droits;
const droitsEdition = { ...droits, peutEditerContenu: true, peutModifierPlan: true };
const modele = (id: string, titre: string, systemeCatalogueId: string): ModeleRecette => ({
  id,
  titre,
  systemeCatalogueId,
  etapes: [{ ordre: 1, instructions: 'Se connecter', resultatAttendu: 'Accueil affiché' }],
  version: 1,
  modifieLe: '2026-10-01T10:00:00Z',
  modifieParId: null,
  modifiePar: null,
});

describe('Onglet Recette', () => {
  beforeEach(() => vi.clearAllMocks());

  it('présente une section par système de la MEP, avec ses fiches', () => {
    rendre(mep({ phase: 'Preparation', systemes, fichesRecette: [fiche('f1')] }));
    const bdd = screen.getByRole('region', { name: 'BDD' });
    expect(within(bdd).getByText('Tables clients')).toBeInTheDocument();
    const web = screen.getByRole('region', { name: 'WEB' });
    expect(within(web).getByText('Aucune fiche de recette pour le système WEB.')).toBeInTheDocument();
    // Plus de section « Sans système » quand toutes les fiches ont leur système.
    expect(screen.queryByRole('region', { name: 'Sans système' })).toBeNull();
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

  it('garde visibles les anciennes fiches sans système, sans bouton d’ajout', () => {
    rendre(
      mep({
        phase: 'Preparation',
        systemes,
        droits: droitsEdition,
        fichesRecette: [fiche('f1', { systemeId: null, titre: 'Recette générale' })],
      }),
    );
    const section = screen.getByRole('region', { name: 'Sans système' });
    expect(within(section).getByText('Recette générale')).toBeInTheDocument();
    expect(within(section).queryByRole('button', { name: /Nouvelle fiche/ })).toBeNull();
  });

  it('cliquer sur une fiche ouvre sa page (adresse partageable)', async () => {
    rendre(mep({ phase: 'Preparation', systemes, fichesRecette: [fiche('f1')] }));
    await userEvent.click(screen.getByRole('link', { name: /Tables clients/ }));
    expect(screen.getByTestId('adresse')).toHaveTextContent('?onglet=plan&plan=recette&fiche=f1');
    const fil = screen.getByRole('navigation', { name: 'Fil d’Ariane' });
    expect(fil).toHaveTextContent('Recette/BDD/Tables clients');
    await userEvent.click(within(fil).getByRole('link', { name: 'Recette' }));
    expect(screen.getByTestId('adresse')).toHaveTextContent('?onglet=plan&plan=recette');
    expect(screen.getByRole('region', { name: 'BDD' })).toBeInTheDocument();
  });

  it('en préparation, « Nouvelle fiche » crée la fiche dans le système puis ouvre sa page', async () => {
    vi.mocked(api.ajouterFicheRecette).mockResolvedValue(
      mep({ phase: 'Preparation', systemes, fichesRecette: [fiche('f9', { systemeId: 's2', titre: 'Accueil' })] }),
    );
    const { run } = rendre(mep({ phase: 'Preparation', systemes, droits: droitsEdition }));
    const web = screen.getByRole('region', { name: 'WEB' });
    await userEvent.click(within(web).getByRole('button', { name: 'Nouvelle fiche WEB' }));
    await userEvent.type(await screen.findByLabelText(/Titre de la fiche/), 'Page d’accueil');
    await userEvent.click(screen.getByRole('button', { name: 'Créer la fiche' }));
    await waitFor(() => expect(run).toHaveBeenCalled());
    expect(api.ajouterFicheRecette).toHaveBeenCalledWith('m1', 's2', 'Page d’accueil');
    await waitFor(() => expect(screen.getByTestId('adresse')).toHaveTextContent('fiche=f9'));
  });

  it('« Depuis la bibliothèque » propose les modèles du système et insère la copie choisie', async () => {
    vi.mocked(api.modelesRecette).mockResolvedValue([
      modele('r1', 'Connexion au portail', 'c-web'),
      modele('r2', 'Droits applicatifs', 'c-bdd'),
    ]);
    vi.mocked(api.catalogueSystemes).mockResolvedValue([
      { id: 'c-web', nom: 'WEB', description: '', cible: '', actif: true },
      { id: 'c-bdd', nom: 'BDD', description: '', cible: '', actif: true },
    ]);
    const { run } = rendre(mep({ phase: 'Preparation', systemes, droits: droitsEdition }));
    await userEvent.click(screen.getByRole('button', { name: 'Ajouter une fiche WEB depuis la bibliothèque' }));
    const liste = await screen.findByRole('list', { name: 'Modèles de recette' });
    await within(liste).findByText('Connexion au portail');
    expect(within(liste).queryByText('Droits applicatifs')).toBeNull();
    await userEvent.selectOptions(screen.getByLabelText('Systèmes des modèles'), 'tous');
    expect(within(liste).getByText('Droits applicatifs')).toBeInTheDocument();
    await userEvent.click(within(liste).getByRole('button', { name: 'Insérer Connexion au portail' }));
    await waitFor(() => expect(run).toHaveBeenCalled());
    expect(api.insererModeleRecette).toHaveBeenCalledWith('m1', 'r1', 's2');
  });

  it('en préparation, sans droit d’édition : pas de bouton d’ajout', () => {
    rendre(mep({ phase: 'Preparation', systemes, fichesRecette: [fiche('f1')] }));
    expect(screen.queryByRole('button', { name: /Nouvelle fiche/ })).toBeNull();
    expect(screen.queryByRole('button', { name: /bibliothèque/ })).toBeNull();
  });

  describe('page d’une fiche en préparation', () => {
    it('une étape se rédige en blocs puis un résultat attendu en texte simple ; les id connus sont conservés', async () => {
      const { run } = rendre(
        mep({ phase: 'Preparation', systemes, fichesRecette: [fiche('f1')], droits: droitsEdition }),
        '?onglet=plan&plan=recette&fiche=f1',
      );
      expect(screen.queryByLabelText('Système')).toBeNull(); // la fiche garde son système
      await userEvent.click(screen.getByRole('button', { name: 'Ajouter une étape de test' }));
      await userEvent.type(screen.getByLabelText('Déroulé du test, étape 2, bloc 1 (texte)'), 'Cliquer sur Clients');
      await userEvent.type(screen.getByLabelText('Résultat attendu, étape 2'), 'La liste s’affiche');
      await userEvent.click(screen.getByRole('button', { name: 'Enregistrer la fiche' }));
      await waitFor(() => expect(run).toHaveBeenCalled());
      expect(api.modifierFicheRecette).toHaveBeenCalledWith('m1', 'f1', {
        titre: 'Tables clients',
        etapes: [
          { id: 't1', instructions: 'Ouvrir la page', resultatAttendu: 'La page s’affiche' },
          { id: undefined, instructions: 'Cliquer sur Clients', resultatAttendu: 'La liste s’affiche' },
        ],
      });
    });

    it('une ancienne fiche sans système se rattache à un système', async () => {
      const { run } = rendre(
        mep({
          phase: 'Preparation',
          systemes,
          fichesRecette: [fiche('f1', { systemeId: null })],
          droits: droitsEdition,
        }),
        '?onglet=plan&plan=recette&fiche=f1',
      );
      expect(screen.getByRole('navigation', { name: 'Fil d’Ariane' })).toHaveTextContent('Sans système');
      await userEvent.selectOptions(screen.getByLabelText('Rattacher au système'), 's2');
      await userEvent.click(screen.getByRole('button', { name: 'Enregistrer la fiche' }));
      await waitFor(() => expect(run).toHaveBeenCalled());
      expect(api.modifierFicheRecette).toHaveBeenCalledWith('m1', 'f1', expect.objectContaining({ systemeId: 's2' }));
    });

    it('« Enregistrer comme modèle » copie la fiche dans la bibliothèque avec le système du catalogue', async () => {
      vi.mocked(api.catalogueSystemes).mockResolvedValue([
        { id: 'c-bdd', nom: 'BDD', description: '', cible: '', actif: true },
      ]);
      vi.mocked(api.modeleDepuisFiche).mockResolvedValue(modele('r9', 'Tables clients', 'c-bdd'));
      rendre(
        mep({ phase: 'Preparation', systemes, fichesRecette: [fiche('f1')], droits: droitsEdition }),
        '?onglet=plan&plan=recette&fiche=f1',
      );
      await userEvent.click(screen.getByRole('button', { name: 'Enregistrer comme modèle' }));
      await screen.findByRole('option', { name: 'BDD' });
      await userEvent.click(screen.getByRole('button', { name: 'Enregistrer le modèle' }));
      await waitFor(() => expect(api.modeleDepuisFiche).toHaveBeenCalledWith('m1', 'f1', 'Tables clients', 'c-bdd'));
    });

    it('une fiche inconnue propose de revenir à la recette', () => {
      rendre(mep({ phase: 'Preparation', systemes }), '?onglet=plan&plan=recette&fiche=inconnue');
      expect(screen.getByText('Fiche de recette introuvable')).toBeInTheDocument();
    });
  });

  describe('déroulé en phase Recette', () => {
    const enRecette = (f: FicheRecette[], d = {}) =>
      mep({ phase: 'Recette', systemes, fichesRecette: f, droits: { ...droits, participantId: 'p1', ...d } });

    it('Conforme / Échec exigent une observation, puis appellent l’API de l’étape', async () => {
      const { run } = rendre(enRecette([fiche('f1')]), '?onglet=plan&plan=recette&fiche=f1');
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
      const { run } = rendre(enRecette([f], { estPilote: true }), '?onglet=plan&plan=recette&fiche=f1');
      expect(screen.getByText('Erreur 500')).toBeInTheDocument();
      await userEvent.click(screen.getByRole('button', { name: /Refaire l’étape 1/ }));
      await waitFor(() => expect(run).toHaveBeenCalledTimes(1));
      expect(api.refaireEtapeTest).toHaveBeenCalledWith('m1', 'f1', 't1');
      await userEvent.click(screen.getByRole('button', { name: 'Refaire la fiche' }));
      expect(api.refaireFicheRecette).toHaveBeenCalledWith('m1', 'f1');
    });

    it('une validation venue d’Excel montre le nom du métier et qui a importé le fichier', () => {
      const f = fiche('f1', {
        etapes: [
          test('t1', {
            resultat: 'Conforme',
            observation: '1 250 soldes',
            par: 'Claire',
            le: '2026-10-19T08:00:00Z',
            valideePar: 'Marie (paie)',
            valideeLe: '2026-10-18',
          }),
        ],
      });
      rendre(enRecette([f]), '?onglet=plan&plan=recette&fiche=f1');
      expect(
        screen.getByText(/Validé par Marie \(paie\) le 18\/10\/2026 · fichier Excel importé par Claire/),
      ).toBeInTheDocument();
    });

    it('un non-pilote ne voit pas « Refaire »', () => {
      const f = fiche('f1', { etapes: [test('t1', { resultat: 'Conforme', observation: 'ok' })] });
      rendre(enRecette([f]), '?onglet=plan&plan=recette&fiche=f1');
      expect(screen.queryByRole('button', { name: /Refaire/ })).toBeNull();
    });

    it('sans plan de retour arrière, le pilote ne peut pas le déclencher', () => {
      const f = fiche('f1');
      rendre({ ...enRecette([f], { estPilote: true }), retourArriereActive: false });
      expect(screen.queryByRole('button', { name: /Déclencher le retour arrière/ })).toBeNull();
      expect(screen.getByText(/n’a pas de plan de retour arrière/)).toBeInTheDocument();
    });

    it('avec un plan de retour arrière, le pilote peut le déclencher', () => {
      rendre(enRecette([fiche('f1')], { estPilote: true }));
      expect(screen.getByRole('button', { name: /Déclencher le retour arrière/ })).toBeInTheDocument();
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

  describe('Excel', () => {
    const enRecette = () =>
      mep({ phase: 'Recette', systemes, fichesRecette: [fiche('f1')], droits: { ...droits, participantId: 'p1' } });

    it('« Exporter en Excel » télécharge le fichier de la MEP', async () => {
      const clic = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (
        this: HTMLAnchorElement,
      ) {
        expect(this.getAttribute('href')).toBe('/api/meps/m1/recette/excel');
      });
      rendre(enRecette());
      await userEvent.click(screen.getByRole('button', { name: 'Exporter en Excel' }));
      expect(clic).toHaveBeenCalledTimes(1);
      clic.mockRestore();
    });

    it('« Importer la validation » n’apparaît qu’en recette', () => {
      rendre(mep({ phase: 'Preparation', systemes, fichesRecette: [fiche('f1')] }));
      expect(screen.getByRole('button', { name: 'Exporter en Excel' })).toBeEnabled();
      expect(screen.queryByRole('button', { name: 'Importer la validation' })).toBeNull();
    });

    it('montre l’aperçu des changements puis reporte la validation', async () => {
      vi.mocked(api.apercuValidation).mockResolvedValue({
        changements: [
          {
            ficheId: 'f1',
            etapeId: 't1',
            systeme: 'BDD',
            fiche: 'Tables clients',
            etape: 1,
            resultat: 'Conforme',
            observation: '1 250 lignes',
            validePar: 'Marie',
            date: '2026-10-18',
          },
        ],
        inchangees: 2,
        sansValidation: 0,
      });
      const { run } = rendre(enRecette());
      await userEvent.click(screen.getByRole('button', { name: 'Importer la validation' }));
      const fichier = new File(['xlsx'], 'recette.xlsx');
      await userEvent.upload(screen.getByLabelText('Fichier Excel de la recette'), fichier);
      const liste = await screen.findByRole('list', { name: 'Validations à reporter' });
      expect(liste).toHaveTextContent('BDD · Tables clients · étape 1');
      expect(liste).toHaveTextContent('Par Marie le 18/10/2026');
      expect(screen.getByText(/1 étape de test va être renseignée\. 2 déjà à jour\./)).toBeInTheDocument();
      expect(api.importerValidation).not.toHaveBeenCalled();
      await userEvent.click(screen.getByRole('button', { name: 'Reporter 1 validation' }));
      await waitFor(() => expect(run).toHaveBeenCalled());
      expect(api.importerValidation).toHaveBeenCalledWith('m1', fichier);
    });

    it('un fichier refusé liste les raisons et ne propose pas de reporter', async () => {
      vi.mocked(api.apercuValidation).mockRejectedValue(
        new ApiError(422, 'Fichier refusé, rien n’a été modifié : …', [
          'Onglet « WEB » introuvable : les onglets du fichier ne doivent être ni renommés ni supprimés.',
          'Onglet BDD, ligne 6 : validation « Bof » inconnue (Validé ou Refusé attendu).',
        ]),
      );
      rendre(enRecette());
      await userEvent.click(screen.getByRole('button', { name: 'Importer la validation' }));
      await userEvent.upload(screen.getByLabelText('Fichier Excel de la recette'), new File(['x'], 'recette.xlsx'));
      const alerte = await screen.findByRole('alert');
      expect(alerte).toHaveTextContent('Fichier refusé, rien n’a été modifié');
      expect(alerte).toHaveTextContent('Onglet « WEB » introuvable');
      expect(alerte).toHaveTextContent('validation « Bof » inconnue');
      expect(screen.getByRole('button', { name: 'Reporter' })).toBeDisabled();
    });
  });
});
