import { api } from '../../api';
import { useMep } from '../../mepContext';
import { PiecesJointes } from '../../ui/PiecesJointes';
import { SectionCarte } from './Formulaires';
import { estTerminee } from '../../domaine';

/** Pièces jointes de la fiche MEP (hors pièces d'étapes et de résolutions). */
export function PiecesSection() {
  const { mep, run } = useMep();
  return (
    <SectionCarte numero={9} titre="Pièces jointes" description="Procédure, schémas, configurations.">
      <PiecesJointes
        titre="Documents de la MEP"
        lectureSeule={estTerminee(mep)}
        pilote={mep.droits.estPilote}
        pieces={mep.piecesJointes.filter((p) => p.etapeId === null && p.executionResolutionId === null)}
        onDeposer={(f, signaler) => run(() => api.deposerPieceMep(mep.id, f), undefined, signaler)}
        onSupprimer={(p, signaler) => run(() => api.supprimerPiece(mep.id, p.id), undefined, signaler)}
      />
    </SectionCarte>
  );
}
