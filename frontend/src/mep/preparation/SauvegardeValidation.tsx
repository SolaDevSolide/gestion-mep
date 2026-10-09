import { Save, ShieldCheck } from 'lucide-react';
import { api } from '../../api';
import { depuisInputDateTime, formatDateHeure, versInputDateTime } from '../../format';
import { useDraft, useReportDirty } from '../../hooks';
import { useMep } from '../../mepContext';
import { Button } from '../../ui/Button';
import { Checkbox } from '../../ui/Checkbox';
import { Field, Input, Select, Textarea } from '../../ui/Input';
import { SectionCarte, vide } from './Formulaires';

export function SauvegardeSection() {
  const { mep, run } = useMep();
  const d = useDraft(mep.sauvegarde);
  useReportDirty(d.dirty);
  const v = d.value;
  const editable = mep.droits.peutModifierPlan || mep.droits.estPilote;

  async function enregistrer() {
    const ok = await run(() => api.modifierSauvegarde(mep.id, { ...v, version: mep.version }), 'Sauvegarde enregistrée.');
    if (ok) d.reset();
  }

  return (
    <SectionCarte
      numero={5}
      titre="Sauvegarde"
      description="Ce qui est sauvegardé avant l’intervention, et la preuve que la restauration fonctionne."
      pied={editable && (
        <>
          <Button variante="primaire" icone={<Save className="size-4" aria-hidden />} disabled={!d.dirty} onClick={() => void enregistrer()}>Enregistrer</Button>
          {d.dirty && <Button variante="fantome" onClick={d.reset}>Annuler les modifications</Button>}
        </>
      )}
    >
      <fieldset className="mb-5">
        <legend className="mb-2 text-[13px] font-medium">Éléments sauvegardés</legend>
        <div className="flex flex-wrap gap-x-8 gap-y-2">
          <Checkbox label="Structure" checked={v.structure} disabled={!editable} onChange={(c) => d.update({ structure: c })} />
          <Checkbox label="Données" checked={v.donnees} disabled={!editable} onChange={(c) => d.update({ donnees: c })} />
          <Checkbox label="Configuration" checked={v.configuration} disabled={!editable} onChange={(c) => d.update({ configuration: c })} />
        </div>
      </fieldset>
      <div className="grid gap-4 md:grid-cols-2">
        <Field label="Responsable de la sauvegarde">
          {(id) => (
            <Select id={id} value={v.responsableId ?? ''} disabled={!editable} onChange={(e) => d.update({ responsableId: vide(e.target.value) })}>
              <option value="">— choisir —</option>
              {mep.participants.map((p) => <option key={p.id} value={p.id}>{p.nom}</option>)}
            </Select>
          )}
        </Field>
        <Field label="Réalisée le">
          {(id) => <Input id={id} type="datetime-local" disabled={!editable} value={versInputDateTime(v.realiseeLe)} onChange={(e) => d.update({ realiseeLe: depuisInputDateTime(e.target.value) })} />}
        </Field>
        <Field label="Procédure de sauvegarde" className="md:col-span-2">{(id) => <Textarea id={id} disabled={!editable} value={v.procedure} onChange={(e) => d.update({ procedure: e.target.value })} />}</Field>
        <Field label="Copies (emplacements)" className="md:col-span-2">{(id) => <Textarea id={id} rows={2} disabled={!editable} value={v.copies} onChange={(e) => d.update({ copies: e.target.value })} />}</Field>
      </div>
      <div className="mt-5 rounded-lg border border-line bg-surface-2/50 p-4">
        <Checkbox label="Restauration testée" description="Une sauvegarde jamais restaurée n’est pas une sauvegarde." checked={v.restaurationTestee} disabled={!editable} onChange={(c) => d.update({ restaurationTestee: c })} />
        <div className="mt-4 grid gap-4 md:grid-cols-2">
          <Field label="Date du test de restauration">
            {(id) => <Input id={id} type="date" disabled={!editable} value={v.dateTestRestauration ?? ''} onChange={(e) => d.update({ dateTestRestauration: e.target.value || null })} />}
          </Field>
          <Field label="Preuve du test">{(id) => <Textarea id={id} rows={2} disabled={!editable} value={v.preuveTestRestauration} onChange={(e) => d.update({ preuveTestRestauration: e.target.value })} />}</Field>
        </div>
      </div>
    </SectionCarte>
  );
}

export function ValidationSection() {
  const { mep, run } = useMep();
  const peut = mep.droits.peutModifierPlan && mep.droits.participantId !== null;
  return (
    <SectionCarte numero={6} titre="Validation avant intervention" description="La procédure doit être relue par une autre personne que son auteur.">
      <div className="space-y-5">
        <div>
          <Checkbox
            label="Procédure relue"
            checked={mep.procedureRelue !== null}
            disabled={!peut}
            onChange={(c) => void run(() => api.procedureRelue(mep.id, c), c ? 'Procédure marquée relue.' : 'Relecture retirée.')}
          />
          {mep.procedureRelue && (
            <p className="mt-1.5 ml-[30px] flex items-center gap-1.5 text-xs text-ok-fg"><ShieldCheck className="size-3.5" aria-hidden />Relue par {mep.procedureRelue.par} le {formatDateHeure(mep.procedureRelue.le)}.</p>
          )}
          {!peut && mep.droits.peutModifierPlan && <p className="mt-1.5 ml-[30px] text-xs text-fg-subtle">Seul un participant relié à un compte peut valider la relecture.</p>}
        </div>
        <div>
          <Checkbox label="Démarrage autorisé" checked={mep.demarrageAutorise !== null} disabled onChange={() => undefined} />
          <p className="mt-1.5 ml-[30px] text-xs text-fg-subtle">
            {mep.demarrageAutorise
              ? `Autorisé par ${mep.demarrageAutorise.par} le ${formatDateHeure(mep.demarrageAutorise.le)}.`
              : 'Le démarrage est autorisé par le pilote lors du passage en exécution.'}
          </p>
        </div>
      </div>
    </SectionCarte>
  );
}
