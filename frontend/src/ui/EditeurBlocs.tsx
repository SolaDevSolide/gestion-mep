import { ArrowDown, ArrowUp, Code2, Plus, Type, X } from 'lucide-react';
import { useRef, useState } from 'react';
import { decouperBlocs, recomposerBlocs, type Bloc, type TypeBloc } from '../blocs';
import { BoutonIcone, Button } from './Button';
import { cn } from './cn';
import { Markdown } from './Instructions';
import { Segmented } from './Segmented';
import { Textarea } from './Input';

interface BlocEdite extends Bloc {
  cle: number;
}

interface Props {
  /** Chaîne markdown (les blocs de code y sont délimités par ```). */
  valeur: string;
  onChange: (markdown: string) => void;
  /** Nom accessible de l'éditeur (ex. « Instructions »). */
  label: string;
  /** Type du bloc proposé quand la valeur est vide. */
  typeInitial?: TypeBloc;
  disabled?: boolean;
  className?: string;
}

let compteur = 0;
const numeroter = (b: Bloc): BlocEdite => ({ ...b, cle: ++compteur });
function depuis(valeur: string, typeInitial: TypeBloc): BlocEdite[] {
  const blocs = decouperBlocs(valeur);
  return (blocs.length ? blocs : [{ type: typeInitial, contenu: '' }]).map(numeroter);
}

/**
 * Éditeur d'instructions en blocs : chaque bloc est Texte (markdown, avec aperçu) ou Code (chasse fixe, copiable
 * en exécution). La valeur émise est la chaîne markdown recomposée ; les blocs vides restent affichés localement.
 */
export function EditeurBlocs({ valeur, onChange, label, typeInitial = 'texte', disabled, className }: Props) {
  const [blocs, setBlocs] = useState<BlocEdite[]>(() => depuis(valeur, typeInitial));
  const emis = useRef(valeur);

  // Valeur changée de l'extérieur (annulation, rechargement) : on repart de la valeur reçue.
  if (valeur !== emis.current && valeur !== recomposerBlocs(blocs)) {
    const nouveaux = depuis(valeur, typeInitial);
    emis.current = valeur;
    setBlocs(nouveaux);
  }

  function changer(liste: BlocEdite[]) {
    setBlocs(liste);
    const md = recomposerBlocs(liste);
    emis.current = md;
    if (md !== valeur) onChange(md);
  }
  const maj = (i: number, patch: Partial<Bloc>) => changer(blocs.map((b, j) => (j === i ? { ...b, ...patch } : b)));
  const deplacer = (i: number, delta: number) => {
    const j = i + delta;
    if (j < 0 || j >= blocs.length) return;
    const l = [...blocs];
    [l[i], l[j]] = [l[j], l[i]];
    changer(l);
  };
  const supprimer = (i: number) => {
    const l = blocs.filter((_, j) => j !== i);
    changer(l.length ? l : [numeroter({ type: 'texte', contenu: '' })]);
  };
  const ajouter = (type: TypeBloc) => changer([...blocs, numeroter({ type, contenu: '' })]);

  return (
    <div role="group" aria-label={label} className={cn('grid gap-2', className)}>
      {blocs.map((b, i) => {
        const nom = `${label}, bloc ${i + 1}`;
        return (
          <div key={b.cle} className="grid gap-1.5 rounded-lg border border-line bg-surface p-2">
            <div className="flex items-center justify-between gap-2">
              <Segmented
                label={`Type du bloc ${i + 1}`}
                valeur={b.type}
                disabled={disabled}
                onChange={(t) => maj(i, { type: t })}
                options={[
                  { valeur: 'texte', libelle: <><Type className="size-3.5" aria-hidden />Texte</> },
                  { valeur: 'code', libelle: <><Code2 className="size-3.5" aria-hidden />Code</> },
                ]}
              />
              <span className="mr-auto text-[11px] text-fg-subtle">{b.type === 'texte' ? 'Markdown' : 'Copiable en exécution'}</span>
              {!disabled && (
                <span className="flex items-center">
                  <BoutonIcone label={`Monter le bloc ${i + 1}`} taille="sm" className="size-7" disabled={i === 0} onClick={() => deplacer(i, -1)}><ArrowUp className="size-3.5" aria-hidden /></BoutonIcone>
                  <BoutonIcone label={`Descendre le bloc ${i + 1}`} taille="sm" className="size-7" disabled={i === blocs.length - 1} onClick={() => deplacer(i, 1)}><ArrowDown className="size-3.5" aria-hidden /></BoutonIcone>
                  <BoutonIcone label={`Supprimer le bloc ${i + 1}`} taille="sm" className="size-7" onClick={() => supprimer(i)}><X className="size-3.5" aria-hidden /></BoutonIcone>
                </span>
              )}
            </div>
            <Textarea
              aria-label={`${nom} (${b.type === 'texte' ? 'texte' : 'code'})`}
              mono={b.type === 'code'}
              rows={b.type === 'code' ? 2 : 3}
              disabled={disabled}
              value={b.contenu}
              onChange={(e) => maj(i, { contenu: e.target.value })}
              placeholder={b.type === 'code' ? 'Commande exacte, copiée telle quelle' : 'Texte libre : **gras**, listes -, `code`, - [ ] case…'}
              className={cn(b.type === 'code' && 'bg-code-bg text-code-fg')}
            />
            {b.type === 'texte' && b.contenu.trim() && (
              <div className="border-t border-dashed border-line pt-1.5">
                <p className="mb-0.5 text-[10px] font-semibold tracking-wide text-fg-subtle uppercase">Aperçu</p>
                <Markdown source={b.contenu} />
              </div>
            )}
          </div>
        );
      })}
      {!disabled && (
        <div className="flex flex-wrap gap-1.5">
          <Button taille="sm" icone={<Plus className="size-3.5" aria-hidden />} onClick={() => ajouter('texte')}>Bloc texte</Button>
          <Button taille="sm" icone={<Plus className="size-3.5" aria-hidden />} onClick={() => ajouter('code')}>Bloc code</Button>
        </div>
      )}
    </div>
  );
}
