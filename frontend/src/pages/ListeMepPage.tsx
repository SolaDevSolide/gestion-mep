import { ArrowRight, CalendarDays, Plus, Rocket, Search, TimerReset } from 'lucide-react';
import { useMemo, useState, type FormEvent } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { api } from '../api';
import { segmentsResume } from '../domaine';
import { calculerCompteARebours, formatDate, LIBELLE_PHASE } from '../format';
import { useAsync, useNow } from '../hooks';
import { metaPhase } from '../statuts';
import type { MepResume, Phase } from '../types';
import { Alerte } from '../ui/Alerte';
import { Avatar } from '../ui/Avatar';
import { Button } from '../ui/Button';
import { cn } from '../ui/cn';
import { Dialog } from '../ui/Dialog';
import { EmptyState } from '../ui/EmptyState';
import { Field, Input, Select } from '../ui/Input';
import { ProgressBar } from '../ui/ProgressBar';
import { StatusPill } from '../ui/StatusPill';
import { useToast } from '../ui/Toast';
import { OctagonAlert } from 'lucide-react';

function Chrono({ m, now }: { m: MepResume; now: number }) {
  if (m.phase !== 'Execution') return null;
  const c = calculerCompteARebours(m.echeanceRepli, now);
  if (c.niveau === 'aucun') return null;
  return (
    <span
      className={cn(
        'inline-flex items-center gap-1.5 font-mono text-xs tnum',
        c.niveau === 'depasse' ? 'font-bold text-danger-solid' : c.niveau === 'attention' ? 'font-semibold text-warn-solid' : 'text-fg-muted',
      )}
      title="Temps restant avant l’échéance de retour arrière"
    >
      <TimerReset className="size-3.5" aria-hidden />
      {c.texte}
    </span>
  );
}

function NouvelleMepDialog({ open, onOpenChange }: { open: boolean; onOpenChange: (o: boolean) => void }) {
  const navigate = useNavigate();
  const toast = useToast();
  const [outil, setOutil] = useState('');
  const [objet, setObjet] = useState('');
  const [erreur, setErreur] = useState('');
  const [envoi, setEnvoi] = useState(false);

  async function creer(e: FormEvent) {
    e.preventDefault();
    setErreur('');
    setEnvoi(true);
    try {
      const mep = await api.creerMep(outil.trim(), objet.trim());
      toast.succes('MEP créée.');
      onOpenChange(false);
      navigate(`/meps/${mep.id}`);
    } catch (err) {
      setErreur(err instanceof Error ? err.message : 'Création impossible.');
    } finally {
      setEnvoi(false);
    }
  }

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      titre="Nouvelle mise en production"
      description="Vous en serez le pilote. Le plan se prépare ensuite, section par section."
      pied={
        <>
          <Button variante="fantome" onClick={() => onOpenChange(false)}>Annuler</Button>
          <Button type="submit" form="form-nouvelle-mep" variante="primaire" chargement={envoi} disabled={!outil.trim()}>Créer la MEP</Button>
        </>
      }
    >
      <form id="form-nouvelle-mep" onSubmit={(e) => void creer(e)} className="space-y-4">
        <Field label="Outil ou application">{(id) => <Input id={id} value={outil} onChange={(e) => setOutil(e.target.value)} placeholder="ex. Portail clients" autoFocus required />}</Field>
        <Field label="Objet de l’intervention">{(id) => <Input id={id} value={objet} onChange={(e) => setObjet(e.target.value)} placeholder="ex. Passage en version 4.2 et migration de la base" />}</Field>
        {erreur && <Alerte ton="danger" icone={OctagonAlert} role="alert">{erreur}</Alerte>}
      </form>
    </Dialog>
  );
}

const FILTRES: { valeur: Phase | 'toutes'; libelle: string }[] = [
  { valeur: 'toutes', libelle: 'Toutes les phases' },
  { valeur: 'Preparation', libelle: LIBELLE_PHASE.Preparation },
  { valeur: 'Execution', libelle: LIBELLE_PHASE.Execution },
  { valeur: 'RetourArriere', libelle: LIBELLE_PHASE.RetourArriere },
  { valeur: 'Cloturee', libelle: LIBELLE_PHASE.Cloturee },
];

