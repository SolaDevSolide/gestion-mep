import type { Ton } from '../format';

/** Classes Tailwind statiques par ton (nécessaires pour la génération des utilitaires). */
export const TON: Record<
  Ton,
  {
    pastille: string;
    texte: string;
    fond: string;
    bordure: string;
    solide: string;
    bgSolide: string;
    /** Texte posé sur `bgSolide` (blanc ou encre, calculé par la charte). */
    texteSolide: string;
  }
> = {
  ok: {
    pastille: 'bg-ok-bg text-ok-fg border-ok-bd',
    texte: 'text-ok-text',
    fond: 'bg-ok-bg',
    bordure: 'border-ok-bd',
    solide: 'text-ok-text',
    bgSolide: 'bg-ok-solid',
    texteSolide: 'text-ok-solid-fg',
  },
  run: {
    pastille: 'bg-run-bg text-run-fg border-run-bd',
    texte: 'text-run-solid',
    fond: 'bg-run-bg',
    bordure: 'border-run-bd',
    solide: 'text-run-solid',
    bgSolide: 'bg-run-solid',
    texteSolide: 'text-run-solid-fg',
  },
  wait: {
    pastille: 'bg-wait-bg text-wait-fg border-wait-bd',
    texte: 'text-wait-solid',
    fond: 'bg-wait-bg',
    bordure: 'border-wait-bd',
    solide: 'text-wait-solid',
    bgSolide: 'bg-wait-solid',
    texteSolide: 'text-wait-solid-fg',
  },
  done: {
    pastille: 'bg-done-bg text-done-fg border-done-bd',
    texte: 'text-done-solid',
    fond: 'bg-done-bg',
    bordure: 'border-done-bd',
    solide: 'text-done-solid',
    bgSolide: 'bg-done-solid',
    texteSolide: 'text-done-solid-fg',
  },
  danger: {
    pastille: 'bg-danger-bg text-danger-fg border-danger-bd',
    texte: 'text-danger-solid',
    fond: 'bg-danger-bg',
    bordure: 'border-danger-bd',
    solide: 'text-danger-solid',
    bgSolide: 'bg-danger-solid',
    texteSolide: 'text-danger-solid-fg',
  },
  warn: {
    pastille: 'bg-warn-bg text-warn-fg border-warn-bd',
    texte: 'text-warn-text',
    fond: 'bg-warn-bg',
    bordure: 'border-warn-bd',
    solide: 'text-warn-text',
    bgSolide: 'bg-warn-solid',
    texteSolide: 'text-warn-solid-fg',
  },
  later: {
    pastille: 'bg-later-bg text-later-fg border-later-bd border-dashed',
    texte: 'text-later-solid',
    fond: 'bg-later-bg',
    bordure: 'border-later-bd',
    solide: 'text-later-solid',
    bgSolide: 'bg-later-solid',
    texteSolide: 'text-later-solid-fg',
  },
  rollback: {
    pastille: 'bg-rollback-bg text-rollback-fg border-rollback-bd',
    texte: 'text-rollback-solid',
    fond: 'bg-rollback-bg',
    bordure: 'border-rollback-bd',
    solide: 'text-rollback-solid',
    bgSolide: 'bg-rollback-solid',
    texteSolide: 'text-rollback-solid-fg',
  },
};
