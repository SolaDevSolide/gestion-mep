import { useEffect, useState, type FormEvent } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { api } from '../api';
import { BadgePhase } from '../components/Badges';
import { formatDate, nomOuTiret } from '../format';
import type { MepResume } from '../types';

export default function ListeMepPage() {
  const [meps, setMeps] = useState<MepResume[] | null>(null);
  const [erreur, setErreur] = useState('');
  const [creation, setCreation] = useState(false);
  const [outil, setOutil] = useState('');
  const [objet, setObjet] = useState('');
  const [envoi, setEnvoi] = useState(false);
  const navigate = useNavigate();

  useEffect(() => {
    api.meps().then(setMeps).catch((e: Error) => setErreur(e.message));
  }, []);

  async function creer(e: FormEvent) {
    e.preventDefault();
    setEnvoi(true);
    setErreur('');
    try {
      const mep = await api.creerMep(outil.trim(), objet.trim());
      navigate(`/meps/${mep.id}`);
    } catch (err) {
      setErreur(err instanceof Error ? err.message : 'Création impossible.');
      setEnvoi(false);
    }
  }

  return (
    <main className="page">
      <div className="page-titre">
        <h1>Mises en production</h1>
        <button type="button" className="btn btn-primaire" onClick={() => setCreation((v) => !v)}>
          Nouvelle MEP
        </button>
      </div>

      {creation && (
        <form className="carte formulaire-creation" onSubmit={creer}>
          <h2>Nouvelle MEP</h2>
          <label className="champ">
            <span>Outil</span>
            <input value={outil} onChange={(e) => setOutil(e.target.value)} required autoFocus />
          </label>
          <label className="champ">
            <span>Objet</span>
            <input value={objet} onChange={(e) => setObjet(e.target.value)} required />
          </label>
          <div className="actions">
            <button type="submit" className="btn btn-primaire" disabled={envoi}>Créer</button>
            <button type="button" className="btn btn-secondaire" onClick={() => setCreation(false)}>Annuler</button>
          </div>
        </form>
      )}

      {erreur && <p className="alerte alerte-erreur" role="alert">{erreur}</p>}
      {meps === null && !erreur && <p className="chargement">Chargement…</p>}
      {meps !== null && meps.length === 0 && <p className="muted">Aucune MEP pour le moment.</p>}
      {meps !== null && meps.length > 0 && (
        <div className="tableau-conteneur">
          <table className="tableau">
            <thead>
              <tr>
                <th>Outil</th><th>Version cible</th><th>Date</th><th>Phase</th><th>Pilote</th><th>Avancement</th>
              </tr>
            </thead>
            <tbody>
              {meps.map((m) => (
                <tr key={m.id}>
                  <td><Link to={`/meps/${m.id}`} className="lien-fort">{m.outil || '(sans nom)'}</Link><div className="petit muted">{m.objet}</div></td>
                  <td>{nomOuTiret(m.versionCible)}</td>
                  <td>{formatDate(m.dateMep)}</td>
                  <td><BadgePhase phase={m.phase} /></td>
                  <td>{nomOuTiret(m.pilote)}</td>
                  <td>
                    {m.nbReussies}/{m.nbEtapes} réussies
                    {m.nbEchecs > 0 && <span className="texte-danger"> · {m.nbEchecs} {m.nbEchecs > 1 ? 'échecs' : 'échec'}</span>}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </main>
  );
}
