import { Flag, Loader2, OctagonAlert, RefreshCw, Siren } from 'lucide-react';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useParams, useSearchParams } from 'react-router-dom';
import { api, ApiError } from '../api';
import { formatDateHeure } from '../format';
import { DirtyContext, type DirtyRegistry } from '../hooks';
import { BilanCarte } from '../mep/BilanCarte';
import { EnTeteMep } from '../mep/EnTeteMep';
import EtapesTab from '../mep/EtapesTab';
import ExecutionTab from '../mep/ExecutionTab';
import JournalTab from '../mep/JournalTab';
import PreparationTab from '../mep/PreparationTab';
import RecetteTab from '../mep/RecetteTab';
import RetourArriereTab from '../mep/RetourArriereTab';
import { MepContext, type MepContextValue } from '../mepContext';
import type { Mep, Phase } from '../types';
import { Alerte } from '../ui/Alerte';
import { Button } from '../ui/Button';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '../ui/Tabs';
import { useToast } from '../ui/Toast';

type Onglet = 'preparation' | 'etapes' | 'execution' | 'recette' | 'retour' | 'journal';
const ONGLETS: Onglet[] = ['preparation', 'etapes', 'execution', 'recette', 'retour', 'journal'];

function ongletParDefaut(m: Mep): Onglet {
  switch (m.phase) {
    case 'Preparation': return 'preparation';
    case 'Execution': return 'execution';
    case 'Recette': return 'recette';
    case 'RetourArriere': return 'retour';
    case 'Cloturee': return m.issue === 'Repliee' ? 'retour' : 'execution';
  }
}

