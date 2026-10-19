import '@testing-library/jest-dom/vitest';

// Données partagées entre écrans (référentiels.ts) : gardées en mémoire d'un montage à l'autre, oubliées entre deux
// tests. Import tardif : le module doit voir les simulations de l'API déclarées par le fichier de test.
afterEach(async () => {
  const { oublierReferentiels } = await import('../referentiels');
  oublierReferentiels();
});
