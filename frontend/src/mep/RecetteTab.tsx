import { ArrowDown, ArrowUp, Check, ChevronDown, ChevronRight, ClipboardCheck, FlaskConical, Phone, Plus, RotateCcw, Save, ShieldAlert, Trash2, XCircle } from 'lucide-react';
import { useContext, useRef, useState } from 'react';
import { api } from '../api';
import { AuthContext } from '../auth';
import { couleurSysteme, styleSysteme } from '../couleursSysteme';
import { bilanRecette, fichesRecetteTriees, responsablesDuSysteme } from '../domaine';
import { formatDateHeure, LIBELLE_RESULTAT } from '../format';
import { useDraft, useReportDirty } from '../hooks';
import { useMep } from '../mepContext';
import type { EtapeTest, FicheRecette, Systeme } from '../types';
import { Alerte } from '../ui/Alerte';
import { BoutonIcone, Button } from '../ui/Button';
import { Card } from '../ui/Card';
import { cn } from '../ui/cn';
import { ConfirmDialog } from '../ui/Dialog';
import { EmptyState } from '../ui/EmptyState';
import { Field, Input, Select, Textarea } from '../ui/Input';
import { ProgressBar } from '../ui/ProgressBar';
import { StatusPill } from '../ui/StatusPill';
import { BoutonCloturer, BoutonDeclencherRepli } from './ActionsPilote';
import { ChampBlocs, RenduBlocs } from './ChampBlocs';
import { FormulaireDialog, vide } from './preparation/Formulaires';
import { BadgeDisponibilite } from './preparation/ResponsablesTechniquesSection';

/* ------------------------------------------------------------------ Brouillon d'édition d'une fiche */

interface EtapeBrouillon { cle: string; id?: string; instructions: string; resultatAttendu: string }
interface FicheBrouillon { titre: string; systemeId: string; etapes: EtapeBrouillon[] }

const brouillonDe = (f: FicheRecette): FicheBrouillon => ({
  titre: f.titre,
  systemeId: f.systemeId ?? '',
  etapes: f.etapes.map((e) => ({ cle: e.id, id: e.id, instructions: e.instructions, resultatAttendu: e.resultatAttendu })),
});

function deplacer<T>(liste: T[], i: number, delta: number): T[] {
  const j = i + delta;
  if (j < 0 || j >= liste.length) return liste;
  const copie = [...liste];
  [copie[i], copie[j]] = [copie[j], copie[i]];
  return copie;
}

/* ------------------------------------------------------------------ Édition (préparation) */

