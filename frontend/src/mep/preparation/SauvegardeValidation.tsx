import { DatabaseBackup, Save, ShieldCheck, TriangleAlert } from 'lucide-react';
import { api } from '../../api';
import { depuisInputDateTime, formatDateHeure, versInputDateTime } from '../../format';
import { useDraft, useReportDirty } from '../../hooks';
import { couleurSysteme, styleSysteme } from '../../couleursSysteme';
import { useMep } from '../../mepContext';
import type { ModeSauvegarde, StatutSauvegarde, Systeme } from '../../types';
import { Alerte } from '../../ui/Alerte';
import { AvisConflit } from '../../ui/AvisConflit';
import { Button } from '../../ui/Button';
import { Checkbox } from '../../ui/Checkbox';
import { EmptyState } from '../../ui/EmptyState';
import { Field, Input, Select, Textarea } from '../../ui/Input';
import { Segmented } from '../../ui/Segmented';
import { BoutonGoNoGo } from '../ActionsPilote';
import { ChampBlocs } from '../ChampBlocs';
import { ApprobationsBloc } from './Approbations';
import { SectionCarte, vide } from './Formulaires';
import { estTerminee } from '../../domaine';

/**
 * Ligne de sauvegarde d'un système : requise (moment, contenu, emplacement et méthode de récupération, ces deux derniers
 * réclamés par « Vérifier le plan ») ou non requise (justification obligatoire).
 */
function LigneSauvegardeSysteme({ systeme }: { systeme: Systeme }) {
  const { mep, run } = useMep();
  const edition = mep.droits.peutEditerContenu;
  const d = useDraft(systeme.sauvegarde);
  useReportDirty(d.dirty, `Sauvegarde de ${systeme.nom}`);
  const v = d.value;
  const manqueEtape = mep.erreursPlan.some((e) => e.startsWith(`Sauvegarde de ${systeme.nom} pendant la MEP`));
  const invalide = v.statut === 'ARenseigner' || (v.statut === 'NonRequise' && !v.justification.trim());
  const manquants = [
    !v.emplacement.trim() && 'l’emplacement',
    !v.methodeRecuperation.trim() && 'la méthode de récupération',
  ].filter((x): x is string => !!x);

  async function enregistrer() {
    if (d.conflit) return;
    const s = {
      ...v,
      contenu: v.statut === 'Requise' ? v.contenu.trim() : '',
      justification: v.statut === 'NonRequise' ? v.justification.trim() : '',
      emplacement: v.statut === 'Requise' ? v.emplacement.trim() : '',
      methodeRecuperation: v.statut === 'Requise' ? v.methodeRecuperation.trim() : '',
    };
    const ok = await run(
      () => api.modifierSauvegardeSysteme(mep.id, systeme.id, { ...s, version: mep.version }),
      `Sauvegarde de ${systeme.nom} enregistrée.`,
    );
    if (ok) d.reset();
  }

  return (
    <li className="space-y-3 px-4 py-4" aria-label={`Sauvegarde de ${systeme.nom}`}>
      <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
        <span
          className="inline-flex min-w-28 items-center gap-2 rounded-md border px-2.5 py-1 text-sm font-semibold"
          style={styleSysteme(systeme.nom)}
        >
          <span className="size-2 rounded-full" style={{ backgroundColor: couleurSysteme(systeme.nom) }} aria-hidden />
          {systeme.nom}
        </span>
        <Segmented<StatutSauvegarde | ''>
          label={`Sauvegarde de ${systeme.nom}`}
          valeur={v.statut === 'ARenseigner' ? '' : v.statut}
          disabled={!edition}
          onChange={(statut) => {
            if (statut) d.update({ statut });
          }}
          options={[
            { valeur: 'Requise', libelle: 'Requise' },
            { valeur: 'NonRequise', libelle: 'Non requise' },
          ]}
        />
        {v.statut === 'ARenseigner' && (
          <span className="text-xs text-warn-fg">À renseigner : {systeme.nom} doit-il être sauvegardé ?</span>
        )}
        {edition && d.dirty && (
          <span className="ml-auto flex items-center gap-2">
            <Button variante="fantome" taille="sm" onClick={d.reset}>
              Annuler
            </Button>
            <Button
              variante="primaire"
              taille="sm"
              icone={<Save className="size-4" aria-hidden />}
              disabled={invalide || d.conflit}
              action={enregistrer}
            >
              Enregistrer
            </Button>
          </span>
        )}
      </div>
      <AvisConflit brouillon={d} />

      {v.statut === 'Requise' && (
        <div className="grid gap-4 md:grid-cols-[14rem_minmax(0,1fr)]">
          <div className="space-y-1.5">
            <span className="text-[13px] font-medium">Quand ?</span>
            <div>
              <Segmented<ModeSauvegarde>
                label={`Moment de la sauvegarde de ${systeme.nom}`}
                valeur={v.moment}
                disabled={!edition}
                onChange={(moment) => d.update({ moment })}
                options={[
                  { valeur: 'AvantMep', libelle: 'Avant la MEP' },
                  { valeur: 'PendantMep', libelle: 'Pendant la MEP' },
                ]}
              />
            </div>
          </div>
          <ChampBlocs
            label="Quoi et comment ?"
            valeur={v.contenu}
            disabled={!edition}
            onChange={(contenu) => d.update({ contenu })}
          />
          <div className="grid gap-4 md:col-span-2 md:grid-cols-2">
            <Field label="Emplacement de la sauvegarde *">
              {(id) => (
                <Input
                  id={id}
                  disabled={!edition}
                  value={v.emplacement}
                  onChange={(e) => d.update({ emplacement: e.target.value })}
                  placeholder="ex. /sauvegardes/clients.sql sur srv-sauvegarde"
                />
              )}
            </Field>
            <Field label="Méthode de récupération *">
              {(id) => (
                <Textarea
                  id={id}
                  rows={2}
                  disabled={!edition}
                  value={v.methodeRecuperation}
                  onChange={(e) => d.update({ methodeRecuperation: e.target.value })}
                  placeholder="ex. psql -d clients < clients.sql"
                />
              )}
            </Field>
            {manquants.length > 0 && (
              <p className="flex items-center gap-1.5 text-xs text-warn-fg md:col-span-2">
                <TriangleAlert className="size-3.5" aria-hidden />À compléter avant le Go : {manquants.join(' et ')}.
              </p>
            )}
          </div>
          {v.moment === 'PendantMep' && (
            <div className="md:col-span-2">
              <Alerte
                ton={manqueEtape ? 'warn' : 'run'}
                icone={DatabaseBackup}
                titre={
                  manqueEtape
                    ? 'Aucune étape de sauvegarde sur ce système'
                    : 'Sauvegarde réalisée par une étape du plan'
                }
              >
                Dans l’onglet « Étapes », ajoutez une étape de déploiement de type « Sauvegarde » sur {systeme.nom}.
              </Alerte>
            </div>
          )}
        </div>
      )}

      {v.statut === 'NonRequise' && (
        <Field label="Justification *">
          {(id) => (
            <Textarea
              id={id}
              rows={2}
              disabled={!edition}
              value={v.justification}
              onChange={(e) => d.update({ justification: e.target.value })}
              placeholder="ex. Aucune donnée : le service est sans état."
            />
          )}
        </Field>
      )}
    </li>
  );
}

