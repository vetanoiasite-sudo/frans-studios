import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// In development the API runs on Kestrel (https://localhost:7220). Proxying keeps the app
// same-origin, exactly like production, so cookies and CSRF behave identically.
const api = { target: 'https://localhost:7220', changeOrigin: false, secure: false }

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    strictPort: true,
    proxy: {
      '/api': api,
      '/media': api,
      '/robots.txt': api,
      '/sitemap.xml': api,
    },
  },
  build: {
    outDir: 'dist',
    assetsDir: 'assets',
    sourcemap: false,
    // Hashed file names → served with "immutable" caching by ASP.NET Core.
  },
})
