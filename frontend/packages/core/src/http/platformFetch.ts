import { StandardCrudRequestError } from '@icp-platform/sdk';

const WRITE_METHODS = new Set(['POST', 'PUT', 'PATCH', 'DELETE']);
const MUTATION_OUTCOME_UNKNOWN_CODE = 'Platform.MutationOutcomeUnknown';
const ANTIFORGERY_ENDPOINT = '/api/auth/antiforgery-token';
const ANTIFORGERY_HEADER = 'X-CSRF-TOKEN';
const ANTIFORGERY_FAILURE_CODE = 'ANTIFORGERY_TOKEN_INVALID';
const REQUEST_LOCALE_HEADER = 'X-ICP-Locale';

let cachedRequestToken: string | undefined;
let tokenGeneration = 0;
let requestTokenPromise: { generation: number; promise: Promise<string> } | undefined;

type PlatformRequestLocaleState = {
  provider?: () => string;
  accessRejectionHandler?: (isWrite: boolean) => Promise<void>;
};

type PlatformGlobal = typeof globalThis & {
  __icpPlatformRequestLocaleState__?: PlatformRequestLocaleState;
};

function requestLocaleState(): PlatformRequestLocaleState {
  const platformGlobal = globalThis as PlatformGlobal;
  platformGlobal.__icpPlatformRequestLocaleState__ ??= {};
  return platformGlobal.__icpPlatformRequestLocaleState__;
}

interface AntiforgeryTokenResponse {
  requestToken: string;
}

interface PlatformProblemDetails {
  code?: string;
}

/** Shell 只重取当前主体；响应仍由原调用方消费，禁止重放业务请求。 */
export function installPlatformAccessRejectionHandler(handler: (isWrite: boolean) => Promise<void>): () => void {
  requestLocaleState().accessRejectionHandler = handler;
  return () => {
    if (requestLocaleState().accessRejectionHandler === handler) requestLocaleState().accessRejectionHandler = undefined;
  };
}

async function observeAccessResponse(response: Response, path: string, isWrite = false): Promise<Response> {
  // 当前主体请求自身由认证边界处理，避免拒绝刷新形成递归。
  if ((response.status === 401 || response.status === 403) && path !== '/api/auth/current-user') {
    void notifyAccessRejection(isWrite);
  }
  return response;
}

async function notifyAccessRejection(isWrite: boolean): Promise<void> {
  // 刷新是拒绝后的独立同步；慢请求或通知故障不能延迟、覆盖已知的原拒绝，也不能形成未处理拒绝。
  try {
    await requestLocaleState().accessRejectionHandler?.(isWrite);
  } catch {
    console.error('当前权限刷新失败，原请求拒绝结果保持不变。');
  }
}

export function installPlatformRequestLocaleProvider(provider: () => string): () => void {
  requestLocaleState().provider = provider;
  return () => {
    const state = requestLocaleState();
    if (state.provider === provider) {
      state.provider = undefined;
    }
  };
}

export function invalidatePlatformAntiforgeryToken(): void {
  tokenGeneration += 1;
  cachedRequestToken = undefined;
  requestTokenPromise = undefined;
}

async function getRequestToken(): Promise<string> {
  if (cachedRequestToken) {
    return cachedRequestToken;
  }

  const generation = tokenGeneration;
  if (!requestTokenPromise || requestTokenPromise.generation !== generation) {
    let pending!: Promise<string>;
    // Promise 的 finally 闭包需要引用当前 Promise 本身，不能改成直接 const 初始化。
    // eslint-disable-next-line prefer-const
    pending = (async () => {
      const response = await fetch(ANTIFORGERY_ENDPOINT, {
        method: 'GET',
        credentials: 'include',
        cache: 'no-store',
        headers: { Accept: 'application/json' },
      });
      if (!response.ok) {
        throw new Error(`Unable to obtain antiforgery token (${response.status}).`);
      }

      const payload = await response.json() as AntiforgeryTokenResponse;
      if (!payload.requestToken) {
        throw new Error('Host returned an empty antiforgery token.');
      }

      if (tokenGeneration === generation) {
        cachedRequestToken = payload.requestToken;
      }
      return payload.requestToken;
    })().finally(() => {
      if (requestTokenPromise?.promise === pending) {
        requestTokenPromise = undefined;
      }
    });
    requestTokenPromise = { generation, promise: pending };
  }

  return requestTokenPromise.promise;
}

async function isPreBusinessAntiforgeryFailure(response: Response): Promise<boolean> {
  if (response.status !== 400) {
    return false;
  }

  const contentType = response.headers.get('Content-Type') ?? '';
  if (!contentType.toLowerCase().includes('application/problem+json')) {
    return false;
  }

  try {
    const problem = await response.clone().json() as PlatformProblemDetails;
    return problem.code === ANTIFORGERY_FAILURE_CODE;
  } catch {
    return false;
  }
}

