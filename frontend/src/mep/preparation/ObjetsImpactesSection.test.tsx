import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { api } from '../../api';
import { MepContext } from '../../mepContext';
import { mep } from '../../test/fabrique';
import type { Mep, ObjetCatalogue, ObjetImpacte, SystemeCatalogue } from '../../types';
import { TooltipProvider } from '../../ui/Tooltip';
import { ObjetsImpactesSection } from './ObjetsImpactesSection';

vi.mock('../../api', () => ({
  api: {
    catalogueSystemes: vi.fn(),
    objetsCatalogue: vi.fn(),
    objetsSysteme: vi.fn(async () => ({})),
    supprimerObjetImpacte: vi.fn(async () => ({})),
    importerObjets: vi.fn(),
    urlObjetsExcel: vi.fn(() => '/api/x'),
  },
}));

const fiche: SystemeCatalogue = {
  id: 'c-batch',
  nom: 'BATCH',
  description: '',
  cible: '',
  actif: true,
  environnements: [{ nom: 'Production', cible: '', production: true }],
  categories: ['Ordonnancement', 'Fichiers d’échange', 'Packages'],
  nbObjets: 5,
};

const oc = (id: string, categorie: string, type: string, libelle: string, precision = ''): ObjetCatalogue => ({
  id,
  categorie,
  type,
  nom: id,
  libelle,
  precision,
});
const objetsFiche = [
  oc('FACT01', 'Ordonnancement', 'Chaîne', 'Facturation mensuelle', 'chaîne de nuit'),
  oc('FACT03', 'Ordonnancement', 'Job', 'Génération Factur-X', 'après FACT01'),
  oc('EDTPDF', 'Ordonnancement', 'Job', 'Édition des PDF', 'après FACT03'),
  oc('PURJRN', 'Ordonnancement', 'Job', 'Purge des journaux', 'mensuel'),
  oc('TARIFS.csv', 'Fichiers d’échange', 'Fichier', 'Tarifs du fournisseur'),
];

const impacte = (o: ObjetCatalogue, version: string, p: Partial<ObjetImpacte> = {}): ObjetImpacte => ({
  id: `oi-${o.id}`,
  systemeId: 's1',
  objetCatalogueId: o.id,
  categorie: o.categorie,
  type: o.type,
  nom: o.nom,
  libelle: o.libelle,
  precision: o.precision,
  version,
  repris: false,
  ...p,
});

function rendre(p: Partial<Mep> = {}) {
  vi.mocked(api.catalogueSystemes).mockResolvedValue([fiche]);
  vi.mocked(api.objetsCatalogue).mockResolvedValue(objetsFiche);
  const run = vi.fn(async (action: () => Promise<unknown>) => {
    await action();
    return true;
  });
  const base = mep().systemes[0];
  const m = mep({
    phase: 'Preparation',
    droits: { ...mep().droits, peutEditerContenu: true, peutModifierPlan: true },
    systemes: [{ ...base, nom: 'BATCH', catalogueId: 'c-batch' }],
    objetsImpactes: [impacte(objetsFiche[0], 'v3.2.0')],
    ...p,
  });
  render(
    <MemoryRouter>
      <TooltipProvider>
        <MepContext.Provider value={{ mep: m, run }}>
          <ObjetsImpactesSection />
        </MepContext.Provider>
      </TooltipProvider>
    </MemoryRouter>,
  );
  return { run };
}

