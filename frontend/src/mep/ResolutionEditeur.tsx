import { ArrowDown, ArrowUp, OctagonAlert, Plus, Trash2 } from 'lucide-react';
import { useEffect, useState, type FormEvent } from 'react';
import { api, ApiError } from '../api';
import type { CorrectionType, ResolutionType, SystemeCatalogue } from '../types';
import { Alerte } from '../ui/Alerte';
import { BoutonIcone, Button } from '../ui/Button';
import { Dialog } from '../ui/Dialog';
import { Field, Input, Select, Textarea } from '../ui/Input';
import { useToast } from '../ui/Toast';

interface Brouillon {
  titre: string;
  systemeCatalogueId: string;
  symptomes: string;
  diagnostic: string;
  corrections: CorrectionType[];
  verificationInstruction: string;
  verificationAttendu: string;
}

const correctionVide = (ordre: number): CorrectionType => ({ ordre, instruction: '', resultatAttendu: '' });

function depuis(r: ResolutionType | undefined, systemeParDefaut: string | null | undefined): Brouillon {
  return {
    titre: r?.titre ?? '',
    systemeCatalogueId: r?.systemeCatalogueId ?? systemeParDefaut ?? '',
    symptomes: r?.symptomes ?? '',
    diagnostic: r?.diagnostic ?? '',
    corrections: r ? [...r.corrections].sort((a, b) => a.ordre - b.ordre) : [correctionVide(1)],
    verificationInstruction: r?.verificationInstruction ?? '',
    verificationAttendu: r?.verificationAttendu ?? '',
  };
}

interface Props {
  open: boolean;
  onOpenChange: (o: boolean) => void;
  /** Résolution à modifier ; absente pour une création. */
  initial?: ResolutionType;
  systemeParDefaut?: string | null;
  onEnregistree: (r: ResolutionType) => void;
}

/** Éditeur complet d'une résolution type (utilisé dans les paramètres et depuis l'édition d'une étape). */
export function ResolutionEditeurDialog({ open, onOpenChange, initial, systemeParDefaut, onEnregistree }: Props) {
  const toast = useToast();
  const [b, setB] = useState<Brouillon>(() => depuis(initial, systemeParDefaut));
  const [systemes, setSystemes] = useState<SystemeCatalogue[]>([]);
  const [erreur, setErreur] = useState('');
  const [envoi, setEnvoi] = useState(false);

  useEffect(() => {
    if (open) {
      setB(depuis(initial, systemeParDefaut));
      setErreur('');
      api.catalogueSystemes().then(setSystemes).catch(() => setSystemes([]));
    }
  }, [open, initial, systemeParDefaut]);

  const maj = (patch: Partial<Brouillon>) => setB((x) => ({ ...x, ...patch }));
  const majCorrection = (i: number, patch: Partial<CorrectionType>) => maj({ corrections: b.corrections.map((c, j) => (j === i ? { ...c, ...patch } : c)) });
  const deplacer = (i: number, delta: number) => {
    const j = i + delta;
    if (j < 0 || j >= b.corrections.length) return;
    const l = [...b.corrections];
    [l[i], l[j]] = [l[j], l[i]];
    maj({ corrections: l });
  };

  async function enregistrer(e: FormEvent) {
    e.preventDefault();
    setErreur('');
    setEnvoi(true);
    const saisie = {
      titre: b.titre.trim(),
      systemeCatalogueId: b.systemeCatalogueId || null,
      symptomes: b.symptomes.trim(),
      diagnostic: b.diagnostic.trim(),
      corrections: b.corrections.map((c, i) => ({ ordre: i + 1, instruction: c.instruction.trim(), resultatAttendu: c.resultatAttendu.trim() })),
      verificationInstruction: b.verificationInstruction.trim(),
      verificationAttendu: b.verificationAttendu.trim(),
    };
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
        <div className="grid gap-4 md:grid-cols-2">
          <Field label="Titre *">{(id) => <Input id={id} value={b.titre} onChange={(e) => maj({ titre: e.target.value })} placeholder="ex. API ne répond plus après redémarrage" autoFocus />}</Field>
          <Field label="Système concerné" hint="Sert à proposer cette résolution sur les étapes de ce système.">
            {(id) => (
              <Select id={id} value={b.systemeCatalogueId} onChange={(e) => maj({ systemeCatalogueId: e.target.value })}>
                <option value="">— tous les systèmes —</option>
                {systemes.map((s) => <option key={s.id} value={s.id}>{s.nom}{s.actif ? '' : ' (inactif)'}</option>)}
              </Select>
            )}
          </Field>
        </div>
        <Field label="Symptômes : comment reconnaître ce problème ?">{(id) => <Textarea id={id} rows={2} value={b.symptomes} onChange={(e) => maj({ symptomes: e.target.value })} />}</Field>
        <Field label="Diagnostic : commande ou vérification pour confirmer">{(id) => <Textarea id={id} mono rows={3} value={b.diagnostic} onChange={(e) => maj({ diagnostic: e.target.value })} />}</Field>

        <fieldset className="space-y-3">
          <legend className="mb-1 text-[13px] font-semibold">Corrections, dans l’ordre</legend>
          {b.corrections.map((c, i) => (
            <div key={i} className="rounded-lg border border-line bg-surface-2/40 p-3">
              <div className="mb-2 flex items-center justify-between">
                <span className="flex size-6 items-center justify-center rounded-md bg-accent-soft font-mono text-xs font-bold text-accent-text">{i + 1}</span>
                <div className="flex items-center gap-1">
                  <BoutonIcone label={`Monter la correction ${i + 1}`} taille="sm" disabled={i === 0} onClick={() => deplacer(i, -1)}><ArrowUp className="size-4" aria-hidden /></BoutonIcone>
                  <BoutonIcone label={`Descendre la correction ${i + 1}`} taille="sm" disabled={i === b.corrections.length - 1} onClick={() => deplacer(i, 1)}><ArrowDown className="size-4" aria-hidden /></BoutonIcone>
                  <BoutonIcone label={`Supprimer la correction ${i + 1}`} taille="sm" onClick={() => maj({ corrections: b.corrections.filter((_, j) => j !== i) })}><Trash2 className="size-4" aria-hidden /></BoutonIcone>
                </div>
              </div>
              <div className="grid gap-3">
                <Field label="Instruction exacte">{(id) => <Textarea id={id} mono rows={2} value={c.instruction} onChange={(e) => majCorrection(i, { instruction: e.target.value })} />}</Field>
                <Field label="Résultat attendu">{(id) => <Input id={id} value={c.resultatAttendu} onChange={(e) => majCorrection(i, { resultatAttendu: e.target.value })} />}</Field>
              </div>
            </div>
          ))}
          <Button taille="sm" icone={<Plus className="size-4" aria-hidden />} onClick={() => maj({ corrections: [...b.corrections, correctionVide(b.corrections.length + 1)] })}>Ajouter une correction</Button>
        </fieldset>

        <fieldset className="space-y-3 rounded-lg border border-ok-bd bg-ok-bg/40 p-3">
          <legend className="px-1 text-[13px] font-semibold">Vérification finale</legend>
          <Field label="Instruction de vérification">{(id) => <Textarea id={id} mono rows={2} value={b.verificationInstruction} onChange={(e) => maj({ verificationInstruction: e.target.value })} />}</Field>
          <Field label="Résultat attendu">{(id) => <Input id={id} value={b.verificationAttendu} onChange={(e) => maj({ verificationAttendu: e.target.value })} />}</Field>
        </fieldset>
      </form>
    </Dialog>
  );
}
