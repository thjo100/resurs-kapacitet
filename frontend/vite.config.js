import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// I utveckling proxas /api till .NET-backend (dotnet run på port 5080).
export default defineConfig({
  plugins: [vue()],
  server: {
    port: 5173,
    proxy: {
      '/api': { target: process.env.API_URL || 'http://localhost:5080', changeOrigin: true },
    },
  },
  build: {
    outDir: 'dist',
    emptyOutDir: true,
    chunkSizeWarningLimit: 800,
  },
  test: {
    environment: 'node',
  },
})
