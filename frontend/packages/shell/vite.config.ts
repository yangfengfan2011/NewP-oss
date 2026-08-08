import { fileURLToPath, URL } from 'node:url'
import fs from 'node:fs'
import path from 'node:path'
import react from '@vitejs/plugin-react'
import { defineConfig, type PluginOption } from 'vite'
import { createBuiltinRuntimePluginManifest } from './src/builtin-runtime-plugins'

const frontendPluginManifestPath = 'plugins/frontend-plugin-manifest.json'
const localizationManifestPath = 'plugins/localization-manifest.json'
const metadataRuntimeManifestPath = 'plugins/metadata-runtime-manifest.json'
const metadataRuntimeModulesPrefix = '/plugins/runtime/modules/'
const moduleMetadataRoot = fileURLToPath(new URL('../../../modules', import.meta.url))
const devShellPort = Number.parseInt(process.env.ICP_DEV_SHELL_PORT ?? '3000', 10)
const devHostPort = Number.parseInt(process.env.ICP_DEV_HOST_PORT ?? '5000', 10)

interface RuntimeMetadataModuleEntry {
  moduleId: string
  metadataBasePath: string
  navFiles: string[]
  viewFiles: string[]
  modelFiles: string[]
}

interface RuntimeLanguage {
  code: string
  displayName: string
}

function toRuntimePath(value: string): string {
  return value.split(path.sep).join('/')
}

function listMetadataFiles(metadataRoot: string, relativeDirectory: string): string[] {
  const directory = path.join(metadataRoot, relativeDirectory)
  if (!fs.existsSync(directory)) {
    return []
  }

  const files: string[] = []
  const visit = (current: string) => {
    for (const entry of fs.readdirSync(current, { withFileTypes: true })) {
      const entryPath = path.join(current, entry.name)
      if (entry.isDirectory()) {
        visit(entryPath)
        continue
      }

      if (entry.isFile() && entry.name.endsWith('.json')) {
        files.push(toRuntimePath(path.relative(metadataRoot, entryPath)))
      }
    }
  }

  visit(directory)
  return files.sort((left, right) => left.localeCompare(right))
}

export function createBuiltinRuntimeMetadataManifest(): { modules: RuntimeMetadataModuleEntry[] } {
  if (!fs.existsSync(moduleMetadataRoot)) {
    return { modules: [] }
  }

  const modules = fs.readdirSync(moduleMetadataRoot, { withFileTypes: true })
    .filter(entry => entry.isDirectory())
    .map((entry) => {
      const metadataRoot = path.join(moduleMetadataRoot, entry.name, 'metadata')
      return {
        moduleId: entry.name,
        metadataBasePath: `/plugins/runtime/modules/${entry.name}/metadata`,
        navFiles: listMetadataFiles(metadataRoot, 'navigation'),
        viewFiles: listMetadataFiles(metadataRoot, 'views'),
        modelFiles: listMetadataFiles(metadataRoot, 'models'),
      }
    })
    .filter(entry => entry.navFiles.length > 0 || entry.viewFiles.length > 0 || entry.modelFiles.length > 0)
    .sort((left, right) => left.moduleId.localeCompare(right.moduleId))

  return { modules }
}

function readJsonFile(filePath: string): any {
  return JSON.parse(fs.readFileSync(filePath, 'utf8').replace(/^\uFEFF/, ''))
}

function createEmptyLocalizationCatalogs(languages: RuntimeLanguage[]): Record<string, Record<string, string>> {
  return Object.fromEntries(languages.map(language => [language.code, {}]))
}

function listDictionaryFiles(metadataRoot: string): string[] {
  if (!fs.existsSync(metadataRoot)) {
    return []
  }

  const files: string[] = []
  const visit = (current: string) => {
    for (const entry of fs.readdirSync(current, { withFileTypes: true })) {
      const entryPath = path.join(current, entry.name)
      if (entry.isDirectory()) {
        visit(entryPath)
        continue
      }

      if (entry.isFile() && entry.name.endsWith('.dic.json')) {
        files.push(entryPath)
      }
    }
  }

  visit(metadataRoot)
  return files.sort((left, right) => left.localeCompare(right))
}

function projectDictionaryFile(
  filePath: string,
  supportedLanguages: RuntimeLanguage[],
  catalogs: Record<string, Record<string, string>>,
) {
  const document = readJsonFile(filePath)
  if (document?.type !== 'dic') {
    return
  }

  const dicKey = Object.keys(document).find(key => key !== 'schemaVersion' && key !== 'type')
  if (!dicKey || typeof document[dicKey] !== 'object') {
    return
  }

  for (const [entryKey, translations] of Object.entries(document[dicKey] as Record<string, Record<string, string>>)) {
    for (const language of supportedLanguages) {
      const value = translations?.[language.code]
      if (typeof value === 'string') {
        catalogs[language.code][`${dicKey}.${entryKey}`] = value
      }
    }
  }
}

