import { ArrowLeft, OctagonAlert, Rocket, ShieldQuestion, ThumbsDown } from 'lucide-react';
import { useEffect, useState } from 'react';
import type { CritereGoNoGo } from '../types';
import { Alerte } from '../ui/Alerte';
import { Button } from '../ui/Button';
import { Checkbox } from '../ui/Checkbox';
import { Dialog } from '../ui/Dialog';
import { Field, Textarea } from '../ui/Input';
import { ProgressBar } from '../ui/ProgressBar';

interface Props {
  open: boolean;
  onOpenChange: (o: boolean) => void;
  /** Critères actifs, dans l'ordre. */
  criteres: CritereGoNoGo[];
  /** Anomalies du plan encore à corriger (le serveur refusera le Go tant qu'il en reste). */
  erreursPlan?: string[];
  chargement?: boolean;
  /** Retourne true si la décision a abouti. */
  onGo: (criteresCoches: string[]) => Promise<boolean | void> | boolean | void;
  onNoGo: (motif: string) => Promise<boolean | void> | boolean | void;
}

/** Décision Go / no-go : liste de contrôle à cocher, Go tant que tout n'est pas coché est impossible, no-go avec motif. */
export function GoNoGoDialog({ open, onOpenChange, criteres, erreursPlan = [], chargement, onGo, onNoGo }: Props) {
  const [coches, setCoches] = useState<string[]>([]);
  const [noGo, setNoGo] = useState(false);
  const [motif, setMotif] = useState('');
  const [envoi, setEnvoi] = useState<'go' | 'nogo' | null>(null);

  useEffect(() => {
    if (open) { setCoches([]); setNoGo(false); setMotif(''); }
  }, [open]);

  const tousCoches = criteres.every((c) => coches.includes(c.id));
  const bascule = (id: string, v: boolean) => setCoches((l) => (v ? [...l, id] : l.filter((x) => x !== id)));

  async function decider(type: 'go' | 'nogo') {
    setEnvoi(type);
    try {
      const r = type === 'go' ? await onGo(coches) : await onNoGo(motif.trim());
      if (r !== false) onOpenChange(false);
    } finally {
      setEnvoi(null);
    }
  }

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      taille="md"
      titre={noGo ? 'No-go : ne pas démarrer' : 'Go / no-go'}
      description={noGo
        ? 'La MEP reste en préparation. Le motif est tracé au journal et visible de tous.'
        : 'Dernière vérification avant de figer le plan et de lancer l’exécution. Cochez chaque critère une fois contrôlé.'}
      pied={noGo ? (
        <>
          <Button variante="fantome" icone={<ArrowLeft className="size-4" aria-hidden />} onClick={() => setNoGo(false)}>Retour</Button>
          <Button variante="danger" icone={<ThumbsDown className="size-4" aria-hidden />} chargement={envoi === 'nogo'} disabled={!motif.trim()} onClick={() => void decider('nogo')}>Confirmer le no-go</Button>
        </>
      ) : (
        <>
          <Button variante="fantome" onClick={() => onOpenChange(false)}>Annuler</Button>
          <Button variante="danger-doux" icone={<ThumbsDown className="size-4" aria-hidden />} onClick={() => setNoGo(true)}>No-go</Button>
          <Button variante="succes" icone={<Rocket className="size-4" aria-hidden />} chargement={envoi === 'go'} disabled={!tousCoches || chargement} onClick={() => void decider('go')}>Go : passer en exécution</Button>
        </>
      )}
    >
      {noGo ? (
        <Field label="Motif du no-go (obligatoire)" hint="Expliquez ce qui empêche de démarrer : l’équipe le lira dans le journal.">
          {(id) => <Textarea id={id} rows={4} value={motif} onChange={(e) => setMotif(e.target.value)} placeholder="ex. L’équipe base de données n’est pas joignable ce soir." autoFocus />}
        </Field>
      ) : (
        <div className="space-y-4">
          {erreursPlan.length > 0 && (
            <Alerte ton="warn" icone={OctagonAlert} titre={`${erreursPlan.length} point(s) à corriger dans le plan`}>
              Le serveur refusera le Go tant que le plan est incomplet (voir « Vérifier le plan »).
            </Alerte>
          )}
          {criteres.length === 0 ? (
            <Alerte ton="wait" icone={ShieldQuestion}>Aucun critère de go / no-go n’est actif : le Go peut être donné directement.</Alerte>
          ) : (
            <>
              <div className="flex items-center gap-3">
                <ProgressBar className="flex-1" hauteur="h-1.5" segments={[
                  { valeur: coches.length, ton: 'ok', libelle: 'Contrôlés' },
                  { valeur: criteres.length - coches.length, ton: 'wait', libelle: 'À contrôler' },
                ]} />
                <span className="text-xs font-medium text-fg-muted tnum">{coches.length}/{criteres.length} critères</span>
              </div>
              <ul className="space-y-2" aria-label="Critères de go / no-go">
                {criteres.map((c) => {
                  const coche = coches.includes(c.id);
                  return (
                    <li key={c.id} className={`rounded-lg border px-3.5 py-3 transition-colors duration-150 ${coche ? 'border-ok-bd bg-ok-bg/50' : 'border-line bg-surface-2/40'}`}>
                      <Checkbox label={<span className="font-medium">{c.libelle}</span>} checked={coche} onChange={(v) => bascule(c.id, v)} />
                    </li>
                  );
                })}
              </ul>
            </>
          )}
        </div>
      )}
    </Dialog>
  );
}
