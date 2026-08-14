import { existsSync, readdirSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath, URL } from 'node:url'
import react from '@vitejs/plugin-react'
import { defineConfig, normalizePath, type Plugin } from 'vite'

const devShellPort = Number.parseInt(process.env.ICP_DEV_SHELL_PORT ?? '3000', 10)
const devHostPort = Number.parseInt(process.env.ICP_DEV_HOST_PORT ?? '5000', 10)
const virtualRegistryId = 'virtual:newp-frontend-entry-registry'
const resolvedVirtualRegistryId = `\0${virtualRegistryId}`

function localhostIpv4Redirect(): Plugin {
  return {
    name: 'newp-localhost-ipv4-redirect',
    apply: 'serve',
    configureServer(server) {
      server.middlewares.use((request, response, next) => {
        const host = request.headers.host?.trim().toLowerCase()
        if (!host || !/^localhost(?::\d+)?$/.test(host)) {
          next()
          return
        }

        const requestPath = request.url?.startsWith('/') ? request.url : `/${request.url ?? ''}`
        response.statusCode = 307
        response.setHeader('Location', `http://127.0.0.1:${devShellPort}${requestPath}`)
        response.setHeader('Cache-Control', 'no-store')
        response.end()
      })
    },
  }
}

function newpFrontendEntryRegistry(): Plugin {
  const entries = new Map<string, string>()
  const addEntry = (key: string, path: string) => {
    if (entries.has(key)) {
      throw new Error(`Duplicate frontend implementation entry '${key}'.`)
    }
    entries.set(key, `/@fs/${normalizePath(path)}`)
  }

  const internalPluginsRoot = fileURLToPath(new URL('./src/plugins', import.meta.url))
  if (existsSync(internalPluginsRoot)) {
    for (const item of readdirSync(internalPluginsRoot, { withFileTypes: true })
      .filter((entry) => entry.isFile() && entry.name.endsWith('.ts'))
      .sort((left, right) => left.name.localeCompare(right.name, 'en'))) {
      addEntry(item.name.slice(0, -3), join(internalPluginsRoot, item.name))
    }
  }

  const frontendModulesRoot = fileURLToPath(new URL('../../modules', import.meta.url))
  if (existsSync(frontendModulesRoot)) {
    for (const item of readdirSync(frontendModulesRoot, { withFileTypes: true })
      .filter((entry) => entry.isDirectory())
      .sort((left, right) => left.name.localeCompare(right.name, 'en'))) {
      const entryPath = join(frontendModulesRoot, item.name, 'src', 'index.ts')
      if (existsSync(entryPath)) {
        addEntry(item.name, entryPath)
      }
    }
  }

  return {
    name: 'newp-frontend-entry-registry',
    resolveId(id) {
      return id === virtualRegistryId ? resolvedVirtualRegistryId : null
    },
    load(id) {
      if (id !== resolvedVirtualRegistryId) {
        return null
      }

      const properties = [...entries.entries()]
        .map(([key, path]) => `  ${JSON.stringify(key)}: () => import(${JSON.stringify(path)})`)
        .join(',\n')
      return `const registry = {\n${properties}\n}\nexport default registry\n`
    },
  }
}

export default defineConfig({
  plugins: [localhostIpv4Redirect(), newpFrontendEntryRegistry(), react()],
  resolve: {
    alias: {
      '@icp-platform/core': fileURLToPath(new URL('../core/src/index.ts', import.meta.url)),
      '@icp-platform/sdk': fileURLToPath(new URL('../sdk/src/index.ts', import.meta.url)),
      '@icp-platform/ui/styles': fileURLToPath(
        new URL('../ui/src/styles/index.css', import.meta.url),
      ),
      '@icp-platform/ui/core': fileURLToPath(new URL('../ui/src/core.ts', import.meta.url)),
      '@icp-platform/ui/crud': fileURLToPath(new URL('../ui/src/crud.ts', import.meta.url)),
      '@icp-platform/ui': fileURLToPath(new URL('../ui/src/index.ts', import.meta.url)),
    },
  },
  server: {
    host: '127.0.0.1',
    port: devShellPort,
    strictPort: true,
    proxy: {
      '/api': {
        target: `http://127.0.0.1:${devHostPort}`,
        changeOrigin: true,
      },
      // 只把编译器拥有的活动目录资源交给 Host；/plugins/<module>/<page> 仍是合法 SPA 页面路由。
      '^/plugins/(?:(?:active-module-runtime|frontend-plugin-manifest|localization-manifest|metadata-runtime-manifest)\\.json(?:\\?|$)|runtime(?:/|$))': {
        target: `http://127.0.0.1:${devHostPort}`,
        changeOrigin: true,
      },
    },
  },
  build: {
    manifest: true,
    sourcemap: true,
  },
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: ['./src/__tests__/setupAntd.ts'],
  },
})
