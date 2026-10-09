import { ArrowDown, ArrowUp, CheckCircle2, CircleOff, ListChecks, Lock, OctagonAlert, Pencil, Plus, Save, ShieldCheck, Trash2, UserCheck } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { api } from '../api';
import { useAuth } from '../auth';
import { useAsync } from '../hooks';
import type { CritereGoNoGo } from '../types';
import { Alerte } from '../ui/Alerte';
import { BoutonIcone, Button } from '../ui/Button';
import { Card, CardHeader } from '../ui/Card';
import { Checkbox } from '../ui/Checkbox';
import { ConfirmDialog, Dialog } from '../ui/Dialog';
import { EmptyState } from '../ui/EmptyState';
import { Field, Input } from '../ui/Input';
import { StatusPill } from '../ui/StatusPill';
import { Tooltip } from '../ui/Tooltip';
import { useToast } from '../ui/Toast';

function CritereDialog({ existant, open, onOpenChange, onFait }: { existant: CritereGoNoGo | null; open: boolean; onOpenChange: (o: boolean) => void; onFait: () => void }) {
  const toast = useToast();
  const [libelle, setLibelle] = useState('');
  const [actif, setActif] = useState(true);
  const [erreur, setErreur] = useState('');
  const [envoi, setEnvoi] = useState(false);
  useEffect(() => {
    if (open) { setLibelle(existant?.libelle ?? ''); setActif(existant?.actif ?? true); setErreur(''); }
  }, [open, existant]);

  async function enregistrer() {
    setEnvoi(true);
    setErreur('');
    try {
      if (existant) await api.modifierCritere(existant.id, { libelle: libelle.trim(), actif });
      else await api.creerCritere({ libelle: libelle.trim(), actif });
      toast.succes(existant ? 'Critère modifié.' : 'Critère ajouté.');
      onFait();
      onOpenChange(false);
    } catch (e) {
      setErreur(e instanceof Error ? e.message : 'Enregistrement impossible.');
    } finally {
      setEnvoi(false);
    }
  }

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      titre={existant ? 'Modifier le critère' : 'Nouveau critère'}
      description="Une vérification que le pilote coche avant de donner le Go."
      pied={<><Button variante="fantome" onClick={() => onOpenChange(false)}>Annuler</Button><Button variante="primaire" chargement={envoi} disabled={!libelle.trim()} onClick={() => void enregistrer()}>Enregistrer</Button></>}
    >
      <div className="space-y-4">
        {erreur && <Alerte ton="danger" icone={OctagonAlert} role="alert">{erreur}</Alerte>}
        <Field label="Libellé *">{(id) => <Input id={id} value={libelle} onChange={(e) => setLibelle(e.target.value)} placeholder="ex. Équipe présente et joignable" autoFocus />}</Field>
        <Checkbox label="Actif" description="Seuls les critères actifs sont à cocher lors du Go / no-go." checked={actif} onChange={setActif} />
      </div>
    </Dialog>
  );
}

function Approbations({ admin }: { admin: boolean }) {
  const toast = useToast();
  const { donnees, erreur, recharger } = useAsync(() => api.parametres(), []);
  const [valeur, setValeur] = useState<string | null>(null);
  const [envoi, setEnvoi] = useState(false);
  const courant = valeur ?? String(donnees?.approbationsMinimum ?? 1);
  const nombre = Number(courant);
  const valide = courant !== '' && Number.isInteger(nombre) && nombre >= 0;
  const modifie = donnees !== null && valide && nombre !== donnees.approbationsMinimum;

  async function enregistrer() {
    setEnvoi(true);
    try {
      await api.modifierParametres({ approbationsMinimum: nombre });
      toast.succes('Paramètre enregistré.');
      setValeur(null);
      recharger();
    } catch (e) {
      toast.erreur(e instanceof Error ? e.message : 'Enregistrement impossible.');
    } finally {
      setEnvoi(false);
    }
  }

  return (
    <Card>
      <CardHeader
        titre={<span className="flex items-center gap-2"><UserCheck className="size-4 text-accent-text" aria-hidden />Approbations minimum</span>}
        description="Nombre d’approbateurs qui doivent approuver une MEP avant le Go. 0 : aucune approbation exigée."
      />
      <div className="flex flex-wrap items-end gap-4 p-5">
        {erreur && <Alerte ton="danger" icone={OctagonAlert} role="alert" className="w-full">{erreur}</Alerte>}
        <Field label="Approbations minimum" className="w-40">
          {(id) => <Input id={id} type="number" min={0} step={1} inputMode="numeric" value={courant} disabled={!admin || donnees === null} onChange={(e) => setValeur(e.target.value)} />}
        </Field>
        {admin ? (
          <Button variante="primaire" icone={<Save className="size-4" aria-hidden />} chargement={envoi} disabled={!modifie} onClick={() => void enregistrer()}>Enregistrer</Button>
        ) : (
          <p className="flex items-center gap-1.5 pb-2 text-sm text-fg-subtle"><Lock className="size-3.5" aria-hidden />Lecture seule : seuls les administrateurs peuvent modifier ce paramètre.</p>
        )}
      </div>
    </Card>
  );
}

