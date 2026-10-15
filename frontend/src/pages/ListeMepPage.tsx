import {
  ArrowRight,
  CalendarDays,
  Copy,
  Factory,
  Feather,
  LayoutTemplate,
  Plus,
  Rocket,
  Search,
  TimerReset,
} from 'lucide-react';
import { useMemo, useState, type FormEvent } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { api } from '../api';
import { segmentsResume } from '../domaine';
import { calculerCompteARebours, formatDate, LIBELLE_PHASE } from '../format';
import { useAsync, useNow } from '../hooks';
import { mepCorrespond } from '../personnes';
import { metaPhase } from '../statuts';
import type { MepResume, Phase, TypeMep } from '../types';
import { Alerte } from '../ui/Alerte';
import { Avatar } from '../ui/Avatar';
import { Button } from '../ui/Button';
import { cn } from '../ui/cn';
import { Dialog } from '../ui/Dialog';
import { EmptyState } from '../ui/EmptyState';
import { Field, Input, Select } from '../ui/Input';
import { ProgressBar } from '../ui/ProgressBar';
import { Badge, StatusPill } from '../ui/StatusPill';
import { BoutonExportExcel } from '../ui/BoutonExportExcel';
import { Segmented } from '../ui/Segmented';
import { DuplicationDialog, type SourceDuplication } from '../mep/DuplicationDialog';
import { BadgeSimplifiee, estSimplifiee } from '../mep/typeMep';
import { useToast } from '../ui/Toast';
import { OctagonAlert } from 'lucide-react';

function Chrono({ m, now }: { m: MepResume; now: number }) {
  // Sans plan de retour arrière ou sans échéance : pas de compte à rebours.
  if ((m.phase !== 'Execution' && m.phase !== 'Recette') || !m.retourArriereActive || !m.echeanceRepli) return null;
  const c = calculerCompteARebours(m.echeanceRepli, now);
  if (c.niveau === 'aucun') return null;
  return (
    <span
      className={cn(
        'inline-flex items-center gap-1.5 font-mono text-xs tnum',
        c.niveau === 'depasse'
          ? 'font-bold text-danger-solid'
          : c.niveau === 'attention'
            ? 'font-semibold text-warn-solid'
            : 'text-fg-muted',
      )}
      title="Temps restant avant l’échéance de retour arrière"
    >
      <TimerReset className="size-3.5" aria-hidden />
      {c.texte}
    </span>
  );
}

const TYPES_MEP: { valeur: TypeMep; libelle: string; Icone: typeof Rocket; description: string }[] = [
  {
    valeur: 'Complete',
    libelle: 'MEP complète',
    Icone: Rocket,
    description:
      'Le plan détaillé : versions, pilote et suppléants choisis, durée, criticité et résolutions par étape.',
  },
  {
    valeur: 'Simplifiee',
    libelle: 'MEP simplifiée',
    Icone: Feather,
    description:
      'Pour une petite intervention : un pilote, les autres personnes sont suppléantes, des étapes sans durée ni criticité.',
  },
];

/** Choix du type d'une nouvelle MEP : deux cartes radio (complète par défaut). */
function ChoixType({ valeur, onChange }: { valeur: TypeMep; onChange: (t: TypeMep) => void }) {
  return (
    <fieldset>
      <legend className="mb-1.5 text-[13px] font-medium">Type de MEP</legend>
      <div className="grid gap-2 sm:grid-cols-2">
        {TYPES_MEP.map((t) => {
          const actif = valeur === t.valeur;
          return (
            <label
              key={t.valeur}
              className={cn(
                'flex cursor-pointer flex-col gap-1 rounded-lg border-[1.5px] p-3 transition-colors duration-150 has-[:focus-visible]:ring-2 has-[:focus-visible]:ring-accent',
                actif
                  ? 'border-accent bg-accent-soft shadow-[0_0_0_3px_var(--accent-soft)]'
                  : 'border-line-strong hover:border-accent',
              )}
            >
              <span className="flex items-center gap-2 text-sm font-semibold">
                <input
                  type="radio"
                  name="type-mep"
                  value={t.valeur}
                  checked={actif}
                  onChange={() => onChange(t.valeur)}
                  className="size-4 shrink-0 accent-[var(--accent)]"
                />
                <t.Icone className="size-4 text-fg-subtle" aria-hidden />
                {t.libelle}
              </span>
              <span className="pl-6 text-xs leading-snug text-fg-muted">{t.description}</span>
            </label>
          );
        })}
      </div>
      <p className="mt-1.5 text-xs text-fg-subtle">
        Le type se change pendant la préparation : une MEP simplifiée passe en complète sans rien perdre.
      </p>
    </fieldset>
  );
}

