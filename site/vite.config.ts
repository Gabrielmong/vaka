import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';

// The one place the repository is named when GITHUB_REPOSITORY isn't set (local builds).
const FALLBACK_REPO = 'Gabrielmong/vaka';

declare const process: { env: Record<string, string | undefined> }; // Node global, without pulling in @types/node

// GitHub Pages serves the site from /<repo>/, so every URL is relative (base './').
export default defineConfig({
  plugins: [react()],
  base: './',
  define: { __REPO__: JSON.stringify(process.env.GITHUB_REPOSITORY || FALLBACK_REPO) },
  build: { outDir: 'dist', emptyOutDir: true, chunkSizeWarningLimit: 1500, sourcemap: false },
  test: { environment: 'node' },
});
