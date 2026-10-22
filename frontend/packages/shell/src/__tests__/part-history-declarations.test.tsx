// @vitest-environment jsdom
import { act } from 'react';
import { expect, it, vi } from 'vitest';
import { expectFixedReadHistoryRequests, mountIndependentHistory } from './independentHistoryHarness';

vi.unmock('@icp-platform/core');

const menuItem = (label: string) => [...document.querySelectorAll<HTMLElement>('[role="menuitem"]')].find(item => item.textContent?.trim() === label);
const button = (label: string) => [...document.querySelectorAll<HTMLButtonElement>('button')].find(item => item.textContent?.replace(/\s/g, '') === label);
const checkRequests = (requests: Request[]) => requests.filter(request => new URL(request.url).pathname.includes('/history/check/'));

async function historyAction(container: HTMLElement) {
  await vi.waitFor(() => expect(container.querySelector('tr[data-row-key="entity"]')).not.toBeNull());
  await act(async () => container.querySelector<HTMLButtonElement>('tr[data-row-key="entity"] button[aria-label="更多"]')!.click());
  await vi.waitFor(() => expect(menuItem('履历')).toBeDefined());
  await act(async () => menuItem('履历')!.click());
}

it('checks the Part button declaration before independent navigation and uses the separate list and detail declarations', async () => {
  let finish!: (response: Response) => void;
  const { container, router, requests, points, metadata } = await mountIndependentHistory('Part', {
    checkHistory: request => new URL(request.url).pathname.endsWith('/enter') ? new Promise<Response>(resolve => { finish = resolve; }) : new Response(null, { status: 204 }),
  });
  await historyAction(container);
  await vi.waitFor(() => expect(checkRequests(requests)).toHaveLength(1));
  expect(router.state.location.pathname).toBe('/parts');
  const check = checkRequests(requests)[0];
  expect(check.headers.get('X-NewP-Feature')).toBe(points.historyPosition);
  expect(check.headers.get('X-NewP-Button')).toContain('Part/view:part.part.list/rowActions/action:history');
  await act(async () => finish(new Response(null, { status: 204 })));
  await vi.waitFor(() => expect(router.state.location.pathname).toBe('/parts/history'));
  await vi.waitFor(() => expect(container.querySelector('tr[data-row-key="event-1"]')).not.toBeNull());
  const list = requests.find(request => new URL(request.url).pathname === '/api/part/parts/entity/history')!;
  expect(list.headers.get('X-NewP-Data-Declaration')).toContain('Part/view:part.history.list/read');
  await act(async () => container.querySelector<HTMLButtonElement>('tr[data-row-key="event-1"] button[aria-label="更多"]')!.click());
  await vi.waitFor(() => expect(menuItem('详情')).toBeDefined());
  await act(async () => menuItem('详情')!.click());
  await vi.waitFor(() => expect(document.body.textContent).toContain('独立详情最新操作者'));
  const detail = requests.find(request => new URL(request.url).pathname.endsWith('/history/event-1'))!;
  expect(detail.headers.get('X-NewP-Button')).toContain('Part/view:part.history.list/rowActions/action:view');
  expect(requests.some(request => new URL(request.url).pathname.startsWith('/api/workspace/'))).toBe(false);
  expectFixedReadHistoryRequests(metadata, requests);
});

it('keeps the functionally allowed Part button visible and stays on its list after a read denial', async () => {
  const { container, router, requests } = await mountIndependentHistory('Part', { checkHistory: () => new Response('{}', { status: 404 }) });
  await historyAction(container);
  await vi.waitFor(() => expect(document.body.textContent).toContain('对象不存在或不可访问'));
  expect(router.state.location.pathname).toBe('/parts');
  expect(requests.some(request => new URL(request.url).pathname === '/api/part/parts/entity/history')).toBe(false);
  await act(async () => container.querySelector<HTMLButtonElement>('tr[data-row-key="entity"] button[aria-label="更多"]')!.click());
  await vi.waitFor(() => expect(menuItem('履历')).toBeDefined());
});

