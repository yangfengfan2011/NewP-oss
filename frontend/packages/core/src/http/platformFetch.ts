const WRITE_METHODS = new Set(['POST', 'PUT', 'PATCH', 'DELETE']);
const ANTIFORGERY_ENDPOINT = '/api/auth/antiforgery-token';
const ANTIFORGERY_HEADER = 'X-CSRF-TOKEN';
const ANTIFORGERY_FAILURE_CODE = 'ANTIFORGERY_TOKEN_INVALID';

let cachedRequestToken: string | undefined;
let tokenGeneration = 0;
let requestTokenPromise: { generation: number; promise: Promise<string> } | undefined;

interface AntiforgeryTokenResponse {
  requestToken: string;
}

interface PlatformProblemDetails {
  code?: string;
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
  const source = new Request(requestInput, init);
  const isWrite = WRITE_METHODS.has(source.method.toUpperCase());

  if (!isWrite) {
    return fetch(input, {
      ...init,
      credentials: 'include',
    });
  }

  const send = async (requestToken?: string): Promise<Response> => {
    const headers = new Headers(source.headers);
    if (requestToken) {
      headers.set(ANTIFORGERY_HEADER, requestToken);
    }

    return fetch(new Request(source.clone(), {
      credentials: 'include',
      headers,
    }));
  };

  const response = await send(await getRequestToken());
  if (!await isPreBusinessAntiforgeryFailure(response)) {
    return response;
  }

  // 只有业务执行前的稳定防伪失败可重放一次；网络异常、超时和其他响应原样返回给调用方。
  invalidatePlatformAntiforgeryToken();
  return send(await getRequestToken());
}
