import { ArrowDown, ArrowUp, Bold, Code2, Eye, Italic, Link2, List, ListChecks, PenLine, Plus, SquareTerminal, Trash2, Type } from 'lucide-react';
import { forwardRef, useEffect, useLayoutEffect, useRef, useState, type KeyboardEvent, type TextareaHTMLAttributes } from 'react';
import { decouperBlocs, formaterMarkdown, recomposerBlocs, type ActionFormat, type Bloc, type TypeBloc } from '../blocs';
import { BoutonIcone, Button } from './Button';
import { cn } from './cn';
import { Markdown } from './Instructions';
import { Segmented } from './Segmented';

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
  /** Bloc texte : saisie et aperçu en onglets (défaut) ou côte à côte (panneau agrandi). */
  disposition?: 'onglets' | 'cote';
}

let compteur = 0;
const numeroter = (b: Bloc): BlocEdite => ({ ...b, cle: ++compteur });
function depuis(valeur: string, typeInitial: TypeBloc): BlocEdite[] {
  const blocs = decouperBlocs(valeur);
  return (blocs.length ? blocs : [{ type: typeInitial, contenu: '' }]).map(numeroter);
}

/** Zone de saisie dont la hauteur suit le contenu (plus de petite zone à faire défiler). */
export const ZoneAuto = forwardRef<HTMLTextAreaElement, TextareaHTMLAttributes<HTMLTextAreaElement> & { minLignes?: number }>(function ZoneAuto(
  { className, minLignes = 2, value, ...rest },
  refExterne,
) {
  const ref = useRef<HTMLTextAreaElement | null>(null);
  useLayoutEffect(() => {
    const el = ref.current;
    if (!el) return;
    el.style.height = 'auto';
    if (el.scrollHeight > 0) el.style.height = `${el.scrollHeight + 2}px`;
  }, [value]);
  return (
    <textarea
      ref={(el) => {
        ref.current = el;
        if (typeof refExterne === 'function') refExterne(el);
        else if (refExterne) refExterne.current = el;
      }}
      rows={minLignes}
      value={value}
      className={cn('block w-full resize-none overflow-hidden rounded-md px-3 py-2 text-sm leading-relaxed focus:outline-none focus-visible:ring-2 focus-visible:ring-accent/40 disabled:cursor-not-allowed disabled:opacity-80', className)}
      {...rest}
    />
  );
});

const OUTILS: { action: ActionFormat; libelle: string; Icone: typeof Bold }[] = [
  { action: 'gras', libelle: 'Gras', Icone: Bold },
  { action: 'italique', libelle: 'Italique', Icone: Italic },
  { action: 'liste', libelle: 'Liste à puces', Icone: List },
  { action: 'cases', libelle: 'Liste à cocher', Icone: ListChecks },
  { action: 'code', libelle: 'Code en ligne', Icone: Code2 },
  { action: 'lien', libelle: 'Lien', Icone: Link2 },
];

type OnContenu = (contenu: string, selection?: [number, number]) => void;