export default function GoNoGoPage() {
  const { utilisateur } = useAuth();
  const toast = useToast();
  const admin = utilisateur?.estAdmin ?? false;
  const { donnees, erreur, recharger } = useAsync(() => api.criteresGoNoGo(), []);
  const [edition, setEdition] = useState<{ existant: CritereGoNoGo | null } | null>(null);
  const [suppr, setSuppr] = useState<CritereGoNoGo | null>(null);
  const criteres = useMemo(() => [...(donnees ?? [])].sort((a, b) => a.ordre - b.ordre || a.libelle.localeCompare(b.libelle)), [donnees]);

  async function deplacer(i: number, delta: number) {
    const j = i + delta;
    if (j < 0 || j >= criteres.length) return;
    const liste = [...criteres];
    [liste[i], liste[j]] = [liste[j], liste[i]];
    try {
      // On renumérote de 1 à n : les deux critères échangés (et tout doublon d'ordre) sont mis à jour.
      await Promise.all(liste.map((c, k) => (c.ordre !== k + 1 ? api.modifierCritere(c.id, { libelle: c.libelle, ordre: k + 1, actif: c.actif }) : null)));
    } catch (e) {
      toast.erreur(e instanceof Error ? e.message : 'Réorganisation impossible.');
    }
    recharger();
  }

  async function basculer(c: CritereGoNoGo) {
    try { await api.modifierCritere(c.id, { libelle: c.libelle, ordre: c.ordre, actif: !c.actif }); recharger(); }
    catch (e) { toast.erreur(e instanceof Error ? e.message : 'Modification impossible.'); }
  }

  return (
    <main className="mx-auto w-full max-w-4xl px-6 py-8">
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Go / no-go et approbations</h1>
          <p className="mt-1 text-sm text-fg-subtle">Les vérifications avant de lancer une MEP. {!admin && <span className="inline-flex items-center gap-1"><Lock className="size-3.5" aria-hidden />Lecture seule : seuls les administrateurs peuvent modifier ces réglages.</span>}</p>
        </div>
        {admin && <Button variante="primaire" icone={<Plus className="size-4" aria-hidden />} onClick={() => setEdition({ existant: null })}>Nouveau critère</Button>}
      </div>

      <div className="mt-6 space-y-6">
        <Approbations admin={admin} />

        {erreur && <Alerte ton="danger" icone={OctagonAlert} role="alert">{erreur}</Alerte>}

        <Card className="overflow-hidden">
          <CardHeader
            titre={<span className="flex items-center gap-2"><ShieldCheck className="size-4 text-accent-text" aria-hidden />Critères de go / no-go</span>}
            description="Dans l’ordre où ils sont présentés au pilote. Seuls les critères actifs sont à cocher."
          />
          {donnees !== null && criteres.length === 0 ? (
            <EmptyState icone={ListChecks} titre="Aucun critère" description="Sans critère, le pilote peut donner le Go directement." action={admin ? <Button variante="primaire" icone={<Plus className="size-4" aria-hidden />} onClick={() => setEdition({ existant: null })}>Nouveau critère</Button> : undefined} />
          ) : (
            <ol className="divide-y divide-line">
              {criteres.map((c, i) => (
                <li key={c.id} className="flex flex-wrap items-center gap-x-4 gap-y-2 px-5 py-3 transition-colors duration-150 hover:bg-surface-2/50">
                  <span className="flex size-6 shrink-0 items-center justify-center rounded-md bg-surface-2 font-mono text-xs font-bold tnum">{i + 1}</span>
                  <p className={`min-w-0 flex-1 basis-60 font-medium ${c.actif ? '' : 'text-fg-muted line-through'}`}>{c.libelle}</p>
                  <StatusPill taille="sm" ton={c.actif ? 'ok' : 'wait'} icone={c.actif ? CheckCircle2 : CircleOff}>{c.actif ? 'Actif' : 'Inactif'}</StatusPill>
                  {admin && (
                    <div className="flex items-center gap-0.5">
                      <BoutonIcone label={`Monter ${c.libelle}`} taille="sm" disabled={i === 0} onClick={() => void deplacer(i, -1)}><ArrowUp className="size-4" aria-hidden /></BoutonIcone>
                      <BoutonIcone label={`Descendre ${c.libelle}`} taille="sm" disabled={i === criteres.length - 1} onClick={() => void deplacer(i, 1)}><ArrowDown className="size-4" aria-hidden /></BoutonIcone>
                      <Tooltip content={c.actif ? 'Désactiver' : 'Activer'}>
                        <BoutonIcone label={`${c.actif ? 'Désactiver' : 'Activer'} ${c.libelle}`} taille="sm" onClick={() => void basculer(c)}>{c.actif ? <CircleOff className="size-4" aria-hidden /> : <CheckCircle2 className="size-4" aria-hidden />}</BoutonIcone>
                      </Tooltip>
                      <Tooltip content="Modifier"><BoutonIcone label={`Modifier ${c.libelle}`} taille="sm" onClick={() => setEdition({ existant: c })}><Pencil className="size-4" aria-hidden /></BoutonIcone></Tooltip>
                      <Tooltip content="Supprimer"><BoutonIcone label={`Supprimer ${c.libelle}`} taille="sm" onClick={() => setSuppr(c)}><Trash2 className="size-4" aria-hidden /></BoutonIcone></Tooltip>
                    </div>
                  )}
                </li>
              ))}
            </ol>
          )}
        </Card>
      </div>

      <CritereDialog existant={edition?.existant ?? null} open={edition !== null} onOpenChange={(o) => { if (!o) setEdition(null); }} onFait={recharger} />
      <ConfirmDialog
        open={suppr !== null}
        onOpenChange={(o) => { if (!o) setSuppr(null); }}
        titre={`Supprimer « ${suppr?.libelle ?? ''} » ?`}
        description="Les MEP déjà lancées gardent leur copie des critères cochés."
        libelleConfirmer="Supprimer"
        variante="danger"
        onConfirmer={async () => {
          if (!suppr) return false;
          try { await api.supprimerCritere(suppr.id); toast.succes('Critère supprimé.'); recharger(); return true; }
          catch (e) { toast.erreur(e instanceof Error ? e.message : 'Suppression impossible.'); return false; }
        }}
      />
    </main>
  );
}
