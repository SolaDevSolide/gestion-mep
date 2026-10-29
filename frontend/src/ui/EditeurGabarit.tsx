import {
  forwardRef,
  useId,
  useImperativeHandle,
  useLayoutEffect,
  useRef,
  useState,
  type KeyboardEvent,
  type ReactNode,
} from 'react';
import { normaliser, termes } from '../recherche';
import {
  decouper,
  index as indexDe,
  type CatalogueGabarit,
  type FormatGabarit,
  type TypeMorceau,
} from '../textes/gabarits';
import type { VariableTexte } from '../types';
import { cn } from './cn';

/** Mêmes métriques pour la zone de saisie et le calque coloré qu'elle recouvre : les caractères tombent l'un sur l'autre. */
const METRIQUES =
  'px-3 py-2 font-mono text-ui leading-relaxed whitespace-pre-wrap break-words [overflow-wrap:anywhere]';

const COULEUR: Record<TypeMorceau, string> = {
  texte: '',
  variable: 'rounded-sm bg-accent-soft text-accent-text',
  inconnue:
    'rounded-sm bg-danger-bg text-danger-fg underline decoration-wavy decoration-danger-solid underline-offset-2',
  cle: 'text-fg font-semibold',
  chaine: 'text-ok-text',
  nombre: 'text-run-solid',
  mot: 'text-tertiaire-text',
  ponctuation: 'text-fg-subtle',
};

const MAX_PROPOSITIONS = 8;

interface Props {
  id?: string;
  value: string;
  onChange: (v: string) => void;
  /** Coloration : JSON (clés, textes, nombres), texte simple, adresse ; les variables ressortent toujours. */
  format: FormatGabarit | 'Adresse';
  catalogue: CatalogueGabarit;
  /** Variables proposées par l'autocomplétion (à la frappe de « {{ »). */
  variables: VariableTexte[];
  /** Erreur à montrer dans le texte (ligne et colonne à partir de 1). */
  erreur?: { ligne: number; colonne: number } | null;
  rows?: number;
  /** Une seule ligne logique (adresse, en-tête) : Entrée n'ajoute pas de retour à la ligne. */
  uneLigne?: boolean;
  disabled?: boolean;
  placeholder?: string;
  className?: string;
  onFocus?: () => void;
  'aria-describedby'?: string;
  'aria-invalid'?: boolean;
  'aria-label'?: string;
}

/** Variables qui répondent à une recherche multi-termes (libellé, clé, description), dans l'ordre du catalogue. */
export function variablesProposees(variables: VariableTexte[], recherche: string): VariableTexte[] {
  const ts = termes(recherche.replace(/_/g, ' '));
  return variables.filter((v) => {
    const tout = normaliser(`${v.groupe} ${v.libelle} ${v.cle} ${v.cle.replace(/_/g, ' ')} ${v.description}`);
    return ts.every((t) => tout.includes(t));
  });
}

