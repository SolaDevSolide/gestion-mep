import { ArrowDown, ArrowUp, OctagonAlert, Plus, Stethoscope, Trash2 } from 'lucide-react';
import { useContext, useEffect, useState, type FormEvent } from 'react';
import { AuthContext } from '../auth';
import { api, ApiError } from '../api';
import type { PieceJointe, ResolutionType, ResolutionTypeSaisie, SystemeCatalogue } from '../types';
import { PiecesJointes } from '../ui/PiecesJointes';
import { Alerte } from '../ui/Alerte';
import { BoutonIcone, Button } from '../ui/Button';
import { Dialog } from '../ui/Dialog';
import { EditeurBlocs } from '../ui/EditeurBlocs';
import { Field, Input, Select, Textarea } from '../ui/Input';
import { Instructions, Markdown } from '../ui/Instructions';
import { useToast } from '../ui/Toast';
import { PuceSysteme } from './graphe/systemes';

export interface CorrectionBrouillon { cle: number; instruction: string; resultatAttendu: string }

/** Saisie d'une résolution type ; les instructions sont des chaînes markdown (blocs Texte / Code). */
export interface BrouillonResolution {
  titre: string;
  systemeCatalogueId: string;
  symptomes: string;
  diagnostic: string;
  corrections: CorrectionBrouillon[];
  verificationInstruction: string;
  verificationAttendu: string;
}

let numero = 0;
const correctionVide = (): CorrectionBrouillon => ({ cle: ++numero, instruction: '', resultatAttendu: '' });

export function brouillonDepuis(r: ResolutionType | undefined, systemeParDefaut: string | null | undefined): BrouillonResolution {
  return {
    titre: r?.titre ?? '',
    systemeCatalogueId: r?.systemeCatalogueId ?? systemeParDefaut ?? '',
    symptomes: r?.symptomes ?? '',
    diagnostic: r?.diagnostic ?? '',
    corrections: r ? [...r.corrections].sort((a, b) => a.ordre - b.ordre).map((c) => ({ cle: ++numero, instruction: c.instruction, resultatAttendu: c.resultatAttendu })) : [correctionVide()],
    verificationInstruction: r?.verificationInstruction ?? '',
    verificationAttendu: r?.verificationAttendu ?? '',
  };
}

export function saisieResolution(b: BrouillonResolution): Omit<ResolutionTypeSaisie, 'version'> {
  return {
    titre: b.titre.trim(),
    systemeCatalogueId: b.systemeCatalogueId || null,
    symptomes: b.symptomes.trim(),
    diagnostic: b.diagnostic.trim(),
    corrections: b.corrections.map((c, i) => ({ ordre: i + 1, instruction: c.instruction.trim(), resultatAttendu: c.resultatAttendu.trim() })),
    verificationInstruction: b.verificationInstruction.trim(),
    verificationAttendu: b.verificationAttendu.trim(),
  };
}

const Intertitre = ({ children }: { children: React.ReactNode }) => (
  <h3 className="text-[11px] font-semibold tracking-[0.06em] text-fg-subtle uppercase">{children}</h3>
);

