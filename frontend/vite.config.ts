import { fileURLToPath } from 'node:url'
import react, { reactCompilerPreset } from '@vitejs/plugin-react'
import babel from '@rolldown/plugin-babel'
import { defineConfig } from 'vite'
import packageJson from './package.json' with { type: 'json' }

// https://vite.dev/config/
export default defineConfig({
  plugins: [
    react(),
    // 'infer' compiles what looks like a component or hook without opt-in directives; 'none'
    // leaves a function the compiler can't handle uncompiled instead of failing the build.
    babel({ presets: [reactCompilerPreset({ compilationMode: 'infer', target: '19', panicThreshold: 'none' })] }),
  ],
  define: {
    // package.json is the version of record; the header label reads it through this constant.
    __APP_VERSION__: JSON.stringify(packageJson.version),
  },
  resolve: {
    alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) },
  },
  server: {
    port: 5173,
    // In development the API listens on its own port (launchSettings). Proxying keeps the browser
    // on one origin, exactly as in production, where the API serves the SPA itself — so no CORS.
    proxy: {
      '/api': { target: 'http://localhost:5080', ws: true },
    },
  },
  build: {
    // Each lazy route ships its own stylesheet — which is why a component imports every sheet
    // its classes come from instead of relying on the page that happened to load first.
    cssCodeSplit: true,
  },
})
