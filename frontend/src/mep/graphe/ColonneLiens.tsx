import { Trash2, X } from 'lucide-react';
import { api } from '../../api';
import type { Lien } from '../../graphe';
import { useMep } from '../../mepContext';
import type { Etape, ResolutionType, SystemeCatalogue } from '../../types';
import { Button } from '../../ui/Button';
import { Select } from '../../ui/Input';
import { PiecesJointes } from '../../ui/PiecesJointes';
import { Bloc } from './EspaceEtape';
import { SelecteurResolutions } from './SelecteurResolutions';

/** Pièces jointes d'une étape (dépôt possible tant que la MEP n'est pas clôturée). */
export function PiecesEtape({ etapeId, className }: { etapeId: string; className?: string }) {
  const { mep, run } = useMep();
  return (
    <PiecesJointes
      className={className}
      compact
      lectureSeule={mep.phase === 'Cloturee'}
      pilote={mep.droits.estPilote}
      pieces={mep.piecesJointes.filter((p) => p.etapeId === etapeId)}
      onDeposer={(f, signaler) => run(() => api.deposerPieceEtape(mep.id, etapeId, f), undefined, signaler)}
      onSupprimer={(p, signaler) => run(() => api.supprimerPiece(mep.id, p.id), undefined, signaler)}
    />
  );
}

/** Ligne compacte d'une liste de liens (préalables / débloque). */
export function ItemLien({ etape, onRetirer, libelleRetrait, onOuvrir }: { etape: Etape; onRetirer?: () => void; libelleRetrait: string; onOuvrir: () => void }) {
  return (
    <li className="inline-flex max-w-full items-center gap-0.5 rounded-full border border-line bg-surface-2/60 py-0.5 pr-0.5 pl-2 text-[13px]">
      <button type="button" className="min-w-0 truncate text-left hover:text-accent-text" onClick={onOuvrir} title={`${etape.code} · ${etape.action || 'sans libellé'}`} aria-label={`${etape.code} ${etape.action || 'sans libellé'}`}>
        <b className="font-mono text-xs font-semibold">{etape.code}</b>
      </button>
      {onRetirer && (
        <button type="button" className="shrink-0 rounded p-0.5 text-fg-subtle hover:bg-danger-bg hover:text-danger-solid" aria-label={libelleRetrait} onClick={onRetirer}>
          <X className="size-3.5" aria-hidden />
        </button>
      )}
    </li>
  );
}

interface Props {
  etape: Etape;
  /** Ligne affichée de l'étape (0 : première ligne). */
  ligne: number;
  prealables: Etape[];
  suivants: Etape[];
  /** Étapes des lignes au-dessus pouvant devenir préalables. */
  ajoutsAvant: Etape[];
  /** Étapes des lignes en dessous pouvant attendre celle-ci. */
  ajoutsApres: Etape[];
  resolutions: ResolutionType[];
  catalogue: SystemeCatalogue[];
  /** Résolutions liées dans le brouillon. */
  resolutionIds: string[];
  onResolutions: (ids: string[]) => void;
  /** Systèmes du catalogue de l'étape (suggestions de résolutions). */
  systemesEtape: string[];
  onCreerResolution: (titre: string) => void;
  onLier: (de: string, vers: string) => void;
  onDelier: (liens: Lien[]) => void;
  onSelection: (id: string | null) => void;
  /** Confirmation de suppression affichée. */
  suppr: boolean;
  onSuppr: (afficher: boolean) => void;
  onSupprimer: () => void;
}

/** Colonne droite de l'édition d'une étape : liens (attend / débloque), résolutions, pièces jointes et suppression. */
export function ColonneLiens(props: Props) {
  const { etape, ligne: r, prealables, suivants, ajoutsAvant, ajoutsApres, resolutions, catalogue, resolutionIds, onResolutions, systemesEtape, onCreerResolution, onLier, onDelier, onSelection, suppr, onSuppr, onSupprimer } = props;
  return (
    <>
      <Bloc titre={`Attend (${prealables.length})`}>
        {prealables.length > 0 && (
          <ul className="flex flex-wrap gap-1">
            {prealables.map((p) => (
              <ItemLien key={p.id} etape={p} onOuvrir={() => onSelection(p.id)} libelleRetrait={`Retirer le préalable ${p.code}`} onRetirer={() => onDelier([{ de: p.id, vers: etape.id }])} />
            ))}
          </ul>
        )}
        {r > 0 && prealables.length === 0 && <p className="text-xs text-fg-subtle">Aucun : démarrable dès le Go.</p>}
        {ajoutsAvant.length > 0 && (
          <Select aria-label="Ajouter un préalable" className="h-8 text-[13px]" value="" onChange={(e) => { if (e.target.value) onLier(e.target.value, etape.id); }}>
            <option value="">+ Préalable…</option>
            {ajoutsAvant.map((x) => <option key={x.id} value={x.id}>{x.code} · {x.action || 'sans libellé'}</option>)}
          </Select>
        )}
      </Bloc>
      <Bloc titre={`Débloque (${suivants.length})`}>
        {suivants.length > 0 && (
          <ul className="flex flex-wrap gap-1">
            {suivants.map((s) => (
              <ItemLien key={s.id} etape={s} onOuvrir={() => onSelection(s.id)} libelleRetrait={`Retirer le lien vers ${s.code}`} onRetirer={() => onDelier([{ de: etape.id, vers: s.id }])} />
            ))}
          </ul>
        )}
        {ajoutsApres.length > 0 && (
          <Select aria-label="Ajouter une étape qui attend celle-ci" className="h-8 text-[13px]" value="" onChange={(e) => { if (e.target.value) onLier(etape.id, e.target.value); }}>
            <option value="">+ Étape qui l’attend…</option>
            {ajoutsApres.map((x) => <option key={x.id} value={x.id}>{x.code} · {x.action || 'sans libellé'}</option>)}
          </Select>
        )}
      </Bloc>
      <Bloc titre={`Résolutions en cas d’échec (${resolutionIds.length})`}>
        <SelecteurResolutions
          resolutions={resolutions}
          catalogue={catalogue}
          liees={resolutionIds}
          onChange={onResolutions}
          systemesEtape={systemesEtape}
          onCreer={onCreerResolution}
        />
      </Bloc>
      <PiecesEtape etapeId={etape.id} />
      <div className="border-t border-line pt-3">
        {suppr ? (
          <div role="alertdialog" aria-label={`Supprimer ${etape.code}`} className="grid gap-2 rounded-lg border border-danger-bd bg-danger-bg/60 p-2.5">
            <p className="text-sm font-medium text-danger-fg">Supprimer {etape.code} définitivement ?</p>
            <div className="flex flex-wrap gap-2">
              <Button variante="danger" taille="sm" icone={<Trash2 className="size-4" aria-hidden />} onClick={onSupprimer}>Supprimer définitivement</Button>
              <Button variante="fantome" taille="sm" onClick={() => onSuppr(false)} autoFocus>Annuler</Button>
            </div>
          </div>
        ) : (
          <Button variante="danger-doux" taille="sm" icone={<Trash2 className="size-4" aria-hidden />} onClick={() => onSuppr(true)}>Supprimer l’étape</Button>
        )}
      </div>
    </>
  );
}
