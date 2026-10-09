import { api } from '../../api';
import { depuisInputDateTime, versInputDateTime } from '../../format';
import { useDraft, useReportDirty } from '../../hooks';
import { useMep } from '../../mepContext';
import { Case, Champ, Section, vide } from '../Champ';

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
    <Section numero={4} titre="Sauvegarde">
      <div className="cases">
        <Case label="Structure sauvegardée" checked={v.structure} disabled={!editable} onChange={(c) => d.update({ structure: c })} />
        <Case label="Données sauvegardées" checked={v.donnees} disabled={!editable} onChange={(c) => d.update({ donnees: c })} />
        <Case label="Configuration sauvegardée" checked={v.configuration} disabled={!editable} onChange={(c) => d.update({ configuration: c })} />
      </div>
      <div className="grille">
        <Champ label="Responsable de la sauvegarde">
          <select value={v.responsableId ?? ''} disabled={!editable} onChange={(e) => d.update({ responsableId: vide(e.target.value) })}>
            <option value="">— choisir —</option>
            {mep.participants.map((p) => <option key={p.id} value={p.id}>{p.nom}</option>)}
          </select>
        </Champ>
        <Champ label="Réalisée le">
          <input type="datetime-local" disabled={!editable} value={versInputDateTime(v.realiseeLe)} onChange={(e) => d.update({ realiseeLe: depuisInputDateTime(e.target.value) })} />
        </Champ>
        <Champ label="Procédure de sauvegarde" large><textarea rows={3} disabled={!editable} value={v.procedure} onChange={(e) => d.update({ procedure: e.target.value })} /></Champ>
        <Champ label="Copies (emplacement)" large><textarea rows={2} disabled={!editable} value={v.copies} onChange={(e) => d.update({ copies: e.target.value })} /></Champ>
      </div>
      <div className="cases">
        <Case label="Restauration testée" checked={v.restaurationTestee} disabled={!editable} onChange={(c) => d.update({ restaurationTestee: c })} />
      </div>
      <div className="grille">
        <Champ label="Date du test de restauration">
          <input type="date" disabled={!editable} value={v.dateTestRestauration ?? ''} onChange={(e) => d.update({ dateTestRestauration: e.target.value || null })} />
        </Champ>
        <Champ label="Preuve du test de restauration" large><textarea rows={2} disabled={!editable} value={v.preuveTestRestauration} onChange={(e) => d.update({ preuveTestRestauration: e.target.value })} /></Champ>
      </div>
      {editable && (
        <div className="actions">
          <button type="button" className="btn btn-primaire" disabled={!d.dirty} onClick={() => void enregistrer()}>Enregistrer</button>
        </div>
      )}
    </Section>
  );
}
