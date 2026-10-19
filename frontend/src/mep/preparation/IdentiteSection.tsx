import { Save } from 'lucide-react';
import { useContext, useState } from 'react';
import { api } from '../../api';
import { AuthContext } from '../../auth';
import { formatDateHeure } from '../../format';
import { useDraft, useReportDirty } from '../../hooks';
import { useMep } from '../../mepContext';
import type { MepIdentiteSaisie } from '../../types';
import { AvisConflit } from '../../ui/AvisConflit';
import { Button } from '../../ui/Button';
import { Checkbox } from '../../ui/Checkbox';
import { MultiSelect } from '../../ui/Combobox';
import { ConfirmDialog } from '../../ui/Dialog';
import { Field, Input, Select, Textarea } from '../../ui/Input';
import { Avatar } from '../../ui/Avatar';
import { BoutonModifierEcheance, texteEcheance } from '../EcheanceRepli';
import { estSimplifiee } from '../typeMep';
import { SectionCarte, vide } from './Formulaires';
import { TypeMepEncadre } from './TypeMepEncadre';

export function IdentiteSection() {
  const { mep, run } = useMep();
  const source: Omit<MepIdentiteSaisie, 'version'> = {
    outil: mep.outil,
    versionActuelle: mep.versionActuelle,
    versionCible: mep.versionCible,
    objet: mep.objet,
    impacts: mep.impacts,
    fuseauHoraire: mep.fuseauHoraire,
    indisponibiliteMinutes: mep.indisponibiliteMinutes,
    piloteId: mep.piloteId,
    suppleantIds: mep.suppleantIds,
    bilanActive: mep.bilanActive,
    retourArriereActive: mep.retourArriereActive,
  };
  const d = useDraft(source);
  useReportDirty(d.dirty, 'Intervention');
  const v = d.value;
  const [confirmerDesactivation, setConfirmerDesactivation] = useState(false);
  const retourArriere = v.retourArriereActive ?? true;

  const edition = mep.droits.peutEditerContenu;
  // Un modèle ne s'exécute pas : ni pilote, ni échéance, ni dates (ils se choisissent dans chaque MEP créée depuis le modèle).
  const modele = mep.estModele;
  const lecture = !edition;
  // Pilote et suppléants : le pilote actuel, un suppléant ou un administrateur (tout éditeur tant qu'aucun pilote n'est désigné).
  const admin = useContext(AuthContext)?.utilisateur?.estAdmin ?? false;
  const pilotage = edition && (mep.piloteId === null || mep.droits.estPilote || admin);
  // MEP simplifiée : ni versions, et les suppléants sont toutes les autres personnes de la MEP.
  const simplifiee = estSimplifiee(mep);
  const autres = mep.participants.filter((p) => p.id !== v.piloteId);

  const personnes = (valeur: string | null, set: (x: string | null) => void) => (id: string) => (
    <Select id={id} value={valeur ?? ''} disabled={!pilotage} onChange={(e) => set(vide(e.target.value))}>
      <option value="">— choisir —</option>
      {mep.participants.map((p) => (
        <option key={p.id} value={p.id}>
          {p.nom}
        </option>
      ))}
    </Select>
  );

  async function enregistrer() {
    if (d.conflit) return;
    const ok = await run(
      () => api.modifierIdentite(mep.id, { ...v, version: mep.version }),
      'Intervention enregistrée.',
    );
    if (ok) d.reset();
  }

  return (
    <SectionCarte
      numero={1}
      titre="Intervention"
      pied={
        edition && (
          <>
            <Button
              variante="primaire"
              icone={<Save className="size-4" aria-hidden />}
              disabled={!d.dirty || d.conflit}
              action={enregistrer}
            >
              Enregistrer
            </Button>
            {d.dirty && (
              <Button variante="fantome" onClick={d.reset}>
                Annuler les modifications
              </Button>
            )}
          </>
        )
      }
    >
      <AvisConflit brouillon={d} className="mb-4" />
      <div className="grid gap-4 md:grid-cols-2">
        {!modele && <TypeMepEncadre peutChanger={pilotage} brouillonModifie={d.dirty} />}
        <Field label="Outil">
          {(id) => (
            <Input id={id} value={v.outil} disabled={lecture} onChange={(e) => d.update({ outil: e.target.value })} />
          )}
        </Field>
        <Field label="Objet">
          {(id) => (
            <Input id={id} value={v.objet} disabled={lecture} onChange={(e) => d.update({ objet: e.target.value })} />
          )}
        </Field>
        {!simplifiee && (
          <>
            <Field label="Version actuelle">
              {(id) => (
                <Input
                  id={id}
                  className="font-mono"
                  value={v.versionActuelle}
                  disabled={lecture}
                  onChange={(e) => d.update({ versionActuelle: e.target.value })}
                />
              )}
            </Field>
            <Field label="Version cible">
              {(id) => (
                <Input
                  id={id}
                  className="font-mono"
                  value={v.versionCible}
                  disabled={lecture}
                  onChange={(e) => d.update({ versionCible: e.target.value })}
                />
              )}
            </Field>
          </>
        )}
        {!modele && (
          <div className="grid grid-cols-2 gap-4 text-sm" aria-label="Dates constatées">
            <p>
              <span className="block text-[13px] font-medium">Démarrée le</span>
              <span className="text-fg-muted">
                {mep.demarrageAutorise ? formatDateHeure(mep.demarrageAutorise.le) : '—'}
              </span>
            </p>
            <p>
              <span className="block text-[13px] font-medium">Terminée le</span>
              <span className="text-fg-muted">{mep.cloture ? formatDateHeure(mep.cloture.le) : '—'}</span>
            </p>
          </div>
        )}
        <Field label="Fuseau horaire" hint="Identifiant IANA, par exemple Europe/Paris ou America/New_York.">
          {(id) => (
            <Input
              id={id}
              value={v.fuseauHoraire}
              disabled={lecture}
              onChange={(e) => d.update({ fuseauHoraire: e.target.value })}
            />
          )}
        </Field>
        <Field label="Indisponibilité prévue (minutes)">
          {(id) => (
            <Input
              id={id}
              type="number"
              min={0}
              value={v.indisponibiliteMinutes ?? ''}
              disabled={lecture}
              onChange={(e) =>
                d.update({ indisponibiliteMinutes: e.target.value === '' ? null : Number(e.target.value) })
              }
            />
          )}
        </Field>
        {!modele && (
          <>
            <Field label="Pilote">
              {personnes(v.piloteId, (x) =>
                d.update({ piloteId: x, suppleantIds: v.suppleantIds.filter((s) => s !== x) }),
              )}
            </Field>
            {simplifiee ? (
              <div className="text-sm" aria-label="Suppléants">
                <p className="mb-1.5 text-[13px] font-medium">Suppléants</p>
                <div className="flex min-h-9 flex-wrap items-center gap-2">
                  {v.piloteId ? (
                    <>
                      {autres.length > 0 && (
                        <span className="flex -space-x-1">
                          {autres.slice(0, 6).map((p) => (
                            <Avatar key={p.id} nom={p.nom} taille="sm" className="ring-2 ring-surface" />
                          ))}
                        </span>
                      )}
                      <span className="text-fg-muted">Toutes les autres personnes de la MEP ({autres.length})</span>
                    </>
                  ) : (
                    <span className="text-fg-muted">
                      Toutes les personnes de la MEP sauf le pilote, dès qu’il est désigné.
                    </span>
                  )}
                </div>
                <p className="mt-1 text-xs text-fg-subtle">
                  MEP simplifiée : elles ont les mêmes droits que le pilote.
                </p>
              </div>
            ) : (
              <Field
                label="Suppléants"
                hint={
                  pilotage || lecture
                    ? 'Ils ont les mêmes droits que le pilote.'
                    : 'Seul le pilote, un suppléant ou un administrateur peut changer le pilote et les suppléants.'
                }
              >
                {(id) => (
                  <MultiSelect
                    id={id}
                    options={mep.participants
                      .filter((p) => p.id !== v.piloteId)
                      .map((p) => ({ valeur: p.id, libelle: p.nom, detail: p.role }))}
                    valeurs={v.suppleantIds}
                    onChange={(ids) => d.update({ suppleantIds: ids })}
                    placeholder="Aucun suppléant"
                    placeholderRecherche="Rechercher une personne…"
                    vide="Aucune autre personne : ajoutez-en dans « Personnes »."
                    disabled={!pilotage}
                    libelleInconnu="Personne supprimée"
                  />
                )}
              </Field>
            )}
          </>
        )}
        <div className="space-y-3 rounded-lg border border-line p-4 md:col-span-2">
          <Checkbox
            label="Plan de retour arrière"
            description="Décochez pour une MEP sans retour arrière possible ou utile (préproduction réinstallable par exemple)."
            checked={retourArriere}
            disabled={lecture}
            onChange={(c) => (c ? d.update({ retourArriereActive: true }) : setConfirmerDesactivation(true))}
          />
          {retourArriere && !modele && (
            <div className="text-sm" aria-label="Échéance de retour arrière">
              <p className="mb-1.5 text-[13px] font-medium">Échéance de retour arrière (facultative)</p>
              <div className="flex flex-wrap items-center gap-3">
                <span className={mep.echeanceRepli ? 'font-mono tnum' : 'text-fg-muted'}>
                  {texteEcheance(mep.echeanceRepli, mep.fuseauHoraire)}
                </span>
                <BoutonModifierEcheance />
              </div>
              <p className="mt-1 text-xs text-fg-subtle">
                Heure du fuseau de la MEP. Atteinte en exécution, elle déclenche le retour arrière ; en recette, elle
                est seulement signalée et le pilote décide. Chaque changement est tracé au journal avec son motif.
              </p>
            </div>
          )}
        </div>
        <ConfirmDialog
          open={confirmerDesactivation}
          onOpenChange={setConfirmerDesactivation}
          titre="Désactiver le plan de retour arrière ?"
          description="Adapté aux environnements où l’on peut revenir en arrière à tout moment, comme la préproduction."
          libelleConfirmer="Désactiver"
          variante="danger"
          onConfirmer={() => d.update({ retourArriereActive: false })}
        >
          <ul className="list-disc space-y-1.5 pl-5 text-sm text-fg-muted">
            <li>
              L’onglet Retour arrière est masqué. Les étapes déjà saisies sont gardées et reviennent si vous le
              réactivez.
            </li>
            <li>« Vérifier le plan » ne réclame plus de plan ni d’échéance de retour arrière pour le Go.</li>
            <li>Aucun déclenchement automatique du retour arrière pendant l’exécution.</li>
          </ul>
        </ConfirmDialog>
        <Field label="Impacts attendus" className="md:col-span-2">
          {(id) => (
            <Textarea
              id={id}
              rows={3}
              value={v.impacts}
              disabled={lecture}
              onChange={(e) => d.update({ impacts: e.target.value })}
            />
          )}
        </Field>
        <div className="md:col-span-2">
          <Checkbox
            label="Bilan de clôture"
            checked={v.bilanActive ?? true}
            disabled={lecture}
            onChange={(c) => d.update({ bilanActive: c })}
          />
        </div>
      </div>
    </SectionCarte>
  );
}
