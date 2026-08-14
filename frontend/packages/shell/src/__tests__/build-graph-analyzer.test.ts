// @vitest-environment node

import { mkdtempSync, readFileSync, rmSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { spawnSync } from 'node:child_process'
import { afterEach, describe, expect, it } from 'vitest'

const repositoryRoot = fileURLToPath(new URL('../../../../../', import.meta.url))
const analyzerPath = join(repositoryRoot, 'scripts', 'performance', 'analyze-shell-build.mjs')
const fixtureRoot = join(repositoryRoot, 'scripts', 'performance', 'fixtures', 'build-graph-valid')
const temporaryRoots: string[] = []

afterEach(() => {
  for (const root of temporaryRoots.splice(0)) {
    rmSync(root, { recursive: true, force: true })
  }
})

describe('Shell production build graph CLI', () => {
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
})
