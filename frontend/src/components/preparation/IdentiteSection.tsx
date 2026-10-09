import { api } from '../../api';
import { depuisInputDateTime, versInputDateTime } from '../../format';
import { useDraft, useReportDirty } from '../../hooks';
import { useMep } from '../../mepContext';
import type { MepIdentiteSaisie } from '../../types';
import { Champ, Section, vide } from '../Champ';

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

  const choixPersonnes = (valeur: string | null, set: (x: string | null) => void, desactive: boolean) => (
    <select value={valeur ?? ''} disabled={desactive} onChange={(e) => set(vide(e.target.value))}>
      <option value="">— choisir —</option>
      {mep.participants.map((p) => <option key={p.id} value={p.id}>{p.nom}</option>)}
    </select>
  );

  async function enregistrer() {
    const ok = await run(() => api.modifierIdentite(mep.id, { ...v, version: mep.version }), 'Intervention enregistrée.');
    if (ok) d.reset();
  }

  return (
    <Section numero={1} titre="Intervention">
      <div className="grille">
        <Champ label="Outil"><input value={v.outil} disabled={lecture} onChange={(e) => d.update({ outil: e.target.value })} /></Champ>
        <Champ label="Version actuelle"><input value={v.versionActuelle} disabled={lecture} onChange={(e) => d.update({ versionActuelle: e.target.value })} /></Champ>
        <Champ label="Version cible"><input value={v.versionCible} disabled={lecture} onChange={(e) => d.update({ versionCible: e.target.value })} /></Champ>
        <Champ label="Date de la MEP"><input type="date" value={v.dateMep ?? ''} disabled={lecture} onChange={(e) => d.update({ dateMep: vide(e.target.value) })} /></Champ>
        <Champ label="Heure de début"><input type="time" value={(v.heureDebut ?? '').slice(0, 5)} disabled={lecture} onChange={(e) => d.update({ heureDebut: e.target.value ? `${e.target.value}:00` : null })} /></Champ>
        <Champ label="Heure de fin"><input type="time" value={(v.heureFin ?? '').slice(0, 5)} disabled={lecture} onChange={(e) => d.update({ heureFin: e.target.value ? `${e.target.value}:00` : null })} /></Champ>
        <Champ label="Fuseau horaire"><input value={v.fuseauHoraire} disabled={lecture} onChange={(e) => d.update({ fuseauHoraire: e.target.value })} /></Champ>
        <Champ label="Indisponibilité prévue (minutes)">
          <input type="number" min={0} value={v.indisponibiliteMinutes ?? ''} disabled={lecture}
            onChange={(e) => d.update({ indisponibiliteMinutes: e.target.value === '' ? null : Number(e.target.value) })} />
        </Champ>
        <Champ label="Pilote">{choixPersonnes(v.piloteId, (x) => d.update({ piloteId: x }), lecture)}</Champ>
        <Champ label="Suppléant">{choixPersonnes(v.suppleantId, (x) => d.update({ suppleantId: x }), lecture)}</Champ>
        <Champ label="Échéance de retour arrière (obligatoire)">
          <input type="datetime-local" value={versInputDateTime(v.echeanceRepli)} disabled={!peutEcheance}
            onChange={(e) => d.update({ echeanceRepli: depuisInputDateTime(e.target.value) })} />
        </Champ>
        <Champ label="Objet" large><input value={v.objet} disabled={lecture} onChange={(e) => d.update({ objet: e.target.value })} /></Champ>
        <Champ label="Impacts" large><textarea rows={3} value={v.impacts} disabled={lecture} onChange={(e) => d.update({ impacts: e.target.value })} /></Champ>
      </div>
      {peutEcheance && (
        <div className="actions">
          <button type="button" className="btn btn-primaire" disabled={!d.dirty} onClick={() => void enregistrer()}>Enregistrer</button>
          {!edition && <span className="petit muted">Seule l’échéance de retour arrière est modifiable en exécution.</span>}
        </div>
      )}
    </Section>
  );
}
