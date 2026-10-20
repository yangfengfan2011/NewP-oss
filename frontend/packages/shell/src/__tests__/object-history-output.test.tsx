// @vitest-environment jsdom
import { act, useState } from 'react';
import { MemoryRouter } from 'react-router-dom';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, expect, it, vi } from 'vitest';
import { LocalizationProvider } from '@icp-platform/core';
import { AuthProvider, useAuth } from '../contexts/AuthContext';
import { StandardCrudActionReviewDialog } from '../standard-crud/StandardCrudActionReviewDialog';
import { createObjectHistoryResource } from '../../../ui/src/objectHistoryResource';
import type { StandardCrudActionInputRenderContext } from '@icp-platform/sdk';

it('uses each formal output button identity without mixing the list read declaration', async () => {
  const requests: Request[] = [];
  const fetcher = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    requests.push(new Request(new URL(String(input), 'http://test.local'), init));
    return new Response(new Blob(['xlsx']));
  });
  downloadSpies();
  const declarations = ['exportSelected', 'exportAll', 'printSelected', 'printAll'].map(actionKey => ({
    id: 'button-' + actionKey, moduleId: 'Part', viewKey: 'workspace.history.list', surface: 'outputActions', actionKey,
    execution: '', object: 'Part', businessBinding: 'part.part.history', actions: ['read'], resources: ['part.part'], featurePositions: [featurePosition],
  }));
  await mount(fetcher, { dataAccessDeclarations: declarations });
  await click('导出选中');
  await click('导出全部');
  expect(requests.map(request => request.headers.get('X-NewP-Button'))).toEqual(['button-exportSelected', 'button-exportAll']);
  expect(requests.every(request => !request.headers.has('X-NewP-Data-Declaration'))).toBe(true);
});

const position = { spaceId: 'space', rootFolderId: 'root', linkIds: ['container'] };
const target = { resourceKey: 'part.part', id: 'part-eid', basePath: `/api/workspace/content/part-lid/history?queryScope=${encodeURIComponent(JSON.stringify(position))}` };
const featurePosition = 'transport-history-primary';
const item = { id: 'event-1', eventType: 'part.created', occurredAt: '2026-10-18T03:00:00Z', objectType: 'Part',
  actor: { name: '安全操作者', status: 'available' }, target: { name: '安全对象', code: 'P1', nameSource: 'historical' },
  description: { key: 'Part.Created', parameters: {} }, ipAddress: null };
const row = { id: item.id, historyItem: item, historyFilter: {}, historySource: JSON.stringify([target.resourceKey, target.id, target.basePath, featurePosition]) };
let root: Root | undefined;
let container: HTMLDivElement | undefined;
afterEach(async () => { await act(async () => root?.unmount()); root = undefined; container?.remove(); container = undefined; vi.restoreAllMocks(); vi.unstubAllGlobals(); });
async function mount(fetcher: typeof fetch, context: Partial<StandardCrudActionInputRenderContext> = {}) {
  const resource = createObjectHistoryResource({ resourceKey: 'workspace.history', listViewKey: 'workspace.history.list', resolveTarget: () => target, fetcher });
  const input = resource.actionInputRenderers?.objectHistoryOutput;
  expect(input?.readOnly).toBe(true);
  if (!container) { container = document.createElement('div'); document.body.append(container); root = createRoot(container); }
  await act(async () => root!.render(<LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={{ zhs: {} }}>
    {input!({ action: { key: 'output', labelKey: 'Output', standardAction: 'view', inputKey: 'objectHistoryOutput' }, value: undefined, onChange: vi.fn(),
      featurePosition, selectedRows: [row], listQuery: { pageIndex: 2, pageSize: 20, description: '已应用描述', ipAddress: '192.0.2.1', sortBy: 'TraceId', fields: 'Fields' }, ...context })}
  </LocalizationProvider>));
}
async function click(label: string) {
  const button = [...container!.querySelectorAll('button')].find(element => element.textContent?.replace(/\s/g, '') === label);
  expect(button).toBeDefined();
  await act(async () => button!.click());
}
function downloadSpies() {
  const create = vi.fn(() => 'blob:history');
  const revoke = vi.fn();
  vi.stubGlobal('URL', class extends URL { static createObjectURL = create; static revokeObjectURL = revoke; });
  const download = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {});
  return { create, revoke, download };
}
it('exports selected and all within the captured safe filter and original owner history source', async () => {
  const requests: Request[] = [];
  const fetcher = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    requests.push(new Request(new URL(String(input), 'http://test.local'), init));
    return new Response(new Blob(['xlsx']), { headers: { 'Content-Disposition': 'attachment; filename="object-history.xlsx"' } });
  });
  const { download, create, revoke } = downloadSpies();
  await mount(fetcher);
  await click('导出选中');
  await vi.waitFor(() => expect(download).toHaveBeenCalledOnce());
  expect(requests[0].method).toBe('POST');
  expect(new URL(requests[0].url).pathname).toBe('/api/workspace/content/part-lid/history/export');
  expect(JSON.parse(new URL(requests[0].url).searchParams.get('queryScope')!)).toEqual(position);
  expect(requests[0].headers.get('X-NewP-Feature')).toBe(featurePosition);
  expect(await requests[0].json()).toEqual({ mode: 'selected', selectedIds: ['event-1'], filter: { pageIndex: 2, pageSize: 20, description: '已应用描述', ipAddress: '192.0.2.1' } });
  expect(download.mock.instances[0].download).toBe('object-history.xlsx');
  await click('导出全部');
  await vi.waitFor(() => expect(download).toHaveBeenCalledTimes(2));
  expect(await requests[1].json()).toEqual({ mode: 'all', filter: { pageIndex: 2, pageSize: 20, description: '已应用描述', ipAddress: '192.0.2.1' } });
  expect(create).toHaveBeenCalledTimes(2); expect(revoke).toHaveBeenCalledTimes(2);
});

