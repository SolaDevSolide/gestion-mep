import { Save } from 'lucide-react';
import { api } from '../../api';
import { depuisInputDateTime, versInputDateTime } from '../../format';
import { useDraft, useReportDirty } from '../../hooks';
import { useMep } from '../../mepContext';
import type { MepIdentiteSaisie } from '../../types';
import { Button } from '../../ui/Button';
import { Field, Input, Select, Textarea } from '../../ui/Input';
import { SectionCarte, vide } from './Formulaires';

export function IdentiteSection() {
  const { mep, run } = useMep();
  const source: MepIdentiteSaisie = {
    version: mep.version, outil: mep.outil, versionActuelle: mep.versionActuelle, versionCible: mep.versionCible,
    objet: mep.objet, impacts: mep.impacts, dateMep: mep.dateMep, heureDebut: mep.heureDebut, heureFin: mep.heureFin,
    fuseauHoraire: mep.fuseauHoraire, indisponibiliteMinutes: mep.indisponibiliteMinutes, piloteId: mep.piloteId,
    suppleantId: mep.suppleantId, echeanceRepli: mep.echeanceRepli,
  };
  const d = useDraft(source);
  useReportDirty(d.dirty);
  const v = d.value;

  const edition = mep.droits.peutModifierPlan;
  const peutEcheance = edition || mep.droits.estPilote;
  const lecture = !edition;

  const personnes = (valeur: string | null, set: (x: string | null) => void) => (id: string) => (
    <Select id={id} value={valeur ?? ''} disabled={lecture} onChange={(e) => set(vide(e.target.value))}>
      <option value="">— choisir —</option>
      {mep.participants.map((p) => <option key={p.id} value={p.id}>{p.nom}</option>)}
    </Select>
  );

  async function enregistrer() {
    const ok = await run(() => api.modifierIdentite(mep.id, { ...v, version: mep.version }), 'Intervention enregistrée.');
    if (ok) d.reset();
  }

  return (
    <SectionCarte
      numero={1}
      titre="Intervention"
      description="L’outil concerné, les versions, la fenêtre d’intervention et l’échéance de retour arrière."
      pied={peutEcheance && (
        <>
          <Button variante="primaire" icone={<Save className="size-4" aria-hidden />} disabled={!d.dirty} onClick={() => void enregistrer()}>Enregistrer</Button>
          {d.dirty && <Button variante="fantome" onClick={d.reset}>Annuler les modifications</Button>}
          {!edition && <span className="text-xs text-fg-subtle">Seule l’échéance de retour arrière est modifiable après le passage en exécution.</span>}
        </>
      )}
    >
      <div className="grid gap-4 md:grid-cols-2">
        <Field label="Outil">{(id) => <Input id={id} value={v.outil} disabled={lecture} onChange={(e) => d.update({ outil: e.target.value })} />}</Field>
        <Field label="Objet">{(id) => <Input id={id} value={v.objet} disabled={lecture} onChange={(e) => d.update({ objet: e.target.value })} />}</Field>
        <Field label="Version actuelle">{(id) => <Input id={id} className="font-mono" value={v.versionActuelle} disabled={lecture} onChange={(e) => d.update({ versionActuelle: e.target.value })} />}</Field>
        <Field label="Version cible">{(id) => <Input id={id} className="font-mono" value={v.versionCible} disabled={lecture} onChange={(e) => d.update({ versionCible: e.target.value })} />}</Field>
        <Field label="Date de la MEP">{(id) => <Input id={id} type="date" value={v.dateMep ?? ''} disabled={lecture} onChange={(e) => d.update({ dateMep: vide(e.target.value) })} />}</Field>
        <div className="grid grid-cols-2 gap-4">
          <Field label="Heure de début">{(id) => <Input id={id} type="time" value={(v.heureDebut ?? '').slice(0, 5)} disabled={lecture} onChange={(e) => d.update({ heureDebut: e.target.value ? `${e.target.value}:00` : null })} />}</Field>
          <Field label="Heure de fin">{(id) => <Input id={id} type="time" value={(v.heureFin ?? '').slice(0, 5)} disabled={lecture} onChange={(e) => d.update({ heureFin: e.target.value ? `${e.target.value}:00` : null })} />}</Field>
        </div>
        <Field label="Fuseau horaire">{(id) => <Input id={id} value={v.fuseauHoraire} disabled={lecture} onChange={(e) => d.update({ fuseauHoraire: e.target.value })} />}</Field>
        <Field label="Indisponibilité prévue (minutes)">
          {(id) => (
            <Input id={id} type="number" min={0} value={v.indisponibiliteMinutes ?? ''} disabled={lecture}
              onChange={(e) => d.update({ indisponibiliteMinutes: e.target.value === '' ? null : Number(e.target.value) })} />
          )}
        </Field>
        <Field label="Pilote">{personnes(v.piloteId, (x) => d.update({ piloteId: x }))}</Field>
        <Field label="Suppléant">{personnes(v.suppleantId, (x) => d.update({ suppleantId: x }))}</Field>
        <Field label="Échéance de retour arrière (obligatoire)" hint="Passé ce délai, le retour arrière est déclenché automatiquement." className="md:col-span-2">
          {(id) => (
            <Input id={id} type="datetime-local" value={versInputDateTime(v.echeanceRepli)} disabled={!peutEcheance} className="md:max-w-xs"
              onChange={(e) => d.update({ echeanceRepli: depuisInputDateTime(e.target.value) })} />
          )}
        </Field>
        <Field label="Impacts attendus" className="md:col-span-2">
          {(id) => <Textarea id={id} rows={3} value={v.impacts} disabled={lecture} onChange={(e) => d.update({ impacts: e.target.value })} />}
        </Field>
      </div>
    </SectionCarte>
  );
}
