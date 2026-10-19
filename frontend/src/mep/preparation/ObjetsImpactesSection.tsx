import {
  ChevronDown,
  ChevronUp,
  Download,
  ExternalLink,
  FileSpreadsheet,
  ListChecks,
  OctagonAlert,
  Package,
  Plus,
  Trash2,
  Upload,
} from 'lucide-react';
import { useEffect, useId, useState } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../../api';
import { useMep } from '../../mepContext';
import { useReferentiel } from '../../referentiels';
import { telecharger } from '../../telechargement';
import type { ImportObjets, ObjetCatalogue, ObjetImpacte, Systeme, SystemeCatalogue } from '../../types';
import { Alerte } from '../../ui/Alerte';
import { BoutonIcone, Button } from '../../ui/Button';
import { cn } from '../../ui/cn';
import { ConfirmDialog, Dialog } from '../../ui/Dialog';
import { EmptyState } from '../../ui/EmptyState';
import { ErreurChargement } from '../../ui/ErreurChargement';
import { Menu, MenuContent, MenuItem, MenuLabel, MenuSeparator, MenuTrigger } from '../../ui/Menu';
import { PastilleEnvironnement } from '../../ui/PastilleEnvironnement';
import { Squelette } from '../../ui/Squelette';
import { Badge } from '../../ui/StatusPill';
import { Tooltip } from '../../ui/Tooltip';
import { PuceSysteme } from '../graphe/systemes';
import { ChoixObjetsDialog } from './ChoixObjetsDialog';
import { SectionCarte } from './Formulaires';
import { useObjetsCatalogue } from './donnees';
import { nommerCategorie, parCategorie, resumeObjets } from './objets';

/** Fiche du catalogue d'un système de la MEP et ses objets. */
interface Fiche {
  fiche: SystemeCatalogue;
  objets: ObjetCatalogue[];
}

/**
 * Section 5 de la préparation : objets impactés, un bloc par système. Chaque catégorie est une tuile avec son nombre
 * d'objets ; un clic ouvre son détail. Les objets se choisissent dans la fiche du système au catalogue (dialogue de
 * choix) ou s'importent d'Excel ; rien ne se crée ici.
 */
/** Fiche d'un système de la MEP au catalogue : prête, en chargement ou en erreur (avec de quoi réessayer). */
interface EtatFiche {
  fiche: Fiche | null;
  pret: boolean;
  erreur: unknown;
  enCours: boolean;
  recharger: () => void;
}

export function ObjetsImpactesSection() {
  const { mep } = useMep();
  const edition = mep.droits.peutEditerContenu;
  const [importer, setImporter] = useState(false);
  const catalogue = useReferentiel('catalogue');
  const objets = useObjetsCatalogue(mep);

  function etatFiche(catalogueId: string): EtatFiche {
    const o = objets.get(catalogueId);
    const fiche = catalogue.donnees?.find((c) => c.id === catalogueId);
    const erreur = catalogue.erreur ?? o?.erreur ?? null;
    return {
      fiche: fiche && o?.donnees ? { fiche, objets: o.donnees } : null,
      pret: catalogue.pret && (o?.pret ?? true),
      erreur,
      enCours: catalogue.enCours || (o?.enCours ?? false),
      recharger: () => {
        if (catalogue.erreur != null) catalogue.recharger();
        if (o?.erreur != null) o.recharger();
      },
    };
  }

  const sansSysteme = mep.objetsImpactes.filter((o) => !mep.systemes.some((s) => s.id === o.systemeId));

  return (
    <SectionCarte
      numero={5}
      titre="Objets impactés"
      description="Par système : les catégories et les objets que la MEP modifie, choisis dans la fiche du système au catalogue."
      action={
        <span className="flex flex-wrap gap-2">
          <Button
            taille="sm"
            icone={<FileSpreadsheet className="size-4" aria-hidden />}
            title="Classeur vide à remplir : une ligne par objet (système, catégorie, nom, version ou correction)."
            onClick={() => telecharger(api.urlObjetsExcel(mep.id, true))}
          >
            Modèle Excel
          </Button>
          {edition && (
            <Button taille="sm" icone={<Upload className="size-4" aria-hidden />} onClick={() => setImporter(true)}>
              Importer
            </Button>
          )}
          <Button
            taille="sm"
            icone={<Download className="size-4" aria-hidden />}
            disabled={mep.objetsImpactes.length === 0}
            onClick={() => telecharger(api.urlObjetsExcel(mep.id, false))}
          >
            Exporter
          </Button>
        </span>
      }
    >
      {mep.systemes.length === 0 ? (
        <EmptyState
          icone={Package}
          titre="Aucun système"
          description="Ajoutez d’abord les systèmes touchés (section 3) : leurs objets se choisissent dans leur fiche au catalogue."
        />
      ) : (
        <div className="space-y-4">
          {mep.systemes.map((s) => (
            <BlocSysteme
              key={s.id}
              systeme={s}
              etat={s.catalogueId ? etatFiche(s.catalogueId) : null}
              edition={edition}
            />
          ))}
          {sansSysteme.length > 0 && <BlocSansSysteme objets={sansSysteme} edition={edition} />}
        </div>
      )}
      <ImportObjetsDialog open={importer} onOpenChange={setImporter} />
    </SectionCarte>
  );
}

