import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { PieceJointe } from '../types';
import { PiecesJointes } from './PiecesJointes';
import { TooltipProvider } from './Tooltip';

const base: PieceJointe = {
  id: 'p1',
  mepId: 'm1',
  etapeId: null,
  resolutionTypeId: null,
  executionResolutionId: null,
  nomFichier: 'deploiement.log',
  typeContenu: 'text/plain',
  taille: 4 * 1024 * 1024,
  tailleStockee: 512 * 1024,
  compression: 'gzip',
  sha256: 'a'.repeat(64),
  deposeParId: null,
  deposePar: 'Alice',
  deposeLe: '2026-10-14T21:00:00Z',
};

function rendre(pieces: PieceJointe[]) {
  render(
    <TooltipProvider>
      <PiecesJointes pieces={pieces} lectureSeule onDeposer={vi.fn()} onSupprimer={vi.fn()} />
    </TooltipProvider>,
  );
}

describe('Pièces jointes : taille sur le serveur', () => {
  it('donne en info-bulle la place occupée par un fichier compressé', async () => {
    vi.stubGlobal(
      'ResizeObserver',
      class {
        observe() {}
        unobserve() {}
        disconnect() {}
      },
    );
    rendre([base]);
    await userEvent.hover(screen.getByRole('button', { name: '4 Mo' }));
    expect(
      (
        await screen.findAllByText(
          'Compressé sur le serveur : 512 Ko (−88 %). Le téléchargement rend le fichier d’origine.',
        )
      ).length,
    ).toBeGreaterThan(0);
  });

  it('affiche seulement la taille d’un fichier stocké tel quel', () => {
    rendre([{ ...base, compression: null, tailleStockee: base.taille }]);
    expect(screen.queryByRole('button', { name: '4 Mo' })).not.toBeInTheDocument();
    expect(screen.getByText(/4 Mo · Alice/)).toBeInTheDocument();
  });
});
