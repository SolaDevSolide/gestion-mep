import { ArrowLeft, ArrowRight, Check, ChevronDown, CircleCheck, Info, Link2, Plus, Unlink } from 'lucide-react';
import { useMemo, useState } from 'react';
import { api } from '../../api';
import { compte } from '../../format';
import { useReportDirty } from '../../hooks';
import type { ApplicationLiaison, CatalogueLiaison, LiaisonNoosphere } from '../../types';
import { Alerte } from '../../ui/Alerte';
import { Button } from '../../ui/Button';
import { Card, CardHeader } from '../../ui/Card';
import { CaseSeule, Checkbox } from '../../ui/Checkbox';
import { cn } from '../../ui/cn';
import { Segmented } from '../../ui/Segmented';
import { Badge, StatusPill } from '../../ui/StatusPill';
import { useToast } from '../../ui/Toast';
import {
  applicationsDu,
  basculerApplication,
  basculerDomaine,
  aRapprocher,
  choixInitial,
  etatDomaine,
  mentionCycle,
  rapprochementsInitiaux,
  resumeChoix,
  saisieSuivi,
  type ChoixSuivi,
  type Rapprochement,
} from './liaisons';

type Vue = 'domaines' | 'une';

/**
 * Applications suivies (vue 3c), puis rapprochement des nouvelles avec le catalogue (vue 3d) : rien n'est appliqué avant
 * « Valider et synchroniser ». Un rattachement par le nom, même identique, se confirme toujours ici.
 */
export function SuiviApplications({
  liaison,
  catalogue,
  onTermine,
  onAnnuler,
  rapprocherDabord = false,
}: {
  liaison: LiaisonNoosphere;
  catalogue: CatalogueLiaison;
  onTermine: (c: CatalogueLiaison, l: LiaisonNoosphere) => void;
  onAnnuler?: () => void;
  /** Ouvrir directement sur le rapprochement des applications mises de côté par la synchronisation. */
  rapprocherDabord?: boolean;
}) {
  const toast = useToast();
  const [choix, setChoix] = useState<ChoixSuivi>(() => choixInitial(catalogue));
  const [auto, setAuto] = useState(liaison.suiviAutomatique);
  const [etape, setEtape] = useState<'choisir' | 'rapprocher'>(() =>
    rapprocherDabord && aRapprocher(catalogue).length > 0 ? 'rapprocher' : 'choisir',
  );
  const [rapprochements, setRapprochements] = useState<Record<string, Rapprochement>>(() =>
    rapprocherDabord ? rapprochementsInitiaux(aRapprocher(catalogue)) : {},
  );
  const [erreur, setErreur] = useState('');
  const initial = useMemo(() => choixInitial(catalogue), [catalogue]);
  const modifie =
    auto !== liaison.suiviAutomatique ||
    JSON.stringify([...choix.domaines].sort()) !== JSON.stringify([...initial.domaines].sort()) ||
    JSON.stringify([...choix.applications].sort()) !== JSON.stringify([...initial.applications].sort());
  useReportDirty(modifie, 'Applications suivies de Noosphere');

  const nouvelles = catalogue.applications.filter((a) => choix.applications.includes(a.id) && !a.suivie);
  const retirees = catalogue.applications.filter((a) => a.suivie && !choix.applications.includes(a.id));

  function continuer() {
    setErreur('');
    if (nouvelles.length === 0) return void enregistrer();
    // Les choix déjà faits pour une application restent ; les nouvelles reçoivent leur proposition de départ.
    setRapprochements((r) => ({ ...rapprochementsInitiaux(nouvelles), ...pick(r, nouvelles) }));
    setEtape('rapprocher');
  }

  async function enregistrer() {
    setErreur('');
    try {
      let l = liaison;
      if (auto !== liaison.suiviAutomatique)
        l = await api.optionsLiaison(liaison.id, { envoyerMeps: null, suiviAutomatique: auto });
      const c = await api.suiviLiaison(liaison.id, saisieSuivi(catalogue, choix, rapprochements));
      l = await api.liaison(liaison.id);
      toast.succes(
        `Applications suivies enregistrées : ${compte(c.applications.filter((a) => a.suivie).length, 'application suivie', 'applications suivies')}.`,
      );
      onTermine(c, l);
    } catch (e) {
      setErreur(e instanceof Error ? e.message : 'Enregistrement impossible.');
    }
  }

  if (etape === 'rapprocher')
    return (
      <Rapprocher
        nouvelles={nouvelles}
        retirees={retirees}
        rapprochements={rapprochements}
        onChange={setRapprochements}
        erreur={erreur}
        onRetour={() => setEtape('choisir')}
        onAnnuler={rapprocherDabord ? onAnnuler : undefined}
        onValider={enregistrer}
      />
    );

  return (
    <ChoisirApplications
      catalogue={catalogue}
      choix={choix}
      onChoix={setChoix}
      auto={auto}
      onAuto={setAuto}
      retirees={retirees}
      erreur={erreur}
      confirmee={liaison.confirmeeIci && liaison.confirmeeParNoosphere === true}
      libelleSuite={nouvelles.length > 0 ? 'Continuer : rapprocher du catalogue' : 'Enregistrer et synchroniser'}
      modifie={modifie}
      onSuite={continuer}
      onAnnuler={onAnnuler}
    />
  );
}

