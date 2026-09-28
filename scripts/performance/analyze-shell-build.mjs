import { gzipSync } from 'node:zlib'
import { dirname, isAbsolute, resolve } from 'node:path'
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs'

const FAILURE_CODES = {
  invalidInput: 'BUILD_GRAPH_INPUT_INVALID',
  budgetExceeded: 'BUILD_GRAPH_BUDGET_EXCEEDED',
}

function parseArguments(arguments_) {
  const values = new Map()
  for (let index = 0; index < arguments_.length; index += 2) {
    const name = arguments_[index]
    const value = arguments_[index + 1]
    if (!name?.startsWith('--') || value === undefined) {
      throw new Error('Arguments must use --name value pairs.')
    }
    values.set(name, value)
  }

  for (const required of ['--manifest', '--config', '--assets-root', '--mode', '--output']) {
    if (!values.has(required)) {
      throw new Error(`Missing required argument ${required}.`)
    }
  }
  const mode = values.get('--mode')
  if (mode !== 'baseline' && mode !== 'candidate') {
    throw new Error('--mode must be baseline or candidate.')
  }

  return {
    manifestPath: resolve(values.get('--manifest')),
    configPath: resolve(values.get('--config')),
    assetsRoot: resolve(values.get('--assets-root')),
    mode,
    outputPath: resolve(values.get('--output')),
  }
}

function readJson(path, label) {
  try {
    return JSON.parse(readFileSync(path, 'utf8'))
  } catch (error) {
    throw new Error(`${label} could not be read: ${error.message}`)
  }
}

function resolveContainedFile(root, relativePath) {
  if (typeof relativePath !== 'string' || relativePath.length === 0 || isAbsolute(relativePath)) {
    throw new Error(`Invalid emitted file path '${relativePath}'.`)
  }
  const fullPath = resolve(root, relativePath)
  const rootWithSeparator = `${resolve(root)}\\`
  if (!fullPath.toLowerCase().startsWith(rootWithSeparator.toLowerCase())) {
    throw new Error(`Emitted file path escaped the assets root: ${relativePath}`)
  }
  return fullPath
}

function loadChunkMetrics(manifest, assetsRoot) {
  const metrics = new Map()
  for (const [key, entry] of Object.entries(manifest)) {
    if (!entry || typeof entry.file !== 'string') {
      throw new Error(`Manifest entry '${key}' does not declare a file.`)
    }
    // Vite 可把纯 CSS 产物放在 .js manifest key 下，资产类型以 file 为准。
    if (entry.file.toLowerCase().endsWith('.css')) continue
    const filePath = resolveContainedFile(assetsRoot, entry.file)
    let bytes
    try {
      bytes = readFileSync(filePath)
    } catch (error) {
      throw new Error(`Emitted file '${entry.file}' could not be read: ${error.message}`)
    }
    const sourceMapPath = `${filePath}.map`
    const sourceMap = readJson(sourceMapPath, `Source map for '${entry.file}'`)
    if (!Array.isArray(sourceMap.sources)) {
      throw new Error(`Source map for '${entry.file}' does not declare sources.`)
    }
    metrics.set(key, {
      key,
      file: entry.file.replaceAll('\\', '/'),
      minifiedBytes: bytes.byteLength,
      gzipBytes: gzipSync(bytes, { level: 9 }).byteLength,
      sources: sourceMap.sources.map((source) => String(source).replaceAll('\\', '/')),
    })
  }
  return metrics
}

function validateGraphEdges(manifest) {
  for (const [key, entry] of Object.entries(manifest)) {
    for (const edgeName of ['imports', 'dynamicImports']) {
      const edges = entry[edgeName] ?? []
      if (!Array.isArray(edges)) {
        throw new Error(`Manifest entry '${key}' ${edgeName} must be an array.`)
      }
      for (const target of edges) {
        if (typeof target !== 'string' || !Object.hasOwn(manifest, target)) {
          throw new Error(`Manifest entry '${key}' ${edgeName} references missing entry '${target}'.`)
        }
      }
    }
  }
}

function cssFilesForEntry(entry) {
  const references = entry.css === undefined ? [] : entry.css
  if (!Array.isArray(references)) {
    throw new Error(`Manifest entry '${entry.file}' css must be an array.`)
  }
  const files = entry.file.toLowerCase().endsWith('.css') ? [entry.file, ...references] : references
  return files.map(file => {
    if (typeof file !== 'string') throw new Error(`Invalid emitted CSS file path '${file}'.`)
    return file.replaceAll('\\', '/')
  })
}

