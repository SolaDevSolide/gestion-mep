import { useState } from 'react';
import { api } from '../api';
import { BadgeCriticite, BadgeDisponibilite, BadgeEtat } from '../components/Badges';
import { EtapePanneau } from '../components/EtapePanneau';
import { useMep } from '../mepContext';

export default function EtapesTab() {
  const { mep, run } = useMep();
  const [selection, setSelection] = useState<string | null>(null);
  const etapes = [...mep.etapes].sort((a, b) => a.ordre - b.ordre);
  const courante = etapes.find((e) => e.id === selection) ?? null;
  const codeDe = (id: string) => mep.etapes.find((e) => e.id === id)?.code ?? '?';

  async function ajouter() {
    const avant = new Set(mep.etapes.map((e) => e.id));
    let nouvelle: string | null = null;
    const ok = await run(async () => {
      const m = await api.ajouterEtape(mep.id);
      nouvelle = m.etapes.find((e) => !avant.has(e.id))?.id ?? null;
      return m;
    }, 'Étape ajoutée.');
    if (ok && nouvelle) setSelection(nouvelle);
  }

  return (
    <div className={`etapes-layout${courante ? ' avec-panneau' : ''}`}>
      <div>
        <div className="page-titre">
          <h2>Étapes ({etapes.length})</h2>
          {mep.droits.peutModifierPlan && <button type="button" className="btn btn-primaire" onClick={() => void ajouter()}>Ajouter une étape</button>}
        </div>
        {etapes.length === 0 ? <p className="muted">Aucune étape.</p> : (
          <div className="tableau-conteneur">
            <table className="tableau tableau-cliquable">
              <thead>
                <tr><th>Code</th><th>Action</th><th>Système</th><th>Responsable</th><th>Criticité</th><th>État</th><th>Disponibilité</th><th>Dépend de</th></tr>
              </thead>
              <tbody>
                {etapes.map((e) => (
                  <tr key={e.id} className={e.id === selection ? 'ligne-active' : undefined} onClick={() => setSelection(e.id)} tabIndex={0}
                    onKeyDown={(ev) => { if (ev.key === 'Enter') setSelection(e.id); }}>
                    <td><strong>{e.code}</strong></td>
                    <td>{e.action || <span className="muted">(sans libellé)</span>}</td>
                    <td>{mep.systemes.find((s) => s.id === e.systemeId)?.nom ?? '—'}</td>
                    <td>{mep.participants.find((p) => p.id === e.responsableId)?.nom ?? '—'}</td>
                    <td><BadgeCriticite criticite={e.criticite} /></td>
                    <td><BadgeEtat etat={e.etat} /></td>
                    <td><BadgeDisponibilite dispo={e.disponibilite} /></td>
                    <td>{e.dependances.length ? e.dependances.map(codeDe).join(', ') : '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
      {courante && <EtapePanneau key={courante.id} etape={courante} onFermer={() => setSelection(null)} />}
    </div>
  );
}