/** Bloc Texte : barre d'outils markdown, Écrire / Aperçu (ou côte à côte). */
function BlocTexte({ bloc, nom, disabled, disposition, onContenu, zoneRef }: {
  bloc: BlocEdite; nom: string; disabled?: boolean; disposition: 'onglets' | 'cote'; onContenu: OnContenu; zoneRef: (el: HTMLTextAreaElement | null) => void;
}) {
  const [vue, setVue] = useState<'ecrire' | 'apercu'>('ecrire');
  const zone = useRef<HTMLTextAreaElement | null>(null);
  const cote = disposition === 'cote';

  function formater(action: ActionFormat) {
    const el = zone.current;
    const debut = el?.selectionStart ?? bloc.contenu.length;
    const fin = el?.selectionEnd ?? bloc.contenu.length;
    const r = formaterMarkdown(bloc.contenu, debut, fin, action);
    onContenu(r.valeur, [r.debut, r.fin]);
  }

  const saisie = (
    <ZoneAuto
      ref={(el) => { zone.current = el; zoneRef(el); }}
      aria-label={`${nom} (texte)`}
      minLignes={3}
      disabled={disabled}
      value={bloc.contenu}
      onChange={(e) => onContenu(e.target.value)}
      onKeyDown={(e) => {
        if ((e.ctrlKey || e.metaKey) && (e.key === 'b' || e.key === 'i')) { e.preventDefault(); formater(e.key === 'b' ? 'gras' : 'italique'); }
      }}
      placeholder={'Décrivez l’action… ex. Prévenir le support **avant** de lancer.\n- [ ] vérifier le tableau de bord'}
      className="border border-line-strong bg-surface text-fg placeholder:text-fg-subtle"
    />
  );
  const apercu = (
    <div className="min-h-[4.5rem] rounded-md border border-dashed border-line bg-surface-2/40 px-3 py-2">
      {bloc.contenu.trim() ? <Markdown source={bloc.contenu} /> : <p className="text-sm text-fg-subtle">Rien à afficher pour l’instant.</p>}
    </div>
  );

  return (
    <div className="grid gap-2">
      <div className="flex flex-wrap items-center gap-1">
        {!cote && (
          <div role="tablist" aria-label={`${nom} : mode`} className="mr-2 inline-flex rounded-md border border-line bg-surface-2 p-0.5">
            {([['ecrire', 'Écrire', PenLine], ['apercu', 'Aperçu', Eye]] as const).map(([v, lib, Icone]) => (
              <button
                key={v}
                type="button"
                role="tab"
                aria-selected={vue === v}
                onClick={() => setVue(v)}
                className={cn('inline-flex h-7 items-center gap-1 rounded px-2 text-xs font-medium text-fg-subtle hover:text-fg', vue === v && 'bg-surface text-fg shadow-sm')}
              >
                <Icone className="size-3.5" aria-hidden />{lib}
              </button>
            ))}
          </div>
        )}
        {!disabled && (cote || vue === 'ecrire') && (
          <div role="toolbar" aria-label={`${nom} : mise en forme`} className="inline-flex items-center gap-0.5">
            {OUTILS.map(({ action, libelle, Icone }) => (
              <BoutonIcone key={action} label={libelle} taille="sm" className="size-7" onMouseDown={(e) => e.preventDefault()} onClick={() => formater(action)}>
                <Icone className="size-3.5" aria-hidden />
              </BoutonIcone>
            ))}
          </div>
        )}
      </div>
      {cote ? (
        <div className="grid gap-2 lg:grid-cols-2">{saisie}<div><p className="mb-1 text-[10px] font-semibold tracking-wide text-fg-subtle uppercase">Aperçu</p>{apercu}</div></div>
      ) : vue === 'ecrire' ? saisie : apercu}
    </div>
  );
}

