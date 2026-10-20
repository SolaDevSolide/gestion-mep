import { defineConfig } from '@playwright/test';

// Variables d'environnement de Node (sans dépendre des types de Node pour le reste du projet).
declare const process: { env: Record<string, string | undefined> };

/**
 * Tests de bout en bout (`npm run e2e`), sur une instance déjà démarrée en mode démonstration (DEMO=true) :
 * `E2E_URL` donne son adresse (par défaut le serveur de développement Vite).
 */
export default defineConfig({
  testDir: './e2e',
  testMatch: '*.e2e.ts',
  timeout: 60_000,
  reporter: 'list',
  use: {
    baseURL: process.env.E2E_URL ?? 'http://localhost:5173',
    locale: 'fr-FR',
    // Portable 1366 × 768 : fenêtre utile d'environ 1366 × 641 une fois le navigateur et la barre des tâches déduits.
    viewport: { width: 1366, height: 641 },
    screenshot: 'only-on-failure',
  },
});
