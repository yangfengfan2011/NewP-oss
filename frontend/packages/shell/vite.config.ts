import { existsSync, readFileSync, readdirSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath, URL } from 'node:url'
import react from '@vitejs/plugin-react'
import { defineConfig, normalizePath, type Plugin } from 'vite'

const devShellPort = Number.parseInt(process.env.ICP_DEV_SHELL_PORT ?? '3000', 10)
const devHostPort = Number.parseInt(process.env.ICP_DEV_HOST_PORT ?? '5000', 10)
const virtualRegistryId = 'virtual:newp-frontend-entry-registry'
const resolvedVirtualRegistryId = `\0${virtualRegistryId}`
interface AuthoringFrontendManifest {
  moduleId?: unknown
  frontend?: {
    enabled?: unknown
    entry?: unknown
  }
}

function toKebabCase(value: string): string {
  return [...value].map((character, index) => {
    const normalized = character.toLocaleLowerCase('en-US')
    return character !== normalized && index > 0 ? `-${normalized}` : normalized
  }).join('')
}

function collectEnabledBuiltInFrontendEntries(repositoryRoot: string): Set<string> {
  const entries = new Set<string>()
  const sources = [
    {
      root: join(repositoryRoot, 'modules'),
      manifestName: 'newp.plugin.json',
    },
    {
      root: join(repositoryRoot, 'apps', 'host-api', 'modules'),
      manifestName: 'plugin.json',
    },
  ]

  for (const source of sources) {
    if (!existsSync(source.root)) {
      continue
    }
    for (const item of readdirSync(source.root, { withFileTypes: true })
      .filter((entry) => entry.isDirectory())
      .sort((left, right) => left.name.localeCompare(right.name, 'en'))) {
      const manifestPath = join(source.root, item.name, source.manifestName)
      if (!existsSync(manifestPath)) {
        continue
      }
      const manifest = JSON.parse(readFileSync(manifestPath, 'utf8')) as AuthoringFrontendManifest
      if (manifest.frontend?.enabled === false) {
        continue
      }
      if (typeof manifest.moduleId !== 'string' || manifest.moduleId.trim().length === 0) {
        throw new Error(`Frontend authoring manifest '${manifestPath}' does not declare moduleId.`)
      }
      const entry = typeof manifest.frontend?.entry === 'string' && manifest.frontend.entry.trim().length > 0
        ? manifest.frontend.entry.trim()
        : toKebabCase(manifest.moduleId.trim())
      if (entries.has(entry)) {
        throw new Error(`Duplicate enabled frontend authoring entry '${entry}'.`)
      }
      entries.add(entry)
    }
  }

  return entries
}

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
  const repositoryRoot = fileURLToPath(new URL('../../../', import.meta.url))
  const enabledBuiltInEntries = collectEnabledBuiltInFrontendEntries(repositoryRoot)
  if (existsSync(frontendModulesRoot)) {
    for (const item of readdirSync(frontendModulesRoot, { withFileTypes: true })
      .filter((entry) => entry.isDirectory() && enabledBuiltInEntries.has(entry.name))
      .sort((left, right) => left.name.localeCompare(right.name, 'en'))) {
      const entryPath = join(frontendModulesRoot, item.name, 'src', 'index.ts')
      if (existsSync(entryPath)) {
        addEntry(item.name, entryPath)
        enabledBuiltInEntries.delete(item.name)
      }
    }
  }
  if (enabledBuiltInEntries.size > 0) {
    throw new Error(
      `Enabled frontend implementation entries are missing: ${[...enabledBuiltInEntries].sort().join(', ')}.`,
    )
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
      '@icp-platform/ui/button': fileURLToPath(new URL('../ui/src/components/Button.tsx', import.meta.url)),
      '@icp-platform/ui/core': fileURLToPath(new URL('../ui/src/core.ts', import.meta.url)),
      '@icp-platform/ui/crud-detail': fileURLToPath(new URL('../ui/src/crud-detail.ts', import.meta.url)),
      '@icp-platform/ui/crud-list': fileURLToPath(new URL('../ui/src/crud-list.ts', import.meta.url)),
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
