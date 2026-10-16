import { Flag, OctagonAlert, Siren } from 'lucide-react';
import { useCallback, useContext, useEffect, useMemo, useRef, useState, type KeyboardEvent } from 'react';
import { useParams, useSearchParams } from 'react-router-dom';
import { api, ApiError } from '../api';
import { AuthContext } from '../auth';
import { etapesDuPlan } from '../domaine';
import { ouvrirFlux } from '../flux';
import { formatDateHeure } from '../format';
import { BilanCarte } from '../mep/BilanCarte';
import { EnTeteMep } from '../mep/EnTeteMep';
import EtapesTab from '../mep/EtapesTab';
import ExecutionTab from '../mep/ExecutionTab';
import JournalTab from '../mep/JournalTab';
import {
  estAncienneAdresse,
  estOnglet,
  lireVue,
  parametresVue,
  sousOngletsVisibles,
  vueParDefaut,
  type SousOnglet,
  type Vue,
} from '../mep/onglets';
import PreparationTab from '../mep/PreparationTab';
import RecetteTab from '../mep/RecetteTab';
import RetourArriereTab from '../mep/RetourArriereTab';
import VueEnsembleTab from '../mep/ensemble/VueEnsembleTab';
import {
  libellePriseDeMain,
  lireVerrous,
  nouvellesPrisesDeMain,
  VerrousContext,
  type ValeurVerrous,
} from '../mep/graphe/verrouEtape';
import { MepContext, type MepContextValue } from '../mepContext';
import { nouvelleSession, PresenceProvider, type PresenceValeur } from '../presence';
import type { Mep, Phase, Presence, Verrou } from '../types';
import { Alerte } from '../ui/Alerte';
import { cn } from '../ui/cn';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '../ui/Tabs';
import { SqueletteMep } from '../mep/SqueletteMep';
import { useToast } from '../ui/Toast';

const LIBELLES_SOUS: Record<SousOnglet, string> = { etapes: 'Étapes', retour: 'Retour arrière', recette: 'Recette' };

/** Délai de regroupement des déclarations de présence (focus qui passe d'un champ à l'autre). */
const DELAI_PRESENCE = 250;

/** Sous-onglets de « Plan », sous la barre d'onglets : Étapes, Retour arrière, Recette, avec leur nombre d'éléments. */
function SousOngletsPlan({ mep, sous, onChange }: { mep: Mep; sous: SousOnglet; onChange: (s: SousOnglet) => void }) {
  const nombres: Record<SousOnglet, number> = {
    etapes: etapesDuPlan(mep, 'Deploiement').length,
    retour: etapesDuPlan(mep, 'RetourArriere').length,
    recette: mep.fichesRecette.length,
  };
  const indicateur: Partial<Record<SousOnglet, { classe: string; label: string }>> = {
    ...(mep.phase === 'RetourArriere' && { retour: { classe: 'bg-rollback-bg', label: 'Retour arrière actif' } }),
    ...(mep.phase === 'Recette' && { recette: { classe: 'bg-warn-solid', label: 'Recette en cours' } }),
  };
  // Sans plan de retour arrière (et sans retour arrière déclenché), pas de sous-onglet Retour arrière.
  const visibles = sousOngletsVisibles(mep);
  const clavier = (e: KeyboardEvent<HTMLDivElement>) => {
    const i = visibles.indexOf(sous);
    const suivant =
      e.key === 'ArrowRight'
        ? visibles[(i + 1) % visibles.length]
        : e.key === 'ArrowLeft'
          ? visibles[(i + visibles.length - 1) % visibles.length]
          : null;
    if (!suivant) return;
    e.preventDefault();
    onChange(suivant);
    e.currentTarget.querySelector<HTMLButtonElement>(`[data-sous="${suivant}"]`)?.focus();
  };
  return (
    <div
      role="tablist"
      tabIndex={-1}
      aria-label="Parties du plan"
      onKeyDown={clavier}
      className="-mx-4 flex gap-1 overflow-x-auto border-t border-line px-5 py-1.5 [scrollbar-width:none]!"
    >
      {visibles.map((s) => {
        const actif = s === sous;
        const ind = indicateur[s];
        return (
          <button
            key={s}
            type="button"
            role="tab"
            id={`sous-onglet-${s}`}
            data-sous={s}
            aria-selected={actif}
            aria-controls="panneau-plan"
            tabIndex={actif ? 0 : -1}
            onClick={() => onChange(s)}
            className={cn(
              'inline-flex h-8 shrink-0 items-center gap-2 rounded-lg px-3 text-[13px] font-medium transition-colors duration-150',
              actif ? 'bg-accent-soft text-accent-text' : 'text-fg-subtle hover:bg-surface-2 hover:text-fg',
            )}
          >
            {LIBELLES_SOUS[s]}
            <span
              className={cn(
                'min-w-5 rounded-full px-1.5 text-center text-[11px] font-semibold tnum',
                actif ? 'bg-accent text-accent-fg' : 'bg-surface-2 text-fg-muted',
              )}
            >
              {nombres[s]}
            </span>
            {ind && <span className={cn('size-2 rounded-full', ind.classe)} role="img" aria-label={ind.label} />}
          </button>
        );
      })}
    </div>
  );
}

