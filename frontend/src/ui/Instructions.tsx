import DOMPurify from 'dompurify';
import { marked } from 'marked';
import { Check, Copy } from 'lucide-react';
import { useEffect, useMemo, useRef, useState } from 'react';
import { decouperBlocs } from '../blocs';
import { Button } from './Button';
import { cn } from './cn';

const echapper = (s: string) =>
  s.replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]!);

/**
 * Liste blanche du markdown : uniquement ce que produit marked (GFM). Ni `style`, ni `class`, ni formulaire, ni image externe :
 * un auteur d'étape ne peut pas recouvrir l'écran, imiter une page de connexion ni pister les lecteurs.
 */
const CONFIG_ASSAINISSEMENT = {
  ALLOWED_TAGS: [
    'p',
    'br',
    'hr',
    'strong',
    'em',
    'del',
    's',
    'code',
    'pre',
    'blockquote',
    'ul',
    'ol',
    'li',
    'input',
    'h1',
    'h2',
    'h3',
    'h4',
    'h5',
    'h6',
    'a',
    'img',
    'table',
    'thead',
    'tbody',
    'tr',
    'th',
    'td',
  ],
  ALLOWED_ATTR: ['href', 'title', 'src', 'alt', 'type', 'checked', 'disabled', 'start', 'align'],
  ALLOW_DATA_ATTR: false,
  ALLOW_ARIA_ATTR: false,
  // http(s), mailto, chemin de l'application (« / » mais pas « // ») ou ancre.
  ALLOWED_URI_REGEXP: /^(?:https?:|mailto:|\/(?!\/)|#)/i,
  // Attributs à valeur fixe : non soumis au filtre d'URL ci-dessus.
  ADD_URI_SAFE_ATTR: ['type', 'align', 'start'],
};

const purificateur = DOMPurify();
purificateur.addHook('afterSanitizeAttributes', (noeud) => {
  if (noeud.tagName === 'A') noeud.setAttribute('rel', 'noopener noreferrer');
  // Seules les cases à cocher des listes de tâches sont gardées, en lecture seule.
  if (noeud.tagName === 'INPUT') {
    if (noeud.getAttribute('type') !== 'checkbox') noeud.remove();
    else noeud.setAttribute('disabled', '');
  }
  // Images : seulement celles servies par l'application (pas de requête vers un tiers à la lecture).
  if (noeud.tagName === 'IMG' && !/^\/(?!\/)/.test(noeud.getAttribute('src') ?? '')) noeud.remove();
});

/** Rendu markdown sûr (marked + DOMPurify en liste blanche). Repli : texte échappé avec retours à la ligne. */
export function rendreMarkdown(source: string): string {
  try {
    const html = marked.parse(source ?? '', { breaks: true, gfm: true, async: false }) as string;
    return purificateur.sanitize(html, CONFIG_ASSAINISSEMENT);
  } catch {
    return echapper(source ?? '').replace(/\n/g, '<br>');
  }
}

/** Styles du markdown rendu (listes, code en ligne, cases à cocher…), avec les jetons de l'application. */
export const CLASSES_MARKDOWN = cn(
  'min-w-0 text-sm leading-relaxed [overflow-wrap:anywhere]',
  '[&>:first-child]:mt-0 [&>:last-child]:mb-0 [&_p]:mb-1.5 [&_ul]:mb-1.5 [&_ol]:mb-1.5',
  '[&_ul]:list-disc [&_ol]:list-decimal [&_ul]:pl-5 [&_ol]:pl-5 [&_li]:my-0.5',
  '[&_li:has(>input[type=checkbox])]:list-none [&_li>input[type=checkbox]]:mr-1.5 [&_li>input[type=checkbox]]:-ml-5 [&_li>input[type=checkbox]]:align-middle',
  '[&_code]:rounded [&_code]:bg-code-bg [&_code]:px-1 [&_code]:py-px [&_code]:font-mono [&_code]:text-[0.85em] [&_code]:text-code-fg',
  '[&_pre]:mb-1.5 [&_pre]:overflow-x-auto [&_pre]:rounded-md [&_pre]:border [&_pre]:border-line [&_pre]:bg-code-bg [&_pre]:p-2 [&_pre_code]:bg-transparent [&_pre_code]:p-0',
  '[&_h1]:mt-2 [&_h1]:mb-1 [&_h1]:text-[15px] [&_h1]:font-semibold [&_h2]:mt-2 [&_h2]:mb-1 [&_h2]:text-[15px] [&_h2]:font-semibold [&_h3]:mt-2 [&_h3]:mb-1 [&_h3]:font-semibold',
  '[&_blockquote]:mb-1.5 [&_blockquote]:border-l-[3px] [&_blockquote]:border-line-strong [&_blockquote]:pl-2.5 [&_blockquote]:text-fg-muted',
  '[&_a]:font-medium [&_a]:text-accent-text [&_a]:underline [&_strong]:font-semibold [&_hr]:my-2 [&_hr]:border-line',
  '[&_table]:mb-1.5 [&_table]:border-collapse [&_td]:border [&_td]:border-line [&_td]:px-1.5 [&_th]:border [&_th]:border-line [&_th]:px-1.5',
);

/** Texte markdown rendu. */
export function Markdown({ source, className, vide }: { source: string; className?: string; vide?: React.ReactNode }) {
  const html = useMemo(() => rendreMarkdown(source), [source]);
  if (!source.trim()) return vide ? <p className="text-sm text-fg-subtle">{vide}</p> : null;
  // eslint-disable-next-line react/no-danger -- HTML assaini par DOMPurify
  return <div className={cn(CLASSES_MARKDOWN, className)} dangerouslySetInnerHTML={{ __html: html }} />;
}

/** Bloc de code d'instructions : texte exact, bouton Copier ; si le presse-papiers est refusé, le texte est sélectionné. */
export function BlocCode({ code, libelle = 'Copier la commande' }: { code: string; libelle?: string }) {
  const [etat, setEtat] = useState<'' | 'copie' | 'refus'>('');
  const pre = useRef<HTMLPreElement>(null);
  const minuteur = useRef<number | undefined>(undefined);
  useEffect(() => () => window.clearTimeout(minuteur.current), []);

  function selectionner() {
    const el = pre.current;
    const sel = window.getSelection?.();
    if (!el || !sel) return;
    sel.removeAllRanges();
    const r = document.createRange();
    r.selectNodeContents(el);
    sel.addRange(r);
  }

  async function copier() {
    let ok = false;
    try {
      if (!navigator.clipboard?.writeText) throw new Error('indisponible');
      await navigator.clipboard.writeText(code);
      ok = true;
    } catch {
      selectionner();
    }
    setEtat(ok ? 'copie' : 'refus');
    window.clearTimeout(minuteur.current);
    minuteur.current = window.setTimeout(() => setEtat(''), ok ? 1800 : 6000);
  }

  return (
    <div className="relative min-w-0">
      <pre
        ref={pre}
        tabIndex={0}
        className="max-h-80 overflow-auto rounded-lg border border-line bg-code-bg py-2.5 pr-24 pl-3 font-mono text-[13px] leading-relaxed break-words whitespace-pre-wrap text-code-fg"
      >
        <code>{code}</code>
      </pre>
      <Button
        taille="sm"
        className="absolute top-1.5 right-1.5 h-7 px-2 text-xs"
        onClick={() => void copier()}
        icone={
          etat === 'copie' ? (
            <Check className="size-3.5 text-ok-solid" aria-hidden />
          ) : (
            <Copy className="size-3.5" aria-hidden />
          )
        }
        aria-label={etat === 'copie' ? 'Copié' : libelle}
        title={libelle}
      >
        {etat === 'copie' ? 'Copié' : 'Copier'}
      </Button>
      {etat === 'refus' && (
        <p className="mt-1 text-xs font-medium text-warn-fg">
          Copie refusée par le navigateur : le texte est sélectionné, faites Ctrl+C.
        </p>
      )}
      <span className="sr-only" role="status">
        {etat === 'copie' ? 'Copié dans le presse-papiers' : ''}
      </span>
    </div>
  );
}

/** Instructions en lecture (exécution) : texte markdown rendu, blocs de code copiables. */
export function Instructions({
  source,
  vide = 'Aucune instruction.',
  className,
}: {
  source: string;
  vide?: string | null;
  className?: string;
}) {
  const blocs = useMemo(() => decouperBlocs(source), [source]);
  if (blocs.length === 0) return vide ? <p className={cn('text-sm text-fg-subtle', className)}>{vide}</p> : null;
  return (
    <div className={cn('grid min-w-0 gap-2', className)}>
      {blocs.map((b, i) =>
        b.type === 'code' ? (
          <BlocCode
            key={i}
            code={b.contenu}
            libelle={`Copier la commande ${blocs.slice(0, i + 1).filter((x) => x.type === 'code').length}`}
          />
        ) : (
          <Markdown key={i} source={b.contenu} />
        ),
      )}
    </div>
  );
}