it('loads selected and all safe print data and prints only the rendered safe view rather than the application', async () => {
  const requests: Request[] = [];
  const fetcher = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    requests.push(new Request(new URL(String(input), 'http://test.local'), init));
    return new Response(JSON.stringify({ items: [{ ...item, Fields: 'secret-fields', TraceId: 'secret-trace', actor: { ...item.actor, id: 'secret-account' } }] }));
  });
  await mount(fetcher);
  await click('打印选中');
  await vi.waitFor(() => expect(container!.querySelector('table')).not.toBeNull());
  expect(container!.querySelector('table')!.textContent).toContain('安全操作者');
  expect(container!.querySelector('table')!.textContent).not.toMatch(/secret-|event-1|part-eid|part-lid/);
  expect(new URL(requests[0].url).pathname).toBe('/api/workspace/content/part-lid/history/print');
  expect(requests[0].headers.get('X-NewP-Feature')).toBe(featurePosition);
  expect(await requests[0].json()).toMatchObject({ mode: 'selected', selectedIds: ['event-1'], filter: { description: '已应用描述', pageIndex: 2 } });
  // jsdom 没有系统打印机；仅替换浏览器公开 print 边界，实际打印 DOM 仍由产品创建。
  const appPrint = vi.spyOn(window, 'print').mockImplementation(() => {});
  const framePrint = vi.fn();
  const append = document.body.append.bind(document.body);
  vi.spyOn(document.body, 'append').mockImplementation((...nodes) => {
    append(...nodes);
    for (const node of nodes) if (node instanceof HTMLIFrameElement) vi.spyOn(node.contentWindow!, 'print').mockImplementation(framePrint);
  });
  await click('打印');
  const frame = document.querySelector<HTMLIFrameElement>('iframe[title="操作履历打印"]');
  expect(frame).not.toBeNull();
  expect(frame!.contentDocument!.body.textContent).toContain('安全操作者');
  expect(frame!.contentDocument!.body.textContent).not.toMatch(/secret-|event-1|part-eid|part-lid|导出/);
  expect(frame!.contentWindow!.print).not.toBe(window.print);
  expect(framePrint).toHaveBeenCalledOnce();
  expect(appPrint).not.toHaveBeenCalled();
  await act(async () => frame!.contentWindow!.dispatchEvent(new Event('afterprint')));
  expect(document.querySelector('iframe[title="操作履历打印"]')).toBeNull();
  await click('打印全部');
  await vi.waitFor(() => expect(requests).toHaveLength(2));
  expect(await requests[1].json()).toEqual({ mode: 'all', filter: { pageIndex: 2, pageSize: 20, description: '已应用描述', ipAddress: '192.0.2.1' } });
});

it('freezes output buttons while pending and cancels without downloading a late response or replaying', async () => {
  let finish!: (response: Response) => void;
  let signal: AbortSignal | undefined;
  const fetcher = vi.fn(async (_input: RequestInfo | URL, init?: RequestInit) => {
    signal = init?.signal ?? undefined;
    return await new Promise<Response>(resolve => { finish = resolve; });
  });
  const { download } = downloadSpies();
  await mount(fetcher);
  await click('导出全部');
  await vi.waitFor(() => expect(signal).toBeDefined());
  for (const button of [...container!.querySelectorAll('button')].filter(button => button.textContent?.includes('导出') || button.textContent?.includes('打印')))
    expect(button.disabled).toBe(true);
  await click('导出全部');
  expect(fetcher).toHaveBeenCalledOnce();
  await click('取消输出');
  expect(signal!.aborted).toBe(true);
  await act(async () => finish(new Response(new Blob(['late']))));
  expect(download).not.toHaveBeenCalled();
  expect(fetcher).toHaveBeenCalledOnce();
  expect(container!.textContent).toContain('输出已取消');
});