export default function MepPage() {
  const { id = '' } = useParams();
  const toast = useToast();
  const moi = useContext(AuthContext)?.utilisateur?.id ?? null;
  const [params, setParams] = useSearchParams();
  const [mep, setMep] = useState<Mep | null>(null);
  const [erreurChargement, setErreurChargement] = useState('');
  const [journalTick, setJournalTick] = useState(0);
  const [presences, setPresences] = useState<Presence[]>([]);
  const [verrous, setVerrous] = useState<Verrou[] | null>(null);

  const versionRef = useRef(0);
  const phaseRef = useRef<Phase | null>(null);

  // setParams change à chaque changement d'URL (React Router) : lu par une référence, pour que changer d'onglet ne
  // recrée pas `charger` et ne recharge pas toute la MEP.
  const setParamsRef = useRef(setParams);
  useEffect(() => {
    setParamsRef.current = setParams;
  }, [setParams]);
  /** Change d'onglet ou de sous-onglet ; `garder` conserve les autres paramètres de l'adresse (`?etape=…`). */
  const definirVue = useCallback(
    (v: Vue, garder = false) =>
      setParamsRef.current((p) => parametresVue(v, garder ? p : undefined), { replace: true }),
    [],
  );

  // MEP affichée, lue par les réponses qui arrivent après un changement de page.
  const idRef = useRef(id);
  useEffect(() => {
    idRef.current = id;
  }, [id]);

  const appliquer = useCallback((m: Mep) => {
    // Réponse arrivée en retard : celle d'une autre MEP (changement de page) ou d'une version plus ancienne que celle
    // affichée (une synchronisation partie avant une action et revenue après elle). On l'ignore.
    if (m.id !== idRef.current || m.version < versionRef.current) return;
    versionRef.current = m.version;
    setMep(m);
    setJournalTick((t) => t + 1);
    if (phaseRef.current !== null && phaseRef.current !== m.phase) {
      setParamsRef.current(parametresVue(vueParDefaut(m)), { replace: true });
    }
    phaseRef.current = m.phase;
  }, []);

  const charger = useCallback(async () => {
    try {
      appliquer(await api.mep(id));
      setErreurChargement('');
    } catch (e) {
      if (e instanceof ApiError && e.status === 401) return;
      setErreurChargement(e instanceof Error ? e.message : 'Chargement impossible.');
    }
  }, [id, appliquer]);

  /**
   * Rechargement déclenché à distance : la MEP à jour s'affiche aussitôt. Une saisie en cours n'est pas perdue :
   * chaque formulaire garde son brouillon (`useDraft`) et signale si sa section a changé entre-temps.
   */
  const synchroniser = useCallback(
    async (silencieux = false) => {
      try {
        appliquer(await api.mep(id, silencieux));
      } catch {
        /* silencieux : prochain cycle */
      }
    },
    [id, appliquer],
  );

  useEffect(() => {
    phaseRef.current = null;
    versionRef.current = 0;
    setMep(null);
    setPresences([]);
    setVerrous(null);
    void charger();
  }, [charger]);

  // ---------- Présence : éléments de cet onglet, déclarés au serveur (regroupés), redéclarés à chaque ouverture du flux.
  const session = useMemo(() => nouvelleSession(), []);
  const elementsRef = useRef(new Map<string, string>());
  const envoyeRef = useRef<string | null>(null);
  const minuterieRef = useRef<number | undefined>(undefined);
  const envoyerPresence = useCallback(() => {
    window.clearTimeout(minuterieRef.current);
    minuterieRef.current = window.setTimeout(() => {
      const elements = [...new Set(elementsRef.current.values())].sort();
      const cle = elements.join('\n');
      if (cle === envoyeRef.current) return;
      envoyeRef.current = cle;
      api.declarerPresence(id, session, elements).catch(() => {
        // Flux pas encore (ou plus) ouvert : redéclaré à sa prochaine ouverture.
        envoyeRef.current = null;
      });
    }, DELAI_PRESENCE);
  }, [id, session]);
  useEffect(() => () => window.clearTimeout(minuterieRef.current), []);

  const declarer = useCallback<PresenceValeur['declarer']>(
    (jeton, cle) => {
      const avant = elementsRef.current.get(jeton);
      if (avant === (cle ?? undefined)) return;
      if (cle) elementsRef.current.set(jeton, cle);
      else elementsRef.current.delete(jeton);
      envoyerPresence();
    },
    [envoyerPresence],
  );

  // Flux temps réel (SSE) : versions et présence ; recréé après une erreur HTTP.
  useEffect(
    () =>
      ouvrirFlux(
        `/api/meps/${id}/flux?session=${session}`,
        (version) => {
          if (version > versionRef.current) void synchroniser();
        },
        () => void synchroniser(),
        {
          surPresence: setPresences,
          surVerrous: (v) => {
            const liste = lireVerrous(v);
            if (liste) setVerrous(liste);
          },
          surOuverture: () => {
            envoyeRef.current = null;
            if (elementsRef.current.size > 0) envoyerPresence();
          },
        },
      ),
    [id, session, synchroniser, envoyerPresence],
  );

  const valeurVerrous = useMemo<ValeurVerrous>(() => ({ session, verrous }), [session, verrous]);

  // Prise de main sur une étape (par n'importe qui, y compris sur la vôtre) : annoncée à toutes les personnes présentes.
  const verrousAvantRef = useRef<Verrou[] | null>(null);
  const mepRef = useRef(mep);
  useEffect(() => {
    mepRef.current = mep;
  }, [mep]);
  useEffect(() => {
    const prises = nouvellesPrisesDeMain(verrousAvantRef.current, verrous);
    verrousAvantRef.current = verrous;
    for (const v of prises) {
      if (v.session === session) continue; // c'est cet onglet qui l'a prise
      const code = mepRef.current?.etapes.find((e) => e.id === v.etapeId)?.code ?? 'une étape';
      toast.succes(libellePriseDeMain(v, code));
    }
  }, [verrous, session, toast]);

  const presence = useMemo<PresenceValeur>(
    () => ({ autres: presences.filter((p) => p.utilisateurId !== moi), declarer }),
    [presences, moi, declarer],
  );

  // Rafraîchissement périodique (disponibilités dépendantes de l'heure, échéance automatique), sans indicateur.
  useEffect(() => {
    const t = window.setInterval(() => void synchroniser(true), 30_000);
    return () => window.clearInterval(t);
  }, [synchroniser]);

  const run = useCallback<MepContextValue['run']>(
    async (action, succes, surErreur) => {
      try {
        const m = await action();
        appliquer(m);
        if (succes) toast.succes(succes);
        return true;
      } catch (e) {
        if (e instanceof ApiError && e.status === 409) {
          toast.erreur('Quelqu’un a modifié cette MEP entre-temps : elle est à jour, votre saisie est conservée.');
          await charger();
        } else if (e instanceof ApiError && e.status === 401) {
          /* redirection gérée par le client API */
        } else {
          const message = e instanceof Error ? e.message : 'Une erreur est survenue.';
          if (surErreur) surErreur(message);
          else toast.erreur(message);
        }
        return false;
      }
    },
    [appliquer, charger, toast],
  );

  // Anciennes adresses (?onglet=etapes|retour|recette) : réécrites vers Plan et le bon sous-onglet.
  const ancienne = mep !== null && estAncienneAdresse(params);
  useEffect(() => {
    if (ancienne && mep) definirVue(lireVue(params, mep), true);
  }, [ancienne, mep, params, definirVue]);

  if (erreurChargement && !mep) {
    return (
      <main className="p-6">
        <Alerte ton="danger" icone={OctagonAlert} role="alert">
          {erreurChargement}
        </Alerte>
      </main>
    );
  }
  if (!mep) return <SqueletteMep onglet={params.get('plan') ?? params.get('onglet')} />;

  const vue = lireVue(params, mep);
  const repliActif = mep.phase === 'RetourArriere';
  const ctx: MepContextValue = { mep, run };

  return (
    <MepContext.Provider value={ctx}>
      <PresenceProvider value={presence}>
        <VerrousContext.Provider value={valeurVerrous}>
          <Tabs
            value={vue.onglet}
            onValueChange={(v) => {
              if (estOnglet(v)) definirVue({ onglet: v, sous: vueParDefaut(mep).sous });
            }}
            className="flex min-w-0 flex-1 flex-col"
          >
            <EnTeteMep
              mep={mep}
              onglets={
                <>
                  <TabsList aria-label="Sections de la MEP">
                    {!mep.estModele && (
                      <TabsTrigger value="ensemble">
                        Vue d’ensemble
                        {mep.phase !== 'Preparation' && mep.phase !== 'Cloturee' && (
                          <span
                            className="size-2 animate-pulse rounded-full bg-ok-solid"
                            role="img"
                            aria-label="En direct"
                          />
                        )}
                      </TabsTrigger>
                    )}
                    <TabsTrigger value="preparation">Préparation</TabsTrigger>
                    <TabsTrigger value="plan">
                      Plan
                      {repliActif && (
                        <span
                          className="size-2 rounded-full bg-rollback-bg"
                          role="img"
                          aria-label="Retour arrière actif"
                        />
                      )}
                      {mep.phase === 'Recette' && (
                        <span className="size-2 rounded-full bg-warn-solid" role="img" aria-label="Recette en cours" />
                      )}
                    </TabsTrigger>
                    {!mep.estModele && <TabsTrigger value="execution">Exécution</TabsTrigger>}
                    <TabsTrigger value="journal">Journal</TabsTrigger>
                  </TabsList>
                  {vue.onglet === 'plan' && (
                    <SousOngletsPlan
                      mep={mep}
                      sous={vue.sous}
                      onChange={(s) => definirVue({ onglet: 'plan', sous: s })}
                    />
                  )}
                </>
              }
            />

            {mep.phase === 'RetourArriere' && mep.retourArriere && (
              <div
                role="alert"
                className="flex flex-wrap items-center gap-x-4 gap-y-1 bg-rollback-bg px-6 py-3 text-white"
              >
                <Siren className="size-6 shrink-0" aria-hidden />
                <p className="min-w-0 flex-1 text-[15px]">
                  <strong className="font-extrabold">Retour arrière déclenché</strong>
                  <span aria-hidden> — </span>
                  <span>{mep.retourArriere.motif}</span>
                </p>
                <p className="text-sm opacity-90">
                  {mep.retourArriere.par ? `Par ${mep.retourArriere.par}` : 'Déclenchement automatique'} ·{' '}
                  {formatDateHeure(mep.retourArriere.le)}
                </p>
              </div>
            )}

            {mep.nonRetourFranchi && mep.phase !== 'RetourArriere' && (
              <div
                role="status"
                className="flex flex-wrap items-center gap-x-4 gap-y-1 border-b border-warn-bd bg-warn-bg px-6 py-2.5 text-warn-fg"
              >
                <Flag className="size-5 shrink-0 text-warn-solid" aria-hidden />
                <p className="min-w-0 flex-1 text-sm">
                  <strong className="font-bold">Point de non-retour franchi</strong>
                  <span aria-hidden> — </span>
                  <span>
                    étape {mep.nonRetourFranchi} : le retour arrière n’est plus possible, on corrige en avant.
                  </span>
                </p>
              </div>
            )}

            {mep.phase === 'Cloturee' && mep.bilan && <BilanCarte bilan={mep.bilan} mepId={mep.id} />}

            <div className="flex-1">
              <TabsContent value="ensemble" className="outline-none">
                <VueEnsembleTab />
              </TabsContent>
              <TabsContent value="preparation" className="outline-none">
                <PreparationTab />
              </TabsContent>
              <TabsContent value="plan" className="outline-none">
                <div role="tabpanel" id="panneau-plan" aria-labelledby={`sous-onglet-${vue.sous}`}>
                  {vue.sous === 'etapes' && <EtapesTab />}
                  {vue.sous === 'retour' && <RetourArriereTab />}
                  {vue.sous === 'recette' && <RecetteTab />}
                </div>
              </TabsContent>
              <TabsContent value="execution" className="outline-none">
                <ExecutionTab />
              </TabsContent>
              <TabsContent value="journal" className="outline-none">
                <JournalTab mepId={mep.id} tick={journalTick} />
              </TabsContent>
            </div>
          </Tabs>
        </VerrousContext.Provider>
      </PresenceProvider>
    </MepContext.Provider>
  );
}
