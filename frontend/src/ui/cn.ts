import { clsx, type ClassValue } from 'clsx';
import { extendTailwindMerge } from 'tailwind-merge';

/**
 * Fusion des classes Tailwind qui connaît les jetons de index.css : tailles `text-2xs`, `text-ui`, `text-md` (sinon
 * `text-ui` passerait pour une couleur et effacerait `text-fg`) et étages `z-graphe` … `z-toast`.
 */
const twMerge = extendTailwindMerge({
  extend: {
    theme: { text: ['2xs', 'ui', 'md'] },
    classGroups: { z: [{ z: ['graphe', 'panneau', 'menu', 'dialogue', 'toast'] }] },
  },
});

/** clsx + fusion des classes Tailwind en conflit (la dernière l'emporte). */
export const cn = (...v: ClassValue[]) => twMerge(clsx(v));