/* ------------------------------------------------------------------ Bloc d'un système */

function BlocSysteme({ systeme, etat, edition }: { systeme: Systeme; etat: EtatFiche | null; edition: boolean }) {
  const { mep, run } = useMep();
  const fiche = etat?.fiche ?? null;
  const [ouverte, setOuverte] = useState<string | null>(null);
  const [choix, setChoix] = useState<{ categorie?: string } | null>(null);
  const [retrait, setRetrait] = useState<string | null>(null);
  const objets = mep.objetsImpactes.filter((o) => o.systemeId === systeme.id);
  const categories = parCategorie(objets, fiche?.fiche.categories);
  const detail = categories.find((c) => c.nom === ouverte) ?? null;
  const disponibles = (fiche?.fiche.categories ?? []).filter((c) => !categories.some((x) => x.nom === c));

  function retirerCategorie(nom: string) {
    const liees = objets.filter((o) => o.objetCatalogueId && o.categorie !== nom);
    const reprises = objets.filter((o) => !o.objetCatalogueId && o.categorie === nom);
    return run(async () => {
      let m = mep;
      if (systeme.catalogueId && objets.some((o) => o.objetCatalogueId && o.categorie === nom))
        m = await api.objetsSysteme(mep.id, systeme.id, {
          objets: liees.map((o) => ({ objetCatalogueId: o.objetCatalogueId!, version: o.version })),
        });
      for (const o of reprises) m = await api.supprimerObjetImpacte(mep.id, o.id);
      return m;
    }, `Catégorie ${nom} retirée de ${systeme.nom}.`);
  }

  return (
    <section className="overflow-hidden rounded-xl border border-line" aria-label={`Objets impactés de ${systeme.nom}`}>
      <header className="flex flex-wrap items-center gap-x-3 gap-y-2 border-b border-line bg-surface-2/60 px-4 py-2.5">
        <PuceSysteme nom={systeme.nom} />
        {systeme.environnements.map((e) => (
          <PastilleEnvironnement key={e.nom} nom={e.nom} production={e.production} />
        ))}
        <span className="min-w-0 flex-1 text-sm text-fg-muted">
          {categories.length > 0 ? resumeObjets(categories) : 'Aucun objet impacté'}
        </span>
        {edition && etat && !etat.pret && <Squelette className="h-8 w-48" />}
        {edition && fiche && (
          <Menu>
            <MenuTrigger asChild>
              <Button taille="sm" variante="fantome" icone={<Plus className="size-4" aria-hidden />}>
                Ajouter une catégorie
                <ChevronDown className="size-4" aria-hidden />
              </Button>
            </MenuTrigger>
            <MenuContent className="w-72">
              <MenuLabel>Catégories de {systeme.nom} au catalogue</MenuLabel>
              {disponibles.length === 0 && (
                <p className="px-2.5 py-1.5 text-sm text-fg-subtle">
                  Toutes les catégories de la fiche sont utilisées.
                </p>
              )}
              {disponibles.map((c) => {
                const n = fiche.objets.filter((o) => o.categorie === c).length;
                return (
                  <MenuItem key={c} onSelect={() => setChoix({ categorie: c })}>
                    <span className="flex-1">{c}</span>
                    <span className="text-xs text-fg-subtle tnum">{n} au catalogue</span>
                  </MenuItem>
                );
              })}
              <MenuSeparator />
              <MenuItem asChild>
                <Link to={`/parametres/systemes/${fiche.fiche.id}`}>
                  <ExternalLink className="size-4 text-fg-subtle" aria-hidden />
                  Compléter la fiche {systeme.nom} au catalogue
                </Link>
              </MenuItem>
            </MenuContent>
          </Menu>
        )}
      </header>

      <div className="space-y-4 px-4 py-3">
        {etat?.erreur != null && (
          <ErreurChargement
            variante="ligne"
            erreur={etat.erreur}
            titre={`Fiche de ${systeme.nom} au catalogue non chargée`}
            complement="Les objets déjà retenus restent affichés."
            onReessayer={etat.recharger}
            enCours={etat.enCours}
          />
        )}
        {!systeme.catalogueId && (
          <p className="text-sm text-fg-subtle">
            Système ajouté librement : ses objets impactés se choisissent dans la fiche d’un système du catalogue.
          </p>
        )}
        {systeme.catalogueId && categories.length === 0 && (
          <p className="text-sm text-fg-subtle">
            {edition ? 'Ajoutez une catégorie de la fiche, puis cochez ses objets.' : 'Aucun objet retenu.'}
          </p>
        )}
        {categories.length > 0 && (
          <div className="flex flex-wrap gap-2">
            {categories.map((c) => {
              const actif = c.nom === ouverte;
              const Chevron = actif ? ChevronUp : ChevronDown;
              return (
                <button
                  key={c.nom}
                  type="button"
                  aria-expanded={actif}
                  onClick={() => setOuverte(actif ? null : c.nom)}
                  className={cn(
                    'flex min-w-36 items-center justify-between gap-3 rounded-lg border px-3 py-2 text-left transition-colors duration-150',
                    actif ? 'border-accent bg-accent-soft' : 'border-line bg-surface hover:border-line-strong',
                  )}
                >
                  <span>
                    <span className={cn('block text-sm font-semibold', actif && 'text-accent-text')}>{c.nom}</span>
                    <span className="block text-xs text-fg-subtle">
                      {c.objets.length} {nommerCategorie(c.nom, c.objets.length)}
                    </span>
                  </span>
                  <Chevron className="size-4 text-fg-subtle" aria-hidden />
                </button>
              );
            })}
          </div>
        )}

        {detail && (
          <div className="space-y-2 border-t border-line pt-3">
            <div className="flex flex-wrap items-center gap-2">
              <p className="min-w-0 flex-1 text-sm">
                <span className="font-semibold">
                  {systeme.nom} › {detail.nom}
                </span>
                {fiche && (
                  <span className="text-fg-subtle">
                    {' '}
                    · {detail.objets.filter((o) => o.objetCatalogueId).length} sur{' '}
                    {fiche.objets.filter((o) => o.categorie === detail.nom).length} au catalogue
                  </span>
                )}
              </p>
              {edition && fiche && (
                <Button
                  taille="sm"
                  variante="primaire"
                  icone={<ListChecks className="size-4" aria-hidden />}
                  onClick={() => setChoix({ categorie: detail.nom })}
                >
                  Choisir les objets…
                </Button>
              )}
              {edition && (
                <Button
                  taille="sm"
                  variante="fantome"
                  icone={<Trash2 className="size-4" aria-hidden />}
                  onClick={() => setRetrait(detail.nom)}
                >
                  Retirer la catégorie
                </Button>
              )}
            </div>
            <TableObjets objets={detail.objets} edition={edition} />
          </div>
        )}
      </div>

      {fiche && (
        <ChoixObjetsDialog
          open={choix !== null}
          onOpenChange={(o) => {
            if (!o) setChoix(null);
          }}
          systeme={systeme}
          fiche={fiche.fiche}
          objetsFiche={fiche.objets}
          categorieInitiale={choix?.categorie}
        />
      )}
      <ConfirmDialog
        open={retrait !== null}
        onOpenChange={(o) => {
          if (!o) setRetrait(null);
        }}
        titre={`Retirer la catégorie ${retrait ?? ''} de ${systeme.nom} ?`}
        description="Ses objets sont retirés de la MEP. La fiche du système au catalogue ne change pas."
        libelleConfirmer="Retirer"
        variante="danger"
        onConfirmer={async () => {
          if (!retrait) return false;
          const ok = await retirerCategorie(retrait);
          if (ok) setOuverte(null);
          return ok;
        }}
      />
    </section>
  );
}