function loadCssMetrics(manifest, assetsRoot) {
  const metrics = new Map()
  const cssFiles = new Set(
    Object.values(manifest).flatMap(cssFilesForEntry),
  )
  for (const file of [...cssFiles].sort()) {
    const normalizedFile = file.replaceAll('\\', '/')
    const filePath = resolveContainedFile(assetsRoot, normalizedFile)
    let bytes
    try {
      bytes = readFileSync(filePath)
    } catch (error) {
      throw new Error(`Emitted CSS file '${normalizedFile}' could not be read: ${error.message}`)
    }
    metrics.set(normalizedFile, {
      file: normalizedFile,
      minifiedBytes: bytes.byteLength,
      gzipBytes: gzipSync(bytes, { level: 9 }).byteLength,
    })
  }
  return metrics
}

function collectStaticClosure(manifest, roots) {
  const visited = new Set()
  const visit = (key) => {
    if (visited.has(key)) {
      return
    }
    const entry = manifest[key]
    if (!entry) {
      throw new Error(`Manifest entry '${key}' was not found.`)
    }
    visited.add(key)
    for (const dependency of entry.imports ?? []) {
      visit(dependency)
    }
  }
  for (const root of roots) {
    visit(root)
  }
  return visited
}

function resolveSelectors(selectors, metrics, manifest, label) {
  const resolvedKeys = []
  for (const selector of selectors) {
    if (typeof selector === 'string') {
      if (!metrics.has(selector)) {
        throw new Error(`${label} references unknown manifest entry '${selector}'.`)
      }
      resolvedKeys.push(selector)
      continue
    }
    if (!selector || typeof selector !== 'object') {
      throw new Error(`${label} contains an invalid selector.`)
    }
    if (selector.dynamicImportsOf) {
      const loaderKeys = resolveSelectors(
        [selector.dynamicImportsOf],
        metrics,
        manifest,
        `${label} dynamicImportsOf`,
      )
      const candidates = new Set(loaderKeys.flatMap((key) => manifest[key].dynamicImports ?? []))
      if (selector.match) {
        const matchingKeys = new Set(resolveSelectors(
          [selector.match],
          metrics,
          manifest,
          `${label} match`,
        ))
        for (const key of [...candidates]) {
          if (!matchingKeys.has(key)) candidates.delete(key)
        }
      }
      if (candidates.size === 0) {
        throw new Error(`${label} dynamicImportsOf selector matched no emitted dynamic import.`)
      }
      resolvedKeys.push(...[...candidates].sort())
      continue
    }
    if (typeof selector.manifestKey === 'string') {
      if (!metrics.has(selector.manifestKey)) {
        throw new Error(`${label} references unknown manifest entry '${selector.manifestKey}'.`)
      }
      resolvedKeys.push(selector.manifestKey)
      continue
    }
    if (typeof selector.sourceContains === 'string' && selector.sourceContains.length > 0) {
      const matches = [...metrics.values()]
        .filter((chunk) => chunk.sources.some((source) => source.includes(selector.sourceContains)))
        .map((chunk) => chunk.key)
        .sort()
      if (matches.length === 0) {
        throw new Error(`${label} source selector '${selector.sourceContains}' matched no emitted chunk.`)
      }
      resolvedKeys.push(...matches)
      continue
    }
    if (typeof selector.sourceRegex === 'string' && selector.sourceRegex.length > 0) {
      let pattern
      try {
        pattern = new RegExp(selector.sourceRegex)
      } catch (error) {
        throw new Error(`${label} source regex '${selector.sourceRegex}' is invalid: ${error.message}`)
      }
      const matches = [...metrics.values()]
        .filter((chunk) => chunk.sources.some((source) => pattern.test(source)))
        .map((chunk) => chunk.key)
        .sort()
      if (matches.length === 0) {
        throw new Error(`${label} source regex '${selector.sourceRegex}' matched no emitted chunk.`)
      }
      resolvedKeys.push(...matches)
      continue
    }
    throw new Error(`${label} selector must declare manifestKey, sourceContains, sourceRegex, or dynamicImportsOf.`)
  }
  return [...new Set(resolvedKeys)]
}