/** Champs d'une résolution type : titre, système, symptômes, diagnostic, corrections ordonnées, vérification finale. */
export function FormulaireResolution({ b, onChange, systemes, autoFocus }: {
  b: BrouillonResolution;
  onChange: (b: BrouillonResolution) => void;
  systemes: SystemeCatalogue[];
  autoFocus?: boolean;
}) {
  const maj = (patch: Partial<BrouillonResolution>) => onChange({ ...b, ...patch });
  const majCorrection = (i: number, patch: Partial<CorrectionBrouillon>) => maj({ corrections: b.corrections.map((c, j) => (j === i ? { ...c, ...patch } : c)) });
  const deplacer = (i: number, delta: number) => {
    const j = i + delta;
    if (j < 0 || j >= b.corrections.length) return;
    const l = [...b.corrections];
    [l[i], l[j]] = [l[j], l[i]];
    maj({ corrections: l });
  };

  return (
    <div className="grid gap-5">
      <div className="grid gap-4 md:grid-cols-2">
        <Field label="Titre *">{(id) => <Input id={id} value={b.titre} onChange={(e) => maj({ titre: e.target.value })} placeholder="ex. API : le service ne démarre pas" autoFocus={autoFocus} />}</Field>
        <Field label="Système" hint="Sert à proposer cette résolution sur les étapes de ce système.">
          {(id) => (
            <Select id={id} value={b.systemeCatalogueId} onChange={(e) => maj({ systemeCatalogueId: e.target.value })}>
              <option value="">— tous les systèmes —</option>
              {systemes.map((s) => <option key={s.id} value={s.id}>{s.nom}{s.description ? ` · ${s.description}` : ''}{s.actif ? '' : ' (inactif)'}</option>)}
            </Select>
          )}
        </Field>
      </div>

      <Field label="Symptômes (markdown) : comment reconnaître le problème">
        {(id) => (
          <div className="grid gap-1.5">
            <Textarea id={id} rows={3} value={b.symptomes} onChange={(e) => maj({ symptomes: e.target.value })} placeholder="ex. Après le déploiement, `GET /sante` renvoie **503**." />
            {b.symptomes.trim() && (
              <div className="rounded-md border border-dashed border-line px-2 py-1.5">
                <p className="mb-0.5 text-[10px] font-semibold tracking-wide text-fg-subtle uppercase">Aperçu</p>
                <Markdown source={b.symptomes} />
              </div>
            )}
          </div>
        )}
      </Field>

      <section className="grid gap-1.5">
        <Intertitre>Diagnostic : comment le confirmer</Intertitre>
        <EditeurBlocs label="Diagnostic" typeInitial="code" valeur={b.diagnostic} onChange={(md) => maj({ diagnostic: md })} />
      </section>

      <section className="grid gap-2.5">
        <Intertitre>Corrections, dans l’ordre</Intertitre>
        {b.corrections.map((c, i) => (
          <div key={c.cle} className="grid gap-2.5 rounded-lg border border-line bg-surface-2/40 p-3">
            <div className="flex items-center justify-between gap-2">
              <span className="text-sm font-semibold">Correction {i + 1}</span>
              <div className="flex items-center gap-0.5">
                <BoutonIcone label={`Monter la correction ${i + 1}`} taille="sm" disabled={i === 0} onClick={() => deplacer(i, -1)}><ArrowUp className="size-4" aria-hidden /></BoutonIcone>
                <BoutonIcone label={`Descendre la correction ${i + 1}`} taille="sm" disabled={i === b.corrections.length - 1} onClick={() => deplacer(i, 1)}><ArrowDown className="size-4" aria-hidden /></BoutonIcone>
                <BoutonIcone label={`Supprimer la correction ${i + 1}`} taille="sm" disabled={b.corrections.length === 1} onClick={() => maj({ corrections: b.corrections.filter((_, j) => j !== i) })}><Trash2 className="size-4" aria-hidden /></BoutonIcone>
              </div>
            </div>
            <EditeurBlocs label={`Correction ${i + 1}`} typeInitial="code" valeur={c.instruction} onChange={(md) => majCorrection(i, { instruction: md })} />
            <Field label="Résultat attendu">{(id) => <Input id={id} value={c.resultatAttendu} onChange={(e) => majCorrection(i, { resultatAttendu: e.target.value })} />}</Field>
          </div>
        ))}
        <div><Button taille="sm" icone={<Plus className="size-4" aria-hidden />} onClick={() => maj({ corrections: [...b.corrections, correctionVide()] })}>Ajouter une correction</Button></div>
      </section>

      <section className="grid gap-2.5 rounded-lg border border-ok-bd bg-ok-bg/40 p-3">
        <Intertitre>Vérification finale</Intertitre>
        <EditeurBlocs label="Vérification finale" typeInitial="code" valeur={b.verificationInstruction} onChange={(md) => maj({ verificationInstruction: md })} />
        <Field label="Résultat attendu de la vérification">{(id) => <Input id={id} value={b.verificationAttendu} onChange={(e) => maj({ verificationAttendu: e.target.value })} />}</Field>
      </section>
    </div>
  );
}

/** Aperçu en direct, tel que l'opérateur verra la résolution en exécution. */
export function ApercuResolution({ b, systemes }: { b: BrouillonResolution; systemes: SystemeCatalogue[] }) {
  const systeme = systemes.find((s) => s.id === b.systemeCatalogueId);
  return (
    <div className="grid gap-3.5">
      <Intertitre>Aperçu en exécution</Intertitre>
      <p className="text-base font-semibold">{b.titre.trim() || 'Sans titre'}</p>
      <div>{systeme ? <PuceSysteme nom={systeme.nom} description={systeme.description || undefined} /> : <span className="text-[13px] text-fg-subtle">Tous les systèmes</span>}</div>
      <Markdown source={`**Symptômes** : ${b.symptomes.trim() || '_à compléter_'}`} />
      <section className="grid gap-1.5">
        <p className="flex items-center gap-1.5 text-xs font-semibold tracking-wide text-fg-subtle uppercase"><Stethoscope className="size-3.5" aria-hidden />Diagnostic</p>
        <Instructions source={b.diagnostic} vide="À compléter." />
      </section>
      <ol className="grid gap-2">
        {b.corrections.map((c, i) => (
          <li key={c.cle} className="grid gap-1.5 rounded-lg border border-line p-2.5">
            <p className="text-[13px] font-semibold">Correction {i + 1}</p>
            <Instructions source={c.instruction} vide="À compléter." />
            <p className="text-[13px] text-fg-subtle">Attendu : {c.resultatAttendu.trim() || '…'}</p>
          </li>
        ))}
        <li className="grid gap-1.5 rounded-lg border border-ok-bd p-2.5">
          <p className="text-[13px] font-semibold">Vérification finale</p>
          <Instructions source={b.verificationInstruction} vide="À compléter." />
          <p className="text-[13px] text-fg-subtle">Attendu : {b.verificationAttendu.trim() || '…'}</p>
        </li>
      </ol>
    </div>
  );
}

