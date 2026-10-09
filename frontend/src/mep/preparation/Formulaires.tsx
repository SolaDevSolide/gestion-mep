import { useEffect, useState, type FormEvent, type ReactNode } from 'react';
import { Button } from '../../ui/Button';
import { Card, CardHeader } from '../../ui/Card';
import { Dialog } from '../../ui/Dialog';
import { Field, Input, Select, Textarea } from '../../ui/Input';

export interface ChampForm {
  cle: string;
  label: string;
  type?: 'texte' | 'zone' | 'select';
  options?: { valeur: string; libelle: string }[];
  requis?: boolean;
  placeholder?: string;
  large?: boolean;
}

interface Props {
  open: boolean;
  onOpenChange: (o: boolean) => void;
  titre: string;
  description?: string;
  champs: ChampForm[];
  initial: Record<string, string>;
  libelleValider: string;
  /** Retourne true si l'enregistrement a abouti. */
  onValider: (valeurs: Record<string, string>) => Promise<boolean>;
}

/** Dialogue de saisie générique (participant, livrable, système libre). */
export function FormulaireDialog({ open, onOpenChange, titre, description, champs, initial, libelleValider, onValider }: Props) {
  const [valeurs, setValeurs] = useState(initial);
  const [envoi, setEnvoi] = useState(false);
  useEffect(() => { if (open) setValeurs(initial); }, [open, initial]);

  async function valider(e: FormEvent) {
    e.preventDefault();
    setEnvoi(true);
    try {
      if (await onValider(valeurs)) onOpenChange(false);
    } finally {
      setEnvoi(false);
    }
  }
  const manquant = champs.some((c) => c.requis && !(valeurs[c.cle] ?? '').trim());

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      titre={titre}
      description={description}
      pied={
        <>
          <Button variante="fantome" onClick={() => onOpenChange(false)}>Annuler</Button>
          <Button type="submit" form="formulaire-dialog" variante="primaire" chargement={envoi} disabled={manquant}>{libelleValider}</Button>
        </>
      }
    >
      <form id="formulaire-dialog" onSubmit={(e) => void valider(e)} className="grid gap-4 sm:grid-cols-2">
        {champs.map((c, i) => (
          <Field key={c.cle} label={c.label + (c.requis ? ' *' : '')} className={c.large || c.type === 'zone' ? 'sm:col-span-2' : undefined}>
            {(id) =>
              c.type === 'select' ? (
                <Select id={id} value={valeurs[c.cle] ?? ''} onChange={(e) => setValeurs({ ...valeurs, [c.cle]: e.target.value })}>
                  {(c.options ?? []).map((o) => <option key={o.valeur} value={o.valeur}>{o.libelle}</option>)}
                </Select>
              ) : c.type === 'zone' ? (
                <Textarea id={id} value={valeurs[c.cle] ?? ''} onChange={(e) => setValeurs({ ...valeurs, [c.cle]: e.target.value })} placeholder={c.placeholder} />
              ) : (
                <Input id={id} value={valeurs[c.cle] ?? ''} onChange={(e) => setValeurs({ ...valeurs, [c.cle]: e.target.value })} placeholder={c.placeholder} autoFocus={i === 0} />
              )
            }
          </Field>
        ))}
      </form>
    </Dialog>
  );
}

/** Carte de section avec en-tête et pied d'actions. */
export function SectionCarte({ numero, titre, description, action, children, pied }: { numero: number; titre: string; description?: string; action?: ReactNode; children: ReactNode; pied?: ReactNode }) {
  return (
    <Card>
      <CardHeader
        titre={<span className="flex items-center gap-2.5"><span className="flex size-6 items-center justify-center rounded-md bg-accent-soft font-mono text-xs font-bold text-accent-text">{numero}</span>{titre}</span>}
        description={description}
        action={action}
      />
      <div className="px-5 py-5">{children}</div>
      {pied && <div className="flex flex-wrap items-center gap-3 border-t border-line bg-surface-2/50 px-5 py-3">{pied}</div>}
    </Card>
  );
}

export const vide = (v: string): string | null => (v === '' ? null : v);
