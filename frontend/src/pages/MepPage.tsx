import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useParams } from 'react-router-dom';
import { api, ApiError } from '../api';
import { Banniere } from '../components/Banniere';
import { PlanCheck } from '../components/PlanCheck';
import { DirtyContext, type DirtyRegistry } from '../hooks';
import { MepContext, type MepContextValue } from '../mepContext';
import type { Mep } from '../types';
import EtapesTab from './EtapesTab';
import ExecutionTab from './ExecutionTab';
import JournalTab from './JournalTab';
import PreparationTab from './PreparationTab';

type Onglet = 'preparation' | 'etapes' | 'execution' | 'journal';
const ONGLETS: { id: Onglet; libelle: string }[] = [
  { id: 'preparation', libelle: 'Préparation' },
  { id: 'etapes', libelle: 'Étapes' },
  { id: 'execution', libelle: 'Exécution' },
  { id: 'journal', libelle: 'Journal' },
];

interface Message { id: number; type: 'succes' | 'erreur'; texte: string }

export default function MepPage() {
  const { id = '' } = useParams();
  const [mep, setMep] = useState<Mep | null>(null);
  const [erreurChargement, setErreurChargement] = useState('');
  const [onglet, setOnglet] = useState<Onglet | null>(null);
  const [rechargementEnAttente, setRechargementEnAttente] = useState(false);
  const [epoch, setEpoch] = useState(0);
  const [journalTick, setJournalTick] = useState(0);
  const [message, setMessage] = useState<Message | null>(null);

  const versionRef = useRef(0);
  const dirtyRef = useRef(new Map<string, boolean>());
  const phaseRef = useRef<string | null>(null);

  const registre = useMemo<DirtyRegistry>(() => ({ set: (k, d) => { if (d) dirtyRef.current.set(k, true); else dirtyRef.current.delete(k); } }), []);
  const saisieEnCours = () => dirtyRef.current.size > 0;

  const appliquer = useCallback((m: Mep) => {
    versionRef.current = m.version;
    setMep(m);
    setRechargementEnAttente(false);
    setJournalTick((t) => t + 1);
    if (phaseRef.current !== null && phaseRef.current !== m.phase) {
      setOnglet(m.phase === 'Execution' ? 'execution' : 'preparation');
    }
    phaseRef.current = m.phase;
  }, []);

  const charger = useCallback(async (forcer: boolean) => {
    try {
      const m = await api.mep(id);
      if (forcer) setEpoch((e) => e + 1);
      appliquer(m);
      setErreurChargement('');
    } catch (e) {
      if (e instanceof ApiError && e.status === 401) return;
      setErreurChargement(e instanceof Error ? e.message : 'Chargement impossible.');
    }
  }, [id, appliquer]);

  /** Rechargement déclenché à distance : on protège une saisie en cours. */
  const synchroniser = useCallback(async () => {
    try {
      const m = await api.mep(id);
      if (m.version > versionRef.current && saisieEnCours()) {
        setRechargementEnAttente(true);
        return;
      }
      appliquer(m);
    } catch { /* silencieux : prochain cycle */ }
  }, [id, appliquer]);

  useEffect(() => {
    phaseRef.current = null;
    versionRef.current = 0;
    setMep(null);
    void charger(false).then(() => undefined);
  }, [charger]);

  // Onglet initial selon la phase.
  useEffect(() => {
    if (mep && onglet === null) setOnglet(mep.phase === 'Execution' ? 'execution' : 'preparation');
  }, [mep, onglet]);

  // Flux temps réel (SSE).
  useEffect(() => {
    const es = new EventSource(`/api/meps/${id}/flux`, { withCredentials: true });
    es.addEventListener('maj', (ev) => {
      try {
        const { version } = JSON.parse((ev as MessageEvent<string>).data) as { version: number };
        if (version > versionRef.current) void synchroniser();
      } catch { /* événement illisible */ }
    });
    return () => es.close();
  }, [id, synchroniser]);

  // Rafraîchissement périodique (disponibilités dépendantes de l'heure).
  useEffect(() => {
    const t = window.setInterval(() => void synchroniser(), 30_000);
    return () => window.clearInterval(t);
  }, [synchroniser]);

  useEffect(() => {
    if (message?.type !== 'succes') return;
    const t = window.setTimeout(() => setMessage(null), 4000);
    return () => window.clearTimeout(t);
  }, [message]);

  const notifier = useCallback((type: 'succes' | 'erreur', texte: string) => {
    setMessage({ id: Date.now(), type, texte });
  }, []);

  const run = useCallback<MepContextValue['run']>(async (action, succes) => {
    try {
      const m = await action();
      appliquer(m);
      if (succes) notifier('succes', succes);
      return true;
    } catch (e) {
      if (e instanceof ApiError && e.status === 409) {
        notifier('erreur', 'Quelqu’un a modifié cette MEP, les données ont été rechargées');
        await charger(true);
      } else if (e instanceof ApiError && e.status === 401) {
        /* redirection gérée par le client API */
      } else {
        notifier('erreur', e instanceof Error ? e.message : 'Une erreur est survenue.');
      }
      return false;
    }
  }, [appliquer, charger, notifier]);

  if (erreurChargement && !mep) return <main className="page"><p className="alerte alerte-erreur" role="alert">{erreurChargement}</p></main>;
  if (!mep) return <p className="chargement">Chargement…</p>;

  const courant = onglet ?? 'preparation';
  const ctx: MepContextValue = { mep, run, notifier };

  return (
    <MepContext.Provider value={ctx}>
      <DirtyContext.Provider value={registre}>
        <Banniere />
        <main className="page page-mep">
          {rechargementEnAttente && (
            <div className="alerte alerte-info bandeau-reload" role="status">
              <span>Des modifications ont été reçues — recharger</span>
              <button type="button" className="btn btn-petit btn-primaire" onClick={() => void charger(true)}>
                Recharger (perd la saisie en cours)
              </button>
            </div>
          )}
          <PlanCheck mep={mep} />
          <nav className="onglets" role="tablist">
            {ONGLETS.map((o) => (
              <button
                key={o.id}
                type="button"
                role="tab"
                aria-selected={courant === o.id}
                className={`onglet${courant === o.id ? ' onglet-actif' : ''}`}
                onClick={() => setOnglet(o.id)}
              >
                {o.libelle}
              </button>
            ))}
          </nav>
          <div key={epoch}>
            {courant === 'preparation' && <PreparationTab />}
            {courant === 'etapes' && <EtapesTab />}
            {courant === 'execution' && <ExecutionTab />}
            {courant === 'journal' && <JournalTab mepId={mep.id} tick={journalTick} />}
          </div>
        </main>
        {message && (
          <div className={`toast toast-${message.type}`} role={message.type === 'erreur' ? 'alert' : 'status'}>
            <span>{message.texte}</span>
            <button type="button" aria-label="Fermer" onClick={() => setMessage(null)}>×</button>
          </div>
        )}
      </DirtyContext.Provider>
    </MepContext.Provider>
  );
}