interface Props {
  open: boolean;
  onOpenChange: (o: boolean) => void;
  /** Résolution à modifier ; absente pour une création. */
  initial?: ResolutionType;
  systemeParDefaut?: string | null;
  onEnregistree: (r: ResolutionType) => void;
}

/** Éditeur complet d'une résolution type en dialogue (modification depuis Paramètres › Résolutions types). */
export function ResolutionEditeurDialog({ open, onOpenChange, initial, systemeParDefaut, onEnregistree }: Props) {
  const toast = useToast();
  const [b, setB] = useState<BrouillonResolution>(() => brouillonDepuis(initial, systemeParDefaut));
  const [systemes, setSystemes] = useState<SystemeCatalogue[]>([]);
  const [pieces, setPieces] = useState<PieceJointe[]>([]);
  const admin = useContext(AuthContext)?.utilisateur?.estAdmin ?? false;
  const [erreur, setErreur] = useState('');
  const [envoi, setEnvoi] = useState(false);

  useEffect(() => {
    if (open) {
      setB(brouillonDepuis(initial, systemeParDefaut));
      setErreur('');
      api.catalogueSystemes().then(setSystemes).catch(() => setSystemes([]));
      if (initial) api.piecesResolution(initial.id).then(setPieces).catch(() => setPieces([])); else setPieces([]);
    }
  }, [open, initial, systemeParDefaut]);

  async function enregistrer(e: FormEvent) {
    e.preventDefault();
    setErreur('');
    if (!b.titre.trim()) { setErreur('Indiquez un titre.'); return; }
    setEnvoi(true);
    const saisie = saisieResolution(b);
    try {
      const r = initial ? await api.modifierResolution(initial.id, { ...saisie, version: initial.version }) : await api.creerResolution(saisie);
      toast.succes(initial ? 'Résolution type enregistrée.' : 'Résolution type créée.');
      onEnregistree(r);
      onOpenChange(false);
    } catch (err) {
      const msg = err instanceof Error ? err.message : 'Enregistrement impossible.';
      setErreur(err instanceof ApiError && err.status === 409 ? `${msg}\nRouvrez la résolution pour obtenir la dernière version.` : msg);
    } finally {
      setEnvoi(false);
    }
  }

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      taille="lg"
      titre={initial ? 'Modifier la résolution type' : 'Nouvelle résolution type'}
      description="Une procédure de correction écrite à l’avance, que l’opérateur déroule pas à pas en cas d’échec."
      pied={
        <>
          <Button variante="fantome" onClick={() => onOpenChange(false)}>Annuler</Button>
          <Button type="submit" form="form-resolution" variante="primaire" chargement={envoi}>Enregistrer la résolution</Button>
        </>
      }
    >
      <form id="form-resolution" onSubmit={(e) => void enregistrer(e)} className="space-y-5">
        {erreur && <Alerte ton="danger" icone={OctagonAlert} role="alert" titre="Enregistrement refusé">{erreur}</Alerte>}
        <FormulaireResolution b={b} onChange={setB} systemes={systemes} autoFocus />
      </form>

      <div className="mt-5 border-t border-line pt-5">
        {initial ? (
          <PiecesJointes
            titre="Pièces jointes de la résolution"
            pieces={pieces}
            peutSupprimer={() => admin}
            onDeposer={async (f, signaler) => {
              try { const p = await api.deposerPieceResolution(initial.id, f); setPieces((l) => [...l, p]); return true; }
              catch (e) { signaler(e instanceof Error ? e.message : 'Envoi impossible.'); return false; }
            }}
            onSupprimer={async (p, signaler) => {
              try { await api.supprimerPieceResolution(initial.id, p.id); setPieces((l) => l.filter((x) => x.id !== p.id)); return true; }
              catch (e) { signaler(e instanceof Error ? e.message : 'Suppression impossible.'); return false; }
            }}
          />
        ) : (
          <p className="text-sm text-fg-subtle">Enregistrez d’abord la résolution pour pouvoir y joindre des fichiers.</p>
        )}
      </div>
    </Dialog>
  );
}
