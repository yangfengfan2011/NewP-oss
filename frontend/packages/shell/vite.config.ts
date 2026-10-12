import { existsSync, readdirSync, readFileSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath, URL } from 'node:url'
import { discoverFrontendEntries } from '../../scripts/component-sources.mjs'
import { validateFrontendRuntimeRelease } from '../../scripts/frontend-runtime-release.mjs'
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

  const repositoryRoot = fileURLToPath(new URL('../../../', import.meta.url))
  const excluded = (process.env.NEWP_EXCLUDED_MODULE_IDS ?? '').split(',').map((id) => id.trim()).filter(Boolean)
  for (const [entry, path] of Object.entries(discoverFrontendEntries(repositoryRoot, excluded))) {
    addEntry(entry, path)
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

// URL 组件通过 import map 消费与 Shell 相同的模块实例；共享入口由本次发行构建生成。
function nativeSharedRuntime(): Plugin {
  const modules = {
    '@icp-platform/core': 'core', '@icp-platform/sdk': 'sdk',
    react: 'react', 'react/jsx-runtime': 'react-jsx', 'react/jsx-dev-runtime': 'react-jsx-dev',
    'react-dom': 'react-dom', 'react-dom/client': 'react-dom-client',
  }
  const prefix = '\0newp-shared-runtime:'
  return {
    name: 'newp-native-shared-runtime', apply: 'build',
    resolveId(id) { return id.startsWith(prefix) ? id : null },
    load(id) {
      if (!id.startsWith(prefix)) return null
      const name = id.slice(prefix.length)
      const hasDefault = name === 'react' || name === 'react-dom' || name === 'react-dom/client'
      return `export * from ${JSON.stringify(name)};${hasDefault ? `export { default } from ${JSON.stringify(name)};` : ''}`
    },
    buildStart() {
      const release = JSON.parse(readFileSync(fileURLToPath(new URL('../../../server/plugin-platform/ComponentBundles/platform-runtime-release.json', import.meta.url)), 'utf8'))
      validateFrontendRuntimeRelease(release)
      for (const [name, slug] of Object.entries(modules)) this.emitFile({ type: 'chunk', id: prefix + name, fileName: `shared-runtime/${slug}.js`, preserveSignature: 'strict' })
    },
    transformIndexHtml() {
      return [{ tag: 'script', attrs: { type: 'importmap' }, children: JSON.stringify({ imports: Object.fromEntries(Object.entries(modules).map(([name, slug]) => [name, `/shared-runtime/${slug}.js`])) }), injectTo: 'head-prepend' }]
    },
  }
}

export default defineConfig({
  plugins: [
    localhostIpv4Redirect(),
    newpFrontendEntryRegistry(),
    nativeSharedRuntime(),
    react(),
  ],
  resolve: {
    alias: {
      '@icp-platform/core': fileURLToPath(new URL('../core/src/index.ts', import.meta.url)),
      '@icp-platform/sdk': fileURLToPath(new URL('../sdk/src/index.ts', import.meta.url)),
      '@icp-platform/ui/styles': fileURLToPath(
        new URL('../ui/src/styles/index.css', import.meta.url),
      ),
      '@icp-platform/ui/button': fileURLToPath(new URL('../ui/src/components/Button.tsx', import.meta.url)),
      '@icp-platform/ui/locale': fileURLToPath(new URL('../ui/src/locale.tsx', import.meta.url)),
      '@icp-platform/ui/core': fileURLToPath(new URL('../ui/src/core.ts', import.meta.url)),
      '@icp-platform/ui/crud-detail': fileURLToPath(new URL('../ui/src/crud-detail.ts', import.meta.url)),
      '@icp-platform/ui/crud-form': fileURLToPath(new URL('../ui/src/crud-form.ts', import.meta.url)),
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
