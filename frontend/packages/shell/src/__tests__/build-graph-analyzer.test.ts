// @vitest-environment node

import { copyFileSync, cpSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { spawnSync } from 'node:child_process'
import { gzipSync } from 'node:zlib'
import { afterEach, describe, expect, it } from 'vitest'

const repositoryRoot = fileURLToPath(new URL('../../../../..', import.meta.url))
const analyzerPath = join(repositoryRoot, 'scripts', 'performance', 'analyze-shell-build.mjs')
const fixtureRoot = join(repositoryRoot, 'scripts', 'performance', 'fixtures', 'build-graph-valid')
const cssFixtureRoot = join(repositoryRoot, 'scripts', 'performance', 'fixtures', 'build-graph-css')
const temporaryRoots: string[] = []

interface MutableManifestEntry {
  file: string
  imports: string[]
  [key: string]: unknown
}

interface MutableAnalyzerConfig {
  scenarios: Record<string, { roots: unknown[] }>
  budgets: {
    maxChunkMinifiedBytes: number
    chunkOverrides: Array<{
      sourceContains: string
      [key: string]: unknown
    }>
    [key: string]: unknown
  }
  [key: string]: unknown
}

interface MutableSourceMap {
  version: number
  file: string
  sources: string[]
  names: string[]
  mappings: string
}

afterEach(() => {
  for (const root of temporaryRoots.splice(0)) {
    rmSync(root, { recursive: true, force: true })
  }
})

describe('Shell production build graph CLI', () => {
  function runCssCase(mutate?: (root: string, manifest: Record<string, MutableManifestEntry>) => void) {
    const root = mkdtempSync(join(tmpdir(), 'newp-build-graph-css-'))
    temporaryRoots.push(root)
    cpSync(fixtureRoot, root, { recursive: true })
    cpSync(cssFixtureRoot, root, { recursive: true })
    const manifest = JSON.parse(readFileSync(join(root, 'manifest.json'), 'utf8')) as Record<string, MutableManifestEntry>
    mutate?.(root, manifest)
    writeFileSync(join(root, 'manifest.json'), JSON.stringify(manifest), 'utf8')
    const config = JSON.parse(readFileSync(join(root, 'config.json'), 'utf8'))
    config.forbiddenDependencies = [{ id: 'absent-js-source', sourceContains: '/not-emitted/', scenarios: ['anonymous', 'personnelRoute'] }]
    writeFileSync(join(root, 'config.json'), JSON.stringify(config), 'utf8')
    const resultPath = join(root, 'result.json')
    const execution = spawnSync(process.execPath, [analyzerPath,
      '--manifest', join(root, 'manifest.json'), '--config', join(root, 'config.json'),
      '--assets-root', root, '--mode', 'candidate', '--output', resultPath,
    ], { cwd: repositoryRoot, encoding: 'utf8' })
    return { root, execution, report: JSON.parse(readFileSync(resultPath, 'utf8')) }
  }

  it('counts independent CSS nodes and css arrays once in their scenario and route delta without treating them as JS', () => {
    const { root, execution, report } = runCssCase()
    expect(execution.status, execution.stderr).toBe(0)
    const files = ['assets/main.css', 'assets/standalone.css']
    const bytes = files.map(file => readFileSync(join(root, file)))
    expect(report.scenarios.anonymous.css).toEqual({ files,
      minifiedBytes: bytes.reduce((total, value) => total + value.byteLength, 0),
      gzipBytes: bytes.reduce((total, value) => total + gzipSync(value, { level: 9 }).byteLength, 0),
    })
    expect(report.scenarios.authenticatedHome.css.files).toEqual(['assets/main.css', 'assets/module.css', 'assets/standalone.css'])
    const routeBytes = readFileSync(join(root, 'assets/route.css'))
    expect(report.scenarios.personnelRoute.delta.css).toEqual({ files: ['assets/route.css'], minifiedBytes: routeBytes.byteLength, gzipBytes: gzipSync(routeBytes, { level: 9 }).byteLength })
    expect(report.scenarios.anonymous.files).toEqual(['assets/cycle.js', 'assets/main.js', 'assets/shared.js'])
    expect(report.scenarios.personnelRoute.delta.files).toEqual(['assets/personnel.js'])
    expect(report.chunks.every((chunk: { file: string }) => chunk.file.endsWith('.js'))).toBe(true)
    expect(report.emittedCss.map((asset: { file: string }) => asset.file)).toEqual(['assets/main.css', 'assets/module.css', 'assets/route.css', 'assets/standalone.css'])
  })

  it.each([
    ['missing CSS', (root: string) => rmSync(join(root, 'assets/standalone.css')), "Emitted CSS file 'assets/standalone.css'"],
    ['CSS path escape', (_root: string, manifest: Record<string, MutableManifestEntry>) => { manifest['_PageOutlet-!~{01p}~.js'].file = '../outside.css' }, 'escaped the assets root'],
    ['missing CSS graph edge', (_root: string, manifest: Record<string, MutableManifestEntry>) => { manifest['_PageOutlet-!~{01p}~.js'].imports = ['_missing.css'] }, 'references missing entry'],
    ['missing JS source map', (root: string) => rmSync(join(root, 'assets/main.js.map')), "Source map for 'assets/main.js'"],
    ['invalid JS source map', (root: string) => writeFileSync(join(root, 'assets/main.js.map'), '{}'), 'does not declare sources'],
    ['missing css array asset', (_root: string, manifest: Record<string, MutableManifestEntry>) => { manifest['index.html'].css = ['assets/missing.css'] }, "Emitted CSS file 'assets/missing.css'"],
    ['malformed css array', (_root: string, manifest: Record<string, MutableManifestEntry>) => { manifest['index.html'].css = 'assets/main.css' }, 'css must be an array'],
  ] as const)('fails closed for %s alongside valid independent CSS nodes', (_name, mutate, diagnostic) => {
    const { execution, report } = runCssCase(mutate)
    expect(execution.status).toBe(2)
    expect(report.failureCode).toBe('BUILD_GRAPH_INPUT_INVALID')
    expect(report.diagnostic).toContain(diagnostic)
  })

  it('keeps rc-picker enforced before authentication and report-only in the authenticated shell', () => {
    const config = JSON.parse(readFileSync(
      join(repositoryRoot, 'scripts', 'performance', 'shell-build-graph.config.json'),
      'utf8',
    )) as {
      forbiddenDependencies: Array<{
        id: string
        scenarios: string[]
        enforcement?: 'enforced' | 'report-only'
      }>
    }
    const rules = new Map(config.forbiddenDependencies.map((rule) => [rule.id, rule]))

    expect(rules.get('rc-picker')).toMatchObject({
      scenarios: ['anonymous'],
    })
    expect(rules.get('rc-picker')?.enforcement ?? 'enforced').toBe('enforced')
    expect(rules.get('rc-picker-authenticated-shell')).toMatchObject({
      scenarios: ['authenticatedShell'],
      enforcement: 'report-only',
    })
    expect(rules.get('module-binding-eager-rc-picker')).toMatchObject({
      scenarios: ['moduleBindingEager'],
    })
    expect(rules.get('module-binding-eager-rc-picker')?.enforcement ?? 'enforced').toBe('enforced')
  })

  it('computes static closures and a route delta without double-counting shared or cyclic edges', () => {
    const outputRoot = mkdtempSync(join(tmpdir(), 'newp-build-graph-'))
    temporaryRoots.push(outputRoot)
    const resultPath = join(outputRoot, 'result.json')

    const execution = spawnSync(process.execPath, [
      analyzerPath,
      '--manifest', join(fixtureRoot, 'manifest.json'),
      '--config', join(fixtureRoot, 'config.json'),
      '--assets-root', fixtureRoot,
      '--mode', 'candidate',
      '--output', resultPath,
    ], {
      cwd: repositoryRoot,
      encoding: 'utf8',
    })

    expect(execution.status, execution.stderr).toBe(0)
    const report = JSON.parse(readFileSync(resultPath, 'utf8'))

    expect(report.schemaVersion).toBe('1.0')
    expect(report.verdict).toBe('passed')
    expect(report.scenarios.anonymous.files).toEqual([
      'assets/cycle.js',
      'assets/main.js',
      'assets/shared.js',
    ])
    expect(report.scenarios.authenticatedHome.files).toEqual([
      'assets/cycle.js',
      'assets/main.js',
      'assets/module.js',
      'assets/shared.js',
    ])
    expect(report.scenarios.personnelRoute.delta.files).toEqual(['assets/personnel.js'])
    expect(report.scenarios.personnelRoute.delta.minifiedBytes).toBe(
      readFileSync(join(fixtureRoot, 'assets', 'personnel.js')).byteLength,
    )
    expect(report.scenarios.personnelRoute.delta.gzipBytes).toBeGreaterThan(0)
    expect(report.targetCapabilityAssets).toEqual(['assets/personnel.js'])
    expect(report.scenarios.anonymous.css.files).toEqual(['assets/main.css'])
    expect(report.scenarios.authenticatedHome.css.files).toEqual([
      'assets/main.css',
      'assets/module.css',
    ])
    expect(report.emittedCss.map((asset: { file: string }) => asset.file)).toEqual([
      'assets/main.css',
      'assets/module.css',
    ])
  })

  it('records structural violations in baseline mode and rejects the same graph in candidate mode', () => {
    const outputRoot = mkdtempSync(join(tmpdir(), 'newp-build-graph-'))
    temporaryRoots.push(outputRoot)
    const configPath = join(fixtureRoot, 'config-violating.json')
    const run = (mode: 'baseline' | 'candidate') => {
      const resultPath = join(outputRoot, `${mode}.json`)
      const execution = spawnSync(process.execPath, [
        analyzerPath,
        '--manifest', join(fixtureRoot, 'manifest.json'),
        '--config', configPath,
        '--assets-root', fixtureRoot,
        '--mode', mode,
        '--output', resultPath,
      ], {
        cwd: repositoryRoot,
        encoding: 'utf8',
      })
      return { execution, report: JSON.parse(readFileSync(resultPath, 'utf8')) }
    }

    const baseline = run('baseline')
    expect(baseline.execution.status, baseline.execution.stderr).toBe(0)
    expect(baseline.report.verdict).toBe('failed')
    expect(baseline.report.enforcement).toBe('record-only')
    expect(baseline.report.failureCode).toBe('BUILD_GRAPH_BUDGET_EXCEEDED')
    expect(baseline.report.forbiddenReachability).toContainEqual(expect.objectContaining({
      dependency: 'fixture-shared-package',
      scenario: 'anonymous',
      chain: ['index.html', '_shared.js'],
    }))
    expect(baseline.report.forbiddenReachability).toContainEqual(expect.objectContaining({
      dependency: 'fixture-module-binding-eager',
      scenario: 'authenticatedHome',
      enforcement: 'report-only',
      attribution: 'module-binding-eager',
    }))
    expect(baseline.report.violations).not.toContainEqual(expect.objectContaining({
      subject: 'authenticatedHome:fixture-module-binding-eager',
    }))
    expect(baseline.report.sharedDependencies).toContainEqual(expect.objectContaining({
      file: 'assets/shared.js',
      bindingEntries: ['secondary.html', 'src/module.ts'],
    }))
    expect(baseline.report.violations.map((violation: { code: string }) => violation.code)).toEqual(
      expect.arrayContaining([
        'BUILD_GRAPH_CHUNK_TOO_LARGE',
        'BUILD_GRAPH_FORBIDDEN_DEPENDENCY',
        'BUILD_GRAPH_SCENARIO_TOO_LARGE',
        'BUILD_GRAPH_BINDING_ENTRY_TOO_LARGE',
      ]),
    )

    const candidate = run('candidate')
    expect(candidate.execution.status, candidate.execution.stderr).toBe(1)
    expect(candidate.report.verdict).toBe('failed')
    expect(candidate.report.enforcement).toBe('enforced')
    expect(candidate.report.violations).toEqual(baseline.report.violations)
  })

  it('fails closed when the manifest contains an unresolved dynamic edge', () => {
    const outputRoot = mkdtempSync(join(tmpdir(), 'newp-build-graph-'))
    temporaryRoots.push(outputRoot)
    const resultPath = join(outputRoot, 'invalid.json')

    const execution = spawnSync(process.execPath, [
      analyzerPath,
      '--manifest', join(fixtureRoot, 'manifest-missing-edge.json'),
      '--config', join(fixtureRoot, 'config-missing-edge.json'),
      '--assets-root', fixtureRoot,
      '--mode', 'candidate',
      '--output', resultPath,
    ], {
      cwd: repositoryRoot,
      encoding: 'utf8',
    })

    expect(execution.status).toBe(2)
    expect(JSON.parse(readFileSync(resultPath, 'utf8'))).toEqual(expect.objectContaining({
      verdict: 'failed',
      failureCode: 'BUILD_GRAPH_INPUT_INVALID',
      diagnostic: expect.stringContaining("missing-route.ts"),
    }))
  })

  it('grants a larger chunk budget only to one source-attributed target-owned chunk', () => {
    const outputRoot = mkdtempSync(join(tmpdir(), 'newp-build-graph-'))
    temporaryRoots.push(outputRoot)
    const proTableSource = '../../node_modules/.pnpm/@ant-design+pro-table@3.21.0/node_modules/@ant-design/pro-table/es/index.js'
    const forbiddenSource = '../../modules/platform/organization/src/pages/LegacyPage.tsx'

    const runCase = (
      name: string,
      mutate?: (input: {
        root: string
        manifest: Record<string, MutableManifestEntry>
        config: MutableAnalyzerConfig
        sourceMap: MutableSourceMap
      }) => void,
    ) => {
      const root = join(outputRoot, name)
      cpSync(fixtureRoot, root, { recursive: true })
      const manifest = JSON.parse(readFileSync(join(root, 'manifest.json'), 'utf8')) as Record<string, MutableManifestEntry>
      const config = JSON.parse(readFileSync(join(root, 'config.json'), 'utf8')) as MutableAnalyzerConfig
      const sourceMap = {
        version: 3,
        file: 'pro-table.js',
        sources: [proTableSource],
        names: [],
        mappings: '',
      }
      writeFileSync(join(root, 'assets', 'pro-table.js'), `export const proTableRuntime = '${'x'.repeat(64)}'\n`, 'utf8')
      writeFileSync(join(root, 'assets', 'pro-table.js.map'), `${JSON.stringify(sourceMap)}\n`, 'utf8')
      manifest['_pro-table.js'] = {
        file: 'assets/pro-table.js',
        imports: [],
      }
      manifest['src/personnel.ts'].imports.push('_pro-table.js')
      config.budgets.maxChunkMinifiedBytes = 40
      config.budgets.chunkOverrides = [{
        id: 'standard-list-table-runtime',
        scenario: 'personnelRoute',
        targetRoots: [{
          dynamicImportsOf: { sourceRegex: '(?:^|/)modules/(?:[^/]+/)+src/index\\.ts$' },
          match: { sourceRegex: '(?:^|/)modules/(?:[^/]+/)+src/standard-crud/[^/]+StandardCrudResource\\.tsx$' },
        }],
        sourceContains: '/node_modules/.pnpm/@ant-design+pro-table@',
        forbiddenSources: [{ sourceRegex: '(?:^|/)modules/(?:[^/]+/)+src/pages/' }],
        maxMinifiedBytes: 200,
      }]
      mutate?.({ root, manifest, config, sourceMap })
      writeFileSync(join(root, 'manifest.json'), `${JSON.stringify(manifest, null, 2)}\n`, 'utf8')
      writeFileSync(join(root, 'config.json'), `${JSON.stringify(config, null, 2)}\n`, 'utf8')
      writeFileSync(join(root, 'assets', 'pro-table.js.map'), `${JSON.stringify(sourceMap)}\n`, 'utf8')
      const resultPath = join(root, 'result.json')
      const execution = spawnSync(process.execPath, [
        analyzerPath,
        '--manifest', join(root, 'manifest.json'),
        '--config', join(root, 'config.json'),
        '--assets-root', root,
        '--mode', 'candidate',
        '--output', resultPath,
      ], {
        cwd: repositoryRoot,
        encoding: 'utf8',
      })
      return { execution, report: JSON.parse(readFileSync(resultPath, 'utf8')) }
    }

    const accepted = runCase('accepted')
    expect(accepted.execution.status, accepted.execution.stderr).toBe(0)
    expect(accepted.report.verdict).toBe('passed')
    expect(accepted.report.chunkBudgetOverrides).toEqual([expect.objectContaining({
      id: 'standard-list-table-runtime',
      file: 'assets/pro-table.js',
      scenario: 'personnelRoute',
      limitBytes: 200,
      source: proTableSource,
      chain: ['src/personnel.ts', '_pro-table.js'],
    })])

    const zeroMatches = runCase('zero-matches', ({ config }) => {
      config.budgets.chunkOverrides[0].sourceContains = '/not-emitted/'
    })
    expect(zeroMatches.execution.status).toBe(2)
    expect(zeroMatches.report.diagnostic).toContain('matched 0 chunks')

    const multipleMatches = runCase('multiple-matches', ({ root, manifest }) => {
      copyFileSync(join(root, 'assets', 'pro-table.js'), join(root, 'assets', 'pro-table-copy.js'))
      copyFileSync(join(root, 'assets', 'pro-table.js.map'), join(root, 'assets', 'pro-table-copy.js.map'))
      manifest['_pro-table-copy.js'] = { file: 'assets/pro-table-copy.js', imports: [] }
      manifest['src/personnel.ts'].imports.push('_pro-table-copy.js')
    })
    expect(multipleMatches.execution.status).toBe(2)
    expect(multipleMatches.report.diagnostic).toContain('matched 2 chunks')

    const outsideTarget = runCase('outside-target', ({ manifest, config }) => {
      manifest['src/personnel.ts'].imports = manifest['src/personnel.ts'].imports.filter((key: string) => key !== '_pro-table.js')
      manifest['src/detail.ts'].imports.push('_pro-table.js')
      config.scenarios.personnelRoute.roots.push('src/detail.ts')
    })
    expect(outsideTarget.execution.status).toBe(2)
    expect(outsideTarget.report.diagnostic).toContain('is not in the target static closure')

    const forbiddenMix = runCase('forbidden-mix', ({ sourceMap }) => {
      sourceMap.sources.push(forbiddenSource)
    })
    expect(forbiddenMix.execution.status).toBe(2)
    expect(forbiddenMix.report.diagnostic).toContain(forbiddenSource)
  })
})
