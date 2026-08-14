// @vitest-environment node

import { spawn, type ChildProcess } from 'node:child_process'
import { createServer, type Server } from 'node:http'
import { mkdtempSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { afterEach, describe, expect, it } from 'vitest'

const repositoryRoot = fileURLToPath(new URL('../../../../../', import.meta.url))
const serverPath = join(repositoryRoot, 'scripts', 'performance', 'production-shell-server.mjs')
const temporaryRoots: string[] = []
const childProcesses: ChildProcess[] = []
const upstreamServers: Server[] = []

afterEach(async () => {
  for (const child of childProcesses.splice(0)) {
    if (child.exitCode === null) {
      child.kill()
      await new Promise((resolve) => child.once('exit', resolve))
    }
  }
  for (const server of upstreamServers.splice(0)) {
    await new Promise<void>((resolve, reject) => server.close((error) => error ? reject(error) : resolve()))
  }
  for (const root of temporaryRoots.splice(0)) {
    rmSync(root, { recursive: true, force: true })
  }
})

describe('production Shell static server CLI', () => {
  it('serves immutable assets, falls back to index.html, and proxies Host surfaces', async () => {
    const root = mkdtempSync(join(tmpdir(), 'newp-production-shell-'))
    temporaryRoots.push(root)
    mkdirSync(join(root, 'assets'))
    writeFileSync(join(root, 'index.html'), '<main>production shell</main>')
    writeFileSync(join(root, 'assets', 'app.js'), 'export const built = true')

    const upstream = createServer((request, response) => {
      response.setHeader('content-type', 'application/json')
      response.end(JSON.stringify({ path: request.url }))
    })
    upstreamServers.push(upstream)
    await new Promise<void>((resolve) => upstream.listen(0, '127.0.0.1', resolve))
    const upstreamAddress = upstream.address()
    if (!upstreamAddress || typeof upstreamAddress === 'string') {
      throw new Error('upstream server did not expose a TCP port')
    }

    const readyFile = join(root, 'ready.json')
    const runId = 'fixture-run'
    const child = spawn(process.execPath, [
      serverPath,
      '--assets-root', root,
      '--host-origin', `http://127.0.0.1:${upstreamAddress.port}`,
      '--port', '0',
      '--run-id', runId,
      '--ready-file', readyFile,
    ], {
      cwd: repositoryRoot,
      stdio: ['ignore', 'pipe', 'pipe'],
    })
    childProcesses.push(child)
    const ready = await waitForReadyFile(readyFile)
    const origin = `http://127.0.0.1:${ready.port}`

    const asset = await fetch(`${origin}/assets/app.js`)
    expect(await asset.text()).toBe('export const built = true')
    expect(asset.headers.get('cache-control')).toContain('immutable')

    const api = await fetch(`${origin}/api/example?value=1`)
    expect(await api.json()).toEqual({ path: '/api/example?value=1' })

    const route = await fetch(`${origin}/organization/persons`)
    expect(await route.text()).toBe('<main>production shell</main>')

    const identity = await fetch(`${origin}/__newp/performance/ready`)
    expect(await identity.json()).toEqual({ runId })
  })
})

async function waitForReadyFile(path: string): Promise<{ port: number }> {
  const deadline = Date.now() + 10_000
  while (Date.now() < deadline) {
    try {
      return JSON.parse(readFileSync(path, 'utf8'))
    } catch {
      await new Promise((resolve) => setTimeout(resolve, 25))
    }
  }
  throw new Error('production Shell server did not write readiness evidence')
}
