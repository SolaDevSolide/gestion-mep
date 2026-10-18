import { Save } from 'lucide-react';
import { api } from '../../api';
import { useAuth } from '../../auth';
import { formatMinutes } from '../../format';
import { useDraft, useReportDirty } from '../../hooks';
import { useMep } from '../../mepContext';
import { TextesACopier } from '../../textes/TextesACopier';
import type { Communication, Mep, NiveauRisque } from '../../types';
import { AvisConflit } from '../../ui/AvisConflit';
import { Button } from '../../ui/Button';
import { Checkbox } from '../../ui/Checkbox';
import { Field, Input, Select, Textarea } from '../../ui/Input';
import { Segmented } from '../../ui/Segmented';
import { SectionCarte, vide } from './Formulaires';

/** Heure « HH:MM:SS » du serveur pour un champ heure (« HH:MM »), et inversement. */
const versHeure = (t: string | null) => (t ? t.slice(0, 5) : '');
const depuisHeure = (v: string) => (v ? `${v}:00` : null);

/**
 * Qui modifie la section Communication : les éditeurs en préparation, puis le pilote, ses suppléants ou un administrateur
 * jusqu'à la clôture (preuve de validation, heure limite…).
 */
export function peutModifierCommunication(mep: Mep, admin: boolean): boolean {
  if (mep.phase === 'Cloturee') return false;
  if (mep.phase === 'Preparation') return mep.droits.peutEditerContenu;
  return mep.droits.estPilote || admin;
}

