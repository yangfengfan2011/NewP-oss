import { createReadStream, existsSync, mkdirSync, statSync, writeFileSync } from 'node:fs'
import { createServer, request as httpRequest } from 'node:http'
import { request as httpsRequest } from 'node:https'
import { dirname, extname, isAbsolute, resolve, sep } from 'node:path'

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
  for (const required of ['--assets-root', '--host-origin', '--port', '--run-id', '--ready-file']) {
    if (!values.has(required)) {
      throw new Error(`Missing required argument ${required}.`)
    }
  }
  const port = Number.parseInt(values.get('--port'), 10)
  if (!Number.isInteger(port) || port < 0 || port > 65535) {
    throw new Error('--port must be between 0 and 65535.')
  }
  const assetsRoot = resolve(values.get('--assets-root'))
  const readyFile = resolve(values.get('--ready-file'))
  if (!existsSync(resolve(assetsRoot, 'index.html'))) {
    throw new Error('Production assets root must contain index.html.')
  }
  return {
    assetsRoot,
    hostOrigin: new URL(values.get('--host-origin')),
    port,
    runId: values.get('--run-id'),
    readyFile,
  }
}

function isProxyPath(pathname) {
  return pathname === '/api'
    || pathname.startsWith('/api/')
    || /^\/plugins\/(?:active-module-runtime|frontend-plugin-manifest|localization-manifest|metadata-runtime-manifest)\.json$/.test(pathname)
    || pathname === '/plugins/runtime'
    || pathname.startsWith('/plugins/runtime/')
}

function contentType(path) {
  return {
    '.css': 'text/css; charset=utf-8',
    '.html': 'text/html; charset=utf-8',
    '.js': 'text/javascript; charset=utf-8',
    '.json': 'application/json; charset=utf-8',
    '.map': 'application/json; charset=utf-8',
    '.svg': 'image/svg+xml',
    '.png': 'image/png',
    '.jpg': 'image/jpeg',
    '.jpeg': 'image/jpeg',
    '.woff': 'font/woff',
    '.woff2': 'font/woff2',
  }[extname(path).toLowerCase()] ?? 'application/octet-stream'
}

function resolveStaticFile(assetsRoot, pathname) {
  let decoded
  try {
    decoded = decodeURIComponent(pathname)
  } catch {
    return null
  }
  const relative = decoded.replace(/^\/+/, '')
  if (isAbsolute(relative)) {
    return null
  }
  const candidate = resolve(assetsRoot, relative)
  if (!candidate.toLowerCase().startsWith(`${assetsRoot}${sep}`.toLowerCase())) {
    return null
  }
  return existsSync(candidate) && statSync(candidate).isFile() ? candidate : null
}

function proxy(request, response, options) {
  const target = new URL(request.url, options.hostOrigin)
  const send = target.protocol === 'https:' ? httpsRequest : httpRequest
  const headers = { ...request.headers, host: target.host }
  delete headers.connection
  const upstream = send(target, {
    method: request.method,
    headers,
  }, (upstreamResponse) => {
    const responseHeaders = { ...upstreamResponse.headers }
    delete responseHeaders.connection
    response.writeHead(upstreamResponse.statusCode ?? 502, responseHeaders)
    upstreamResponse.pipe(response)
  })
  upstream.on('error', () => {
    if (!response.headersSent) {
      response.writeHead(502, { 'content-type': 'application/json; charset=utf-8' })
    }
    response.end(JSON.stringify({ error: 'PRODUCTION_SHELL_UPSTREAM_UNAVAILABLE' }))
  })
  request.pipe(upstream)
}

function serveFile(request, response, path, immutable) {
  response.setHeader('content-type', contentType(path))
  response.setHeader('cache-control', immutable
    ? 'public, max-age=31536000, immutable'
    : 'no-store')
  if (request.method === 'HEAD') {
    response.writeHead(200)
    response.end()
    return
  }
  response.writeHead(200)
  createReadStream(path).pipe(response)
}

const options = parseArguments(process.argv.slice(2))
const server = createServer((request, response) => {
  const requestUrl = new URL(request.url ?? '/', 'http://127.0.0.1')
  if (requestUrl.pathname === '/__newp/performance/ready') {
    response.writeHead(200, {
      'content-type': 'application/json; charset=utf-8',
      'cache-control': 'no-store',
    })
    response.end(JSON.stringify({ runId: options.runId }))
    return
  }
  if (isProxyPath(requestUrl.pathname)) {
    proxy(request, response, options)
    return
  }
  if (request.method !== 'GET' && request.method !== 'HEAD') {
    response.writeHead(405, { allow: 'GET, HEAD' })
    response.end()
    return
  }
  const staticFile = resolveStaticFile(options.assetsRoot, requestUrl.pathname)
  if (staticFile) {
    serveFile(request, response, staticFile, requestUrl.pathname.startsWith('/assets/'))
    return
  }
  serveFile(request, response, resolve(options.assetsRoot, 'index.html'), false)
})

server.listen(options.port, '127.0.0.1', () => {
  const address = server.address()
  if (!address || typeof address === 'string') {
    throw new Error('Production Shell server did not expose a TCP port.')
  }
  mkdirSync(dirname(options.readyFile), { recursive: true })
  writeFileSync(options.readyFile, `${JSON.stringify({
    schemaVersion: '1.0',
    runId: options.runId,
    pid: process.pid,
    port: address.port,
  }, null, 2)}\n`, 'utf8')
  console.log(`PRODUCTION_SHELL_READY: ${address.port}`)
})

for (const signal of ['SIGINT', 'SIGTERM']) {
  process.on(signal, () => {
    server.close(() => process.exit(0))
  })
}