function pick(r: Record<string, Rapprochement>, apps: ApplicationLiaison[]) {
  const garde: Record<string, Rapprochement> = {};
  for (const a of apps) if (r[a.id]) garde[a.id] = r[a.id];
  return garde;
}

/* ------------------------------------------------------------------ 3c. Choisir */

function ChoisirApplications({
  catalogue: c,
  choix,
  onChoix,
  auto,
  onAuto,
  retirees,
  erreur,
  confirmee,
  libelleSuite,
  modifie,
  onSuite,
  onAnnuler,
}: {
  catalogue: CatalogueLiaison;
  choix: ChoixSuivi;
  onChoix: (c: ChoixSuivi) => void;
  auto: boolean;
  onAuto: (v: boolean) => void;
  retirees: ApplicationLiaison[];
  erreur: string;
  confirmee: boolean;
  libelleSuite: string;
  modifie: boolean;
  onSuite: () => void | Promise<void>;
  onAnnuler?: () => void;
}) {
  const [vue, setVue] = useState<Vue>('domaines');
  // Domaines dépliés : au départ, ceux qui sont suivis en partie (le choix une par une s'y voit).
  const [ouverts, setOuverts] = useState<Set<string>>(
    () => new Set(c.domaines.filter((d) => etatDomaine(c, choix, d.id).coche === 'indetermine').map((d) => d.id)),
  );
  const sansDomaine = c.applications.filter((a) => !a.domaineId || !c.domaines.some((d) => d.id === a.domaineId));
  const domainesSuivis = c.domaines.filter((d) => choix.domaines.includes(d.id)).map((d) => d.nom);
  const nbSuivies = choix.applications.length;

  const basculer = (id: string) =>
    setOuverts((o) => {
      const n = new Set(o);
      if (n.has(id)) n.delete(id);
      else n.add(id);
      return n;
    });

  const caseApplication = (a: ApplicationLiaison) => (
    <Checkbox
      key={a.id}
      checked={choix.applications.includes(a.id)}
      onChange={(v) => onChoix(basculerApplication(c, choix, a.id, v))}
      label={a.nom}
      description={
        vue === 'une' ? [a.domaine, a.equipe, mentionCycle(a.cycle)].filter(Boolean).join(' · ') : mentionCycle(a.cycle)
      }
    />
  );

  return (
    <Card>
      <CardHeader
        titre="Applications suivies"
        description="Ce que le catalogue des systèmes suit. Cochez un domaine pour toute sa famille, ou dépliez-le pour choisir une par une."
        action={
          confirmee && (
            <StatusPill ton="ok" icone={CircleCheck} taille="sm">
              Liaison confirmée
            </StatusPill>
          )
        }
      />
      <div className="flex flex-wrap items-center justify-between gap-3 border-b border-line px-5 py-3">
        <Segmented<Vue>
          label="Présentation des applications"
          valeur={vue}
          onChange={setVue}
          options={[
            { valeur: 'domaines', libelle: 'Par domaine' },
            { valeur: 'une', libelle: 'Une par une' },
          ]}
        />
        <p className="text-ui text-fg-subtle">
          {compte(c.applications.length, 'application visible', 'applications visibles')} de tous dans Noosphere,{' '}
          {compte(c.domaines.length, 'domaine')}
        </p>
      </div>

      {vue === 'domaines' ? (
        <ul className="divide-y divide-line">
          {c.domaines.map((d) => {
            const e = etatDomaine(c, choix, d.id);
            const apps = applicationsDu(c, d.id);
            const equipes = [...new Set(apps.map((a) => a.equipe).filter(Boolean))];
            const ouvert = ouverts.has(d.id);
            const idListe = `domaine-${d.id}`;
            return (
              <li key={d.id} className="px-5 py-3.5">
                <div className="flex items-start gap-3">
                  <CaseSeule
                    className="mt-0.5"
                    label={`Suivre tout le domaine ${d.nom}`}
                    checked={e.coche}
                    onChange={(v) => onChoix(basculerDomaine(c, choix, d.id, v))}
                  />
                  <button
                    type="button"
                    className="flex min-w-0 flex-1 flex-wrap items-start justify-between gap-x-3 gap-y-1.5 text-left"
                    aria-expanded={ouvert}
                    aria-controls={idListe}
                    onClick={() => basculer(d.id)}
                  >
                    <span className="min-w-0 flex-1 basis-40">
                      <span className="block text-sm font-medium">{d.nom}</span>
                      <span className="block text-xs text-fg-subtle">
                        {compte(apps.length, 'application')}
                        {equipes.length > 0 && ` · ${equipes.join(', ')}`}
                      </span>
                    </span>
                    <span className="flex shrink-0 items-center gap-2">
                      {e.entier && <Badge ton="ok">Tout le domaine</Badge>}
                      {!e.entier && e.suivies > 0 && (
                        <Badge ton="warn">
                          {e.suivies} sur {e.total}, choisie{e.suivies > 1 ? 's' : ''} une par une
                        </Badge>
                      )}
                      <ChevronDown
                        className={cn(
                          'size-4 text-fg-subtle transition-transform duration-200',
                          !ouvert && '-rotate-90',
                        )}
                        aria-hidden
                      />
                    </span>
                  </button>
                </div>
                {ouvert && (
                  <div
                    id={idListe}
                    className="mt-3 grid animate-fade-in gap-x-6 gap-y-2.5 pl-8 sm:grid-cols-2 lg:grid-cols-3"
                  >
                    {apps.map(caseApplication)}
                  </div>
                )}
              </li>
            );
          })}
          {sansDomaine.length > 0 && (
            <li className="px-5 py-3.5">
              <p className="text-sm font-medium">Sans domaine</p>
              <div className="mt-3 grid gap-x-6 gap-y-2.5 sm:grid-cols-2 lg:grid-cols-3">
                {sansDomaine.map(caseApplication)}
              </div>
            </li>
          )}
        </ul>
      ) : (
        <div className="grid gap-x-6 gap-y-3 px-5 py-4 sm:grid-cols-2 lg:grid-cols-3">
          {c.applications.map(caseApplication)}
        </div>
      )}

      <div className="grid gap-3 border-t border-line px-5 py-4">
        <Checkbox
          checked={auto}
          onChange={onAuto}
          label="Suivre automatiquement les nouvelles applications des domaines suivis"
          description={
            domainesSuivis.length > 0
              ? `Une application créée dans ${domainesSuivis.join(' ou ')} arrive au catalogue à la synchronisation suivante. Les domaines choisis une par une ne sont pas concernés.`
              : 'Une application créée dans un domaine suivi arrive au catalogue à la synchronisation suivante.'
          }
        />
        {retirees.length > 0 && (
          <Alerte ton="run" icone={Unlink}>
            {retirees.length === 1
              ? `« ${retirees[0].systeme ?? retirees[0].nom} » ne sera plus suivi : le système reste au catalogue, devenu local.`
              : `${retirees.length} systèmes ne seront plus suivis (${retirees.map((a) => a.systeme ?? a.nom).join(', ')}) : ils restent au catalogue, devenus locaux.`}
          </Alerte>
        )}
        {erreur && (
          <Alerte ton="danger" icone={Info} role="alert">
            {erreur}
          </Alerte>
        )}
      </div>
      <div className="flex flex-wrap items-center gap-x-4 gap-y-2 rounded-b-xl border-t border-line bg-surface-2/50 px-5 py-3.5">
        <Button
          variante="primaire"
          className="h-auto min-h-9 max-w-full py-1.5 text-left whitespace-normal"
          icone={<ArrowRight className="size-4 shrink-0" aria-hidden />}
          disabled={!modifie}
          action={onSuite}
        >
          {libelleSuite}
        </Button>
        {onAnnuler && (
          <Button variante="fantome" onClick={onAnnuler}>
            Annuler
          </Button>
        )}
        <p className="min-w-0 flex-1 basis-60 text-ui text-fg-subtle">
          <strong className="font-semibold text-fg">
            {compte(nbSuivies, 'application suivie', 'applications suivies')}
          </strong>
          {nbSuivies > 0 && ` : ${resumeChoix(c, choix)}.`}
        </p>
      </div>
    </Card>
  );
}