/** Début d'une variable en cours de frappe (« {{pil » juste avant le curseur), ou null. */
export function jetonEnCours(texte: string, curseur: number): { debut: number; recherche: string } | null {
  const avant = texte.slice(0, curseur);
  const m = /\{\{\s*([a-z0-9_]*)$/i.exec(avant);
  return m ? { debut: m.index, recherche: m[1] } : null;
}

/**
 * Zone de saisie d'un gabarit de connexion : une zone de texte ordinaire (pas de dépendance), recouvrant un calque qui
 * colore les variables (inconnues soulignées) et le JSON, et montre l'endroit d'une erreur. La frappe de « {{ » ouvre
 * l'autocomplétion des variables (↑ ↓ pour choisir, Entrée ou Tab pour insérer, Échap pour fermer). La zone grandit avec
 * son contenu : pas de défilement interne qui décalerait le calque.
 */
export const EditeurGabarit = forwardRef<HTMLTextAreaElement, Props>(function EditeurGabarit(
  {
    id,
    value,
    onChange,
    format,
    catalogue,
    variables,
    erreur,
    rows = 3,
    uneLigne,
    disabled,
    placeholder,
    className,
    onFocus,
    ...aria
  },
  ref,
) {
  const zone = useRef<HTMLTextAreaElement>(null);
  const marque = useRef<HTMLSpanElement>(null);
  useImperativeHandle<HTMLTextAreaElement | null, HTMLTextAreaElement | null>(ref, () => zone.current, []);
  const idListe = useId();
  const [curseur, setCurseur] = useState<number | null>(null);
  const [ferme, setFerme] = useState(false);
  const [choix, setChoix] = useState(0);
  const [position, setPosition] = useState<{ haut: number; gauche: number } | null>(null);

  const enCours = curseur !== null && !ferme && !disabled ? jetonEnCours(value, curseur) : null;
  const propositions = enCours ? variablesProposees(variables, enCours.recherche).slice(0, MAX_PROPOSITIONS) : [];
  const ouvert = enCours !== null && propositions.length > 0;
  const actif = Math.min(choix, Math.max(0, propositions.length - 1));

  // Hauteur selon le contenu (le calque n'a pas de défilement propre).
  useLayoutEffect(() => {
    const el = zone.current;
    if (!el) return;
    el.style.height = 'auto';
    el.style.height = `${el.scrollHeight + 2}px`;
  }, [value]);

  // Liste placée sous le curseur : la marque posée dans le calque à la position du curseur donne ses coordonnées.
  useLayoutEffect(() => {
    if (!ouvert || !marque.current) {
      setPosition(null);
      return;
    }
    const m = marque.current;
    const largeur = zone.current?.clientWidth ?? 0;
    setPosition({ haut: m.offsetTop + m.offsetHeight + 4, gauche: Math.max(0, Math.min(m.offsetLeft, largeur - 288)) });
  }, [ouvert, curseur, value]);

  const indexErreur = erreur ? indexDe(value, erreur.ligne, erreur.colonne) : -1;

  function inserer(cle: string) {
    if (!enCours || curseur === null) return;
    const apres = value.slice(curseur);
    const fermeture = /^\s*\}\}/.exec(apres);
    const fin = curseur + (fermeture ? fermeture[0].length : 0);
    const jeton = `{{${cle}}}`;
    const nouveau = value.slice(0, enCours.debut) + jeton + value.slice(fin);
    const pos = enCours.debut + jeton.length;
    onChange(nouveau);
    setCurseur(pos);
    requestAnimationFrame(() => {
      zone.current?.focus();
      zone.current?.setSelectionRange(pos, pos);
    });
  }

  function clavier(e: KeyboardEvent<HTMLTextAreaElement>) {
    if (ouvert) {
      if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
        e.preventDefault();
        const n = propositions.length;
        setChoix((actif + (e.key === 'ArrowDown' ? 1 : n - 1)) % n);
        return;
      }
      if (e.key === 'Enter' || e.key === 'Tab') {
        e.preventDefault();
        inserer(propositions[actif].cle);
        return;
      }
      if (e.key === 'Escape') {
        e.preventDefault();
        e.stopPropagation();
        setFerme(true);
        return;
      }
    }
    if (uneLigne && e.key === 'Enter') e.preventDefault();
  }

  // Calque : morceaux colorés, l'erreur et la marque du curseur insérées à leur place.
  const morceaux = decouper(value, format, catalogue);
  const rendu: ReactNode[] = [];
  let pos = 0;
  const marqueA = ouvert ? curseur : null;
  morceaux.forEach((m, i) => {
    const debut = pos;
    const fin = pos + m.texte.length;
    pos = fin;
    const coupures = [indexErreur, marqueA].filter((x): x is number => x !== null && x >= debut && x < fin);
    if (coupures.length === 0) {
      rendu.push(
        <span key={i} className={COULEUR[m.type]}>
          {m.texte}
        </span>,
      );
      return;
    }
    let p = debut;
    for (const c of [...new Set(coupures)].sort((a, b) => a - b)) {
      if (c > p)
        rendu.push(
          <span key={`${i}-${p}`} className={COULEUR[m.type]}>
            {value.slice(p, c)}
          </span>,
        );
      if (c === marqueA) rendu.push(<span key={`m-${c}`} ref={marque} />);
      if (c === indexErreur) {
        rendu.push(
          <span
            key={`e-${c}`}
            className="rounded-sm bg-danger-solid/25 text-danger-fg outline outline-1 outline-danger-solid"
          >
            {value[c] === '\n' ? ' \n' : value[c]}
          </span>,
        );
        p = c + 1;
      } else p = c;
    }
    if (p < fin)
      rendu.push(
        <span key={`${i}-${p}-f`} className={COULEUR[m.type]}>
          {value.slice(p, fin)}
        </span>,
      );
  });
  if (marqueA === value.length) rendu.push(<span key="m-fin" ref={marque} />);
  if (indexErreur >= value.length && erreur)
    rendu.push(
      <span key="e-fin" className="rounded-sm bg-danger-solid/25 outline outline-1 outline-danger-solid">
        {' '}
      </span>,
    );

  return (
    <div className={cn('relative', className)}>
      <div
        className={cn(
          'relative rounded-lg border border-line-strong bg-surface shadow-sm transition-[border-color,box-shadow] duration-150',
          'focus-within:border-accent focus-within:ring-2 focus-within:ring-accent/30',
          aria['aria-invalid'] && 'border-danger-bd',
          disabled && 'bg-surface-2 opacity-80',
        )}
      >
        <pre aria-hidden className={cn(METRIQUES, 'pointer-events-none absolute inset-0 m-0 overflow-hidden text-fg')}>
          {rendu}
          {'\n'}
        </pre>
        <textarea
          ref={zone}
          id={id}
          rows={rows}
          value={value}
          disabled={disabled}
          placeholder={placeholder}
          spellCheck={false}
          autoCapitalize="off"
          autoComplete="off"
          role="combobox"
          aria-autocomplete="list"
          aria-expanded={ouvert}
          aria-controls={ouvert ? idListe : undefined}
          aria-activedescendant={ouvert ? `${idListe}-${actif}` : undefined}
          {...aria}
          onFocus={onFocus}
          onBlur={() => setFerme(true)}
          onChange={(e) => {
            const v = uneLigne ? e.target.value.replace(/\r?\n/g, ' ') : e.target.value;
            onChange(v);
            setCurseur(e.target.selectionStart);
            setFerme(false);
            setChoix(0);
          }}
          onSelect={(e) => setCurseur(e.currentTarget.selectionStart)}
          onKeyDown={clavier}
          className={cn(
            METRIQUES,
            'relative block w-full resize-none overflow-hidden bg-transparent text-transparent caret-fg outline-none',
            'placeholder:text-fg-subtle selection:bg-accent/25 selection:text-transparent disabled:cursor-not-allowed',
          )}
        />
      </div>
      {ouvert && (
        <ul
          id={idListe}
          role="listbox"
          aria-label="Variables"
          style={position ? { top: position.haut, left: position.gauche } : undefined}
          className={cn(
            'absolute z-menu max-h-72 w-72 max-w-full overflow-y-auto rounded-xl border border-line bg-surface p-1 shadow-lg',
            !position && 'top-full left-0 mt-1',
          )}
        >
          {propositions.map((v, i) => (
            <li
              key={v.cle}
              id={`${idListe}-${i}`}
              role="option"
              aria-selected={i === actif}
              onMouseDown={(e) => {
                e.preventDefault();
                inserer(v.cle);
              }}
              onMouseEnter={() => setChoix(i)}
              className={cn('cursor-pointer rounded-lg px-2.5 py-1.5 text-sm', i === actif && 'bg-surface-2')}
            >
              <span className="flex items-baseline justify-between gap-2">
                <span className="truncate font-medium text-fg">{v.libelle}</span>
                <span className="shrink-0 text-2xs text-fg-subtle">{v.groupe}</span>
              </span>
              <span className="block truncate font-mono text-xs text-accent-text">
                {`{{${v.cle}}}`}
                {v.liste && <span className="ml-1.5 font-sans text-fg-subtle">liste</span>}
              </span>
            </li>
          ))}
          <li aria-hidden className="border-t border-line px-2.5 pt-1.5 pb-1 text-2xs text-fg-subtle">
            ↑ ↓ choisir · Entrée insérer · Échap fermer
          </li>
        </ul>
      )}
    </div>
  );
});