export default function MepPage() {
  const { id = '' } = useParams();
  const toast = useToast();
  const [params, setParams] = useSearchParams();
  const [mep, setMep] = useState<Mep | null>(null);
  const [erreurChargement, setErreurChargement] = useState('');
  const [rechargementEnAttente, setRechargementEnAttente] = useState(false);
  const [epoch, setEpoch] = useState(0);
  const [journalTick, setJournalTick] = useState(0);

  const versionRef = useRef(0);
  const dirtyRef = useRef(new Map<string, boolean>());
  const phaseRef = useRef<Phase | null>(null);

  const registre = useMemo<DirtyRegistry>(() => ({ set: (k, d) => { if (d) dirtyRef.current.set(k, true); else dirtyRef.current.delete(k); } }), []);
  const saisieEnCours = () => dirtyRef.current.size > 0;

  const definirOnglet = useCallback((o: Onglet) => setParams({ onglet: o }, { replace: true }), [setParams]);

  const appliquer = useCallback((m: Mep) => {
    versionRef.current = m.version;
    setMep(m);
    setRechargementEnAttente(false);
    setJournalTick((t) => t + 1);
    if (phaseRef.current !== null && phaseRef.current !== m.phase) {
      setParams({ onglet: ongletParDefaut(m) }, { replace: true });
    }
    phaseRef.current = m.phase;
  }, [setParams]);

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
    void charger(false);
  }, [charger]);

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

  // Rafraîchissement périodique (disponibilités dépendantes de l'heure, échéance automatique).
  useEffect(() => {
    const t = window.setInterval(() => void synchroniser(), 30_000);
    return () => window.clearInterval(t);
  }, [synchroniser]);

  const run = useCallback<MepContextValue['run']>(async (action, succes, surErreur) => {
    try {
      const m = await action();
      appliquer(m);
      if (succes) toast.succes(succes);
      return true;
    } catch (e) {
      if (e instanceof ApiError && e.status === 409) {
        toast.erreur('Quelqu’un a modifié cette MEP, les données ont été rechargées.');
        await charger(true);
      } else if (e instanceof ApiError && e.status === 401) {
        /* redirection gérée par le client API */
      } else {
        const message = e instanceof Error ? e.message : 'Une erreur est survenue.';
        if (surErreur) surErreur(message); else toast.erreur(message);
      }
      return false;
    }
  }, [appliquer, charger, toast]);

  if (erreurChargement && !mep) {
    return (
      <main className="p-6">
        <Alerte ton="danger" icone={OctagonAlert} role="alert">{erreurChargement}</Alerte>
      </main>
    );
  }
  if (!mep) {
    return (
      <div className="flex flex-1 items-center justify-center py-24 text-fg-subtle" role="status">
        <Loader2 className="mr-2 size-5 animate-spin" aria-hidden /> Chargement de la MEP…
      </div>
    );
  }

  const demande = params.get('onglet');
  const courant: Onglet = ONGLETS.includes(demande as Onglet) ? (demande as Onglet) : ongletParDefaut(mep);
  const repliActif = mep.phase === 'RetourArriere';
  const ctx: MepContextValue = { mep, run };

  return (
    <MepContext.Provider value={ctx}>
      <DirtyContext.Provider value={registre}>
        <Tabs value={courant} onValueChange={(v) => definirOnglet(v as Onglet)} className="flex min-w-0 flex-1 flex-col">
          <EnTeteMep
            mep={mep}
            onglets={
              <TabsList aria-label="Sections de la MEP">
                <TabsTrigger value="preparation">Préparation</TabsTrigger>
                <TabsTrigger value="etapes">Étapes</TabsTrigger>
                <TabsTrigger value="execution">Exécution</TabsTrigger>
                {(mep.phase === 'Recette' || mep.phase === 'Cloturee') && (
                  <TabsTrigger value="recette">
                    Recette
                    {mep.phase === 'Recette' && <span className="size-2 rounded-full bg-warn-solid" role="img" aria-label="Recette en cours" />}
                  </TabsTrigger>
                )}
                <TabsTrigger value="retour">
                  Retour arrière
                  {repliActif && <span className="size-2 rounded-full bg-rollback-bg" role="img" aria-label="Retour arrière actif" />}
                </TabsTrigger>
                <TabsTrigger value="journal">Journal</TabsTrigger>
              </TabsList>
            }
          />

          {mep.phase === 'RetourArriere' && mep.retourArriere && (
            <div role="alert" className="flex flex-wrap items-center gap-x-4 gap-y-1 bg-rollback-bg px-6 py-3 text-white">
              <Siren className="size-6 shrink-0" aria-hidden />
              <p className="min-w-0 flex-1 text-[15px]">
                <strong className="font-extrabold">Retour arrière déclenché</strong>
                <span aria-hidden> — </span>
                <span>{mep.retourArriere.motif}</span>
              </p>
              <p className="text-sm opacity-90">
                {mep.retourArriere.par ? `Par ${mep.retourArriere.par}` : 'Déclenchement automatique'} · {formatDateHeure(mep.retourArriere.le)}
              </p>
            </div>
          )}

          {mep.nonRetourFranchi && mep.phase !== 'RetourArriere' && (
            <div role="status" className="flex flex-wrap items-center gap-x-4 gap-y-1 border-b border-warn-bd bg-warn-bg px-6 py-2.5 text-warn-fg">
              <Flag className="size-5 shrink-0 text-warn-solid" aria-hidden />
              <p className="min-w-0 flex-1 text-sm">
                <strong className="font-bold">Point de non-retour franchi</strong>
                <span aria-hidden> — </span>
                <span>étape {mep.nonRetourFranchi} : le retour arrière n’est plus possible, on corrige en avant.</span>
              </p>
            </div>
          )}

          {mep.phase === 'Cloturee' && mep.bilan && <BilanCarte bilan={mep.bilan} />}

          {rechargementEnAttente && (
            <div role="status" className="mx-6 mt-4">
              <Alerte
                ton="run"
                icone={RefreshCw}
                action={<Button taille="sm" variante="primaire" onClick={() => void charger(true)}>Recharger (perd la saisie en cours)</Button>}
              >
                Des modifications ont été reçues — recharger
              </Alerte>
            </div>
          )}

          <div key={epoch} className="flex-1">
            <TabsContent value="preparation" className="outline-none"><PreparationTab /></TabsContent>
            <TabsContent value="etapes" className="outline-none"><EtapesTab /></TabsContent>
            <TabsContent value="execution" className="outline-none"><ExecutionTab /></TabsContent>
            <TabsContent value="recette" className="outline-none"><RecetteTab /></TabsContent>
            <TabsContent value="retour" className="outline-none"><RetourArriereTab /></TabsContent>
            <TabsContent value="journal" className="outline-none"><JournalTab mepId={mep.id} tick={journalTick} /></TabsContent>
          </div>
        </Tabs>
      </DirtyContext.Provider>
    </MepContext.Provider>
  );
}