it('cancels a pending Part entry without navigating when the allowed response arrives late', async () => {
  let finish!: (response: Response) => void;
  const { container, router, requests } = await mountIndependentHistory('Part', {
    checkHistory: () => new Promise<Response>(resolve => { finish = resolve; }),
  });
  await historyAction(container);
  await vi.waitFor(() => expect(checkRequests(requests)).toHaveLength(1));
  await vi.waitFor(() => expect(button('取消')).toBeDefined());
  await act(async () => button('取消')!.click());
  expect(checkRequests(requests)[0].signal.aborted).toBe(true);
  await act(async () => finish(new Response(null, { status: 204 })));
  expect(router.state.location.pathname).toBe('/parts');
  expect(requests.some(request => new URL(request.url).pathname === '/api/part/parts/entity/history')).toBe(false);
});

it('checks the detail contribution own declaration before opening its history modal', async () => {
  let finish!: (response: Response) => void;
  const { requests } = await mountIndependentHistory('Part', { fullDetail: true,
    checkHistory: () => new Promise<Response>(resolve => { finish = resolve; }),
  });
  await vi.waitFor(() => expect(button('操作履历')).toBeDefined());
  await act(async () => button('操作履历')!.click());
  await vi.waitFor(() => expect(checkRequests(requests)).toHaveLength(1));
  expect(checkRequests(requests)[0].headers.get('X-NewP-Button')).toContain('Part/view:part.history.list/detailContributions/action:part.object.history');
  expect(document.querySelector('.ant-modal')).toBeNull();
  await act(async () => finish(new Response('{}', { status: 404 })));
  await vi.waitFor(() => expect(document.body.textContent).toContain('对象不存在或不可访问'));
  expect(document.querySelector('.ant-modal')).toBeNull();
  expect(requests.some(request => new URL(request.url).pathname === '/api/part/parts/entity/history')).toBe(false);
});

it('checks readonly Part details and output before opening either input', async () => {
  const { container, requests } = await mountIndependentHistory('Part', {
    checkHistory: request => new Response(null, { status: new URL(request.url).pathname.endsWith('/enter') ? 204 : 404 }),
  });
  await historyAction(container);
  await vi.waitFor(() => expect(container.querySelector('tr[data-row-key="event-1"]')).not.toBeNull());
  await act(async () => container.querySelector<HTMLButtonElement>('tr[data-row-key="event-1"] button[aria-label="更多"]')!.click());
  await vi.waitFor(() => expect(menuItem('详情')).toBeDefined());
  await act(async () => menuItem('详情')!.click());
  await vi.waitFor(() => expect(checkRequests(requests).some(request => new URL(request.url).pathname.endsWith('/detail'))).toBe(true));
  expect(document.querySelector('.ant-drawer')).toBeNull();
  expect(requests.some(request => new URL(request.url).pathname.endsWith('/history/event-1'))).toBe(false);
  await vi.waitFor(() => expect(button('输出')).toBeDefined());
  await act(async () => button('输出')!.click());
  await vi.waitFor(() => expect(checkRequests(requests).some(request => new URL(request.url).pathname.endsWith('/preview'))).toBe(true));
  expect(document.querySelector('.ant-modal')).toBeNull();
  expect(requests.some(request => /\/history\/(export|print)$/.test(new URL(request.url).pathname))).toBe(false);
});

it.each(['cancel', 'leave'] as const)('discards a late contribution allow after %s and prevents repeated entry checks', async boundary => {
  let finish!: (response: Response) => void;
  const { container, router, requests } = await mountIndependentHistory('Part', { fullDetail: true,
    checkHistory: () => new Promise<Response>(resolve => { finish = resolve; }),
  });
  await vi.waitFor(() => expect(button('操作履历')).toBeDefined());
  const entry = button('操作履历')!;
  await act(async () => { entry.click(); entry.click(); });
  await vi.waitFor(() => expect(checkRequests(requests)).toHaveLength(1));
  expect(document.querySelector('.ant-modal')).toBeNull();
  if (boundary === 'cancel') {
    await vi.waitFor(() => expect(button('取消')).toBeDefined());
    await act(async () => button('取消')!.click());
  } else await act(async () => router.navigate('/parts'));
  expect(checkRequests(requests)[0].signal.aborted).toBe(true);
  await act(async () => finish(new Response(null, { status: 204 })));
  expect(document.querySelector('.ant-modal')).toBeNull();
  expect(requests.some(request => new URL(request.url).pathname === '/api/part/parts/entity/history')).toBe(false);
  expect(container.textContent).not.toContain('列表操作者');
});