function FicheEdition({ fiche }: { fiche: FicheRecette }) {
  const { mep, run } = useMep();
  const d = useDraft(brouillonDe(fiche));
  useReportDirty(d.dirty);
  const v = d.value;
  const compteur = useRef(0);
  const [suppr, setSuppr] = useState(false);
  const [envoi, setEnvoi] = useState(false);

  const majEtape = (i: number, patch: Partial<EtapeBrouillon>) => d.update({ etapes: v.etapes.map((e, k) => (k === i ? { ...e, ...patch } : e)) });

  async function enregistrer() {
    setEnvoi(true);
    try {
      const ok = await run(
        () => api.modifierFicheRecette(mep.id, fiche.id, {
          systemeId: vide(v.systemeId),
          titre: v.titre.trim(),
          etapes: v.etapes.map((e) => ({ id: e.id, instructions: e.instructions.trim(), resultatAttendu: e.resultatAttendu.trim() })),
        }),
        'Fiche de recette enregistrée.',
      );
      if (ok) d.reset();
    } finally {
      setEnvoi(false);
    }
  }

  return (
    <div className="space-y-4">
      <div className="grid gap-4 md:grid-cols-[minmax(0,1fr)_14rem]">
        <Field label="Titre de la fiche *">{(id) => <Input id={id} value={v.titre} onChange={(e) => d.update({ titre: e.target.value })} placeholder="ex. Tables clients" />}</Field>
        <Field label="Système">
          {(id) => (
            <Select id={id} value={v.systemeId} onChange={(e) => d.update({ systemeId: e.target.value })}>
              <option value="">— Sans système —</option>
              {mep.systemes.map((s) => <option key={s.id} value={s.id}>{s.nom}</option>)}
            </Select>
          )}
        </Field>
      </div>

      {v.etapes.length === 0 ? (
        <p className="rounded-lg border border-dashed border-line-strong px-4 py-6 text-center text-sm text-fg-subtle">Aucune étape de test : ajoutez-en au moins une.</p>
      ) : (
        <ol className="space-y-3" aria-label="Étapes de test">
          {v.etapes.map((e, i) => (
            <li key={e.cle} className="rounded-lg border border-line p-3">
              <div className="mb-2 flex items-center gap-2">
                <span className="flex size-6 items-center justify-center rounded-md bg-surface-2 font-mono text-xs font-bold">{i + 1}</span>
                <h4 className="flex-1 text-sm font-semibold">Étape {i + 1}</h4>
                <BoutonIcone label={`Monter l’étape ${i + 1}`} taille="sm" disabled={i === 0} onClick={() => d.update({ etapes: deplacer(v.etapes, i, -1) })}><ArrowUp className="size-4" aria-hidden /></BoutonIcone>
                <BoutonIcone label={`Descendre l’étape ${i + 1}`} taille="sm" disabled={i === v.etapes.length - 1} onClick={() => d.update({ etapes: deplacer(v.etapes, i, 1) })}><ArrowDown className="size-4" aria-hidden /></BoutonIcone>
                <BoutonIcone label={`Supprimer l’étape ${i + 1}`} taille="sm" onClick={() => d.update({ etapes: v.etapes.filter((_, k) => k !== i) })}><Trash2 className="size-4" aria-hidden /></BoutonIcone>
              </div>
              <div className="grid gap-3 lg:grid-cols-2">
                <ChampBlocs label={`Instructions de l’étape ${i + 1}`} valeur={e.instructions} onChange={(instructions) => majEtape(i, { instructions })} />
                <ChampBlocs label={`Résultat attendu de l’étape ${i + 1}`} valeur={e.resultatAttendu} onChange={(resultatAttendu) => majEtape(i, { resultatAttendu })} />
              </div>
            </li>
          ))}
        </ol>
      )}

      <div className="flex flex-wrap items-center gap-2">
        <Button taille="sm" icone={<Plus className="size-4" aria-hidden />} onClick={() => d.update({ etapes: [...v.etapes, { cle: `nouvelle-${++compteur.current}`, instructions: '', resultatAttendu: '' }] })}>Ajouter une étape de test</Button>
        <span className="ml-auto flex items-center gap-2">
          <Button variante="danger-doux" taille="sm" icone={<Trash2 className="size-4" aria-hidden />} onClick={() => setSuppr(true)}>Supprimer la fiche</Button>
          {d.dirty && <Button variante="fantome" taille="sm" onClick={d.reset}>Annuler les modifications</Button>}
          <Button variante="primaire" taille="sm" icone={<Save className="size-4" aria-hidden />} disabled={!d.dirty} chargement={envoi} onClick={() => void enregistrer()}>Enregistrer la fiche</Button>
        </span>
      </div>

      <ConfirmDialog
        open={suppr}
        onOpenChange={setSuppr}
        titre="Supprimer cette fiche ?"
        description={`« ${fiche.titre || 'Fiche sans titre'} » et ses ${fiche.etapes.length} étape(s) de test seront retirées de la recette.`}
        libelleConfirmer="Supprimer"
        variante="danger"
        onConfirmer={() => run(() => api.supprimerFicheRecette(mep.id, fiche.id), 'Fiche de recette supprimée.')}
      />
    </div>
  );
}

/* ------------------------------------------------------------------ Lecture et déroulé */

