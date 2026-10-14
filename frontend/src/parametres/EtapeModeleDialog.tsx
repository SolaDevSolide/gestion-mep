import { OctagonAlert } from 'lucide-react';
import { useEffect, useState } from 'react';
import { api } from '../api';
import { TYPES_ETAPE } from '../bibliotheque';
import { couleurSysteme, styleSysteme } from '../couleursSysteme';
import { LIBELLE_CRITICITE, LIBELLE_FIN } from '../format';
import { SelecteurResolutions } from '../mep/graphe/SelecteurResolutions';
import type {
  Criticite,
  FinEtape,
  ModeleEtape,
  ModeleEtapeSaisie,
  ResolutionType,
  SystemeCatalogue,
  TypeEtape,
} from '../types';
import { Alerte } from '../ui/Alerte';
import { Button } from '../ui/Button';
import { cn } from '../ui/cn';
import { Dialog } from '../ui/Dialog';
import { EditeurBlocs, ZoneAuto } from '../ui/EditeurBlocs';
import { Field, Input, Select, Textarea } from '../ui/Input';
import { Segmented } from '../ui/Segmented';
import { useToast } from '../ui/Toast';

/** Ouverture de l'éditeur : création (système proposé), modification, ou copie d'une étape modèle existante. */
export type EditionModele =
  | { mode: 'creation'; systemeId?: string }
  | { mode: 'modification'; modele: ModeleEtape }
  | { mode: 'copie'; modele: ModeleEtape };

interface Brouillon {
  action: string;
  systemeCatalogueIds: string[];
  type: TypeEtape;
  criticite: Criticite | '';
  duree: string;
  instructions: string;
  resultatAttendu: string;
  fin: FinEtape;
  sauvegardeEmplacement: string;
  sauvegardeMethodeRecuperation: string;
  resolutionIds: string[];
}

function brouillon(e: EditionModele | null): Brouillon {
  const m = e && e.mode !== 'creation' ? e.modele : null;
  return {
    action: m ? (e?.mode === 'copie' ? `${m.action} (copie)` : m.action) : '',
    systemeCatalogueIds: m?.systemeCatalogueIds ?? (e?.mode === 'creation' && e.systemeId ? [e.systemeId] : []),
    type: m?.type ?? 'Standard',
    criticite: m ? (m.criticite ?? '') : 'Moderee',
    duree: m?.dureeEstimeeMinutes == null ? '' : String(m.dureeEstimeeMinutes),
    instructions: m?.instructions ?? '',
    resultatAttendu: m?.resultatAttendu ?? '',
    fin: m?.fin ?? 'TestUnitaire',
    sauvegardeEmplacement: m?.sauvegardeEmplacement ?? '',
    sauvegardeMethodeRecuperation: m?.sauvegardeMethodeRecuperation ?? '',
    resolutionIds: m?.resolutionIds ?? [],
  };
}

/**
 * Création, modification ou copie d'une étape modèle : nom, systèmes du catalogue, type (et champs de sauvegarde),
 * criticité, durée, fin, instructions, résultat attendu et résolutions. Jamais de point de non-retour.
 */
