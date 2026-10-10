import { Trash2, X } from 'lucide-react';
import { pairesDuGroupe, type GroupeLiens, type Lien } from '../../graphe';
import type { Etape } from '../../types';
import { Button } from '../../ui/Button';
import { Card } from '../../ui/Card';
import { TitrePanneau } from './PanneauResume';

/** Panneau d'un lien ou d'une jonction sélectionné : liste des liens (× pour un seul), suppression de tous. */
export function PanneauLiens({ groupe, etapes, onSupprimer, onFermer }: {
  groupe: GroupeLiens;
  etapes: Etape[];
  onSupprimer: (liens: Lien[]) => void;
  onFermer: () => void;
}) {
  const code = (id: string) => etapes.find((e) => e.id === id)?.code ?? '?';
  const paires = pairesDuGroupe(groupe);
  const plusieurs = paires.length > 1;
  return (
    <Card className="grid w-80 gap-3 p-4 shadow-lg">
      <TitrePanneau>{plusieurs ? 'Jonction sélectionnée' : 'Lien sélectionné'}</TitrePanneau>
      <p className="text-sm">
        {groupe.cibles.map(code).join(', ')} {groupe.cibles.length > 1 ? 'attendent' : 'attend'} : {groupe.sources.map(code).join(', ')}.
      </p>
      <ul className="grid gap-1 text-sm">
        {paires.map((l) => (
          <li key={`${l.de}>${l.vers}`} className="flex items-center justify-between gap-2 rounded-md bg-surface-2 px-2 py-1">
            <span><b className="font-mono text-xs font-medium">{code(l.de)}</b> → <b className="font-mono text-xs font-medium">{code(l.vers)}</b></span>
            <button type="button" className="rounded px-1 text-fg-subtle hover:text-danger-solid" aria-label={`Supprimer le lien ${code(l.de)} vers ${code(l.vers)}`} onClick={() => onSupprimer([l])}>
              <X className="size-4" aria-hidden />
            </button>
          </li>
        ))}
      </ul>
      <div className="flex flex-wrap gap-2">
        <Button variante="danger-doux" taille="sm" icone={<Trash2 className="size-4" aria-hidden />} onClick={() => onSupprimer(paires)}>
          {plusieurs ? `Supprimer ces ${paires.length} liens` : 'Supprimer le lien'}
        </Button>
        <Button variante="fantome" taille="sm" onClick={onFermer}>Fermer</Button>
      </div>
    </Card>
  );
}