function EtapeTestLigne({ fiche, etape, numero, peutAgir }: { fiche: FicheRecette; etape: EtapeTest; numero: number; peutAgir: boolean }) {
  const { mep, run } = useMep();
  const [observation, setObservation] = useState('');
  const enRecette = mep.phase === 'Recette';
  const fait = etape.resultat !== null;
  const conforme = etape.resultat === 'Conforme';

  async function valider(resultat: 'Conforme' | 'Echec') {
    const ok = await run(
      () => api.resultatEtapeTest(mep.id, fiche.id, etape.id, resultat, observation.trim()),
      resultat === 'Conforme' ? `Étape ${numero} conforme.` : `Étape ${numero} en échec.`,
    );
    if (ok) setObservation('');
  }

  return (
    <li className={cn('space-y-3 rounded-lg border p-3', fait ? (conforme ? 'border-ok-bd' : 'border-danger-bd') : 'border-line')}>
      <div className="flex flex-wrap items-center gap-2">
        <span className="flex size-6 items-center justify-center rounded-md bg-surface-2 font-mono text-xs font-bold">{numero}</span>
        <h4 className="flex-1 text-sm font-semibold">Étape {numero}</h4>
        {fait ? (
          <StatusPill ton={conforme ? 'ok' : 'danger'} icone={conforme ? Check : XCircle}>{LIBELLE_RESULTAT[etape.resultat!]}</StatusPill>
        ) : enRecette ? (
          <StatusPill ton="wait" icone={FlaskConical}>À réaliser</StatusPill>
        ) : null}
        {enRecette && fait && mep.droits.estPilote && (
          <Button taille="sm" icone={<RotateCcw className="size-4" aria-hidden />} aria-label={`Refaire l’étape ${numero} de ${fiche.titre}`} onClick={() => void run(() => api.refaireEtapeTest(mep.id, fiche.id, etape.id), `Étape ${numero} à refaire.`)}>Refaire</Button>
        )}
      </div>
      <div>
        <p className="text-[11px] font-semibold tracking-wide text-fg-subtle uppercase">Instructions</p>
        {etape.instructions.trim() ? <RenduBlocs source={etape.instructions} /> : <p className="text-sm text-fg-subtle">Instructions à compléter.</p>}
      </div>
      <div className="rounded-lg border border-ok-bd bg-ok-bg px-3.5 py-2.5 text-ok-fg">
        <p className="text-[11px] font-semibold tracking-wide uppercase">Résultat attendu</p>
        {etape.resultatAttendu.trim() ? <RenduBlocs source={etape.resultatAttendu} /> : <p className="text-sm">Résultat attendu à compléter.</p>}
      </div>

      {fait && (
        <div className="rounded-lg border border-line bg-surface-2/50 px-3.5 py-2.5 text-sm">
          <p className="whitespace-pre-line">{etape.observation || 'Aucune observation.'}</p>
          <p className="mt-1 text-xs text-fg-subtle">{etape.par ?? 'Quelqu’un'}{etape.le && ` · ${formatDateHeure(etape.le)}`}</p>
        </div>
      )}

      {enRecette && !fait && peutAgir && (
        <div className="space-y-3">
          <Field label={`Observation de l’étape ${numero} (obligatoire)`} hint="Ce que vous avez constaté : sera conservé comme preuve.">
            {(id) => <Textarea id={id} rows={2} value={observation} onChange={(e) => setObservation(e.target.value)} placeholder="ex. La page d’accueil s’affiche en 1,2 s, version 4.2 visible." />}
          </Field>
          <div className="grid gap-3 sm:grid-cols-2">
            <Button variante="succes" taille="lg" icone={<Check className="size-5" aria-hidden />} disabled={!observation.trim()} onClick={() => void valider('Conforme')}>Conforme</Button>
            <Button variante="danger" taille="lg" icone={<XCircle className="size-5" aria-hidden />} disabled={!observation.trim()} onClick={() => void valider('Echec')}>Échec</Button>
          </div>
        </div>
      )}
      {enRecette && !fait && !peutAgir && <p className="text-xs text-fg-subtle">Seul un participant relié à un compte, le pilote ou un administrateur peut saisir le résultat.</p>}
    </li>
  );
}

