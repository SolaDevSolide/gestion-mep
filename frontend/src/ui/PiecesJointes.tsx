import { CloudUpload, Download, File, FileImage, FileText, Loader2, OctagonAlert, Paperclip, Trash2 } from 'lucide-react';
import { useContext, useRef, useState, type DragEvent } from 'react';
import { urlPieceJointe } from '../api';
import { AuthContext } from '../auth';
import { formatDateHeure, formatTaille } from '../format';
import type { PieceJointe } from '../types';
import { Alerte } from './Alerte';
import { BoutonIcone, Button } from './Button';
import { cn } from './cn';
import { ConfirmDialog } from './Dialog';
import { Tooltip } from './Tooltip';

interface Props {
  pieces: PieceJointe[];
  /** Dépose un fichier ; retourne vrai si l'envoi a abouti. `signaler` affiche une erreur dans la zone (ex. 413). */
  onDeposer: (fichier: File, signaler: (message: string) => void) => Promise<boolean>;
  /** Supprime une pièce ; retourne vrai si elle a été supprimée. */
  onSupprimer: (piece: PieceJointe, signaler: (message: string) => void) => Promise<boolean>;
  /** Dépôt et suppression interdits (MEP clôturée, par ex.). */
  lectureSeule?: boolean;
  /** Le pilote (ou un suppléant) peut supprimer toutes les pièces ; sinon, seul le déposant. */
  pilote?: boolean;
  /** Remplace entièrement la règle de suppression. */
  peutSupprimer?: (piece: PieceJointe) => boolean;
  titre?: string;
  className?: string;
  compact?: boolean;
}

function IconeFichier({ type }: { type: string }) {
  const c = 'size-4.5';
  if (type.startsWith('image/')) return <FileImage className={c} aria-hidden />;
  if (type.startsWith('text/') || type === 'application/pdf') return <FileText className={c} aria-hidden />;
  return <File className={c} aria-hidden />;
}

/** Zone de dépôt (glisser-déposer + bouton) et liste des pièces jointes avec téléchargement et suppression. */
export function PiecesJointes({ pieces, onDeposer, onSupprimer, lectureSeule, pilote, peutSupprimer, titre = 'Pièces jointes', className, compact }: Props) {
  const moi = useContext(AuthContext)?.utilisateur?.id ?? null;
  const entree = useRef<HTMLInputElement>(null);
  const [survol, setSurvol] = useState(false);
  const [envoi, setEnvoi] = useState(0);
  const [erreur, setErreur] = useState('');
  const [aSupprimer, setASupprimer] = useState<PieceJointe | null>(null);
  const peut = peutSupprimer ?? ((p: PieceJointe) => !!pilote || (moi !== null && p.deposeParId === moi));

  async function deposer(fichiers: FileList | File[]) {
    setErreur('');
    for (const f of Array.from(fichiers)) {
      setEnvoi((n) => n + 1);
      try {
        await onDeposer(f, (m) => setErreur(`${f.name} : ${m}`));
      } finally {
        setEnvoi((n) => n - 1);
      }
    }
  }
  function lacher(e: DragEvent) {
    e.preventDefault();
    setSurvol(false);
    if (!lectureSeule && e.dataTransfer.files.length > 0) void deposer(e.dataTransfer.files);
  }

  return (
    <section className={className} aria-label={titre}>
      <h3 className="mb-2 flex items-center gap-1.5 text-xs font-semibold tracking-wide text-fg-subtle uppercase">
        <Paperclip className="size-3.5" aria-hidden />{titre}
        {pieces.length > 0 && <span className="rounded-full bg-surface-2 px-1.5 text-[11px] font-bold tnum">{pieces.length}</span>}
      </h3>

      {erreur && <Alerte ton="danger" icone={OctagonAlert} role="alert" className="mb-2.5">{erreur}</Alerte>}

      {!lectureSeule && (
        <div
          onDragOver={(e) => { e.preventDefault(); setSurvol(true); }}
          onDragLeave={() => setSurvol(false)}
          onDrop={lacher}
          data-testid="zone-depot"
          className={cn(
            'flex items-center gap-3 rounded-xl border-2 border-dashed px-4 text-sm transition-colors duration-150',
            compact ? 'py-1.5 pr-1.5' : 'py-5',
            survol ? 'border-accent bg-accent-soft text-accent-text' : 'border-line-strong bg-surface-2/40 text-fg-muted',
          )}
        >
          {envoi > 0 ? <Loader2 className="size-5 shrink-0 animate-spin" aria-hidden /> : <CloudUpload className={cn('size-5 shrink-0', survol && 'text-accent')} aria-hidden />}
          <p className="min-w-0 flex-1">{envoi > 0 ? 'Envoi en cours…' : survol ? 'Relâchez pour déposer' : compact ? 'Glisser un fichier ou' : 'Glissez un fichier ici ou'}</p>
          <input ref={entree} type="file" multiple className="sr-only" aria-label={`Choisir un fichier : ${titre}`} tabIndex={-1}
            onChange={(e) => { if (e.target.files) void deposer(e.target.files); e.target.value = ''; }} />
          <Button taille="sm" icone={<Paperclip className="size-4" aria-hidden />} disabled={envoi > 0} onClick={() => entree.current?.click()}>Parcourir…</Button>
        </div>
      )}

      {pieces.length > 0 ? (
        <ul className={cn('divide-y divide-line overflow-hidden rounded-lg border border-line bg-surface', !lectureSeule && 'mt-2.5')}>
          {pieces.map((p) => (
            <li key={p.id} className="flex items-center gap-3 px-3 py-2">
              <span className="text-fg-subtle"><IconeFichier type={p.typeContenu} /></span>
              <div className="min-w-0 flex-1">
                <p className="truncate text-sm font-medium" title={p.nomFichier}>{p.nomFichier}</p>
                <p className="truncate text-xs text-fg-subtle tnum">{formatTaille(p.taille)} · {p.deposePar ?? 'Inconnu'} · {formatDateHeure(p.deposeLe)}</p>
              </div>
              <Tooltip content="Télécharger">
                <a
                  href={urlPieceJointe(p.id)}
                  download={p.nomFichier}
                  aria-label={`Télécharger ${p.nomFichier}`}
                  className="inline-flex size-8 items-center justify-center rounded-md text-fg-muted transition-colors duration-150 hover:bg-surface-2 hover:text-fg"
                >
                  <Download className="size-4" aria-hidden />
                </a>
              </Tooltip>
              {!lectureSeule && peut(p) && (
                <Tooltip content="Supprimer">
                  <BoutonIcone label={`Supprimer ${p.nomFichier}`} taille="sm" onClick={() => setASupprimer(p)}><Trash2 className="size-4" aria-hidden /></BoutonIcone>
                </Tooltip>
              )}
            </li>
          ))}
        </ul>
      ) : lectureSeule ? <p className="text-sm text-fg-subtle">Aucune pièce jointe.</p> : null}

      <ConfirmDialog
        open={aSupprimer !== null}
        onOpenChange={(o) => { if (!o) setASupprimer(null); }}
        titre="Supprimer cette pièce jointe ?"
        description={aSupprimer ? `« ${aSupprimer.nomFichier} » sera définitivement supprimée.` : undefined}
        libelleConfirmer="Supprimer"
        variante="danger"
        onConfirmer={async () => {
          if (!aSupprimer) return false;
          setErreur('');
          await onSupprimer(aSupprimer, setErreur);
          return true;
        }}
      />
    </section>
  );
}
