import { OctagonAlert } from 'lucide-react';
import { api } from '../api';
import { formatDateHeure } from '../format';
import { useAsync } from '../hooks';
import type { ResolutionType, VersionResolutionType } from '../types';
import { Alerte } from '../ui/Alerte';
import { Dialog } from '../ui/Dialog';
import { Badge } from '../ui/StatusPill';

const ACTIONS: Record<VersionResolutionType['action'], string> = {
  creee: 'Création',
  modifiee: 'Modification',
  desactivee: 'Désactivation',
  existante: 'Version reprise',
};

/** « Modifiée par Claire le … » : auteur et date de la dernière modification d'une fiche. */
export function auteurModification(r: Pick<ResolutionType, 'modifieeLe' | 'modifieeParNom'>): string {
  return `${r.modifieeParNom ? `Modifiée par ${r.modifieeParNom}` : 'Modifiée'} le ${formatDateHeure(r.modifieeLe)}`;
}

/** Historique des versions d'une résolution type : qui a changé quoi, et quand. */
export function HistoriqueResolutionDialog({ resolution, onFermer }: { resolution: ResolutionType | null; onFermer: () => void }) {
  return (
    <Dialog open={resolution !== null} onOpenChange={(o) => { if (!o) onFermer(); }} titre={`Historique de « ${resolution?.titre ?? ''} »`} taille="lg">
      {resolution && <Versions id={resolution.id} />}
    </Dialog>
  );
}

function Versions({ id }: { id: string }) {
  const versions = useAsync(() => api.versionsResolution(id), [id]);
  if (versions.erreur) return <Alerte ton="danger" icone={OctagonAlert} role="alert">{versions.erreur}</Alerte>;
  if (versions.donnees === null) return <p className="text-sm text-fg-muted">Chargement…</p>;
  return (
    <ol className="grid gap-3">
      {versions.donnees.map((v) => (
        <li key={`${v.version}-${v.modifieeLe}`} className="rounded-xl border border-line p-3">
          <div className="flex flex-wrap items-center gap-2 text-sm">
            <Badge>v{v.version}</Badge>
            <span className="font-medium">{ACTIONS[v.action]}</span>
            <span className="text-fg-muted">{v.modifieeParNom ? `par ${v.modifieeParNom}` : 'auteur inconnu'} le {formatDateHeure(v.modifieeLe)}</span>
          </div>
          <p className="mt-2 text-sm font-medium">{v.titre}</p>
          <ol className="mt-1 list-decimal pl-5 text-sm text-fg-muted">
            {v.corrections.map((c) => <li key={c.ordre}><span className="font-mono text-xs">{c.instruction}</span> → {c.resultatAttendu}</li>)}
          </ol>
          <p className="mt-1 text-sm text-fg-muted">Vérification : {v.verificationInstruction} → {v.verificationAttendu}</p>
        </li>
      ))}
    </ol>
  );
}