function FicheLecture({ fiche, peutAgir }: { fiche: FicheRecette; peutAgir: boolean }) {
  if (fiche.etapes.length === 0) return <p className="text-sm text-fg-subtle">Aucune étape de test.</p>;
  return (
    <ol className="space-y-3" aria-label="Étapes de test">
      {fiche.etapes.map((e, i) => <EtapeTestLigne key={e.id} fiche={fiche} etape={e} numero={i + 1} peutAgir={peutAgir} />)}
    </ol>
  );
}

/* ------------------------------------------------------------------ Fiche et section */

function FicheCarte({ fiche, peutAgir }: { fiche: FicheRecette; peutAgir: boolean }) {
  const { mep, run } = useMep();
  const edition = mep.droits.peutEditerContenu;
  const enRecette = mep.phase === 'Recette';
  const conformes = fiche.etapes.filter((e) => e.resultat === 'Conforme').length;
  const echecs = fiche.etapes.filter((e) => e.resultat === 'Echec').length;
  const terminee = fiche.etapes.length > 0 && conformes === fiche.etapes.length;
  const [ouvert, setOuvert] = useState(edition ? fiche.etapes.length === 0 : !terminee);
  const corps = `fiche-${fiche.id}`;
  const dejaFait = conformes + echecs > 0;

  return (
    <li className="rounded-xl border border-line bg-surface shadow-sm">
      <div className="flex flex-wrap items-center gap-x-3 gap-y-2 px-4 py-3">
        <button
          type="button"
          aria-expanded={ouvert}
          aria-controls={corps}
          onClick={() => setOuvert(!ouvert)}
          className="flex min-w-0 flex-1 basis-56 items-center gap-2 rounded-md text-left"
        >
          {ouvert ? <ChevronDown className="size-4 shrink-0 text-fg-subtle" aria-hidden /> : <ChevronRight className="size-4 shrink-0 text-fg-subtle" aria-hidden />}
          <span className="min-w-0">
            <span className="block truncate text-[15px] font-semibold">{fiche.titre || <em className="font-normal text-fg-subtle not-italic">Fiche sans titre</em>}</span>
            <span className="block text-xs text-fg-subtle">{fiche.etapes.length} étape{fiche.etapes.length > 1 ? 's' : ''} de test</span>
          </span>
        </button>
        {(enRecette || mep.phase === 'Cloturee') && fiche.etapes.length > 0 && (
          echecs > 0
            ? <StatusPill ton="danger" icone={XCircle} taille="sm">{echecs} en échec</StatusPill>
            : terminee
              ? <StatusPill ton="ok" icone={Check} taille="sm">Conforme</StatusPill>
              : <StatusPill ton="wait" icone={FlaskConical} taille="sm">{conformes}/{fiche.etapes.length}</StatusPill>
        )}
        {enRecette && mep.droits.estPilote && dejaFait && (
          <Button taille="sm" icone={<RotateCcw className="size-4" aria-hidden />} aria-label={`Refaire toute la fiche ${fiche.titre}`} onClick={() => void run(() => api.refaireFicheRecette(mep.id, fiche.id), 'Fiche à refaire.')}>Refaire la fiche</Button>
        )}
      </div>
      {ouvert && (
        <div id={corps} className="border-t border-line px-4 py-4">
          {edition ? <FicheEdition fiche={fiche} /> : <FicheLecture fiche={fiche} peutAgir={peutAgir} />}
        </div>
      )}
    </li>
  );
}