export function SauvegardeSection() {
  const { mep, run } = useMep();
  const d = useDraft(mep.sauvegarde);
  useReportDirty(d.dirty, 'Sauvegarde');
  const v = d.value;
  // Après la préparation, seuls le pilote et ses suppléants complètent les preuves ; plus personne une fois la MEP clôturée.
  const editable = mep.droits.peutEditerContenu || (mep.droits.estPilote && !estTerminee(mep));
  const aucunSauvegarde = mep.systemes.length > 0 && mep.systemes.every((s) => s.sauvegarde.statut === 'NonRequise');

  async function enregistrer() {
    if (d.conflit) return;
    const ok = await run(
      () => api.modifierSauvegarde(mep.id, { ...v, version: mep.version }),
      'Sauvegarde enregistrée.',
    );
    if (ok) d.reset();
  }

  return (
    <SectionCarte
      numero={6}
      titre="Sauvegarde"
      description="Par système : requise ou non, quand et comment."
      pied={
        editable && (
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
      {aucunSauvegarde && (
        <div className="mb-4">
          <Alerte ton="warn" icone={TriangleAlert} role="status" titre="Aucun système n’est sauvegardé">
            Tous les systèmes sont marqués « non requise ». Vérifiez que c’est voulu.
          </Alerte>
        </div>
      )}
      {mep.systemes.length === 0 ? (
        <EmptyState
          icone={DatabaseBackup}
          titre="Aucun système"
          description="Ajoutez d’abord les systèmes de la MEP dans la section « Systèmes »."
        />
      ) : (
        <ul
          className="divide-y divide-line overflow-hidden rounded-lg border border-line"
          aria-label="Sauvegarde par système"
        >
          {mep.systemes.map((s) => (
            <LigneSauvegardeSysteme key={s.id} systeme={s} />
          ))}
        </ul>
      )}
      {!mep.droits.peutEditerContenu && (
        <p className="mt-2 text-xs text-fg-subtle">La sauvegarde de chaque système ne se change qu’en préparation.</p>
      )}

      <h3 className="mt-6 mb-3 text-sm font-semibold">
        {mep.estModele ? 'Responsable et procédure' : 'Réalisation et restauration'}
      </h3>
      <div className="grid gap-4 md:grid-cols-2">
        <Field label="Responsable de la sauvegarde">
          {(id) => (
            <Select
              id={id}
              value={v.responsableId ?? ''}
              disabled={!editable}
              onChange={(e) => d.update({ responsableId: vide(e.target.value) })}
            >
              <option value="">— choisir —</option>
              {mep.participants.map((p) => (
                <option key={p.id} value={p.id}>
                  {p.nom}
                </option>
              ))}
            </Select>
          )}
        </Field>
        {!mep.estModele && (
          <Field label="Réalisée le">
            {(id) => (
              <Input
                id={id}
                type="datetime-local"
                disabled={!editable}
                value={versInputDateTime(v.realiseeLe)}
                onChange={(e) => d.update({ realiseeLe: depuisInputDateTime(e.target.value) })}
              />
            )}
          </Field>
        )}
        <Field label="Procédure de sauvegarde" className="md:col-span-2">
          {(id) => (
            <Textarea
              id={id}
              disabled={!editable}
              value={v.procedure}
              onChange={(e) => d.update({ procedure: e.target.value })}
            />
          )}
        </Field>
        <Field label="Copies (emplacements)" className="md:col-span-2">
          {(id) => (
            <Textarea
              id={id}
              rows={2}
              disabled={!editable}
              value={v.copies}
              onChange={(e) => d.update({ copies: e.target.value })}
            />
          )}
        </Field>
      </div>
      {/* Réalisation et test de restauration : propres à chaque MEP, absents d'un modèle. */}
      {!mep.estModele && (
        <div className="mt-5 rounded-lg border border-line bg-surface-2/50 p-4">
          <Checkbox
            label="Restauration testée"
            description="Une sauvegarde jamais restaurée n’est pas une sauvegarde."
            checked={v.restaurationTestee}
            disabled={!editable}
            onChange={(c) => d.update({ restaurationTestee: c })}
          />
          <div className="mt-4 grid gap-4 md:grid-cols-2">
            <Field label="Date du test de restauration">
              {(id) => (
                <Input
                  id={id}
                  type="date"
                  disabled={!editable}
                  value={v.dateTestRestauration ?? ''}
                  onChange={(e) => d.update({ dateTestRestauration: e.target.value || null })}
                />
              )}
            </Field>
            <Field label="Preuve du test">
              {(id) => (
                <Textarea
                  id={id}
                  rows={2}
                  disabled={!editable}
                  value={v.preuveTestRestauration}
                  onChange={(e) => d.update({ preuveTestRestauration: e.target.value })}
                />
              )}
            </Field>
          </div>
        </div>
      )}
    </SectionCarte>
  );
}

export function ValidationSection() {
  const { mep, run } = useMep();
  const peut = mep.droits.peutModifierPlan && mep.droits.participantId !== null;
  return (
    <SectionCarte
      numero={7}
      titre="Validation avant intervention"
      description={
        mep.estModele
          ? 'Approbateurs repris dans chaque MEP créée depuis ce modèle. La relecture, les approbations et le Go se font dans la MEP.'
          : 'Relecture, approbations, puis Go du pilote.'
      }
    >
      <div className="space-y-5">
        {!mep.estModele && (
          <>
            <div>
              <Checkbox
                label="Procédure relue"
                checked={mep.procedureRelue !== null}
                disabled={!peut}
                onChange={(c) =>
                  void run(() => api.procedureRelue(mep.id, c), c ? 'Procédure marquée relue.' : 'Relecture retirée.')
                }
              />
              {mep.procedureRelue && (
                <p className="mt-1.5 ml-[30px] flex items-center gap-1.5 text-xs text-ok-fg">
                  <ShieldCheck className="size-3.5" aria-hidden />
                  Relue par {mep.procedureRelue.par} le {formatDateHeure(mep.procedureRelue.le)}.
                </p>
              )}
              {!peut && mep.droits.peutModifierPlan && (
                <p className="mt-1.5 ml-[30px] text-xs text-fg-subtle">
                  Seul un participant relié à un compte peut valider la relecture.
                </p>
              )}
            </div>
            <div>
              <Checkbox
                label="Démarrage autorisé"
                checked={mep.demarrageAutorise !== null}
                disabled
                onChange={() => undefined}
              />
              <p className="mt-1.5 ml-[30px] text-xs text-fg-subtle">
                {mep.demarrageAutorise
                  ? `Autorisé par ${mep.demarrageAutorise.par} le ${formatDateHeure(mep.demarrageAutorise.le)}.`
                  : 'Se coche automatiquement au Go, donné par le pilote ou un suppléant.'}
              </p>
              {!mep.demarrageAutorise && mep.phase === 'Preparation' && mep.droits.estPilote && (
                <p className="mt-1 ml-[30px]">
                  <BoutonGoNoGo lien />
                </p>
              )}
              {mep.goNoGo.length > 0 && (
                <ul className="mt-2 ml-[30px] space-y-0.5 text-xs text-fg-muted" aria-label="Critères validés au Go">
                  {mep.goNoGo.map((c) => (
                    <li key={c.libelle} className="flex items-center gap-1.5">
                      <ShieldCheck className="size-3.5 text-ok-solid" aria-hidden />
                      {c.libelle}
                      {c.par && <span className="text-fg-subtle"> · {c.par}</span>}
                    </li>
                  ))}
                </ul>
              )}
            </div>
          </>
        )}
        <ApprobationsBloc />
      </div>
    </SectionCarte>
  );
}