/* ------------------------------------------------------------------ 3d. Rapprocher */

const VISIBLES_SANS_PROCHE = 3;

function Rapprocher({
  nouvelles,
  retirees,
  rapprochements,
  onChange,
  erreur,
  onRetour,
  onValider,
  onAnnuler,
}: {
  nouvelles: ApplicationLiaison[];
  retirees: ApplicationLiaison[];
  rapprochements: Record<string, Rapprochement>;
  onChange: (r: Record<string, Rapprochement>) => void;
  erreur: string;
  onRetour: () => void;
  onValider: () => Promise<void>;
  /** Ouvert depuis « Rapprocher » de la liaison active : revenir sans rien changer. */
  onAnnuler?: () => void;
}) {
  const [tout, setTout] = useState(false);
  // Celles qui ont un système proche d'abord : ce sont elles qui demandent un choix.
  const avecProche = nouvelles.filter((a) => a.suggestions.length > 0);
  const sansProche = nouvelles.filter((a) => a.suggestions.length === 0);
  const montrees = tout ? sansProche : sansProche.slice(0, VISIBLES_SANS_PROCHE);
  const cachees = sansProche.length - montrees.length;
  const aChoisir = nouvelles.filter((a) => !rapprochements[a.id]).length;
  const rattachees = nouvelles.filter((a) => rapprochements[a.id]?.type === 'rattacher');
  const creees = nouvelles.filter((a) => rapprochements[a.id]?.type === 'creer').length;
  // Exemple du changement de nom d'un système rattaché : « BILLING-API » deviendra « Billing ».
  const exemple = rattachees
    .map((a) => ({ app: a, r: rapprochements[a.id] }))
    .find((x) => x.r?.type === 'rattacher' && x.r.nom !== x.app.nom);
  const exempleNom = exemple?.r?.type === 'rattacher' ? exemple.r.nom : null;

  const choisir = (id: string, r: Rapprochement | null) => {
    const n = { ...rapprochements };
    if (r) n[id] = r;
    else delete n[id];
    onChange(n);
  };

  const ligne = (a: ApplicationLiaison) => {
    const r = rapprochements[a.id];
    const proposee = (r?.type === 'rattacher' && a.suggestions.find((s) => s.id === r.systemeId)) || a.suggestions[0];
    return (
      <li
        key={a.id}
        className="grid gap-2 px-5 py-3.5 text-sm md:grid-cols-[minmax(0,1fr)_minmax(0,1.15fr)_minmax(0,1.1fr)] md:items-center md:gap-4"
      >
        <div className="min-w-0">
          <p className="font-medium">{a.nom}</p>
          <p className="text-ui text-fg-subtle">{[a.domaine, a.equipe].filter(Boolean).join(' · ') || '—'}</p>
        </div>
        <div className="min-w-0">
          {proposee ? (
            <>
              <p className="flex flex-wrap items-center gap-2">
                <span className="font-mono font-medium">{proposee.nom}</span>
                <Badge ton={proposee.memeNom ? 'ok' : 'warn'}>{proposee.memeNom ? 'même nom' : 'nom proche'}</Badge>
              </p>
              <p className="text-ui text-fg-subtle">
                Système existant
                {a.suggestions.length > 1 &&
                  ` · ${a.suggestions.length - 1} autre${a.suggestions.length > 2 ? 's' : ''} proche${a.suggestions.length > 2 ? 's' : ''}`}
              </p>
            </>
          ) : (
            <p className="text-fg-subtle">Aucun système au nom proche</p>
          )}
        </div>
        <div className="flex flex-wrap items-center gap-2" role="group" aria-label={`Choix pour ${a.nom}`}>
          {!r && proposee && (
            <>
              <Button
                taille="sm"
                variante="primaire"
                icone={<Link2 className="size-4" aria-hidden />}
                onClick={() => choisir(a.id, { type: 'rattacher', systemeId: proposee.id, nom: proposee.nom })}
              >
                Rattacher
              </Button>
              <Button
                taille="sm"
                icone={<Plus className="size-4" aria-hidden />}
                onClick={() => choisir(a.id, { type: 'creer' })}
              >
                Créer un système
              </Button>
            </>
          )}
          {r?.type === 'rattacher' && (
            <StatusPill ton="ok" icone={Check} taille="sm" className="animate-fade-in">
              Rattachée à {r.nom}
            </StatusPill>
          )}
          {r?.type === 'creer' && (
            <StatusPill ton="run" icone={Plus} taille="sm" className="animate-fade-in">
              Nouveau système
            </StatusPill>
          )}
          {r && a.suggestions.length > 0 && (
            <Button taille="sm" variante="fantome" onClick={() => choisir(a.id, null)}>
              Changer
            </Button>
          )}
          {/* Plusieurs systèmes proches : le suivant se propose au rattachement. */}
          {!r && a.suggestions.length > 1 && (
            <select
              aria-label={`Système à rattacher à ${a.nom}`}
              className="h-8 rounded-md border border-line-strong bg-surface px-2 text-ui"
              value=""
              onChange={(e) => {
                const s = a.suggestions.find((x) => x.id === e.target.value);
                if (s) choisir(a.id, { type: 'rattacher', systemeId: s.id, nom: s.nom });
              }}
            >
              <option value="">Autre système…</option>
              {a.suggestions.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.nom}
                </option>
              ))}
            </select>
          )}
        </div>
      </li>
    );
  };

  return (
    <Card>
      <CardHeader
        titre="Rapprocher du catalogue"
        description="Chaque application suivie devient un système du catalogue. Rattachez-la à un système existant au nom proche, ou créez-en un. Rien n’est appliqué sans votre choix."
      />
      <div
        role="row"
        className="hidden gap-4 border-b border-line bg-surface-2/50 px-5 py-2 text-2xs font-semibold tracking-wider text-fg-subtle uppercase md:grid md:grid-cols-[minmax(0,1fr)_minmax(0,1.15fr)_minmax(0,1.1fr)]"
      >
        <span>Application Noosphere</span>
        <span>Système du catalogue</span>
        <span>Choix</span>
      </div>
      <ul className="apparition divide-y divide-line">
        {avecProche.map(ligne)}
        {montrees.map(ligne)}
      </ul>
      <div className="grid gap-3 border-t border-line px-5 py-4">
        {cachees > 0 && (
          <p className="text-sm text-fg-muted">
            Et {cachees} autre{cachees > 1 ? 's' : ''} sans système au nom proche : un nouveau système chacune.{' '}
            <button
              type="button"
              className="font-semibold text-accent-text hover:underline"
              onClick={() => setTout(true)}
            >
              Voir les {cachees}
            </button>
          </p>
        )}
        <Alerte ton="run" icone={Info}>
          Un système rattaché garde ses environnements, cibles, objets et son historique de MEP. Son nom et sa
          description prennent ceux de Noosphere
          {exemple && exempleNom ? ` : « ${exempleNom} » deviendra « ${exemple.app.nom} ».` : '.'}
        </Alerte>
        {retirees.length > 0 && (
          <Alerte ton="run" icone={Unlink}>
            {compte(retirees.length, 'système ne sera plus suivi', 'systèmes ne seront plus suivis')} : ils restent au
            catalogue, devenus locaux.
          </Alerte>
        )}
        {erreur && (
          <Alerte ton="danger" icone={Info} role="alert">
            {erreur}
          </Alerte>
        )}
      </div>
      <div className="flex flex-wrap items-center gap-x-4 gap-y-2 rounded-b-xl border-t border-line bg-surface-2/50 px-5 py-3.5">
        <Button
          variante="primaire"
          icone={<Check className="size-4" aria-hidden />}
          disabled={aChoisir > 0}
          action={onValider}
        >
          Valider et synchroniser
        </Button>
        <Button variante="fantome" icone={<ArrowLeft className="size-4" aria-hidden />} onClick={onRetour}>
          {onAnnuler ? 'Toutes les applications' : 'Retour'}
        </Button>
        {onAnnuler && (
          <Button variante="fantome" onClick={onAnnuler}>
            Annuler
          </Button>
        )}
        <p className="min-w-0 flex-1 basis-60 text-ui text-fg-subtle" aria-live="polite">
          {[
            aChoisir > 0 && compte(aChoisir, 'rattachement à choisir', 'rattachements à choisir'),
            compte(rattachees.length, 'rattaché'),
            compte(creees, 'création'),
          ]
            .filter(Boolean)
            .join(' · ')}
        </p>
      </div>
    </Card>
  );
}