function SectionSysteme({ systeme, fiches, peutAgir, onAjouter }: { systeme: Systeme | null; fiches: FicheRecette[]; peutAgir: boolean; onAjouter: () => void }) {
  const { mep } = useMep();
  const edition = mep.droits.peutEditerContenu;
  const titreId = `recette-${systeme?.id ?? 'sans-systeme'}`;
  const responsables = responsablesDuSysteme(mep, systeme?.id ?? null);
  const etapes = fiches.flatMap((f) => f.etapes);
  const conformes = etapes.filter((e) => e.resultat === 'Conforme').length;

  return (
    <section aria-labelledby={titreId} className="overflow-hidden rounded-xl border bg-surface" style={systeme ? { borderColor: couleurSysteme(systeme.nom) } : undefined}>
      <header
        className={cn('flex flex-wrap items-center gap-x-4 gap-y-2 px-4 py-3', !systeme && 'border-b border-line bg-surface-2')}
        style={systeme ? styleSysteme(systeme.nom) : undefined}
      >
        <div className="min-w-0 flex-1 basis-56">
          <h2 id={titreId} className="flex items-center gap-2 text-base font-bold">
            {systeme && <span className="size-2.5 shrink-0 rounded-full" style={{ backgroundColor: couleurSysteme(systeme.nom) }} aria-hidden />}
            {systeme ? systeme.nom : 'Sans système'}
          </h2>
          {systeme?.cible && <p className="truncate font-mono text-xs opacity-80">{systeme.cible}</p>}
        </div>
        {etapes.length > 0 && (mep.phase === 'Recette' || mep.phase === 'Cloturee') && (
          <span className="text-xs font-medium tnum">{conformes}/{etapes.length} conforme{conformes > 1 ? 's' : ''}</span>
        )}
        {edition && <Button variante="secondaire" taille="sm" icone={<Plus className="size-4" aria-hidden />} onClick={onAjouter}>Ajouter une recette</Button>}
      </header>
      <div className="space-y-3 px-4 py-4">
        {systeme && responsables.length > 0 && (
          <p className="flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-fg-muted" aria-label={`Responsables techniques de ${systeme.nom}`}>
            <span className="font-semibold text-fg">À appeler :</span>
            {responsables.map((r) => (
              <span key={r.participant.id} className="inline-flex items-center gap-1.5">
                {r.participant.nom}
                <BadgeDisponibilite valeur={r.disponibilite} />
                <span className="inline-flex items-center gap-1"><Phone className="size-3" aria-hidden />{r.contactJourJ || 'contact non renseigné'}</span>
              </span>
            ))}
          </p>
        )}
        {systeme && responsables.length === 0 && mep.phase === 'Recette' && (
          <p className="text-xs text-fg-subtle">Aucun responsable technique désigné pour {systeme.nom}.</p>
        )}
        {fiches.length === 0 ? (
          <p className="rounded-lg border border-dashed border-line-strong px-4 py-5 text-center text-sm text-fg-subtle">
            {systeme ? `Aucune recette pour le système ${systeme.nom}.` : 'Aucune recette sans système.'}
          </p>
        ) : (
          <ul className="space-y-3">{fiches.map((f) => <FicheCarte key={f.id} fiche={f} peutAgir={peutAgir} />)}</ul>
        )}
      </div>
    </section>
  );
}

/* ------------------------------------------------------------------ Onglet */

/**
 * Onglet Recette : les systèmes de la MEP en sections colorées, chacun avec ses fiches de recette.
 * En préparation : édition des fiches. En phase Recette : déroulé (Conforme / Échec + observation), « Refaire » pour le pilote.
 */
const INITIAL_AJOUT = { titre: '' };

