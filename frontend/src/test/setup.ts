import '@testing-library/jest-dom/vitest';

// Données partagées entre écrans (referentiels.ts) : comme un onglet rouvert, un test affiche aussitôt les réponses du
// test précédent puis les relit. Import tardif : le module doit voir les simulations de l'API du fichier de test.
afterEach(async () => {
  const { vieillirReferentiels } = await import('../referentiels');
  vieillirReferentiels();
});