/** Section 8 de la préparation : informations de communication (toutes facultatives) et textes à copier. */
export function CommunicationSection() {
  const { mep, run } = useMep();
  const admin = useAuth().utilisateur?.estAdmin ?? false;
  const d = useDraft<Communication>(mep.communication);
  useReportDirty(d.dirty, 'Communication');
  const v = d.value;
  const edition = peutModifierCommunication(mep, admin);
  const lecture = !edition;
  const pieces = mep.piecesJointes.filter((p) => p.etapeId === null);

  async function enregistrer() {
    if (d.conflit) return;
    const s = {
      ...v,
      valideePar: v.valideeParUtilisateurs ? v.valideePar : '',
      valideeLe: v.valideeParUtilisateurs ? v.valideeLe : null,
      preuveValidationId: v.valideeParUtilisateurs ? v.preuveValidationId : null,
    };
    const ok = await run(
      () => api.modifierCommunication(mep.id, { ...s, version: mep.version }),
      'Communication enregistrée.',
    );
    if (ok) d.reset();
  }

  return (
    <div className="space-y-4">
      <SectionCarte
        numero={8}
        titre="Communication"
        description="Tout est facultatif. Ces informations remplissent les textes à copier (annonce, rappel, demande de MEP, compte rendu)."
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
              {mep.phase !== 'Preparation' && (
                <span className="text-xs text-fg-subtle">
                  Après le Go, le pilote complète encore la communication jusqu’à la clôture.
                </span>
              )}
            </>
          )
        }
      >
        <AvisConflit brouillon={d} className="mb-4" />
        <div className="grid gap-4 md:grid-cols-2">
          <Field label="Référence de la demande" hint="Numéro du ticket ou de la demande de changement.">
            {(id) => (
              <Input
                id={id}
                className="font-mono"
                value={v.referenceDemande}
                disabled={lecture}
                placeholder="ex. CHG-2026-0412"
                onChange={(e) => d.update({ referenceDemande: e.target.value })}
              />
            )}
          </Field>
          <div className="flex min-w-0 flex-col gap-1.5">
            <span className="text-[13px] font-medium">Niveau de risque (analyse d’impact)</span>
            <Segmented<NiveauRisque>
              label="Niveau de risque"
              valeur={v.niveauRisque}
              disabled={lecture}
              onChange={(niveauRisque) => d.update({ niveauRisque })}
              className="self-start"
              options={[
                { valeur: 'NonEvalue', libelle: 'Non évalué' },
                { valeur: 'Faible', libelle: 'Faible' },
                { valeur: 'Modere', libelle: 'Modéré' },
                { valeur: 'Eleve', libelle: 'Élevé' },
              ]}
            />
          </div>
          <Field
            label="Bénéfices et nouveautés"
            hint="Une nouveauté par ligne : elles deviennent une liste à puces dans les textes."
            className="md:col-span-2"
          >
            {(id) => (
              <Textarea
                id={id}
                rows={3}
                value={v.benefices}
                disabled={lecture}
                onChange={(e) => d.update({ benefices: e.target.value })}
              />
            )}
          </Field>
          <Field label="Utilisateurs impactés" className="md:col-span-2">
            {(id) => (
              <Input
                id={id}
                value={v.utilisateursImpactes}
                disabled={lecture}
                placeholder="ex. agences du réseau Sud, service comptabilité"
                onChange={(e) => d.update({ utilisateursImpactes: e.target.value })}
              />
            )}
          </Field>

          <div className="grid gap-4 rounded-lg border border-line p-4 sm:grid-cols-3 md:col-span-2">
            <Field label="Date annoncée" hint="Créneau annoncé aux utilisateurs.">
              {(id) => (
                <Input
                  id={id}
                  type="date"
                  value={v.dateAnnoncee ?? ''}
                  disabled={lecture}
                  onChange={(e) => d.update({ dateAnnoncee: vide(e.target.value) })}
                />
              )}
            </Field>
            <Field label="De">
              {(id) => (
                <Input
                  id={id}
                  type="time"
                  value={versHeure(v.heureDebutAnnoncee)}
                  disabled={lecture}
                  onChange={(e) => d.update({ heureDebutAnnoncee: depuisHeure(e.target.value) })}
                />
              )}
            </Field>
            <Field label="À">
              {(id) => (
                <Input
                  id={id}
                  type="time"
                  value={versHeure(v.heureFinAnnoncee)}
                  disabled={lecture}
                  onChange={(e) => d.update({ heureFinAnnoncee: depuisHeure(e.target.value) })}
                />
              )}
            </Field>
          </div>

          <div className="space-y-3 rounded-lg border border-line p-4 md:col-span-2">
            <Checkbox
              label="Arrêt applicatif nécessaire"
              description={
                <>
                  Les utilisateurs ne peuvent pas travailler pendant l’intervention. La durée est celle de
                  l’indisponibilité prévue (section Intervention) :{' '}
                  {mep.indisponibiliteMinutes ? (
                    <span className="font-medium text-fg">{formatMinutes(mep.indisponibiliteMinutes)}</span>
                  ) : (
                    <span className="font-medium text-warn-fg">non renseignée</span>
                  )}
                  .
                </>
              }
              checked={v.arretApplicatif}
              disabled={lecture}
              onChange={(arretApplicatif) => d.update({ arretApplicatif })}
            />
          </div>

          <div className="grid gap-4 rounded-lg border border-line p-4 md:col-span-2 md:grid-cols-[minmax(0,1fr)_10rem]">
            <Field label="Action demandée aux utilisateurs">
              {(id) => (
                <Input
                  id={id}
                  value={v.actionUtilisateurs}
                  disabled={lecture}
                  placeholder="ex. quitter l’application"
                  onChange={(e) => d.update({ actionUtilisateurs: e.target.value })}
                />
              )}
            </Field>
            <Field label="Heure limite" hint="Le jour de la MEP.">
              {(id) => (
                <Input
                  id={id}
                  type="time"
                  value={versHeure(v.heureLimite)}
                  disabled={lecture}
                  onChange={(e) => d.update({ heureLimite: depuisHeure(e.target.value) })}
                />
              )}
            </Field>
          </div>

          <div className="space-y-3 rounded-lg border border-line p-4 md:col-span-2">
            <Checkbox
              label="Validée par les utilisateurs"
              description="Les utilisateurs ont recetté ou accepté la nouvelle version avant la MEP."
              checked={v.valideeParUtilisateurs}
              disabled={lecture}
              onChange={(valideeParUtilisateurs) => d.update({ valideeParUtilisateurs })}
            />
            {v.valideeParUtilisateurs && (
              <div className="grid gap-4 md:grid-cols-[minmax(0,1fr)_10rem_minmax(0,1fr)]">
                <Field label="Validée par">
                  {(id) => (
                    <Input
                      id={id}
                      value={v.valideePar}
                      disabled={lecture}
                      placeholder="ex. Claire (service Commandes)"
                      onChange={(e) => d.update({ valideePar: e.target.value })}
                    />
                  )}
                </Field>
                <Field label="Le">
                  {(id) => (
                    <Input
                      id={id}
                      type="date"
                      value={v.valideeLe ?? ''}
                      disabled={lecture}
                      onChange={(e) => d.update({ valideeLe: vide(e.target.value) })}
                    />
                  )}
                </Field>
                <Field label="Preuve" hint="Une pièce jointe de la MEP (section 9).">
                  {(id) => (
                    <Select
                      id={id}
                      value={v.preuveValidationId ?? ''}
                      disabled={lecture}
                      onChange={(e) => d.update({ preuveValidationId: vide(e.target.value) })}
                    >
                      <option value="">— Aucune —</option>
                      {pieces.map((p) => (
                        <option key={p.id} value={p.id}>
                          {p.nomFichier}
                        </option>
                      ))}
                    </Select>
                  )}
                </Field>
              </div>
            )}
          </div>
        </div>
      </SectionCarte>
      <TextesACopier mep={mep} />
    </div>
  );
}
