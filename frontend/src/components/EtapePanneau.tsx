import { api } from '../api';
import { LIBELLE_CRITICITE } from '../format';
import { useDraft, useReportDirty } from '../hooks';
import { useMep } from '../mepContext';
import type { Criticite, Etape, EtapeSaisie } from '../types';
import { BadgeDisponibilite, BadgeEtat } from './Badges';
import { Champ, vide } from './Champ';

const CRITICITES = Object.keys(LIBELLE_CRITICITE) as Criticite[];

export function EtapePanneau({ etape, onFermer }: { etape: Etape; onFermer: () => void }) {
  const { mep, run } = useMep();
  const edition = mep.droits.peutModifierPlan;
  const source: EtapeSaisie = {
    revision: etape.revision, action: etape.action, systemeId: etape.systemeId, responsableId: etape.responsableId,
    criticite: etape.criticite, instructions: etape.instructions, resultatAttendu: etape.resultatAttendu,
    dependances: etape.dependances,
  };
  const d = useDraft(source);
  useReportDirty(d.dirty);
  const v = d.value;
  const index = mep.etapes.findIndex((e) => e.id === etape.id);

  const autres = mep.etapes.filter((e) => e.id !== etape.id);
  const basculer = (id: string) =>
    d.update({ dependances: v.dependances.includes(id) ? v.dependances.filter((x) => x !== id) : [...v.dependances, id] });

  async function enregistrer() {
    const ok = await run(() => api.modifierEtape(mep.id, etape.id, { ...v, revision: etape.revision }), `${etape.code} enregistrée.`);
    if (ok) d.reset();
  }
  async function supprimer() {
    if (!window.confirm(`Supprimer l’étape ${etape.code} ?`)) return;
    if (await run(() => api.supprimerEtape(mep.id, etape.id), `${etape.code} supprimée.`)) onFermer();
  }

  return (
    <aside className="panneau" aria-label={`Édition de ${etape.code}`}>
      <div className="panneau-tete">
        <h2>{etape.code}</h2>
        <BadgeEtat etat={etape.etat} />
        <BadgeDisponibilite dispo={etape.disponibilite} />
        <button type="button" className="btn btn-petit btn-secondaire" onClick={onFermer}>Fermer</button>
      </div>
      {!edition && <p className="alerte alerte-info">Plan figé : lecture seule.</p>}

      <Champ label="Action"><input value={v.action} disabled={!edition} onChange={(e) => d.update({ action: e.target.value })} /></Champ>
      <Champ label="Système">
        <select value={v.systemeId ?? ''} disabled={!edition} onChange={(e) => d.update({ systemeId: vide(e.target.value) })}>
          <option value="">— choisir —</option>
          {mep.systemes.map((s) => <option key={s.id} value={s.id}>{s.nom}</option>)}
        </select>
      </Champ>
      <Champ label="Responsable">
        <select value={v.responsableId ?? ''} disabled={!edition} onChange={(e) => d.update({ responsableId: vide(e.target.value) })}>
          <option value="">— choisir —</option>
          {mep.participants.map((p) => <option key={p.id} value={p.id}>{p.nom}</option>)}
        </select>
      </Champ>
      <Champ label="Criticité">
        <select value={v.criticite} disabled={!edition} onChange={(e) => d.update({ criticite: e.target.value as Criticite })}>
          {CRITICITES.map((c) => <option key={c} value={c}>{LIBELLE_CRITICITE[c]}</option>)}
        </select>
      </Champ>
      <Champ label="Instructions exactes">
        <textarea className="mono" rows={7} value={v.instructions} disabled={!edition} onChange={(e) => d.update({ instructions: e.target.value })} />
      </Champ>
      <Champ label="Résultat attendu">
        <textarea rows={3} value={v.resultatAttendu} disabled={!edition} onChange={(e) => d.update({ resultatAttendu: e.target.value })} />
      </Champ>

      <fieldset>
        <legend>Dépendances (étapes à réussir avant)</legend>
        {autres.length === 0 && <p className="muted petit">Aucune autre étape.</p>}
        <div className="cases cases-colonne">
          {autres.map((e) => (
            <label key={e.id} className="case">
              <input type="checkbox" disabled={!edition} checked={v.dependances.includes(e.id)} onChange={() => basculer(e.id)} />
              <span>{e.code} — {e.action || '(sans libellé)'}</span>
            </label>
          ))}
        </div>
      </fieldset>

      {etape.preuve && (<><h4>Preuve</h4><pre className="instructions">{etape.preuve}</pre></>)}

      {edition && (
        <div className="actions">
          <button type="button" className="btn btn-primaire" disabled={!d.dirty} onClick={() => void enregistrer()}>Enregistrer</button>
          <button type="button" className="btn btn-secondaire" disabled={index <= 0} onClick={() => void run(() => api.deplacerEtape(mep.id, etape.id, 'haut'))}>Monter</button>
          <button type="button" className="btn btn-secondaire" disabled={index < 0 || index >= mep.etapes.length - 1} onClick={() => void run(() => api.deplacerEtape(mep.id, etape.id, 'bas'))}>Descendre</button>
          <button type="button" className="btn btn-danger" onClick={() => void supprimer()}>Supprimer</button>
        </div>
      )}
    </aside>
  );
}
