import { defineConfig, type PluginOption } from 'vite'
import react from '@vitejs/plugin-react'
import { createBuiltinRuntimePluginManifest } from './src/builtin-runtime-plugins'

const frontendPluginManifestPath = 'plugins/frontend-plugin-manifest.json'

function builtinFrontendPluginManifestPlugin(): PluginOption {
  const manifestSource = `${JSON.stringify(createBuiltinRuntimePluginManifest(), null, 2)}\n`

  return {
    name: 'builtin-frontend-plugin-manifest',
    configureServer(server) {
      server.middlewares.use((req, res, next) => {
        const requestPath = req.url?.split('?')[0]
        if (requestPath !== `/${frontendPluginManifestPath}`) {
          next()
          return
        }

        res.setHeader('Content-Type', 'application/json; charset=utf-8')
        res.end(manifestSource)
      })
    },
    generateBundle() {
      this.emitFile({
        type: 'asset',
        fileName: frontendPluginManifestPath,
        source: manifestSource,
      })
    },
  }
}

export default defineConfig({
  plugins: [react(), builtinFrontendPluginManifestPlugin()],
  server: {
    port: 3000,
    proxy: {
      '/api': {
        target: 'http://localhost:5000',
        changeOrigin: true
      }
    }
  },
  test: {
    globals: true,
    environment: 'jsdom'
  }
})