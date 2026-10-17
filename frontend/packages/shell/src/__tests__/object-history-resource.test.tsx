// @vitest-environment jsdom
import { afterEach, expect, it, vi } from 'vitest';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { LocalizationProvider, PlatformQueryProvider } from '@icp-platform/core';
import { createObjectHistoryResource } from '../../../ui/src/objectHistoryResource';
import ObjectHistoryDetails from '../../../ui/src/ObjectHistoryDetails';
import { readObjectHistoryDetail } from '../../../ui/src/objectHistoryRequest';

const position = { spaceId: 'space', rootFolderId: 'root', linkIds: ['container'] };
const target = { resourceKey: 'part.part', id: 'part-eid',
  basePath: `/api/workspace/content/part-lid/history?queryScope=${encodeURIComponent(JSON.stringify(position))}` };
const item = { id: 'event-1', eventType: 'part.created', occurredAt: '2026-10-18T03:00:00Z', objectType: 'Part',
  actor: { name: null, status: 'unavailable' }, target: { name: null, code: null, nameSource: 'unrecorded' },
  description: { key: 'Part.Created', parameters: { object: '允许的名称' } }, ipAddress: null };

it('reads real detail within the same source and filter and retains only the safe item contract', async () => {
  const requests: Array<{ url: URL; init: RequestInit | undefined }> = [];
  const fetcher = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    requests.push({ url: new URL(String(input), 'http://test.local'), init });
    return new Response(JSON.stringify({ ...item, id: 'event/1', TraceId: 'private-trace', Fields: ['private'], Changes: ['private'],
      actor: { ...item.actor, id: 'hidden-account' }, target: { ...item.target, id: 'hidden-object', privatePath: 'private/path' } }));
  });
  const controller = new AbortController();
  const result = await readObjectHistoryDetail(target, 'event/1', { eventType: 'part.created', from: '2026-07-18T00:00:00Z' },
    { featurePosition: 'transport-history-position', signal: controller.signal }, fetcher);
  expect(result).toEqual({ ...item, id: 'event/1' });
  expect(requests).toHaveLength(1);
  expect(requests[0].url.pathname).toBe('/api/workspace/content/part-lid/history/event%2F1');
  expect(JSON.parse(requests[0].url.searchParams.get('queryScope')!)).toEqual(position);
  expect(requests[0].url.searchParams.get('eventType')).toBe('part.created');
  expect(requests[0].url.searchParams.get('from')).toBe('2026-07-18T00:00:00Z');
  expect(requests[0].init).toMatchObject({ method: 'GET', signal: controller.signal });
  expect(new Headers(requests[0].init?.headers).get('X-NewP-Feature')).toBe('transport-history-position');
  expect(requests[0].init?.body).toBeUndefined();
});

const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } });

it('keeps denial, cancellation and malformed responses as failures instead of a list snapshot', async () => {
  const denied = vi.fn(async () => json({ title: '履历不可用' }, 403));
  await expect(readObjectHistoryDetail(target, item.id, {}, { featurePosition: 'history-transport' }, denied)).rejects.toMatchObject({ status: 403 });
  const malformed = vi.fn(async () => json({ ...item, actor: { name: 'Unknown', status: 'unsupported' } }));
  await expect(readObjectHistoryDetail(target, item.id, {}, { featurePosition: 'history-transport' }, malformed)).rejects.toMatchObject({ status: 502 });
  await expect(readObjectHistoryDetail(target, item.id, {}, {}, denied)).rejects.toMatchObject({ status: 400 });
  expect(denied).toHaveBeenCalledOnce();
  const controller = new AbortController(); controller.abort();
  const cancelled = vi.fn(async (_input: RequestInfo | URL, init?: RequestInit) => {
    init?.signal?.throwIfAborted(); return json(item);
  });
  await expect(readObjectHistoryDetail(target, item.id, {}, { featurePosition: 'history-transport', signal: controller.signal }, cancelled)).rejects.toMatchObject({ name: 'AbortError' });
  expect(cancelled).toHaveBeenCalledOnce();
});