export function EtapeModeleDialog({
  edition,
  catalogue,
  resolutions,
  onFermer,
  onEnregistre,
}: {
  edition: EditionModele | null;
  catalogue: SystemeCatalogue[];
  resolutions: ResolutionType[];
  onFermer: () => void;
  onEnregistre: (m: ModeleEtape) => void;
}) {
  const toast = useToast();
  const [v, setV] = useState<Brouillon>(() => brouillon(edition));
  const [erreur, setErreur] = useState('');
  const [envoi, setEnvoi] = useState(false);
  const maj = (patch: Partial<Brouillon>) => setV((x) => ({ ...x, ...patch }));

  useEffect(() => {
    if (!edition) return;
    setV(brouillon(edition));
    setErreur('');
  }, [edition]);

  const existant = edition?.mode === 'modification' ? edition.modele : null;
  const basculer = (id: string) =>
    maj({
      systemeCatalogueIds: v.systemeCatalogueIds.includes(id)
        ? v.systemeCatalogueIds.filter((x) => x !== id)
        : catalogue.filter((s) => s.id === id || v.systemeCatalogueIds.includes(s.id)).map((s) => s.id),
    });

  async function enregistrer() {
    setErreur('');
    if (v.systemeCatalogueIds.length === 0) {
      setErreur('Choisissez au moins un système du catalogue.');
      return;
    }
    const duree = v.duree.trim() === '' ? null : Math.max(0, Math.round(Number(v.duree)));
    const saisie: Omit<ModeleEtapeSaisie, 'version'> = {
      action: v.action.trim(),
      systemeCatalogueIds: v.systemeCatalogueIds,
      type: v.type,
      criticite: v.criticite || null,
      dureeEstimeeMinutes: duree !== null && Number.isFinite(duree) ? duree : null,
      instructions: v.instructions,
      resultatAttendu: v.resultatAttendu.trim(),
      fin: v.fin,
      sauvegardeEmplacement: v.type === 'Sauvegarde' ? v.sauvegardeEmplacement.trim() : '',
      sauvegardeMethodeRecuperation: v.type === 'Sauvegarde' ? v.sauvegardeMethodeRecuperation.trim() : '',
      resolutionIds: v.resolutionIds,
    };
    setEnvoi(true);
    try {
      const m = existant
        ? await api.modifierEtapeModele(existant.id, { ...saisie, version: existant.version })
        : await api.creerEtapeModele(saisie);
      toast.succes(existant ? 'Étape modèle enregistrée.' : 'Étape modèle ajoutée à la bibliothèque.');
      onEnregistre(m);
      onFermer();
    } catch (e) {
      setErreur(e instanceof Error ? e.message : 'Enregistrement impossible.');
    } finally {
      setEnvoi(false);
    }
  }

  const titre =
    edition?.mode === 'modification'
      ? `Modifier « ${edition.modele.action} »`
      : edition?.mode === 'copie'
        ? `Dupliquer « ${edition.modele.action} »`
        : 'Nouvelle étape modèle';

  return (
    <Dialog
      open={edition !== null}
      onOpenChange={(o) => {
        if (!o) onFermer();
      }}
      titre={titre}
      description="Les étapes déjà insérées dans des MEP sont des copies : elles ne changent pas. Une étape modèle n’a jamais de point de non-retour."
      taille="xl"
      pied={
        <>
          <Button variante="fantome" onClick={onFermer}>
            Annuler
          </Button>
          <Button variante="primaire" chargement={envoi} onClick={() => void enregistrer()}>
            Enregistrer
          </Button>
        </>
      }
    >
      <div className="grid gap-5 lg:grid-cols-[minmax(0,18rem)_minmax(0,1fr)]">
        <div className="grid content-start gap-4">
          <Field label="Nom de l’étape modèle *">
            {(id) => (
              <Input
                id={id}
                value={v.action}
                onChange={(e) => maj({ action: e.target.value })}
                placeholder="ex. Sauvegarde complète de la base"
              />
            )}
          </Field>
          <fieldset className="grid gap-1.5">
            <legend className="mb-1.5 text-[13px] font-medium">Systèmes du catalogue *</legend>
            <div className="flex flex-wrap gap-1.5">
              {catalogue
                .filter((s) => s.actif || v.systemeCatalogueIds.includes(s.id))
                .map((s) => {
                  const actif = v.systemeCatalogueIds.includes(s.id);
                  return (
                    <button
                      key={s.id}
                      type="button"
                      aria-pressed={actif}
                      onClick={() => basculer(s.id)}
                      className={cn(
                        'inline-flex items-center gap-1.5 rounded-full border-[1.5px] px-2.5 py-0.5 text-[13px] font-semibold transition-colors',
                        !actif && 'border-line-strong text-fg-subtle hover:text-fg',
                      )}
                      style={actif ? styleSysteme(s.nom) : undefined}
                    >
                      <i className="size-2 rounded-full" style={{ background: couleurSysteme(s.nom) }} aria-hidden />
                      {s.nom}
                    </button>
                  );
                })}
            </div>
          </fieldset>
          <Field label="Type d’étape">
            {(id) => (
              <Select id={id} value={v.type} onChange={(e) => maj({ type: e.target.value as TypeEtape })}>
                {TYPES_ETAPE.map((t) => (
                  <option key={t.valeur} value={t.valeur}>
                    {t.libelle}
                  </option>
                ))}
              </Select>
            )}
          </Field>
          {v.type === 'Sauvegarde' && (
            <>
              <Field label="Emplacement de la sauvegarde" hint="Repris dans chaque copie, à adapter à la MEP.">
                {(id) => (
                  <Input
                    id={id}
                    value={v.sauvegardeEmplacement}
                    onChange={(e) => maj({ sauvegardeEmplacement: e.target.value })}
                  />
                )}
              </Field>
              <Field label="Méthode de récupération">
                {(id) => (
                  <Textarea
                    id={id}
                    rows={2}
                    value={v.sauvegardeMethodeRecuperation}
                    onChange={(e) => maj({ sauvegardeMethodeRecuperation: e.target.value })}
                  />
                )}
              </Field>
            </>
          )}
          <div className="grid grid-cols-2 gap-3">
            <Field label="Criticité">
              {(id) => (
                <Select
                  id={id}
                  value={v.criticite}
                  onChange={(e) => maj({ criticite: e.target.value as Criticite | '' })}
                >
                  <option value="">—</option>
                  {(Object.keys(LIBELLE_CRITICITE) as Criticite[]).map((c) => (
                    <option key={c} value={c}>
                      {LIBELLE_CRITICITE[c]}
                    </option>
                  ))}
                </Select>
              )}
            </Field>
            <Field label="Durée (min)">
              {(id) => (
                <Input
                  id={id}
                  type="number"
                  min={0}
                  inputMode="numeric"
                  placeholder="facultatif"
                  value={v.duree}
                  onChange={(e) => maj({ duree: e.target.value })}
                />
              )}
            </Field>
          </div>
          <div className="grid gap-1.5">
            <span className="text-[13px] font-medium">Fin de l’étape</span>
            <Segmented
              label="Fin de l’étape"
              valeur={v.fin}
              onChange={(fin) => maj({ fin })}
              options={[
                { valeur: 'TestUnitaire', libelle: LIBELLE_FIN.TestUnitaire },
                { valeur: 'Verification', libelle: LIBELLE_FIN.Verification },
              ]}
            />
          </div>
        </div>
        <div className="grid min-w-0 content-start gap-4">
          <div className="grid gap-1.5">
            <span className="text-[13px] font-medium">Instructions exactes</span>
            <EditeurBlocs label="Instructions" valeur={v.instructions} onChange={(md) => maj({ instructions: md })} />
          </div>
          <div className="grid gap-1.5">
            <span className="text-[13px] font-medium">Résultat attendu</span>
            <ZoneAuto
              aria-label="Résultat attendu"
              value={v.resultatAttendu}
              onChange={(e) => maj({ resultatAttendu: e.target.value })}
              placeholder="ex. `GET /sante` renvoie 200 avec la nouvelle version."
              className="border border-line-strong bg-surface text-fg placeholder:text-fg-subtle"
            />
          </div>
          <div className="grid gap-1.5">
            <span className="text-[13px] font-medium">Résolutions en cas d’échec ({v.resolutionIds.length})</span>
            <SelecteurResolutions
              resolutions={resolutions}
              catalogue={catalogue}
              liees={v.resolutionIds}
              onChange={(ids) => maj({ resolutionIds: ids })}
              systemesEtape={v.systemeCatalogueIds}
            />
          </div>
        </div>
      </div>
      {erreur && (
        <Alerte ton="danger" icone={OctagonAlert} role="alert" className="mt-4">
          {erreur}
        </Alerte>
      )}
    </Dialog>
  );
}
