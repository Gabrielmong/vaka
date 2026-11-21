import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// Output is embedded into SensorPanel.exe (see SensorPanel.csproj). The page is served with a
// strict CSP, so: no inline scripts, no modulepreload polyfill, assets only from 'self'.
export default defineConfig({
  plugins: [react()],
  base: '/',
  build: {
    outDir: 'dist',
    emptyOutDir: true,
    modulePreload: { polyfill: false },
    assetsInlineLimit: 0,
    chunkSizeWarningLimit: 1500,
    sourcemap: false,
  },
});