export default function ListeMepPage() {
  const { donnees: meps, erreur } = useAsync(() => api.meps(), []);
  const [recherche, setRecherche] = useState('');
  const [phase, setPhase] = useState<Phase | 'toutes'>('toutes');
  const [creation, setCreation] = useState(false);
  const now = useNow(1000);

  const filtrees = useMemo(() => {
    const q = recherche.trim().toLowerCase();
    return (meps ?? []).filter((m) =>
      (phase === 'toutes' || m.phase === phase) &&
      (!q || [m.outil, m.versionCible, m.objet, m.pilote ?? ''].some((t) => t.toLowerCase().includes(q))),
    );
  }, [meps, recherche, phase]);

  return (
    <main className="mx-auto w-full max-w-6xl px-6 py-8">
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Mises en production</h1>
          <p className="mt-1 text-sm text-fg-subtle">Préparez, exécutez et suivez vos interventions.</p>
        </div>
        <Button variante="primaire" icone={<Plus className="size-4" aria-hidden />} onClick={() => setCreation(true)}>Nouvelle MEP</Button>
      </div>

      <div className="mt-6 flex flex-wrap items-center gap-3">
        <div className="relative min-w-60 flex-1 sm:max-w-sm">
          <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-fg-subtle" aria-hidden />
          <Input aria-label="Rechercher une MEP" placeholder="Rechercher (outil, version, pilote…)" className="pl-9" value={recherche} onChange={(e) => setRecherche(e.target.value)} />
        </div>
        <div className="w-52">
          <Select aria-label="Filtrer par phase" value={phase} onChange={(e) => setPhase(e.target.value as Phase | 'toutes')}>
            {FILTRES.map((f) => <option key={f.valeur} value={f.valeur}>{f.libelle}</option>)}
          </Select>
        </div>
      </div>

      {erreur && <Alerte ton="danger" icone={OctagonAlert} role="alert" className="mt-6">{erreur}</Alerte>}

      {meps === null && !erreur && (
        <div className="mt-6 space-y-3" aria-busy="true">
          {[0, 1, 2].map((i) => <div key={i} className="h-24 animate-pulse rounded-xl border border-line bg-surface" />)}
        </div>
      )}

      {meps !== null && meps.length === 0 && (
        <div className="mt-6 rounded-xl border border-dashed border-line-strong bg-surface">
          <EmptyState
            icone={Rocket}
            titre="Aucune mise en production pour l’instant"
            description="Créez votre première MEP pour préparer le plan, désigner l’équipe et suivre l’exécution en direct."
            action={<Button variante="primaire" icone={<Plus className="size-4" aria-hidden />} onClick={() => setCreation(true)}>Nouvelle MEP</Button>}
          />
        </div>
      )}

      {meps !== null && meps.length > 0 && filtrees.length === 0 && (
        <div className="mt-6 rounded-xl border border-dashed border-line-strong bg-surface">
          <EmptyState icone={Search} titre="Aucun résultat" description="Aucune MEP ne correspond à ces critères." action={<Button onClick={() => { setRecherche(''); setPhase('toutes'); }}>Effacer les filtres</Button>} />
        </div>
      )}

      <ul className="mt-6 space-y-3">
        {filtrees.map((m) => {
          const meta = metaPhase(m.phase, m.issue ?? null);
          return (
            <li key={m.id}>
              <Link
                to={`/meps/${m.id}`}
                className="group block rounded-xl border border-line bg-surface p-5 shadow-sm transition-[border-color,box-shadow,transform] duration-150 hover:border-line-strong hover:shadow-md"
              >
                <div className="flex flex-wrap items-start justify-between gap-x-6 gap-y-3">
                  <div className="min-w-0 flex-1">
                    <div className="flex flex-wrap items-center gap-x-3 gap-y-1.5">
                      <h2 className="flex items-center gap-2 text-base font-semibold tracking-tight">
                        <span className="truncate">{m.outil || 'Sans nom'}</span>
                        <ArrowRight className="size-4 shrink-0 text-fg-subtle" aria-hidden />
                        <span className="font-mono text-[15px] font-semibold">{m.versionCible || '?'}</span>
                      </h2>
                      <StatusPill ton={meta.ton} icone={meta.Icone} anime={meta.anime}>{meta.libelle}</StatusPill>
                    </div>
                    {m.objet && <p className="mt-1 line-clamp-1 text-sm text-fg-muted">{m.objet}</p>}
                  </div>
                  <div className="flex items-center gap-5 text-sm text-fg-muted">
                    <span className="inline-flex items-center gap-1.5"><CalendarDays className="size-4 text-fg-subtle" aria-hidden />{formatDate(m.dateMep)}</span>
                    <span className="inline-flex items-center gap-2">
                      {m.pilote ? <Avatar nom={m.pilote} taille="sm" /> : null}
                      <span>{m.pilote ?? 'Pas de pilote'}</span>
                    </span>
                  </div>
                </div>
                <div className="mt-4 flex flex-wrap items-center gap-x-6 gap-y-2">
                  <ProgressBar className="min-w-48 flex-1" segments={segmentsResume(m.nbEtapes, m.nbReussies, m.nbEchecs)} />
                  <span className="text-xs text-fg-subtle tnum">{m.nbReussies}/{m.nbEtapes} étapes réussies{m.nbEchecs > 0 && <span className="font-semibold text-danger-solid"> · {m.nbEchecs} en échec</span>}</span>
                  <Chrono m={m} now={now} />
                </div>
              </Link>
            </li>
          );
        })}
      </ul>
      <NouvelleMepDialog open={creation} onOpenChange={setCreation} />
    </main>
  );
}