/** Bloc Code : zone sombre à chasse fixe ; Tab insère deux espaces (Échap puis Tab pour sortir). */
function BlocCode({ bloc, nom, disabled, onContenu, onLangage, zoneRef }: {
  bloc: BlocEdite; nom: string; disabled?: boolean; onContenu: OnContenu; onLangage: (l: string) => void; zoneRef: (el: HTMLTextAreaElement | null) => void;
}) {
  const sortie = useRef(false);
  function clavier(e: KeyboardEvent<HTMLTextAreaElement>) {
    if (e.key === 'Escape') { sortie.current = true; return; }
    if (e.key !== 'Tab' || e.shiftKey || sortie.current) { sortie.current = false; return; }
    e.preventDefault();
    const el = e.currentTarget;
    const d = el.selectionStart;
    const f = el.selectionEnd;
    onContenu(`${bloc.contenu.slice(0, d)}  ${bloc.contenu.slice(f)}`, [d + 2, d + 2]);
  }
  return (
    <div className="grid gap-1.5">
      <ZoneAuto
        ref={zoneRef}
        aria-label={`${nom} (code)`}
        minLignes={2}
        spellCheck={false}
        wrap="off"
        disabled={disabled}
        value={bloc.contenu}
        onChange={(e) => onContenu(e.target.value)}
        onKeyDown={clavier}
        placeholder="Collez la commande exacte… ex. cd /opt/commandes && ./deploy.sh 2.4.0"
        className="overflow-x-auto border border-[#2b3340] bg-[#11161d] font-mono text-[13px] whitespace-pre text-[#e6edf3] placeholder:text-[#8b97a6]"
      />
      <div className="flex flex-wrap items-center justify-between gap-2 text-[11px] text-fg-subtle">
        <span title="Tab insère deux espaces ; Échap puis Tab pour sortir de la zone.">Copiable en exécution</span>
        {!disabled && (
          <input
            aria-label={`${nom} : langage (facultatif)`}
            value={bloc.langage ?? ''}
            onChange={(e) => onLangage(e.target.value.replace(/[`\s]/g, ''))}
            placeholder="langage (ex. bash)"
            className="h-6 w-36 rounded border border-line bg-surface px-1.5 font-mono text-[11px] text-fg"
          />
        )}
      </div>
    </div>
  );
}

/**
 * Éditeur d'instructions en blocs : chaque bloc est Texte (markdown, barre d'outils, aperçu) ou Code (chasse fixe,
 * copiable en exécution). La valeur émise est la chaîne markdown recomposée ; les blocs vides restent affichés localement.
 */
export function EditeurBlocs({ valeur, onChange, label, typeInitial = 'texte', disabled, className, disposition = 'onglets' }: Props) {
  const [blocs, setBlocs] = useState<BlocEdite[]>(() => depuis(valeur, typeInitial));
  const [insertion, setInsertion] = useState<number | null>(null);
  const emis = useRef(valeur);
  const zones = useRef(new Map<number, HTMLTextAreaElement>());
  const aFocaliser = useRef<{ cle: number; selection?: [number, number] } | null>(null);

  // Valeur changée de l'extérieur (annulation, rechargement) : on repart de la valeur reçue.
  if (valeur !== emis.current && valeur !== recomposerBlocs(blocs)) {
    emis.current = valeur;
    setBlocs(depuis(valeur, typeInitial));
  }

  useEffect(() => {
    const f = aFocaliser.current;
    if (!f) return;
    aFocaliser.current = null;
    const el = zones.current.get(f.cle);
    if (!el) return;
    el.focus();
    if (f.selection) el.setSelectionRange(f.selection[0], f.selection[1]);
  });

  function changer(liste: BlocEdite[]) {
    setBlocs(liste);
    const md = recomposerBlocs(liste);
    emis.current = md;
    if (md !== valeur) onChange(md);
  }
  const maj = (i: number, patch: Partial<Bloc>) => changer(blocs.map((b, j) => (j === i ? { ...b, ...patch } : b)));
  const contenu = (i: number, c: string, selection?: [number, number]) => {
    if (selection) aFocaliser.current = { cle: blocs[i].cle, selection };
    maj(i, { contenu: c });
  };
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
  const inserer = (position: number, type: TypeBloc) => {
    const nouveau = numeroter({ type, contenu: '' });
    aFocaliser.current = { cle: nouveau.cle };
    setInsertion(null);
    changer([...blocs.slice(0, position), nouveau, ...blocs.slice(position)]);
  };

  return (
    <div role="group" aria-label={label} className={cn('grid gap-2', className)}>
      {blocs.map((b, i) => {
        const nom = `${label}, bloc ${i + 1}`;
        const zoneRef = (el: HTMLTextAreaElement | null) => { if (el) zones.current.set(b.cle, el); else zones.current.delete(b.cle); };
        return (
          <div key={b.cle} className="grid gap-2">
            <section aria-label={nom} className={cn('grid gap-2 rounded-xl border bg-surface p-3 shadow-sm', b.type === 'code' ? 'border-line-strong' : 'border-line')}>
              <header className="flex flex-wrap items-center gap-2">
                <span className="inline-flex size-6 items-center justify-center rounded-md bg-surface-2 font-mono text-[11px] font-bold text-fg-muted" aria-hidden>{i + 1}</span>
                <Segmented
                  label={`Type du bloc ${i + 1}`}
                  valeur={b.type}
                  disabled={disabled}
                  onChange={(t) => maj(i, { type: t })}
                  options={[
                    { valeur: 'texte', libelle: <><Type className="size-3.5" aria-hidden />Texte</> },
                    { valeur: 'code', libelle: <><SquareTerminal className="size-3.5" aria-hidden />Code</> },
                  ]}
                />
                {!disabled && (
                  <span className="ml-auto flex items-center">
                    <BoutonIcone label={`Monter le bloc ${i + 1}`} taille="sm" disabled={i === 0} onClick={() => deplacer(i, -1)}><ArrowUp className="size-4" aria-hidden /></BoutonIcone>
                    <BoutonIcone label={`Descendre le bloc ${i + 1}`} taille="sm" disabled={i === blocs.length - 1} onClick={() => deplacer(i, 1)}><ArrowDown className="size-4" aria-hidden /></BoutonIcone>
                    <BoutonIcone label={`Supprimer le bloc ${i + 1}`} taille="sm" className="hover:text-danger-solid" onClick={() => supprimer(i)}><Trash2 className="size-4" aria-hidden /></BoutonIcone>
                  </span>
                )}
              </header>
              {b.type === 'texte'
                ? <BlocTexte bloc={b} nom={nom} disabled={disabled} disposition={disposition} zoneRef={zoneRef} onContenu={(c, s) => contenu(i, c, s)} />
                : <BlocCode bloc={b} nom={nom} disabled={disabled} zoneRef={zoneRef} onContenu={(c, s) => contenu(i, c, s)} onLangage={(l) => maj(i, { langage: l || undefined })} />}
            </section>
            {!disabled && i < blocs.length - 1 && (
              <div className="flex items-center justify-center gap-1.5">
                {insertion === i ? (
                  <>
                    <Button taille="sm" variante="fantome" icone={<Type className="size-3.5" aria-hidden />} onClick={() => inserer(i + 1, 'texte')} aria-label={`Insérer un bloc texte après le bloc ${i + 1}`}>Texte</Button>
                    <Button taille="sm" variante="fantome" icone={<SquareTerminal className="size-3.5" aria-hidden />} onClick={() => inserer(i + 1, 'code')} aria-label={`Insérer un bloc code après le bloc ${i + 1}`}>Code</Button>
                  </>
                ) : (
                  <button
                    type="button"
                    onClick={() => setInsertion(i)}
                    aria-label={`Insérer un bloc après le bloc ${i + 1}`}
                    title="Insérer un bloc ici"
                    className="inline-flex size-6 items-center justify-center rounded-full border border-dashed border-line-strong text-fg-subtle hover:border-accent hover:text-accent-text"
                  >
                    <Plus className="size-3.5" aria-hidden />
                  </button>
                )}
              </div>
            )}
          </div>
        );
      })}
      {!disabled && (
        <div className="flex flex-wrap gap-1.5">
          <Button taille="sm" icone={<Plus className="size-3.5" aria-hidden />} onClick={() => inserer(blocs.length, 'texte')} aria-label="Ajouter un bloc texte">Texte</Button>
          <Button taille="sm" icone={<Plus className="size-3.5" aria-hidden />} onClick={() => inserer(blocs.length, 'code')} aria-label="Ajouter un bloc code">Code</Button>
        </div>
      )}
    </div>
  );
}
