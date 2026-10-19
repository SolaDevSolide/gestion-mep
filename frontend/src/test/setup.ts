import '@testing-library/jest-dom/vitest';

// Données partagées entre écrans (referentiels.ts : référentiels, journal d'une MEP…) oubliées après chaque test : un
// test ne voit jamais les réponses d'un autre, quel que soit l'ordre. Import tardif : le module doit voir les
// simulations de l'API du fichier de test.
afterEach(async () => {
  const { oublierReferentiels } = await import('../referentiels');
  oublierReferentiels();
});