/** Objets repris sans système (anciens livrables) : ils se retirent un par un. */
function BlocSansSysteme({ objets, edition }: { objets: ObjetImpacte[]; edition: boolean }) {
  return (
    <section className="overflow-hidden rounded-xl border border-line" aria-label="Objets impactés sans système">
      <header className="flex flex-wrap items-center gap-3 border-b border-line bg-surface-2/60 px-4 py-2.5">
        <span className="text-sm font-semibold">Sans système</span>
        <span className="text-sm text-fg-muted">{resumeObjets(parCategorie(objets))}</span>
      </header>
      <div className="px-4 py-3">
        <TableObjets objets={[...objets].sort((a, b) => a.nom.localeCompare(b.nom, 'fr'))} edition={edition} />
      </div>
    </section>
  );
}

function TableObjets({ objets, edition }: { objets: ObjetImpacte[]; edition: boolean }) {
  const { mep, run } = useMep();
  return (
    <div className="overflow-x-auto rounded-lg border border-line">
      <table className="w-full text-sm">
        <thead className="bg-surface-2 text-left text-xs font-semibold tracking-wide text-fg-subtle uppercase">
          <tr>
            <th className="px-3 py-2">Type</th>
            <th className="px-3 py-2">Nom</th>
            <th className="px-3 py-2">Libellé</th>
            <th className="px-3 py-2">Précision</th>
            <th className="px-3 py-2">Version ou correction</th>
            {edition && (
              <th className="w-10 px-3 py-2">
                <span className="sr-only">Actions</span>
              </th>
            )}
          </tr>
        </thead>
        <tbody className="divide-y divide-line">
          {objets.map((o) => (
            <tr key={o.id}>
              <td className="px-3 py-2 text-fg-muted">{o.type || '—'}</td>
              <td className="px-3 py-2">
                <span className="font-mono text-[13px]">{o.nom}</span>
                {o.repris && (
                  <Badge className="ml-2" ton={o.objetCatalogueId ? undefined : 'warn'}>
                    {o.objetCatalogueId ? 'Repris' : 'Repris, hors catalogue'}
                  </Badge>
                )}
              </td>
              <td className="px-3 py-2">{o.libelle}</td>
              <td className="px-3 py-2 text-fg-muted">{o.precision}</td>
              <td className="px-3 py-2">
                {o.version ? (
                  <span className="rounded-md border border-line bg-surface-2 px-1.5 py-0.5 font-mono text-[12px]">
                    {o.version}
                  </span>
                ) : (
                  <span className="text-fg-subtle">—</span>
                )}
              </td>
              {edition && (
                <td className="px-3 py-1">
                  <Tooltip content="Retirer de la MEP">
                    <BoutonIcone
                      label={`Retirer ${o.nom} de la MEP`}
                      taille="sm"
                      action={() => run(() => api.supprimerObjetImpacte(mep.id, o.id), `${o.nom} retiré.`)}
                    >
                      <Trash2 className="size-4" aria-hidden />
                    </BoutonIcone>
                  </Tooltip>
                </td>
              )}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

/* ------------------------------------------------------------------ Import Excel */

/**
 * Import du classeur rempli (modèle ou export) : les lignes connues du catalogue sont ajoutées ou leur version mise à
 * jour ; les autres sont rejetées et listées, avec un lien vers la fiche à compléter. Rien n'est créé au catalogue.
 */
function ImportObjetsDialog({ open, onOpenChange }: { open: boolean; onOpenChange: (o: boolean) => void }) {
  const { mep, run } = useMep();
  const champId = useId();
  const [resultat, setResultat] = useState<ImportObjets | null>(null);
  const [erreur, setErreur] = useState('');
  const [envoi, setEnvoi] = useState(false);
  useEffect(() => {
    if (open) {
      setResultat(null);
      setErreur('');
    }
  }, [open]);

  async function importer(f: File) {
    setEnvoi(true);
    setErreur('');
    setResultat(null);
    await run(
      async () => {
        const r = await api.importerObjets(mep.id, f);
        setResultat(r);
        return r.mep;
      },
      undefined,
      (message) => setErreur(message),
    );
    setEnvoi(false);
  }

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      taille="lg"
      titre="Importer les objets impactés"
      description="Classeur rempli à partir du modèle ou de l’export : chaque ligne désigne un objet de la fiche de son système au catalogue. Rien n’est retiré de la MEP."
      pied={
        <Button variante={resultat ? 'primaire' : 'fantome'} onClick={() => onOpenChange(false)}>
          {resultat ? 'Fermer' : 'Annuler'}
        </Button>
      }
    >
      <div className="space-y-4">
        <div className="flex flex-wrap items-center gap-3">
          <label
            htmlFor={champId}
            className="inline-flex cursor-pointer items-center gap-2 rounded-lg border border-line-strong bg-surface px-3 py-2 text-sm font-medium hover:bg-surface-2"
          >
            <FileSpreadsheet className="size-4" aria-hidden />
            {resultat ? 'Importer un autre fichier' : 'Choisir le fichier Excel'}
          </label>
          <input
            id={champId}
            type="file"
            accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            className="sr-only"
            aria-label="Fichier Excel des objets impactés"
            disabled={envoi}
            onChange={(e) => {
              const f = e.target.files?.[0];
              e.target.value = '';
              if (f) void importer(f);
            }}
          />
          {envoi && <span className="text-sm text-fg-subtle">Import en cours…</span>}
        </div>
        {erreur && (
          <Alerte ton="danger" icone={OctagonAlert} role="alert">
            {erreur}
          </Alerte>
        )}
        {resultat && (
          <div className="space-y-3">
            <p className="text-sm" role="status">
              {resultat.ajoutes} objet{resultat.ajoutes > 1 ? 's' : ''} ajouté{resultat.ajoutes > 1 ? 's' : ''},{' '}
              {resultat.modifies} version{resultat.modifies > 1 ? 's' : ''} mise{resultat.modifies > 1 ? 's' : ''} à
              jour, {resultat.inchanges} déjà à jour.
            </p>
            {resultat.rejetees.length > 0 && (
              <Alerte
                ton="warn"
                icone={OctagonAlert}
                titre={`${resultat.rejetees.length} ligne${resultat.rejetees.length > 1 ? 's' : ''} rejetée${resultat.rejetees.length > 1 ? 's' : ''} : objets inconnus du catalogue, rien n’a été créé.`}
              >
                <ul className="mt-2 space-y-1.5">
                  {resultat.rejetees.map((r) => (
                    <li key={r.ligne} className="text-sm">
                      <span className="font-mono text-xs text-fg-subtle">Ligne {r.ligne}</span>{' '}
                      <strong className="font-semibold">
                        {[r.systeme, r.categorie, r.nom].filter(Boolean).join(' › ') || '—'}
                      </strong>{' '}
                      : {r.raison}
                      {r.systemeCatalogueId && (
                        <>
                          {' '}
                          <Link
                            to={`/parametres/systemes/${r.systemeCatalogueId}`}
                            className="text-accent-text underline"
                          >
                            Compléter la fiche
                          </Link>
                        </>
                      )}
                    </li>
                  ))}
                </ul>
              </Alerte>
            )}
          </div>
        )}
      </div>
    </Dialog>
  );
}