function findStaticPath(manifest, roots, target) {
  const queue = roots.map((root) => [root])
  const visited = new Set()
  while (queue.length > 0) {
    const path = queue.shift()
    const current = path.at(-1)
    if (current === target) {
      return path
    }
    if (visited.has(current)) {
      continue
    }
    visited.add(current)
    for (const dependency of manifest[current].imports ?? []) {
      queue.push([...path, dependency])
    }
  }
  return []
}

function summarize(keys, metrics) {
  const chunks = [...keys].filter(key => metrics.has(key)).map((key) => metrics.get(key))
  return {
    minifiedBytes: chunks.reduce((total, chunk) => total + chunk.minifiedBytes, 0),
    gzipBytes: chunks.reduce((total, chunk) => total + chunk.gzipBytes, 0),
    files: chunks.map((chunk) => chunk.file).sort(),
  }
}

function cssKeysForChunks(keys, manifest) {
  return new Set([...keys].flatMap((key) => cssFilesForEntry(manifest[key])))
}

function summarizeCss(keys, metrics) {
  const assets = [...keys].map((key) => metrics.get(key))
  return {
    minifiedBytes: assets.reduce((total, asset) => total + asset.minifiedBytes, 0),
    gzipBytes: assets.reduce((total, asset) => total + asset.gzipBytes, 0),
    files: assets.map((asset) => asset.file).sort(),
  }
}

function compileSourceMatcher(definition, label) {
  const hasContains = typeof definition?.sourceContains === 'string' && definition.sourceContains.length > 0
  const hasRegex = typeof definition?.sourceRegex === 'string' && definition.sourceRegex.length > 0
  if (hasContains === hasRegex) {
    throw new Error(`${label} must declare exactly one of sourceContains or sourceRegex.`)
  }
  if (hasContains) {
    return (source) => source.includes(definition.sourceContains)
  }

  let pattern
  try {
    pattern = new RegExp(definition.sourceRegex)
  } catch (error) {
    throw new Error(`${label} source regex '${definition.sourceRegex}' is invalid: ${error.message}`)
  }
  return (source) => pattern.test(source)
}

function resolveChunkBudgetOverrides({ definitions, defaultLimitBytes, metrics, manifest, scenarioClosures }) {
  if (!Array.isArray(definitions)) {
    throw new Error('Chunk budget overrides must be an array.')
  }

  const overrideByChunkKey = new Map()
  const report = []
  const ids = new Set()
  for (const definition of definitions) {
    const id = definition?.id
    if (typeof id !== 'string' || id.length === 0 || ids.has(id)) {
      throw new Error('Chunk budget overrides must declare unique non-empty ids.')
    }
    ids.add(id)
    const scenario = definition.scenario
    const scenarioClosure = scenarioClosures.get(scenario)
    if (!scenarioClosure) {
      throw new Error(`Chunk budget override '${id}' references unknown scenario '${scenario}'.`)
    }
    if (!Array.isArray(definition.targetRoots) || definition.targetRoots.length === 0) {
      throw new Error(`Chunk budget override '${id}' must declare targetRoots.`)
    }
    const targetRoots = resolveSelectors(
      definition.targetRoots,
      metrics,
      manifest,
      `Chunk budget override '${id}' targetRoots`,
    )
    if (targetRoots.length !== 1) {
      throw new Error(`Chunk budget override '${id}' targetRoots matched ${targetRoots.length} chunks; exactly 1 is required.`)
    }
    const limitBytes = definition.maxMinifiedBytes
    if (!Number.isInteger(limitBytes) || limitBytes <= defaultLimitBytes || limitBytes > 550_000) {
      throw new Error(
        `Chunk budget override '${id}' maxMinifiedBytes must be an integer greater than ${defaultLimitBytes} and no greater than 550000.`,
      )
    }

    const sourceMatches = compileSourceMatcher(definition, `Chunk budget override '${id}'`)
    const matchingChunks = [...metrics.values()].filter((chunk) => chunk.sources.some(sourceMatches))
    if (matchingChunks.length !== 1) {
      throw new Error(`Chunk budget override '${id}' source matcher matched ${matchingChunks.length} chunks; exactly 1 is required.`)
    }
    const chunk = matchingChunks[0]
    if (!scenarioClosure.has(chunk.key)) {
      throw new Error(`Chunk budget override '${id}' chunk '${chunk.file}' is not in scenario '${scenario}' static closure.`)
    }
    const targetClosure = collectStaticClosure(manifest, targetRoots)
    if (!targetClosure.has(chunk.key)) {
      throw new Error(`Chunk budget override '${id}' chunk '${chunk.file}' is not in the target static closure.`)
    }
    if (!Array.isArray(definition.forbiddenSources) || definition.forbiddenSources.length === 0) {
      throw new Error(`Chunk budget override '${id}' must declare forbiddenSources.`)
    }
    for (const [index, forbidden] of definition.forbiddenSources.entries()) {
      const isForbidden = compileSourceMatcher(
        forbidden,
        `Chunk budget override '${id}' forbiddenSources[${index}]`,
      )
      const forbiddenSource = chunk.sources.find(isForbidden)
      if (forbiddenSource) {
        throw new Error(`Chunk budget override '${id}' chunk '${chunk.file}' contains forbidden source '${forbiddenSource}'.`)
      }
    }
    if (overrideByChunkKey.has(chunk.key)) {
      throw new Error(`Chunk '${chunk.file}' is claimed by multiple chunk budget overrides.`)
    }

    const matchingSource = chunk.sources.find(sourceMatches)
    const resolved = {
      id,
      scenario,
      targetRoots,
      key: chunk.key,
      file: chunk.file,
      source: matchingSource,
      chain: findStaticPath(manifest, targetRoots, chunk.key),
      defaultLimitBytes,
      limitBytes,
    }
    overrideByChunkKey.set(chunk.key, resolved)
    report.push(resolved)
  }

  return { overrideByChunkKey, report }
}