export default function RecetteTab() {
  const { mep, run } = useMep();
  const auth = useContext(AuthContext);
  const [ajout, setAjout] = useState<{ systemeId: string | null } | null>(null);
  const fiches = fichesRecetteTriees(mep);
  const bilan = bilanRecette(mep);
  const enRecette = mep.phase === 'Recette';
  const edition = mep.droits.peutEditerContenu;
  const peutAgir = mep.droits.estPilote || mep.droits.participantId !== null || (auth?.utilisateur?.estAdmin ?? false);

  const idsSystemes = new Set(mep.systemes.map((s) => s.id));
  const sansSysteme = fiches.filter((f) => f.systemeId === null || !idsSystemes.has(f.systemeId));

  return (
    <div className="mx-auto w-full max-w-5xl space-y-5 px-6 py-6">
      {bilan.total > 0 && (enRecette || mep.phase === 'Cloturee') ? (
        <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
          <ProgressBar className="min-w-48 flex-1" segments={[
            { valeur: bilan.conformes, ton: 'ok', libelle: 'Conformes' },
            { valeur: bilan.echecs, ton: 'danger', libelle: 'Échecs' },
            { valeur: bilan.restantes, ton: 'wait', libelle: 'Restantes' },
          ]} />
          <p className="text-sm font-medium text-fg-muted tnum">{bilan.conformes}/{bilan.total} conforme{bilan.conformes > 1 ? 's' : ''}{bilan.echecs > 0 && <span className="font-semibold text-danger-solid"> · {bilan.echecs} en échec</span>}</p>
        </div>
      ) : (
        <p className="text-sm text-fg-muted">
          {fiches.length} fiche{fiches.length > 1 ? 's' : ''} de recette, {bilan.total} étape{bilan.total > 1 ? 's' : ''} de test.
          {mep.phase === 'Preparation' && ' Elles seront déroulées une fois le déploiement terminé ; la clôture réussie exige que toutes soient conformes.'}
        </p>
      )}

      {enRecette && bilan.echecs > 0 && (
        <Alerte ton="danger" icone={XCircle} role="alert" titre="Une étape de test est en échec">
          La clôture réussie est impossible tant qu’une étape n’est pas conforme. Le pilote peut faire refaire l’étape ou déclencher le retour arrière.
        </Alerte>
      )}
      {enRecette && mep.droits.estPilote && (
        <Alerte
          ton={mep.nonRetourFranchi ? 'warn' : 'rollback'}
          icone={ShieldAlert}
          titre="Décision du pilote"
          action={<div className="flex flex-wrap items-center gap-2"><BoutonDeclencherRepli taille="md" /><BoutonCloturer taille="md" /></div>}
        >
          {mep.nonRetourFranchi
            ? `Point de non-retour franchi (${mep.nonRetourFranchi}) : le retour arrière n’est plus possible, on corrige en avant.`
            : 'Le retour arrière reste possible pendant la recette. La clôture réussie exige que toutes les étapes de test soient conformes.'}
        </Alerte>
      )}

      {mep.systemes.length === 0 && sansSysteme.length === 0 ? (
        <Card><EmptyState icone={ClipboardCheck} titre="Aucun système" description="Ajoutez les systèmes de la MEP en préparation : la recette s’organise par système." /></Card>
      ) : (
        <div className="space-y-5">
          {mep.systemes.map((s) => (
            <SectionSysteme key={s.id} systeme={s} fiches={fiches.filter((f) => f.systemeId === s.id)} peutAgir={peutAgir} onAjouter={() => setAjout({ systemeId: s.id })} />
          ))}
          {(sansSysteme.length > 0 || edition) && (
            <SectionSysteme systeme={null} fiches={sansSysteme} peutAgir={peutAgir} onAjouter={() => setAjout({ systemeId: null })} />
          )}
        </div>
      )}

      <FormulaireDialog
        open={ajout !== null}
        onOpenChange={(o) => { if (!o) setAjout(null); }}
        titre="Ajouter une recette"
        description={ajout?.systemeId ? `Pour le système ${mep.systemes.find((s) => s.id === ajout.systemeId)?.nom ?? ''}. Vous ajouterez ensuite ses étapes de test.` : 'Sans système. Vous ajouterez ensuite ses étapes de test.'}
        champs={[{ cle: 'titre', label: 'Titre de la fiche', requis: true, large: true, placeholder: 'ex. Tables clients' }]}
        initial={INITIAL_AJOUT}
        libelleValider="Ajouter"
        onValider={(v) => run(() => api.ajouterFicheRecette(mep.id, ajout?.systemeId ?? null, v.titre.trim()), 'Fiche de recette ajoutée.')}
      />
    </div>
  );
}
