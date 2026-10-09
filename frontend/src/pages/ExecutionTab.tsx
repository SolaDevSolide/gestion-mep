import { useMemo, useState } from 'react';
import { api } from '../api';
import { EtapeCarte, type ActionsEtape } from '../components/EtapeCarte';
import { useLocalStorageState } from '../hooks';
import { useMep } from '../mepContext';

export default function ExecutionTab() {
  const { mep, run } = useMep();
  const { participantId, estPilote } = mep.droits;
  const [systemesChoisis, setSystemesChoisis] = useLocalStorageState<string[]>(`mep.${mep.id}.mesSystemes`, []);
  const aDesEtapes = participantId !== null && mep.etapes.some((e) => e.responsableId === participantId);
  const [seulementMoiChoix, setSeulementMoi] = useState<boolean | null>(null);
  const seulementMoi = seulementMoiChoix ?? aDesEtapes;

  const etapes = useMemo(() => {
    const valides = new Set(mep.systemes.map((s) => s.id));
    const filtre = systemesChoisis.filter((id) => valides.has(id));
    return [...mep.etapes]
      .sort((a, b) => a.ordre - b.ordre)
      .filter((e) => filtre.length === 0 || (e.systemeId !== null && filtre.includes(e.systemeId)))
      .filter((e) => !seulementMoi || (participantId !== null && e.responsableId === participantId));
  }, [mep.etapes, mep.systemes, systemesChoisis, seulementMoi, participantId]);

  const actions: ActionsEtape = {
    demarrer: (e) => run(() => api.demarrerEtape(mep.id, e.id, e.revision), `${e.code} démarrée.`),
    terminer: (e, resultat, preuve) =>
      run(() => api.terminerEtape(mep.id, e.id, e.revision, resultat, preuve, resultat === 'Reussie'),
        resultat === 'Reussie' ? `${e.code} réussie.` : `${e.code} en échec : prévenez le pilote.`),
    autoriserReprise: (e) => run(() => api.autoriserReprise(mep.id, e.id, e.revision), `Reprise autorisée pour ${e.code}.`),
  };

  const basculerSysteme = (id: string) =>
    setSystemesChoisis(systemesChoisis.includes(id) ? systemesChoisis.filter((x) => x !== id) : [...systemesChoisis, id]);

  return (
    <div>
      {mep.phase !== 'Execution' && (
        <p className="alerte alerte-info">La MEP est encore en préparation : les actions ne sont pas disponibles tant que le pilote n’a pas passé la MEP en exécution.</p>
      )}
      <section className="filtres carte" aria-label="Filtres">
        <fieldset>
          <legend>Mes systèmes {systemesChoisis.length === 0 && <span className="muted petit">(aucun filtre : tout est affiché)</span>}</legend>
          <div className="cases">
            {mep.systemes.map((s) => (
              <label key={s.id} className="case">
                <input type="checkbox" checked={systemesChoisis.includes(s.id)} onChange={() => basculerSysteme(s.id)} />
                <span>{s.nom}</span>
              </label>
            ))}
            {mep.systemes.length === 0 && <span className="muted">Aucun système déclaré.</span>}
          </div>
        </fieldset>
        <label className="case">
          <input type="checkbox" checked={seulementMoi} onChange={(e) => setSeulementMoi(e.target.checked)} />
          <span>Seulement mes actions</span>
        </label>
      </section>

      <h2 className="titre-liste">Mes actions <span className="muted">({etapes.length}/{mep.etapes.length})</span></h2>
      {etapes.length === 0 && <p className="muted">Aucune étape ne correspond à ces filtres.</p>}
      <div className="liste-cartes">
        {etapes.map((e) => (
          <EtapeCarte
            key={e.id}
            etape={e}
            etapes={mep.etapes}
            systeme={mep.systemes.find((s) => s.id === e.systemeId)}
            responsable={mep.participants.find((p) => p.id === e.responsableId)}
            participantId={participantId}
            estPilote={estPilote}
            actions={actions}
            ouverteParDefaut={participantId !== null && e.responsableId === participantId && ['Disponible', 'EnCours'].includes(e.disponibilite.statut)}
          />
        ))}
      </div>
    </div>
  );
}
