/** Préférence « réduire les animations » du système : transitions instantanées (les CSS sont déjà coupées dans index.css). */
export function mouvementReduit(): boolean {
  return typeof window.matchMedia === 'function' && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
}