it('adapts the safe page to a standard resource without forwarding generic fields or sort parameters', async () => {
  const requests: Request[] = [];
  const fetcher = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    requests.push(new Request(new URL(String(input), 'http://test.local'), init));
    return json({ items: [{ ...item, Fields: 'admin-only' }], total: 1, pageIndex: 1, pageSize: 20 });
  });
  const resolveTarget = vi.fn(() => target);
  const resource = createObjectHistoryResource({ resourceKey: 'workspace.history', listViewKey: 'workspace.history.list', resolveTarget, fetcher });
  const controller = new AbortController();
  const page = await resource.dataAdapter.list({ pageIndex: 1, pageSize: 20, eventType: 'part.created', fields: 'TraceId', sortBy: 'TraceId' },
    { queryScope: { sourceLinkId: 'part-lid' }, featurePosition: 'history-transport', signal: controller.signal });
  expect(resolveTarget).toHaveBeenCalledWith({ sourceLinkId: 'part-lid' });
  expect(page.items).toHaveLength(1);
  expect(resource.dataAdapter.getRowId(page.items[0])).toBe(item.id);
  expect(page.items[0].historyItem).toEqual(item);
  expect(page.items[0].historyFilter).toEqual({ eventType: 'part.created' });
  const url = new URL(requests[0].url);
  expect(url.pathname).toBe('/api/workspace/content/part-lid/history');
  expect(url.searchParams.get('eventType')).toBe('part.created');
  expect(url.searchParams.get('pageSize')).toBe('20');
  expect(url.searchParams.has('fields')).toBe(false);
  expect(url.searchParams.has('sortBy')).toBe(false);
  expect(requests[0].signal.aborted).toBe(false);
  expect(resource.actionInputRenderers?.objectHistoryDetails.readOnly).toBe(true);
});

let root: Root | undefined;
let container: HTMLDivElement | undefined;
afterEach(async () => { await act(async () => root?.unmount()); root = undefined; container?.remove(); container = undefined; });
async function renderDetail(source: typeof target, fetcher: typeof fetch, actor = 'actor-1') {
  if (!container) { container = document.createElement('div'); document.body.append(container); root = createRoot(container); }
  await act(async () => root!.render(<LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={{ zhs: { 'Part.Created': '创建了{object}' } }}>
    <PlatformQueryProvider scopeKey={actor}><ObjectHistoryDetails target={source} recordId={item.id} filter={{ eventType: item.eventType }}
      featurePosition="history-transport" fetcher={fetcher} /></PlatformQueryProvider>
  </LocalizationProvider>));
}

it('renders only the fresh safe detail and explicit missing-name and IP placeholders', async () => {
  const fetcher = vi.fn(async () => json({ ...item, TraceId: 'private-trace', Fields: 'private-fields',
    actor: { ...item.actor, id: 'hidden-account' }, target: { ...item.target, id: 'hidden-object' } }));
  await renderDetail(target, fetcher);
  await vi.waitFor(() => expect(container!.textContent).toContain('创建了允许的名称'));
  expect(container!.textContent).toContain('操作者不可用');
  expect(container!.textContent).toContain('未记录');
  expect(container!.textContent).not.toMatch(/private-trace|private-fields|hidden-account|hidden-object|event-1/);
});

it('aborts an old source read and ignores its late result when the source changes', async () => {
  let oldSignal: AbortSignal | undefined;
  let finishOld!: (response: Response) => void;
  const fetcher = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    if (String(input).includes('part-lid/history')) {
      oldSignal = init?.signal ?? undefined;
      return await new Promise<Response>(resolve => { finishOld = resolve; });
    }
    return json({ ...item, actor: { status: 'available', name: '新来源操作者' } });
  });
  await renderDetail(target, fetcher);
  await vi.waitFor(() => expect(oldSignal).toBeDefined());
  await renderDetail({ ...target, basePath: target.basePath.replace('part-lid', 'other-lid') }, fetcher);
  await vi.waitFor(() => expect(oldSignal!.aborted).toBe(true));
  await vi.waitFor(() => expect(container!.textContent).toContain('新来源操作者'));
  await act(async () => finishOld(json({ ...item, actor: { status: 'available', name: '失效旧来源' } })));
  expect(container!.textContent).not.toContain('失效旧来源');
});

it('clears the old detail on actor scope change and does not display stale data after denial', async () => {
  let denied = false;
  const fetcher = vi.fn(async () => denied ? json({ title: '无履历资格' }, 403) : json({ ...item, actor: { status: 'available', name: '原账号可见' } }));
  await renderDetail(target, fetcher);
  await vi.waitFor(() => expect(container!.textContent).toContain('原账号可见'));
  denied = true;
  await renderDetail(target, fetcher, 'actor-2');
  await vi.waitFor(() => expect(container!.querySelector('.ant-alert-error')).not.toBeNull());
  expect(container!.textContent).not.toContain('原账号可见');
});

it('decodes unavailable target names as explicit placeholders without rendering supplied private text or original IDs', async () => {
  const payload = { ...item, target: { nameSource: 'unavailable', name: '给定敏感名称', code: 'PRIVATE-CODE', id: 'original-object-id' } };
  const fetcher = vi.fn(async () => json(payload));
  const received = await readObjectHistoryDetail(target, item.id, {}, { featurePosition: 'history-transport' }, fetcher);
  expect(received.target).toEqual({ nameSource: 'unavailable', name: null, code: null });
  await renderDetail(target, fetcher);
  await vi.waitFor(() => expect(container!.textContent).toContain('名称不可用'));
  expect(container!.textContent).not.toMatch(/给定敏感名称|PRIVATE-CODE|original-object-id|part-eid|part-lid/);
});