function NouvelleMepDialog({
  open,
  onOpenChange,
  modele = false,
}: {
  open: boolean;
  onOpenChange: (o: boolean) => void;
  /** Crée un modèle au lieu d'une MEP. */
  modele?: boolean;
}) {
  const navigate = useNavigate();
  const toast = useToast();
  const [outil, setOutil] = useState('');
  const [objet, setObjet] = useState('');
  const [type, setType] = useState<TypeMep>('Complete');
  const [erreur, setErreur] = useState('');
  const [envoi, setEnvoi] = useState(false);

  async function creer(e: FormEvent) {
    e.preventDefault();
    setErreur('');
    setEnvoi(true);
    try {
      // Un modèle est toujours une MEP complète.
      const mep = await api.creerMep(outil.trim(), objet.trim(), modele, modele ? 'Complete' : type);
      toast.succes(modele ? 'Modèle créé.' : 'MEP créée.');
      onOpenChange(false);
      void navigate(`/meps/${mep.id}`);
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
      titre={modele ? 'Nouveau modèle' : 'Nouvelle mise en production'}
      description={
        modele
          ? 'Un modèle ne s’exécute pas : il sert de base aux prochaines MEP. Le plan se prépare ensuite, section par section.'
          : 'Vous en serez le pilote. Le plan se prépare ensuite, section par section.'
      }
      pied={
        <>
          <Button variante="fantome" onClick={() => onOpenChange(false)}>
            Annuler
          </Button>
          <Button
            type="submit"
            form="form-nouvelle-mep"
            variante="primaire"
            chargement={envoi}
            disabled={!outil.trim()}
          >
            {modele ? 'Créer le modèle' : 'Créer la MEP'}
          </Button>
        </>
      }
    >
      <form id="form-nouvelle-mep" onSubmit={(e) => void creer(e)} className="space-y-4">
        {!modele && <ChoixType valeur={type} onChange={setType} />}
        <Field label="Outil ou application">
          {(id) => (
            <Input
              id={id}
              value={outil}
              onChange={(e) => setOutil(e.target.value)}
              placeholder="ex. Portail clients"
              autoFocus
              required
            />
          )}
        </Field>
        <Field label="Objet de l’intervention">
          {(id) => (
            <Input
              id={id}
              value={objet}
              onChange={(e) => setObjet(e.target.value)}
              placeholder={
                modele
                  ? 'ex. Montée de version standard du portail'
                  : 'ex. Passage en version 4.2 et migration de la base'
              }
            />
          )}
        </Field>
        {erreur && (
          <Alerte ton="danger" icone={OctagonAlert} role="alert">
            {erreur}
          </Alerte>
        )}
      </form>
    </Dialog>
  );
}

const FILTRES: { valeur: Phase | 'toutes'; libelle: string }[] = [
  { valeur: 'toutes', libelle: 'Toutes les phases' },
  { valeur: 'Preparation', libelle: LIBELLE_PHASE.Preparation },
  { valeur: 'Execution', libelle: LIBELLE_PHASE.Execution },
  { valeur: 'Recette', libelle: LIBELLE_PHASE.Recette },
  { valeur: 'RetourArriere', libelle: LIBELLE_PHASE.RetourArriere },
  { valeur: 'Cloturee', libelle: LIBELLE_PHASE.Cloturee },
];

export default function ListeMepPage() {
  const [vue, setVue] = useState<'meps' | 'modeles'>('meps');
  const { donnees: meps, erreur } = useAsync(() => api.meps(vue === 'modeles'), [vue]);
  const [recherche, setRecherche] = useState('');
  const [phase, setPhase] = useState<Phase | 'toutes'>('toutes');
  const { donnees: equipes } = useAsync(() => api.equipes(), []);
  const [equipe, setEquipe] = useState('');
  const [creation, setCreation] = useState(false);
  const [duplication, setDuplication] = useState<SourceDuplication | null>(null);
  const [depuisModele, setDepuisModele] = useState(false);
  const [modeles, setModeles] = useState<SourceDuplication[]>([]);
  const modele = vue === 'modeles';
  const now = useNow(1000);

  // Recherche aussi sur les personnes de la MEP et tous leurs comptes reliés ; équipe : une de ses personnes en fait partie.
  const filtrees = useMemo(
    () => (meps ?? []).filter((m) => (phase === 'toutes' || m.phase === phase) && mepCorrespond(m, recherche, equipe)),
    [meps, recherche, phase, equipe],
  );

  return (
    <main className="mx-auto w-full max-w-6xl px-6 py-8">
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Mises en production</h1>
          <p className="mt-1 text-sm text-fg-subtle">Préparez, exécutez et suivez vos interventions.</p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          {/* Clés distinctes : sans elles, React réutilise le même bouton d'une vue à l'autre et sa transition anime
              le changement de couleur (primaire ↔ secondaire). */}
          {modele ? (
            <Button
              key="nouveau-modele"
              variante="primaire"
              icone={<Plus className="size-4" aria-hidden />}
              onClick={() => setCreation(true)}
            >
              Nouveau modèle
            </Button>
          ) : (
            <>
              <Button
                key="depuis-modele"
                icone={<LayoutTemplate className="size-4" aria-hidden />}
                onClick={() => {
                  setDepuisModele(true);
                  api
                    .meps(true)
                    .then((l) => setModeles(l.map((m) => ({ id: m.id, outil: m.outil, estModele: true }))))
                    .catch(() => setModeles([]));
                }}
              >
                Nouvelle MEP depuis un modèle
              </Button>
              <Button
                key="nouvelle-mep"
                variante="primaire"
                icone={<Plus className="size-4" aria-hidden />}
                onClick={() => setCreation(true)}
              >
                Nouvelle MEP
              </Button>
            </>
          )}
        </div>
      </div>

      <div className="mt-6 flex flex-wrap items-center gap-3">
        <div className="relative min-w-60 flex-1 sm:max-w-sm">
          <Search
            className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-fg-subtle"
            aria-hidden
          />
          <Input
            aria-label="Rechercher une MEP"
            placeholder="Rechercher (outil, version, personne…)"
            className="pl-9"
            value={recherche}
            onChange={(e) => setRecherche(e.target.value)}
          />
        </div>
        <Segmented
          label="Afficher les MEP ou les modèles"
          valeur={vue}
          onChange={(v) => {
            setVue(v);
            setPhase('toutes');
          }}
          options={[
            {
              valeur: 'meps',
              libelle: (
                <>
                  <Rocket className="size-4" aria-hidden />
                  MEP
                </>
              ),
            },
            {
              valeur: 'modeles',
              libelle: (
                <>
                  <LayoutTemplate className="size-4" aria-hidden />
                  Modèles
                </>
              ),
            },
          ]}
        />
        {equipes && equipes.length > 0 && (
          <div className="w-52">
            <Select aria-label="Filtrer par équipe" value={equipe} onChange={(e) => setEquipe(e.target.value)}>
              <option value="">Toutes les équipes</option>
              {equipes.map((e) => (
                <option key={e.id} value={e.id}>
                  {e.nom}
                </option>
              ))}
            </Select>
          </div>
        )}
        {!modele && (
          <div className="w-52">
            <Select
              aria-label="Filtrer par phase"
              value={phase}
              onChange={(e) => setPhase(e.target.value as Phase | 'toutes')}
            >
              {FILTRES.map((f) => (
                <option key={f.valeur} value={f.valeur}>
                  {f.libelle}
                </option>
              ))}
            </Select>
          </div>
        )}
        <BoutonExportExcel
          className="sm:ml-auto"
          url={api.urlExportMeps({ modele, equipe, phase, recherche })}
          disabled={meps === null}
          title={
            modele
              ? 'Les modèles affichés, recherche et équipe comprises.'
              : 'Les MEP affichées, filtres compris (recherche, équipe, phase).'
          }
        />
      </div>

      {erreur && (
        <Alerte ton="danger" icone={OctagonAlert} role="alert" className="mt-6">
          {erreur}
        </Alerte>
      )}

      {meps === null && !erreur && (
        <div className="mt-6 space-y-3" aria-busy="true">
          {[0, 1, 2].map((i) => (
            <div key={i} className="h-24 animate-pulse rounded-xl border border-line bg-surface" />
          ))}
        </div>
      )}

      {meps !== null && meps.length === 0 && (
        <div className="mt-6 rounded-xl border border-dashed border-line-strong bg-surface">
          <EmptyState
            icone={Rocket}
            titre={modele ? 'Aucun modèle pour l’instant' : 'Aucune mise en production pour l’instant'}
            description={
              modele
                ? 'Créez un modèle, ou dupliquez une MEP en cochant « En faire un modèle » : il servira de base à vos prochaines interventions.'
                : 'Créez votre première MEP pour préparer le plan, désigner l’équipe et suivre l’exécution en direct.'
            }
            action={
              <Button
                variante="primaire"
                icone={<Plus className="size-4" aria-hidden />}
                onClick={() => setCreation(true)}
              >
                {modele ? 'Nouveau modèle' : 'Nouvelle MEP'}
              </Button>
            }
          />
        </div>
      )}

      {meps !== null && meps.length > 0 && filtrees.length === 0 && (
        <div className="mt-6 rounded-xl border border-dashed border-line-strong bg-surface">
          <EmptyState
            icone={Search}
            titre="Aucun résultat"
            description="Aucune MEP ne correspond à ces critères."
            action={
              <Button
                onClick={() => {
                  setRecherche('');
                  setPhase('toutes');
                  setEquipe('');
                }}
              >
                Effacer les filtres
              </Button>
            }
          />
        </div>
      )}

      <ul className="mt-6 space-y-3">
        {filtrees.map((m) => {
          const meta = metaPhase(m.phase, m.issue);
          return (
            <li key={m.id} className="relative">
              <Link
                to={`/meps/${m.id}`}
                className="group block rounded-xl border border-line bg-surface p-5 shadow-sm transition-[border-color,box-shadow,transform] duration-150 hover:border-line-strong hover:shadow-md"
              >
                <div className="flex flex-wrap items-start justify-between gap-x-6 gap-y-3">
                  <div className="min-w-0 flex-1">
                    <div className="flex flex-wrap items-center gap-x-3 gap-y-1.5">
                      <h2 className="flex items-center gap-2 text-base font-semibold tracking-tight">
                        <span className="truncate">{m.outil || 'Sans nom'}</span>
                        {!estSimplifiee(m) && m.versionCible.trim() && (
                          <>
                            <ArrowRight className="size-4 shrink-0 text-fg-subtle" aria-hidden />
                            <span className="font-mono text-[15px] font-semibold">{m.versionCible}</span>
                          </>
                        )}
                      </h2>
                      {!m.estModele && (
                        <StatusPill ton={meta.ton} icone={meta.Icone} anime={meta.anime}>
                          {meta.libelle}
                        </StatusPill>
                      )}
                      {m.estModele && (
                        <Badge ton="later">
                          <LayoutTemplate className="size-3" aria-hidden />
                          Modèle
                        </Badge>
                      )}
                      {m.toucheProduction && (
                        <Badge ton="warn">
                          <Factory className="size-3" aria-hidden />
                          Production
                          <span className="sr-only"> : la MEP touche un environnement de production</span>
                        </Badge>
                      )}
                      {estSimplifiee(m) && <BadgeSimplifiee className="text-[11px]" />}
                    </div>
                    {m.objet && <p className="mt-1 line-clamp-1 text-sm text-fg-muted">{m.objet}</p>}
                  </div>
                  <div className="flex items-center gap-5 text-sm text-fg-muted">
                    <span className="inline-flex items-center gap-1.5">
                      <CalendarDays className="size-4 text-fg-subtle" aria-hidden />
                      {m.estModele
                        ? m.creeLe
                          ? `Créé le ${formatDate(m.creeLe)}`
                          : 'Modèle'
                        : m.dateMep
                          ? `Démarrée le ${formatDate(m.dateMep)}`
                          : m.creeLe
                            ? `Créée le ${formatDate(m.creeLe)}`
                            : 'Pas encore démarrée'}
                    </span>
                    {!m.estModele && (
                      <span className="inline-flex items-center gap-2">
                        {m.pilote ? <Avatar nom={m.pilote} taille="sm" /> : null}
                        <span>{m.pilote ?? 'Pas de pilote'}</span>
                      </span>
                    )}
                  </div>
                </div>
                {m.estModele ? (
                  <p className="mt-4 pr-32 text-xs text-fg-subtle tnum">
                    {m.nbEtapes} étape{m.nbEtapes > 1 ? 's' : ''} de déploiement
                  </p>
                ) : (
                  <div className="mt-4 flex flex-wrap items-center gap-x-6 gap-y-2 pr-32">
                    <ProgressBar
                      className="min-w-48 flex-1"
                      segments={segmentsResume(m.nbEtapes, m.nbReussies, m.nbEchecs)}
                    />
                    <span className="text-xs text-fg-subtle tnum">
                      {m.nbReussies}/{m.nbEtapes} étapes réussies
                      {m.nbEchecs > 0 && (
                        <span className="font-semibold text-danger-solid"> · {m.nbEchecs} en échec</span>
                      )}
                    </span>
                    <Chrono m={m} now={now} />
                  </div>
                )}
              </Link>
              <Button
                taille="sm"
                className="absolute right-4 bottom-4"
                icone={<Copy className="size-3.5" aria-hidden />}
                aria-label={`Dupliquer ${m.outil || 'la MEP'}`}
                onClick={() => setDuplication({ id: m.id, outil: m.outil, estModele: m.estModele })}
              >
                Dupliquer
              </Button>
            </li>
          );
        })}
      </ul>
      <NouvelleMepDialog open={creation} onOpenChange={setCreation} modele={modele} />
      <DuplicationDialog
        open={duplication !== null}
        onOpenChange={(o) => {
          if (!o) setDuplication(null);
        }}
        source={duplication}
      />
      <DuplicationDialog open={depuisModele} onOpenChange={setDepuisModele} modeles={modeles} />
    </main>
  );
}
