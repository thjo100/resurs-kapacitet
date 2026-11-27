import { readFileSync } from 'node:fs'
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// Vanligt bygge: frontenden serveras av .NET-backend (wwwroot). I utveckling proxas /api till :5080.
// Demobygge (`npm run build:demo`): allt i ett JS-paket med en webbläsarbaserad backend, se src/demo/mockApi.js.
export default defineConfig(({ mode }) => {
  const demo = mode === 'demo'
  // Demobygget visar samma release notes som backend. Läses bara i demoläge, så containerbygget
  // (som bara har frontend-mappen) inte behöver backend-filerna.
  const releaseNotes = demo
    ? readFileSync(new URL('../backend/src/ResursKapacitet.Api/ReleaseNotes.json', import.meta.url), 'utf8')
    : '[]'
  return {
    plugins: [vue()],
    define: { __RELEASE_NOTES__: releaseNotes },
    base: demo ? './' : '/',
    server: {
      port: 5173,
      proxy: {
        '/api': { target: process.env.API_URL || 'http://localhost:5080', changeOrigin: true },
      },
    },
    build: {
      outDir: demo ? 'dist-demo' : 'dist',
      emptyOutDir: true,
      chunkSizeWarningLimit: 800,
      cssCodeSplit: !demo,
      rollupOptions: demo ? { output: { inlineDynamicImports: true } } : {},
    },
    test: {
      environment: 'node',
    },
  }
})
