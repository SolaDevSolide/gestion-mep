import { Search } from 'lucide-react';
import { useEffect, useId, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../../api';
import { useMep } from '../../mepContext';
import type { ObjetCatalogue, Systeme, SystemeCatalogue } from '../../types';
import { Button } from '../../ui/Button';
import { CaseSeule } from '../../ui/Checkbox';
import { cn } from '../../ui/cn';
import { Dialog } from '../../ui/Dialog';
import { Input, Select } from '../../ui/Input';
import { Segmented } from '../../ui/Segmented';
import { correspond } from './objets';

/** Objets cochés : identifiant de l'objet du catalogue → version ou correction. */
type Selection = Map<string, string>;

/**
 * Choix outillé des objets impactés d'un système, parmi ceux de sa fiche au catalogue : catégories à gauche (cochés /
 * total), recherche multi-termes, filtre par type, bascule Tous / Cochés ; la case d'en-tête coche les objets affichés,
 * une même version s'applique aux cochés et reste modifiable ligne par ligne. Aucun objet ne se crée ici : il s'ajoute
 * à la fiche du système au catalogue.
 */
export function ChoixObjetsDialog({
  open,
  onOpenChange,
  systeme,
  fiche,
  objetsFiche,
  categorieInitiale,
}: {
  open: boolean;
  onOpenChange: (o: boolean) => void;
  systeme: Systeme;
  fiche: SystemeCatalogue;
  objetsFiche: ObjetCatalogue[];
  categorieInitiale?: string;
}) {
  const { mep, run } = useMep();
  const [selection, setSelection] = useState<Selection>(new Map());
  const [categorie, setCategorie] = useState('');
  const [recherche, setRecherche] = useState('');
  const [type, setType] = useState('');
  const [mode, setMode] = useState<'tous' | 'coches'>('tous');
  const [versionGroupe, setVersionGroupe] = useState('');
  const [envoi, setEnvoi] = useState(false);
  const idVersion = useId();

  const actuels = useMemo(
    () => mep.objetsImpactes.filter((o) => o.systemeId === systeme.id && o.objetCatalogueId),
    [mep.objetsImpactes, systeme.id],
  );
  useEffect(() => {
    if (!open) return;
    setSelection(new Map(actuels.map((o) => [o.objetCatalogueId!, o.version])));
    setCategorie(categorieInitiale ?? fiche.categories[0] ?? '');
    setRecherche('');
    setType('');
    setMode('tous');
    setVersionGroupe('');
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  const dansCategorie = objetsFiche.filter((o) => o.categorie === categorie);
  const types = [...new Set(dansCategorie.map((o) => o.type).filter(Boolean))].sort((a, b) => a.localeCompare(b, 'fr'));
  const cochesCategorie = dansCategorie.filter((o) => selection.has(o.id));
  const affiches = dansCategorie.filter(
    (o) => correspond(o, recherche) && (!type || o.type === type) && (mode === 'tous' || selection.has(o.id)),
  );
  const nbAffichesCoches = affiches.filter((o) => selection.has(o.id)).length;
  const etatEntete: boolean | 'indetermine' =
    affiches.length > 0 && nbAffichesCoches === affiches.length ? true : nbAffichesCoches > 0 ? 'indetermine' : false;

  const modifier = (f: (s: Selection) => void) =>
    setSelection((avant) => {
      const s = new Map(avant);
      f(s);
      return s;
    });
  const cocher = (o: ObjetCatalogue, c: boolean) =>
    modifier((s) => (c ? s.set(o.id, s.get(o.id) ?? versionGroupe.trim()) : s.delete(o.id)));
  const cocherAffiches = (c: boolean) =>
    modifier((s) => affiches.forEach((o) => (c ? s.set(o.id, s.get(o.id) ?? versionGroupe.trim()) : s.delete(o.id))));
  const appliquerVersion = () => modifier((s) => cochesCategorie.forEach((o) => s.set(o.id, versionGroupe.trim())));
  const toutDecocher = () => modifier((s) => dansCategorie.forEach((o) => s.delete(o.id)));

  // Un objet retenu puis retiré de la fiche reste dans la MEP : il n'apparaît pas ici mais n'est pas retiré.
  const horsFiche = actuels.filter((o) => !objetsFiche.some((x) => x.id === o.objetCatalogueId));
  const total = selection.size;

  async function enregistrer() {
    setEnvoi(true);
    const objets = [
      ...objetsFiche
        .filter((o) => selection.has(o.id))
        .map((o) => ({ objetCatalogueId: o.id, version: selection.get(o.id)!.trim() })),
      ...horsFiche.map((o) => ({ objetCatalogueId: o.objetCatalogueId!, version: o.version })),
    ];
    const ok = await run(
      () => api.objetsSysteme(mep.id, systeme.id, { objets }),
      `Objets impactés de ${systeme.nom} enregistrés.`,
    );
    setEnvoi(false);
    if (ok) onOpenChange(false);
  }

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      taille="xl"
      titre={`Objets impactés de ${systeme.nom}`}
      description={`Cochez les objets que la MEP modifie, parmi ceux de la fiche ${fiche.nom} au catalogue.`}
      pied={
        <>
          <p className="mr-auto text-sm text-fg-muted">
            Un objet manque ?{' '}
            <Link to={`/parametres/systemes/${fiche.id}`} className="font-medium text-accent-text hover:underline">
              Ajoutez-le à la fiche {fiche.nom} du catalogue
            </Link>
          </p>
          <Button variante="fantome" onClick={() => onOpenChange(false)}>
            Annuler
          </Button>
          <Button variante="primaire" chargement={envoi} onClick={() => void enregistrer()}>
            Enregistrer ({total} objet{total > 1 ? 's' : ''})
          </Button>
        </>
      }
    >
      <div className="grid gap-5 md:grid-cols-[13rem_minmax(0,1fr)]">
        <nav aria-label="Catégories de la fiche">
          <p className="mb-2 text-xs font-semibold tracking-wide text-fg-subtle uppercase">Catégories</p>
          {fiche.categories.length === 0 ? (
            <p className="text-sm text-fg-subtle">La fiche n’a encore aucune catégorie.</p>
          ) : (
            <ul className="space-y-0.5">
              {fiche.categories.map((c) => {
                const liste = objetsFiche.filter((o) => o.categorie === c);
                const n = liste.filter((o) => selection.has(o.id)).length;
                const actif = c === categorie;
                return (
                  <li key={c}>
                    <button
                      type="button"
                      aria-current={actif ? 'true' : undefined}
                      onClick={() => {
                        setCategorie(c);
                        setType('');
                      }}
                      className={cn(
                        'flex w-full items-center justify-between gap-2 rounded-lg px-2.5 py-2 text-left text-sm transition-colors duration-150',
                        actif
                          ? 'bg-accent-soft font-semibold text-accent-text'
                          : 'text-fg-muted hover:bg-surface-2 hover:text-fg',
                      )}
                    >
                      <span className="min-w-0 truncate">{c}</span>
                      <span
                        className={cn(
                          'shrink-0 rounded-full px-1.5 font-mono text-[11px] tnum',
                          n > 0 ? 'bg-accent text-accent-fg' : 'bg-surface-2 text-fg-subtle',
                        )}
                        aria-label={`${n} coché${n > 1 ? 's' : ''} sur ${liste.length}`}
                      >
                        {n}/{liste.length}
                      </span>
                    </button>
                  </li>
                );
              })}
            </ul>
          )}
          <p className="mt-3 px-2.5 text-xs text-fg-subtle">
            Une catégorie sans objet coché n’apparaît pas dans la MEP.
          </p>
        </nav>

        <div className="min-w-0 space-y-3">
          <div className="flex flex-wrap items-center gap-2">
            <div className="relative min-w-48 flex-1">
              <Search
                className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-fg-subtle"
                aria-hidden
              />
              <Input
                value={recherche}
                onChange={(e) => setRecherche(e.target.value)}
                placeholder="Rechercher : nom, libellé, type, précision…"
                aria-label="Rechercher un objet"
                className="pl-9"
              />
            </div>
            <Select
              value={type}
              onChange={(e) => setType(e.target.value)}
              aria-label="Filtrer par type"
              className="w-44"
            >
              <option value="">Tous les types</option>
              {types.map((t) => (
                <option key={t} value={t}>
                  {t}
                </option>
              ))}
            </Select>
            <Segmented
              label="Objets affichés"
              valeur={mode}
              onChange={setMode}
              options={[
                { valeur: 'tous', libelle: `Tous (${dansCategorie.length})` },
                { valeur: 'coches', libelle: `Cochés (${cochesCategorie.length})` },
              ]}
            />
          </div>

          <div className="flex flex-wrap items-center gap-x-3 gap-y-2 rounded-lg border border-accent/40 bg-accent-soft/40 px-3 py-2 text-sm">
            <span className="font-medium tnum">
              {cochesCategorie.length} coché{cochesCategorie.length > 1 ? 's' : ''} sur {dansCategorie.length}
            </span>
            <span className="hidden h-4 w-px bg-line sm:block" aria-hidden />
            <span className="flex flex-wrap items-center gap-2 text-fg-muted">
              <label htmlFor={idVersion}>Version ou correction pour les cochés</label>
              <Input
                id={idVersion}
                value={versionGroupe}
                onChange={(e) => setVersionGroupe(e.target.value)}
                className="h-8 w-36 font-mono text-[13px]"
              />
            </span>
            <Button taille="sm" disabled={cochesCategorie.length === 0} onClick={appliquerVersion}>
              Appliquer
            </Button>
            <Button
              taille="sm"
              variante="fantome"
              className="ml-auto"
              disabled={cochesCategorie.length === 0}
              onClick={toutDecocher}
            >
              Tout décocher
            </Button>
          </div>

          <div className="max-h-[50vh] overflow-auto rounded-lg border border-line">
            <table className="w-full text-sm">
              <thead className="sticky top-0 z-10 bg-surface-2 text-left text-xs font-semibold tracking-wide text-fg-subtle uppercase">
                <tr>
                  <th className="w-10 px-3 py-2">
                    <CaseSeule
                      checked={etatEntete}
                      disabled={affiches.length === 0}
                      onChange={() => cocherAffiches(etatEntete !== true)}
                      label="Cocher les objets affichés"
                    />
                  </th>
                  <th className="px-3 py-2">Nom</th>
                  <th className="px-3 py-2">Libellé</th>
                  <th className="px-3 py-2">Type</th>
                  <th className="px-3 py-2">Précision</th>
                  <th className="px-3 py-2">Version ou correction</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-line">
                {affiches.length === 0 && (
                  <tr>
                    <td colSpan={6} className="px-3 py-6 text-center text-fg-subtle">
                      {dansCategorie.length === 0
                        ? 'Aucun objet dans cette catégorie de la fiche.'
                        : 'Aucun objet ne correspond.'}
                    </td>
                  </tr>
                )}
                {affiches.map((o) => {
                  const coche = selection.has(o.id);
                  return (
                    <tr key={o.id} className={cn(coche && 'bg-accent-soft/30')}>
                      <td className="px-3 py-1.5">
                        <CaseSeule checked={coche} onChange={(c) => cocher(o, c)} label={`Cocher ${o.nom}`} />
                      </td>
                      <td className="px-3 py-1.5 font-mono text-[13px]">{o.nom}</td>
                      <td className="px-3 py-1.5">{o.libelle}</td>
                      <td className="px-3 py-1.5 text-fg-muted">{o.type}</td>
                      <td className="px-3 py-1.5 text-fg-muted">{o.precision}</td>
                      <td className="px-3 py-1.5">
                        <Input
                          value={selection.get(o.id) ?? ''}
                          disabled={!coche}
                          aria-label={`Version ou correction de ${o.nom}`}
                          onChange={(e) => modifier((s) => s.set(o.id, e.target.value))}
                          className="h-8 font-mono text-[13px]"
                        />
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
          <p className="text-xs text-fg-subtle tnum">
            {affiches.length} affiché{affiches.length > 1 ? 's' : ''} sur {dansCategorie.length} · la case d’en-tête
            coche ou décoche les objets affichés (après recherche ou filtre).
          </p>
        </div>
      </div>
    </Dialog>
  );
}