describe('Objets impactés', () => {
  beforeEach(() => vi.clearAllMocks());

  it('un bloc par système : environnements, résumé, tuiles par catégorie et détail', async () => {
    rendre();
    const bloc = screen.getByRole('region', { name: 'Objets impactés de BATCH' });
    expect(within(bloc).getByText('Production')).toBeInTheDocument();
    expect(within(bloc).getAllByText('1 ordonnancement')).toHaveLength(2); // résumé et tuile
    await userEvent.click(within(bloc).getByRole('button', { name: /Ordonnancement/, expanded: false }));
    expect(await within(bloc).findByText(/1 sur 4 au catalogue/)).toBeInTheDocument();
    const ligne = within(bloc).getByText('FACT01').closest('tr')!;
    expect(ligne).toHaveTextContent('Facturation mensuelle');
    expect(ligne).toHaveTextContent('v3.2.0');
  });

  it('« Ajouter une catégorie » ne propose que les catégories de la fiche non utilisées, et un lien vers la fiche', async () => {
    rendre();
    await waitFor(() => expect(api.objetsCatalogue).toHaveBeenCalledWith('c-batch'));
    await userEvent.click(await screen.findByRole('button', { name: /Ajouter une catégorie/ }));
    const menu = await screen.findByRole('menu');
    expect(within(menu).queryByText('Ordonnancement')).toBeNull();
    expect(within(menu).getByText('Fichiers d’échange')).toBeInTheDocument();
    expect(within(menu).getByText('Packages')).toBeInTheDocument();
    expect(within(menu).getByRole('menuitem', { name: /Compléter la fiche BATCH/ })).toHaveAttribute(
      'href',
      '/parametres/systemes/c-batch',
    );
  });

  it('choix outillé : recherche multi-termes, case d’en-tête sur les affichés, version pour les cochés', async () => {
    rendre();
    const bloc = screen.getByRole('region', { name: 'Objets impactés de BATCH' });
    await userEvent.click(within(bloc).getByRole('button', { name: /Ordonnancement/ }));
    await userEvent.click(await within(bloc).findByRole('button', { name: /Choisir les objets/ }));
    const dialogue = await screen.findByRole('dialog', { name: 'Objets impactés de BATCH' });
    expect(within(dialogue).getByLabelText('1 coché sur 4')).toBeInTheDocument();

    // « FACT » trouve aussi EDTPDF (« après FACT03 »), pas PURJRN.
    await userEvent.type(within(dialogue).getByLabelText('Rechercher un objet'), 'FACT');
    expect(within(dialogue).getByText('EDTPDF')).toBeInTheDocument();
    expect(within(dialogue).queryByText('PURJRN')).toBeNull();
    await userEvent.click(within(dialogue).getByRole('checkbox', { name: 'Cocher les objets affichés' }));
    expect(within(dialogue).getByRole('checkbox', { name: 'Cocher EDTPDF' })).toBeChecked();

    await userEvent.type(within(dialogue).getByLabelText('Version ou correction pour les cochés'), 'v3.3');
    await userEvent.click(within(dialogue).getByRole('button', { name: 'Appliquer' }));
    const fact03 = within(dialogue).getByLabelText('Version ou correction de FACT03');
    await userEvent.clear(fact03);
    await userEvent.type(fact03, 'v3.3.1');
    await userEvent.click(within(dialogue).getByRole('button', { name: 'Enregistrer (3 objets)' }));
    await waitFor(() =>
      expect(api.objetsSysteme).toHaveBeenCalledWith('m1', 's1', {
        objets: [
          { objetCatalogueId: 'FACT01', version: 'v3.3' },
          { objetCatalogueId: 'FACT03', version: 'v3.3.1' },
          { objetCatalogueId: 'EDTPDF', version: 'v3.3' },
        ],
      }),
    );
    expect(within(dialogue).getByRole('link', { name: /Ajoutez-le à la fiche BATCH/ })).toHaveAttribute(
      'href',
      '/parametres/systemes/c-batch',
    );
  });

  it('retirer une catégorie retire ses objets du système', async () => {
    rendre({
      objetsImpactes: [impacte(objetsFiche[0], 'v1'), impacte(objetsFiche[4], 'format 2026')],
    });
    const bloc = screen.getByRole('region', { name: 'Objets impactés de BATCH' });
    expect(await within(bloc).findByText('1 ordonnancement, 1 fichier d’échange')).toBeInTheDocument();
    await userEvent.click(within(bloc).getByRole('button', { name: /Ordonnancement/ }));
    await userEvent.click(within(bloc).getByRole('button', { name: 'Retirer la catégorie' }));
    await userEvent.click(await screen.findByRole('button', { name: 'Retirer' }));
    await waitFor(() =>
      expect(api.objetsSysteme).toHaveBeenCalledWith('m1', 's1', {
        objets: [{ objetCatalogueId: 'TARIFS.csv', version: 'format 2026' }],
      }),
    );
  });

  it('un ancien livrable repris hors catalogue est signalé et se retire', async () => {
    rendre({
      objetsImpactes: [
        impacte(oc('Archive', 'Packages', 'Package', ''), '2.0', { id: 'l1', objetCatalogueId: null, repris: true }),
      ],
    });
    const bloc = screen.getByRole('region', { name: 'Objets impactés de BATCH' });
    await userEvent.click(within(bloc).getByRole('button', { name: /Packages/ }));
    expect(within(bloc).getByText('Repris, hors catalogue')).toBeInTheDocument();
    await userEvent.click(within(bloc).getByRole('button', { name: 'Retirer Archive de la MEP' }));
    await waitFor(() => expect(api.supprimerObjetImpacte).toHaveBeenCalledWith('m1', 'l1'));
  });

  it('import Excel : bilan et lignes rejetées avec un lien vers la fiche', async () => {
    rendre();
    vi.mocked(api.importerObjets).mockResolvedValue({
      mep: mep(),
      ajoutes: 2,
      modifies: 1,
      inchanges: 0,
      rejetees: [
        {
          ligne: 7,
          systeme: 'BATCH',
          categorie: 'Ordonnancement',
          nom: 'FACT99',
          raison: 'Objet absent de la catégorie Ordonnancement de la fiche BATCH du catalogue.',
          systemeCatalogueId: 'c-batch',
        },
      ],
    });
    await userEvent.click(screen.getByRole('button', { name: 'Importer' }));
    const dialogue = await screen.findByRole('dialog', { name: 'Importer les objets impactés' });
    const fichier = new File(['x'], 'objets.xlsx');
    await userEvent.upload(within(dialogue).getByLabelText('Fichier Excel des objets impactés'), fichier);
    expect(await within(dialogue).findByText(/2 objets ajoutés, 1 version mise à jour/)).toBeInTheDocument();
    expect(within(dialogue).getByText(/1 ligne rejetée/)).toBeInTheDocument();
    expect(within(dialogue).getByText(/FACT99/)).toBeInTheDocument();
    expect(within(dialogue).getByRole('link', { name: 'Compléter la fiche' })).toHaveAttribute(
      'href',
      '/parametres/systemes/c-batch',
    );
  });
});
