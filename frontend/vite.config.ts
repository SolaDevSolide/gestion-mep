import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 5173,
    proxy: {
      '/api': { target: 'http://localhost:5080', changeOrigin: false, secure: false },
      '/charte.css': { target: 'http://localhost:5080', changeOrigin: false, secure: false },
    },
  },
  build: { outDir: 'dist', emptyOutDir: true },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
    // Feuilles de style ignorées, sauf lues en texte (?raw) : un test compare la charte par défaut à index.css.
    css: { include: [/\.css\?raw$/] },
  },
});
