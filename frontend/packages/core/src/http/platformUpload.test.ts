import { afterEach, describe, expect, it, vi } from 'vitest';
import { invalidatePlatformAntiforgeryToken, installPlatformRequestLocaleProvider, platformUpload } from './platformFetch';

class UploadTransport extends EventTarget {
  static requests: UploadTransport[] = [];
  upload = new EventTarget();
  headers = new Map<string, string>();
  withCredentials = false;
  responseType = '';
  status = 0;
  statusText = '';
  response: Blob | null = null;
  body: unknown;
  url = '';
  method = '';
  open(method: string, url: string) { this.method = method; this.url = url; }
  setRequestHeader(key: string, value: string) { this.headers.set(key.toLowerCase(), value); }
  getAllResponseHeaders() { return 'content-type: application/json\r\n'; }
  send(body: unknown) { this.body = body; UploadTransport.requests.push(this); }
  abort() { this.dispatchEvent(new Event('abort')); }
  respond(status = 200, body = '{"uploadId":"upload-1"}') {
    this.status = status; this.response = new Blob([body]); this.dispatchEvent(new Event('load'));
  }
  progress(loaded: number, total: number) {
    this.upload.dispatchEvent(Object.assign(new Event('progress'), { lengthComputable: true, loaded, total }));
  }
}

describe('platformUpload', () => {
  afterEach(() => { invalidatePlatformAntiforgeryToken(); vi.unstubAllGlobals(); UploadTransport.requests = []; });

  it('sends one credentialled multipart write with csrf, controlled locale and real upload progress', async () => {
    vi.stubGlobal('XMLHttpRequest', UploadTransport);
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{"requestToken":"csrf-1"}')));
    const uninstall = installPlatformRequestLocaleProvider(() => 'en-US');
    const body = new FormData(); body.append('file', new Blob(['file']), 'main.pdf');
    const progress = vi.fn();
    const request = platformUpload('/api/upload', body, { headers: { 'X-NewP-Feature': 'position' }, onProgress: progress });
    await vi.waitFor(() => expect(UploadTransport.requests).toHaveLength(1));
    const xhr = UploadTransport.requests[0];
    expect(xhr.body).toBe(body); expect(xhr.method).toBe('POST'); expect(xhr.withCredentials).toBe(true);
    expect(xhr.headers.get('x-csrf-token')).toBe('csrf-1'); expect(xhr.headers.get('x-icp-locale')).toBe('en-US');
    expect(xhr.headers.get('x-newp-feature')).toBe('position'); expect(xhr.headers.has('content-type')).toBe(false);
    xhr.progress(4, 8); expect(progress).toHaveBeenLastCalledWith(4, 8);
    xhr.respond(); expect((await request).status).toBe(200);
    xhr.progress(8, 8); expect(progress).toHaveBeenCalledTimes(1);
    uninstall();
  });

  it('never repeats a business upload after network failure and releases progress listeners', async () => {
    vi.stubGlobal('XMLHttpRequest', UploadTransport);
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{"requestToken":"csrf-1"}')));
    const progress = vi.fn();
    const outcome = platformUpload('/api/upload', new FormData(), { onProgress: progress }).catch(error => error);
    await vi.waitFor(() => expect(UploadTransport.requests).toHaveLength(1));
    UploadTransport.requests[0].dispatchEvent(new Event('error'));
    expect(await outcome).toMatchObject({ code: 'Platform.MutationOutcomeUnknown', status: 0 });
    UploadTransport.requests[0].progress(8, 8); expect(progress).not.toHaveBeenCalled();
    expect(UploadTransport.requests).toHaveLength(1);
  });

  it('cancels the request, releases listeners and never retries a late response', async () => {
    vi.stubGlobal('XMLHttpRequest', UploadTransport);
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{"requestToken":"csrf-1"}')));
    const controller = new AbortController(); const progress = vi.fn();
    const outcome = platformUpload('/api/upload', new FormData(), { signal: controller.signal, onProgress: progress }).catch(error => error);
    await vi.waitFor(() => expect(UploadTransport.requests).toHaveLength(1));
    controller.abort(); expect(await outcome).toMatchObject({ name: 'AbortError' });
    UploadTransport.requests[0].respond(); UploadTransport.requests[0].progress(8, 8);
    expect(progress).not.toHaveBeenCalled(); expect(UploadTransport.requests).toHaveLength(1);
  });
});