it('keeps an empty selection unavailable but permits all zero-row output and exposes failure without replay', async () => {
  let denied = true;
  const fetcher = vi.fn(async () => denied ? new Response('{}', { status: 403 }) : new Response(JSON.stringify({ items: [] })));
  await mount(fetcher, { selectedRows: [] });
  for (const label of ['导出选中', '打印选中']) {
    const button = [...container!.querySelectorAll('button')].find(element => element.textContent?.replace(/\s/g, '') === label)!;
    expect(button.disabled).toBe(true);
  }
  await click('打印全部');
  await vi.waitFor(() => expect(container!.querySelector('.ant-alert-error')).not.toBeNull());
  expect(container!.querySelector('table')).toBeNull();
  await act(async () => {});
  expect(fetcher).toHaveBeenCalledOnce();
  denied = false;
  await click('打印全部');
  await vi.waitFor(() => expect(container!.querySelector('table')).not.toBeNull());
  expect(container!.querySelectorAll('tbody tr')).toHaveLength(0);
  expect(container!.querySelector('.ant-alert-error')).toBeNull();
  expect(fetcher).toHaveBeenCalledTimes(2);
});

it.each(['query', 'selection'] as const)('rejects missing or stale %s instead of falling back to an unrelated output', async boundary => {
  const fetcher = vi.fn(async () => new Response('{}'));
  await mount(fetcher, boundary === 'query' ? { listQuery: undefined } : { selectedRows: [{ ...row, historySource: 'old-source' }] });
  expect(container!.querySelector('.ant-alert-error')).not.toBeNull();
  expect(container!.querySelector('button')).toBeNull();
  expect(fetcher).not.toHaveBeenCalled();
});

it.each(['source', 'account'] as const)('invalidates readonly output on %s change, aborts the request and ignores late data', async boundary => {
  let finish!: (response: Response) => void;
  let signal: AbortSignal | undefined;
  const fetcher = vi.fn(async (_input: RequestInfo | URL, init?: RequestInit) => {
    signal = init?.signal ?? undefined;
    return await new Promise<Response>(resolve => { finish = resolve; });
  });
  const { download } = downloadSpies();
  const user = { id: 'reader', ecode: 'reader', ename: 'Reader', revision: 1, mustChangePassword: false,
    isLocalAdministrator: false, featurePositions: [featurePosition], timeZoneId: 'UTC' };
  let nextUser = user;
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
    if (String(input).endsWith('current-user')) return new Response(JSON.stringify(nextUser), { headers: { 'Content-Type': 'application/json' } });
    throw new Error(`Unexpected authentication request: ${String(input)}`);
  }));
  const resource = createObjectHistoryResource({ resourceKey: 'workspace.history', listViewKey: 'workspace.history.list', resolveTarget: () => target, fetcher });
  let changeSource!: (value: string) => void;
  let refresh!: () => Promise<void>;
  function Input() {
    refresh = useAuth().retrySessionValidation;
    const [source, setSource] = useState('part-lid'); changeSource = setSource;
    const [open, setOpen] = useState(true);
    return open ? <StandardCrudActionReviewDialog title="输出" action={{ key: 'output', labelKey: 'Output', standardAction: 'view', inputKey: 'objectHistoryOutput' }}
      inputRenderer={resource.actionInputRenderers!.objectHistoryOutput} featurePosition={featurePosition} queryScope={{ sourceLinkId: source }}
      listQuery={{ pageIndex: 1, pageSize: 20 }} selectedRows={[]} value={undefined} pending={false} onChange={vi.fn()} onCancel={() => setOpen(false)} onConfirm={vi.fn()} /> : null;
  }
  container = document.createElement('div'); document.body.append(container); root = createRoot(container);
  await act(async () => root!.render(<MemoryRouter><LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={{ zhs: {} }}>
    <AuthProvider initialUser={user}><Input /></AuthProvider>
  </LocalizationProvider></MemoryRouter>));
  const button = [...document.querySelectorAll('button')].find(element => element.textContent?.replace(/\s/g, '') === '导出全部')!;
  await act(async () => button.click());
  await vi.waitFor(() => expect(signal).toBeDefined());
  if (boundary === 'source') await act(async () => changeSource('other-lid'));
  else { nextUser = { ...user, id: 'other-reader' }; await act(async () => refresh()); }
  await vi.waitFor(() => expect(signal!.aborted).toBe(true));
  await act(async () => finish(new Response(new Blob(['late']))));
  expect(download).not.toHaveBeenCalled();
  expect(fetcher).toHaveBeenCalledOnce();
  expect([...document.querySelectorAll('button')].some(element => element.textContent?.includes('导出全部'))).toBe(false);
});