function analyze(manifest, config, assetsRoot, mode) {
  if (config.schemaVersion !== '1.0' || !config.scenarios || typeof config.scenarios !== 'object') {
    throw new Error('Config must declare schemaVersion 1.0 and scenarios.')
  }
  validateGraphEdges(manifest)
  const metrics = loadChunkMetrics(manifest, assetsRoot)
  const cssMetrics = loadCssMetrics(manifest, assetsRoot)
  const scenarioClosures = new Map()
  const scenarioRoots = new Map()
  const scenarios = {}

  for (const [name, definition] of Object.entries(config.scenarios)) {
    if (!Array.isArray(definition.roots) || definition.roots.length === 0) {
      throw new Error(`Scenario '${name}' must declare at least one root.`)
    }
    const roots = resolveSelectors(definition.roots, metrics, manifest, `Scenario '${name}'`)
    const closure = collectStaticClosure(manifest, roots)
    scenarioRoots.set(name, roots)
    scenarioClosures.set(name, closure)
    scenarios[name] = {
      ...summarize(closure, metrics),
      css: summarizeCss(cssKeysForChunks(closure, manifest), cssMetrics),
    }
  }

  for (const [name, definition] of Object.entries(config.scenarios)) {
    if (!definition.relativeTo) {
      continue
    }
    const baseline = scenarioClosures.get(definition.relativeTo)
    if (!baseline) {
      throw new Error(`Scenario '${name}' references unknown baseline '${definition.relativeTo}'.`)
    }
    const delta = new Set([...scenarioClosures.get(name)].filter((key) => !baseline.has(key)))
    const scenarioCss = cssKeysForChunks(scenarioClosures.get(name), manifest)
    const baselineCss = cssKeysForChunks(baseline, manifest)
    const deltaCss = new Set([...scenarioCss].filter((file) => !baselineCss.has(file)))
    scenarios[name].delta = {
      ...summarize(delta, metrics),
      css: summarizeCss(deltaCss, cssMetrics),
    }
  }

  const bindingKeys = resolveSelectors(config.bindingEntries ?? [], metrics, manifest, 'Binding entries')
  const targetCapabilityAssets = [...new Set(resolveSelectors(
    config.targetCapabilityAssets ?? [],
    metrics,
    manifest,
    'Target capability assets',
  ).map((key) => metrics.get(key).file))].sort()
  const bindingEntries = bindingKeys.map((key) => ({
    key,
    file: metrics.get(key).file,
    minifiedBytes: metrics.get(key).minifiedBytes,
    gzipBytes: metrics.get(key).gzipBytes,
  }))
  const bindingClosures = bindingKeys.map((key) => ({
    key,
    closure: collectStaticClosure(manifest, [key]),
  }))
  const sharedDependencies = [...metrics.keys()]
    .map((key) => ({
      file: metrics.get(key).file,
      bindingEntries: bindingClosures
        .filter((binding) => binding.closure.has(key))
        .map((binding) => binding.key),
    }))
    .filter((item) => item.bindingEntries.length > 1)
    .sort((left, right) => left.file.localeCompare(right.file, 'en'))

  const forbiddenReachability = []
  for (const dependency of config.forbiddenDependencies ?? []) {
    if (!dependency?.id || !Array.isArray(dependency.scenarios)
      || (!dependency.sourceContains && !dependency.sourceRegex)) {
      throw new Error('Forbidden dependency rules must declare id, a source matcher, and scenarios.')
    }
    const enforcement = dependency.enforcement ?? 'enforced'
    if (enforcement !== 'enforced' && enforcement !== 'report-only') {
      throw new Error(`Forbidden dependency '${dependency.id}' enforcement must be enforced or report-only.`)
    }
    const attribution = dependency.attribution ?? 'unclassified'
    if (typeof attribution !== 'string' || attribution.length === 0) {
      throw new Error(`Forbidden dependency '${dependency.id}' attribution must be a non-empty string.`)
    }
    let sourcePattern = null
    if (dependency.sourceRegex) {
      try {
        sourcePattern = new RegExp(dependency.sourceRegex)
      } catch (error) {
        throw new Error(`Forbidden dependency '${dependency.id}' source regex is invalid: ${error.message}`)
      }
    }
    for (const scenarioName of dependency.scenarios) {
      const closure = scenarioClosures.get(scenarioName)
      if (!closure) {
        throw new Error(`Forbidden dependency '${dependency.id}' references unknown scenario '${scenarioName}'.`)
      }
      for (const key of closure) {
        const chunk = metrics.get(key)
        if (!chunk) continue
        const matchingSource = chunk.sources.find((source) =>
          dependency.sourceContains ? source.includes(dependency.sourceContains) : sourcePattern.test(source))
        if (!matchingSource) {
          continue
        }
        forbiddenReachability.push({
          dependency: dependency.id,
          scenario: scenarioName,
          enforcement,
          attribution,
          source: matchingSource,
          file: metrics.get(key).file,
          chain: findStaticPath(manifest, scenarioRoots.get(scenarioName), key),
        })
      }
    }
  }
  forbiddenReachability.sort((left, right) =>
    `${left.scenario}:${left.dependency}:${left.file}`.localeCompare(
      `${right.scenario}:${right.dependency}:${right.file}`,
      'en',
    ))

  const violations = []
  const budgets = config.budgets ?? {}
  const maxChunkBytes = Math.min(budgets.maxChunkMinifiedBytes ?? 500_000, 500_000)
  const chunkBudgetOverrides = resolveChunkBudgetOverrides({
    definitions: budgets.chunkOverrides ?? [],
    defaultLimitBytes: maxChunkBytes,
    metrics,
    manifest,
    scenarioClosures,
  })
  for (const chunk of [...metrics.values()].sort((left, right) => left.file.localeCompare(right.file, 'en'))) {
    const limitBytes = chunkBudgetOverrides.overrideByChunkKey.get(chunk.key)?.limitBytes ?? maxChunkBytes
    if (chunk.minifiedBytes > limitBytes) {
      violations.push({
        code: 'BUILD_GRAPH_CHUNK_TOO_LARGE',
        subject: chunk.file,
        actualBytes: chunk.minifiedBytes,
        limitBytes,
      })
    }
  }
  for (const [scenarioName, scenarioBudget] of Object.entries(budgets.scenarios ?? {})) {
    const scenario = scenarios[scenarioName]
    if (!scenario) {
      throw new Error(`Budget references unknown scenario '${scenarioName}'.`)
    }
    for (const [field, limitField] of [
      ['minifiedBytes', 'maxMinifiedBytes'],
      ['gzipBytes', 'maxGzipBytes'],
    ]) {
      const limit = scenarioBudget[limitField]
      if (typeof limit === 'number' && scenario[field] > limit) {
        violations.push({
          code: 'BUILD_GRAPH_SCENARIO_TOO_LARGE',
          subject: `${scenarioName}.${field}`,
          actualBytes: scenario[field],
          limitBytes: limit,
        })
      }
    }
    if (typeof scenarioBudget.maxDeltaGzipBytes === 'number') {
      if (!scenario.delta) {
        throw new Error(`Scenario '${scenarioName}' has a delta budget but no relativeTo baseline.`)
      }
      if (scenario.delta.gzipBytes > scenarioBudget.maxDeltaGzipBytes) {
        violations.push({
          code: 'BUILD_GRAPH_SCENARIO_TOO_LARGE',
          subject: `${scenarioName}.delta.gzipBytes`,
          actualBytes: scenario.delta.gzipBytes,
          limitBytes: scenarioBudget.maxDeltaGzipBytes,
        })
      }
    }
  }
  for (const binding of bindingEntries) {
    if (typeof budgets.maxBindingEntryGzipBytes === 'number'
      && binding.gzipBytes > budgets.maxBindingEntryGzipBytes) {
      violations.push({
        code: 'BUILD_GRAPH_BINDING_ENTRY_TOO_LARGE',
        subject: binding.key,
        actualBytes: binding.gzipBytes,
        limitBytes: budgets.maxBindingEntryGzipBytes,
      })
    }
  }
  const allBindingEntriesGzipBytes = bindingEntries.reduce((total, binding) => total + binding.gzipBytes, 0)
  if (typeof budgets.maxAllBindingEntriesGzipBytes === 'number'
    && allBindingEntriesGzipBytes > budgets.maxAllBindingEntriesGzipBytes) {
    violations.push({
      code: 'BUILD_GRAPH_BINDING_ENTRIES_TOO_LARGE',
      subject: 'all-binding-entries',
      actualBytes: allBindingEntriesGzipBytes,
      limitBytes: budgets.maxAllBindingEntriesGzipBytes,
    })
  }
  for (const reachable of forbiddenReachability) {
    if (reachable.enforcement === 'report-only') {
      continue
    }
    violations.push({
      code: 'BUILD_GRAPH_FORBIDDEN_DEPENDENCY',
      subject: `${reachable.scenario}:${reachable.dependency}`,
      file: reachable.file,
      source: reachable.source,
      chain: reachable.chain,
    })
  }
  violations.sort((left, right) =>
    `${left.code}:${left.subject}:${left.file ?? ''}`.localeCompare(
      `${right.code}:${right.subject}:${right.file ?? ''}`,
      'en',
    ))

  const passed = violations.length === 0

  return {
    schemaVersion: '1.0',
    mode,
    enforcement: mode === 'candidate' ? 'enforced' : 'record-only',
    verdict: passed ? 'passed' : 'failed',
    failureCode: passed ? null : FAILURE_CODES.budgetExceeded,
    scenarios,
    chunks: [...metrics.values()].map(({ key, file, minifiedBytes, gzipBytes, sources }) => ({
      key,
      file,
      minifiedBytes,
      gzipBytes,
      sources,
    })),
    emittedCss: [...cssMetrics.values()],
    targetCapabilityAssets,
    bindingEntries,
    allBindingEntriesGzipBytes,
    chunkBudgetOverrides: chunkBudgetOverrides.report,
    sharedDependencies,
    forbiddenReachability,
    violations,
  }
}

function writeReport(path, report) {
  mkdirSync(dirname(path), { recursive: true })
  writeFileSync(path, `${JSON.stringify(report, null, 2)}\n`, 'utf8')
}

let options
try {
  options = parseArguments(process.argv.slice(2))
  const manifest = readJson(options.manifestPath, 'Manifest')
  const config = readJson(options.configPath, 'Config')
  const report = analyze(manifest, config, options.assetsRoot, options.mode)
  writeReport(options.outputPath, report)
  console.log(`BUILD_GRAPH_RESULT: ${options.outputPath}`)
  if (options.mode === 'candidate' && report.verdict !== 'passed') {
    process.exitCode = 1
  }
} catch (error) {
  const report = {
    schemaVersion: '1.0',
    mode: options?.mode ?? null,
    verdict: 'failed',
    failureCode: FAILURE_CODES.invalidInput,
    diagnostic: error instanceof Error ? error.message : String(error),
  }
  if (options?.outputPath) {
    writeReport(options.outputPath, report)
    console.error(`BUILD_GRAPH_RESULT: ${options.outputPath}`)
  }
  console.error(`${FAILURE_CODES.invalidInput}: ${report.diagnostic}`)
  process.exitCode = 2
}