export function createBuiltinRuntimeLocalizationManifest() {
  const languagesPath = path.join(moduleMetadataRoot, 'Platform', 'metadata', 'languages.json')
  const languagesManifest = fs.existsSync(languagesPath)
    ? readJsonFile(languagesPath)
    : { defaultLanguage: 'zhs', supportedLanguages: [{ code: 'zhs', displayName: '简体中文' }] }
  const supportedLanguages: RuntimeLanguage[] = Array.isArray(languagesManifest.supportedLanguages)
    ? languagesManifest.supportedLanguages.filter(
      (language: Partial<RuntimeLanguage>): language is RuntimeLanguage =>
        typeof language.code === 'string' && typeof language.displayName === 'string',
    )
    : []
  const catalogs = createEmptyLocalizationCatalogs(supportedLanguages)

  if (fs.existsSync(moduleMetadataRoot)) {
    for (const moduleEntry of fs.readdirSync(moduleMetadataRoot, { withFileTypes: true }).filter(entry => entry.isDirectory())) {
      const metadataRoot = path.join(moduleMetadataRoot, moduleEntry.name, 'metadata')
      for (const dictionaryFile of listDictionaryFiles(metadataRoot)) {
        projectDictionaryFile(dictionaryFile, supportedLanguages, catalogs)
      }
    }
  }

  return {
    defaultLanguage: typeof languagesManifest.defaultLanguage === 'string' ? languagesManifest.defaultLanguage : 'zhs',
    supportedLanguages,
    catalogs,
  }
}

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

function resolveBuiltinMetadataFile(requestPath: string): string | null {
  if (!requestPath.startsWith(metadataRuntimeModulesPrefix)) {
    return null
  }

  const relativeRequest = decodeURIComponent(requestPath.slice(metadataRuntimeModulesPrefix.length))
  const [moduleId, metadataSegment, ...metadataParts] = relativeRequest.split('/')
  if (!moduleId || metadataSegment !== 'metadata' || metadataParts.length === 0) {
    return null
  }

  const metadataRoot = path.join(moduleMetadataRoot, moduleId, 'metadata')
  const candidate = path.resolve(metadataRoot, ...metadataParts)
  if (!candidate.startsWith(path.resolve(metadataRoot) + path.sep) || path.extname(candidate) !== '.json') {
    return null
  }

  return fs.existsSync(candidate) ? candidate : null
}

function builtinRuntimeMetadataPlugin(): PluginOption {
  const manifestSource = `${JSON.stringify(createBuiltinRuntimeMetadataManifest(), null, 2)}\n`
  const localizationSource = `${JSON.stringify(createBuiltinRuntimeLocalizationManifest(), null, 2)}\n`

  return {
    name: 'builtin-runtime-metadata',
    configureServer(server) {
      server.middlewares.use((req, res, next) => {
        const requestPath = req.url?.split('?')[0]
        if (requestPath === `/${metadataRuntimeManifestPath}`) {
          res.setHeader('Content-Type', 'application/json; charset=utf-8')
          res.end(manifestSource)
          return
        }

        if (requestPath === `/${localizationManifestPath}`) {
          res.setHeader('Content-Type', 'application/json; charset=utf-8')
          res.end(localizationSource)
          return
        }

        const metadataFile = requestPath ? resolveBuiltinMetadataFile(requestPath) : null
        if (metadataFile) {
          res.setHeader('Content-Type', 'application/json; charset=utf-8')
          res.end(fs.readFileSync(metadataFile, 'utf8'))
          return
        }

        next()
      })
    },
    generateBundle() {
      this.emitFile({
        type: 'asset',
        fileName: metadataRuntimeManifestPath,
        source: manifestSource,
      })
      this.emitFile({
        type: 'asset',
        fileName: localizationManifestPath,
        source: localizationSource,
      })

      for (const moduleEntry of createBuiltinRuntimeMetadataManifest().modules) {
        for (const metadataFile of [...moduleEntry.navFiles, ...moduleEntry.viewFiles, ...moduleEntry.modelFiles]) {
          const sourcePath = path.join(moduleMetadataRoot, moduleEntry.moduleId, 'metadata', metadataFile)
          this.emitFile({
            type: 'asset',
            fileName: `plugins/runtime/modules/${moduleEntry.moduleId}/metadata/${metadataFile}`,
            source: fs.readFileSync(sourcePath, 'utf8'),
          })
        }
      }
    },
  }
}

export default defineConfig({
  plugins: [react(), builtinFrontendPluginManifestPlugin(), builtinRuntimeMetadataPlugin()],
  resolve: {
    alias: {
      '@icp-platform/core': fileURLToPath(new URL('../core/src/index.ts', import.meta.url)),
      '@icp-platform/sdk': fileURLToPath(new URL('../sdk/src/index.ts', import.meta.url)),
      '@icp-platform/ui/styles': fileURLToPath(
        new URL('../ui/src/styles/index.css', import.meta.url),
      ),
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
    },
  },
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: ['./src/__tests__/setupAntd.ts'],
  },
})
