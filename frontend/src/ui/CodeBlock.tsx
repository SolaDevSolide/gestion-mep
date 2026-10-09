import { Check, Copy } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import { cn } from './cn';
import { Button } from './Button';

async function copierTexte(texte: string): Promise<boolean> {
  try {
    await navigator.clipboard.writeText(texte);
    return true;
  } catch {
    try {
      const ta = document.createElement('textarea');
      ta.value = texte;
      ta.style.position = 'fixed';
      ta.style.opacity = '0';
      document.body.appendChild(ta);
      ta.select();
      const ok = document.execCommand('copy');
      ta.remove();
      return ok;
    } catch {
      return false;
    }
  }
}

interface Props {
  code: string;
  titre?: string;
  className?: string;
  /** Hauteur maximale avant défilement vertical. */
  hauteurMax?: string;
}

/** Bloc de commandes : chasse fixe, jamais de retour à la ligne automatique, bouton Copier. */
export function CodeBlock({ code, titre, className, hauteurMax = 'max-h-72' }: Props) {
  const [copie, setCopie] = useState(false);
  const minuteur = useRef<number | undefined>(undefined);
  useEffect(() => () => window.clearTimeout(minuteur.current), []);

  async function copier() {
    if (await copierTexte(code)) {
      setCopie(true);
      window.clearTimeout(minuteur.current);
      minuteur.current = window.setTimeout(() => setCopie(false), 1800);
    }
  }

  return (
    <div className={cn('overflow-hidden rounded-lg border border-line bg-code-bg', className)}>
      <div className="flex items-center justify-between gap-2 border-b border-line px-3 py-1.5">
        <span className="text-[11px] font-medium tracking-wide text-fg-subtle uppercase">{titre ?? 'Commande'}</span>
        <Button
          taille="sm"
          variante="fantome"
          className="h-7 px-2 text-xs"
          onClick={() => void copier()}
          icone={copie ? <Check className="size-3.5 text-ok-solid" aria-hidden /> : <Copy className="size-3.5" aria-hidden />}
          aria-label={copie ? 'Copié' : `Copier${titre ? ` : ${titre}` : ''}`}
        >
          {copie ? 'Copié' : 'Copier'}
        </Button>
      </div>
      <pre className={cn('overflow-auto px-3 py-2.5 font-mono text-[13px] leading-relaxed whitespace-pre text-code-fg', hauteurMax)} tabIndex={0}>
        <code>{code}</code>
      </pre>
      <span className="sr-only" role="status">{copie ? 'Copié dans le presse-papiers' : ''}</span>
    </div>
  );
}