export async function platformFetch(
  input: RequestInfo | URL,
  init?: RequestInit,
): Promise<Response> {
  const requestInput = typeof input === 'string'
    ? new URL(input, typeof window === 'undefined' ? 'http://localhost' : window.location.origin)
    : input;
  let source = new Request(requestInput, init);
  const requestLocale = requestLocaleState().provider?.();
  if (requestLocale) {
    const headers = new Headers(source.headers);
    headers.set(REQUEST_LOCALE_HEADER, requestLocale);
    source = new Request(source, { headers });
  }
  const isWrite = WRITE_METHODS.has(source.method.toUpperCase());

  if (!isWrite) {
    if (requestLocale) {
      return observeAccessResponse(await fetch(new Request(source, { credentials: 'include' })), new URL(source.url).pathname);
    }

    return observeAccessResponse(await fetch(input, {
      ...init,
      credentials: 'include',
    }), new URL(source.url).pathname);
  }

  const send = async (requestToken?: string): Promise<Response> => {
    const headers = new Headers(source.headers);
    if (requestToken) {
      headers.set(ANTIFORGERY_HEADER, requestToken);
    }

    let response: Response;
    try {
      response = await fetch(new Request(source.clone(), {
        credentials: 'include',
        headers,
      }));
    } catch (cause) {
      // 已进入业务发送边界；未收到响应不能判定失败或自动重放，原因只保留供诊断。
      throw new StandardCrudRequestError('操作结果尚未确认，请先查询实际结果，再决定是否重试。', 0, {}, {
        code: MUTATION_OUTCOME_UNKNOWN_CODE,
        reasonKey: MUTATION_OUTCOME_UNKNOWN_CODE,
      }, undefined, cause);
    }
    return observeAccessResponse(response, new URL(source.url).pathname, true);
  };

  const response = await send(await getRequestToken());
  if (!await isPreBusinessAntiforgeryFailure(response)) {
    return response;
  }

  // 只有业务执行前的稳定防伪失败可重放一次；网络异常、超时和其他响应原样返回给调用方。
  invalidatePlatformAntiforgeryToken();
  return send(await getRequestToken());
}

export interface PlatformUploadOptions {
  headers?: HeadersInit;
  signal?: AbortSignal;
  /** 原生 transport 的实际上传字节；未提供总量时 total 为 undefined。 */
  onProgress?: (loaded: number, total?: number) => void;
}

/** 单个 multipart 写入的进度入口；复用平台防伪、locale 与未知提交结果规则。 */
export async function platformUpload(input: string | URL, body: FormData, options: PlatformUploadOptions = {}): Promise<Response> {
  const origin = typeof window === 'undefined' ? 'http://localhost' : window.location.origin;
  const url = new URL(input, origin);
  if (url.origin !== origin) throw new Error('Platform uploads require the current origin.');
  const send = async (token: string): Promise<Response> => {
    options.signal?.throwIfAborted();
    const headers = new Headers(options.headers);
    headers.set(ANTIFORGERY_HEADER, token);
    const locale = requestLocaleState().provider?.();
    if (locale) headers.set(REQUEST_LOCALE_HEADER, locale);
    // 浏览器独占 multipart boundary；不能由业务调用方伪造它。
    headers.delete('Content-Type');
    const response = await new Promise<Response>((resolve, reject) => {
      const xhr = new XMLHttpRequest();
      const cleanup = () => {
        xhr.removeEventListener('load', loaded); xhr.removeEventListener('error', unknown);
        xhr.removeEventListener('timeout', unknown); xhr.removeEventListener('abort', aborted);
        xhr.upload.removeEventListener('progress', progress); options.signal?.removeEventListener('abort', abort);
      };
      const loaded = () => {
        cleanup();
        const responseHeaders = new Headers();
        for (const line of xhr.getAllResponseHeaders().split('\r\n')) {
          const colon = line.indexOf(':');
          if (colon > 0) responseHeaders.append(line.slice(0, colon).trim(), line.slice(colon + 1).trim());
        }
        resolve(new Response(xhr.status === 204 ? null : xhr.response as Blob, { status: xhr.status, statusText: xhr.statusText, headers: responseHeaders }));
      };
      const unknown = () => {
        cleanup();
        reject(new StandardCrudRequestError('操作结果尚未确认，请先查询实际结果，再决定是否重试。', 0, {}, {
          code: MUTATION_OUTCOME_UNKNOWN_CODE, reasonKey: MUTATION_OUTCOME_UNKNOWN_CODE,
        }));
      };
      const aborted = () => { cleanup(); reject(new DOMException('Upload cancelled.', 'AbortError')); };
      const abort = () => xhr.abort();
      const progress = (event: ProgressEvent) => options.onProgress?.(event.loaded, event.lengthComputable ? event.total : undefined);
      xhr.open('POST', url.toString()); xhr.withCredentials = true; xhr.responseType = 'blob';
      headers.forEach((value, key) => xhr.setRequestHeader(key, value));
      xhr.addEventListener('load', loaded); xhr.addEventListener('error', unknown);
      xhr.addEventListener('timeout', unknown); xhr.addEventListener('abort', aborted);
      xhr.upload.addEventListener('progress', progress); options.signal?.addEventListener('abort', abort, { once: true });
      if (options.signal?.aborted) { aborted(); return; }
      try { xhr.send(body); }
      catch (error) { cleanup(); reject(error); }
    });
    return observeAccessResponse(response, url.pathname, true);
  };
  const response = await send(await getRequestToken());
  if (!await isPreBusinessAntiforgeryFailure(response)) return response;
  invalidatePlatformAntiforgeryToken();
  return send(await getRequestToken());
}
