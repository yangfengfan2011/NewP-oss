// @vitest-environment jsdom

import { act, StrictMode } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { PlatformQueryProvider } from '@icp-platform/core';
import type { IdentityPage, IdentitySecurityEvent } from '../api/identityClient';

const listAccountSecurityEventsMock = vi.hoisted(() => vi.fn());
vi.mock('../api/identityClient', async (importOriginal) => ({
  ...await importOriginal<typeof import('../api/identityClient')>(),
  listAccountSecurityEvents: listAccountSecurityEventsMock,
}));

import { IdentityAccountSecurityEventsSummary } from '../standard-crud/IdentityAccountSecurityEventsSummary';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
const nativeGetComputedStyle = window.getComputedStyle.bind(window);
type EventPage = IdentityPage<IdentitySecurityEvent>;

describe('Identity account security-event summary renderer', () => {
  let container: HTMLDivElement;
  let root: Root;

  beforeEach(() => {
    listAccountSecurityEventsMock.mockReset();
    vi.spyOn(window, 'getComputedStyle').mockImplementation((element) => (
      nativeGetComputedStyle(element)
    ));
    Object.defineProperty(window, 'matchMedia', {
      configurable: true,
      value: vi.fn().mockImplementation((query: string) => ({
        matches: false,
        media: query,
        onchange: null,
        addListener: vi.fn(),
        removeListener: vi.fn(),
        addEventListener: vi.fn(),
        removeEventListener: vi.fn(),
        dispatchEvent: vi.fn(),
      })),
    });
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);
  });

  afterEach(async () => {
    await act(async () => root.unmount());
    vi.restoreAllMocks();
    container.remove();
  });

  async function renderSummary(): Promise<void> {
    await act(async () => {
      root.render(
        <PlatformQueryProvider scopeKey="user-1">
          <IdentityAccountSecurityEventsSummary accountEid="User_2" title="安全事件" />
        </PlatformQueryProvider>,
      );
    });
  }

  it('shares one account and page generation across StrictMode subscribers', async () => {
    let resolveEvents!: (result: EventPage) => void;
    listAccountSecurityEventsMock.mockReturnValue(new Promise((resolve) => {
      resolveEvents = resolve;
    }));

    await act(async () => {
      root.render(
        <StrictMode>
          <PlatformQueryProvider scopeKey="user-1">
            <IdentityAccountSecurityEventsSummary accountEid="User_2" title="安全事件一" />
            <IdentityAccountSecurityEventsSummary accountEid="User_2" title="安全事件二" />
          </PlatformQueryProvider>
        </StrictMode>,
      );
    });

    expect(listAccountSecurityEventsMock).toHaveBeenCalledTimes(1);
    expect(listAccountSecurityEventsMock).toHaveBeenCalledWith(
      'User_2', 1, 20, { signal: expect.any(AbortSignal) },
    );

    await act(async () => resolveEvents({ items: [], page: 1, pageSize: 20, totalCount: 0 }));
    expect(container.textContent).toContain('安全事件一');
    expect(container.textContent).toContain('安全事件二');
    expect(container.querySelectorAll('.ant-empty')).toHaveLength(2);
  });

  it('renders a stable empty state without changing ordinary detail fields', async () => {
    listAccountSecurityEventsMock.mockResolvedValue({ items: [], page: 1, pageSize: 20, totalCount: 0 });

    await renderSummary();
    await vi.waitFor(() => expect(container.textContent).toContain('暂无安全事件'));

    expect(listAccountSecurityEventsMock).toHaveBeenCalledWith(
      'User_2', 1, 20, { signal: expect.any(AbortSignal) },
    );
    expect(container.querySelector('.ant-empty')).not.toBeNull();
  });

  it('renders an isolated failure state when the event query fails', async () => {
    listAccountSecurityEventsMock.mockRejectedValue(new Error('IDENTITY_SECURITY_EVENTS_UNAVAILABLE'));

    await renderSummary();
    await vi.waitFor(() => expect(container.textContent).toContain('安全事件加载失败'));

    expect(container.querySelector('.ant-alert-error')).not.toBeNull();
  });

  it('renders the server display companion without formatting the UTC raw in the browser', async () => {
    listAccountSecurityEventsMock.mockResolvedValue({
      items: [{
        eid: 'event-1', eventType: 'AccountCreated', reason: 'created',
        occurredAt: '2030-01-02T00:04:05Z', occurredAt_DisplayValue: '2030/1/2 08:04:05',
        traceId: 'trace-1', actorKind: 'System',
      }],
      page: 1,
      pageSize: 20,
      totalCount: 1,
    });

    await renderSummary();
    await vi.waitFor(() => expect(container.textContent).toContain('2030/1/2 08:04:05'));

    expect(container.textContent).not.toContain('2030-01-02T00:04:05Z');
  });

  it('leaves the time unavailable when the display companion is null', async () => {
    listAccountSecurityEventsMock.mockResolvedValue({
      items: [{
        eid: 'event-1', eventType: 'AccountCreated', reason: 'created',
        occurredAt: '2030-01-02T00:04:05Z', occurredAt_DisplayValue: null,
        traceId: 'trace-1', actorKind: 'System',
      }],
      page: 1,
      pageSize: 20,
      totalCount: 1,
    });

    await renderSummary();
    await vi.waitFor(() => expect(container.textContent).toContain('AccountCreated'));

    expect(container.textContent).not.toContain('2030-01-02T00:04:05Z');
  });

  it('reads the security events again when the presentation context changes', async () => {
    listAccountSecurityEventsMock
      .mockResolvedValueOnce({
        items: [{
          eid: 'event-1', eventType: 'AccountCreated', reason: 'created',
          occurredAt: '2030-01-02T00:04:05Z', occurredAt_DisplayValue: '2030/1/2 08:04:05',
          traceId: 'trace-1', actorKind: 'System',
        }],
        page: 1,
        pageSize: 20,
        totalCount: 1,
      })
      .mockResolvedValueOnce({
        items: [{
          eid: 'event-1', eventType: 'AccountCreated', reason: 'created',
          occurredAt: '2030-01-02T00:04:05Z', occurredAt_DisplayValue: '1/2/2030 8:04:05 AM',
          traceId: 'trace-1', actorKind: 'System',
        }],
        page: 1,
        pageSize: 20,
        totalCount: 1,
      });

    await act(async () => {
      root.render(
        <PlatformQueryProvider scopeKey="user-1:zhs">
          <IdentityAccountSecurityEventsSummary accountEid="User_2" title="安全事件" />
        </PlatformQueryProvider>,
      );
    });
    await vi.waitFor(() => expect(container.textContent).toContain('2030/1/2 08:04:05'));

    await act(async () => {
      root.render(
        <PlatformQueryProvider scopeKey="user-1:en">
          <IdentityAccountSecurityEventsSummary accountEid="User_2" title="安全事件" />
        </PlatformQueryProvider>,
      );
    });
    await vi.waitFor(() => expect(container.textContent).toContain('1/2/2030 8:04:05 AM'));

    expect(listAccountSecurityEventsMock).toHaveBeenCalledTimes(2);
  });

  it('loads the requested event page independently', async () => {
    listAccountSecurityEventsMock
      .mockResolvedValueOnce({
        items: [{
          eid: 'event-2', eventType: 'AccountDisabled', reason: 'review', occurredAt: '2030-01-02T00:00:00Z',
          traceId: 'trace-2', actorKind: 'IdentityAccount', actorAccountEID: 'User_1',
        }],
        page: 1,
        pageSize: 20,
        totalCount: 21,
      })
      .mockResolvedValueOnce({
        items: [
          {
            eid: 'event-1', eventType: 'AccountCreated', reason: 'created', occurredAt: '2030-01-01T00:00:00Z',
            traceId: 'trace-1', actorKind: 'IdentityAccount', actorAccountEID: 'User_1',
          },
          {
            eid: 'event-0', eventType: 'AccountNamed', reason: 'named', occurredAt: '2029-12-31T00:00:00Z',
            traceId: 'trace-0', actorKind: 'IdentityAccount', actorAccountEID: 'User_1',
          },
        ],
        page: 2,
        pageSize: 20,
        totalCount: 21,
      });

    await renderSummary();
    await vi.waitFor(() => expect(container.textContent).toContain('AccountDisabled'));
    const next = container.querySelector<HTMLButtonElement>('.ant-pagination-next button');
    expect(next).not.toBeNull();
    await act(async () => next?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await vi.waitFor(() => expect(container.textContent).toContain('AccountCreated'));
    expect(container.textContent!.indexOf('AccountCreated')).toBeLessThan(
      container.textContent!.indexOf('AccountNamed'),
    );

    expect(listAccountSecurityEventsMock).toHaveBeenNthCalledWith(
      2, 'User_2', 2, 20, { signal: expect.any(AbortSignal) },
    );
  });

  it('keeps a slow previous account generation from replacing the current account events', async () => {
    const pendingByAccount = new Map<string, (result: EventPage) => void>();
    listAccountSecurityEventsMock.mockImplementation((accountEid: string) => new Promise((resolve) => {
      pendingByAccount.set(accountEid, resolve);
    }));

    await act(async () => {
      root.render(
        <PlatformQueryProvider scopeKey="user-1">
          <IdentityAccountSecurityEventsSummary accountEid="User_A" title="安全事件" />
        </PlatformQueryProvider>,
      );
    });
    await act(async () => {
      root.render(
        <PlatformQueryProvider scopeKey="user-1">
          <IdentityAccountSecurityEventsSummary accountEid="User_B" title="安全事件" />
        </PlatformQueryProvider>,
      );
    });

    expect(listAccountSecurityEventsMock).toHaveBeenNthCalledWith(
      1, 'User_A', 1, 20, { signal: expect.any(AbortSignal) },
    );
    expect(listAccountSecurityEventsMock).toHaveBeenNthCalledWith(
      2, 'User_B', 1, 20, { signal: expect.any(AbortSignal) },
    );

    await act(async () => pendingByAccount.get('User_B')!({
      items: [{
        eid: 'event-b', eventType: 'AccountBChanged', reason: 'B', occurredAt: '2030-01-02T00:00:00Z',
        occurredAt_DisplayValue: null,
        traceId: 'trace-b', actorKind: 'IdentityAccount',
      }],
      page: 1,
      pageSize: 20,
      totalCount: 1,
    }));
    expect(container.textContent).toContain('AccountBChanged');

    await act(async () => pendingByAccount.get('User_A')!({
      items: [{
        eid: 'event-a', eventType: 'AccountAChanged', reason: 'A', occurredAt: '2030-01-01T00:00:00Z',
        occurredAt_DisplayValue: null,
        traceId: 'trace-a', actorKind: 'IdentityAccount',
      }],
      page: 1,
      pageSize: 20,
      totalCount: 1,
    }));
    expect(container.textContent).toContain('AccountBChanged');
    expect(container.textContent).not.toContain('AccountAChanged');
  });

  it('coalesces retry and refresh while retaining the successful event page', async () => {
    let resolveRetry!: (result: EventPage) => void;
    let resolveRefresh!: (result: EventPage) => void;
    const retry = new Promise<EventPage>((resolve) => {
      resolveRetry = resolve;
    });
    const refresh = new Promise<EventPage>((resolve) => {
      resolveRefresh = resolve;
    });
    listAccountSecurityEventsMock
      .mockRejectedValueOnce(new Error('IDENTITY_SECURITY_EVENTS_UNAVAILABLE'))
      .mockReturnValueOnce(retry)
      .mockReturnValueOnce(refresh);

    await renderSummary();
    await vi.waitFor(() => expect(container.textContent).toContain('安全事件加载失败'));

    const retryButton = container.querySelector<HTMLButtonElement>('button[aria-label="重试安全事件"]');
    expect(retryButton).not.toBeNull();
    await act(async () => {
      retryButton!.click();
      retryButton!.click();
    });
    expect(listAccountSecurityEventsMock).toHaveBeenCalledTimes(2);

    await act(async () => resolveRetry({
      items: [{
        eid: 'event-before-refresh', eventType: 'AccountDisabled', reason: 'review',
        occurredAt: '2030-01-02T00:00:00Z', traceId: 'trace-before', actorKind: 'IdentityAccount',
        occurredAt_DisplayValue: null,
      }],
      page: 1,
      pageSize: 20,
      totalCount: 1,
    }));
    expect(container.textContent).toContain('AccountDisabled');

    const refreshButton = container.querySelector<HTMLButtonElement>('button[aria-label="刷新安全事件"]');
    expect(refreshButton).not.toBeNull();
    await act(async () => {
      refreshButton!.click();
      refreshButton!.click();
    });
    expect(listAccountSecurityEventsMock).toHaveBeenCalledTimes(3);
    expect(container.textContent).toContain('AccountDisabled');

    await act(async () => resolveRefresh({
      items: [{
        eid: 'event-after-refresh', eventType: 'AccountEnabled', reason: 'approved',
        occurredAt: '2030-01-03T00:00:00Z', traceId: 'trace-after', actorKind: 'IdentityAccount',
        occurredAt_DisplayValue: null,
      }],
      page: 1,
      pageSize: 20,
      totalCount: 1,
    }));
    expect(container.textContent).toContain('AccountEnabled');
    expect(container.textContent).not.toContain('AccountDisabled');
  });

  it('leaves final-subscriber cancellation to the query runtime grace period', async () => {
    let signal: AbortSignal | undefined;
    listAccountSecurityEventsMock.mockImplementation((
      _accountEid: string,
      _page: number,
      _pageSize: number,
      context: { signal: AbortSignal },
    ) => {
      signal = context.signal;
      return new Promise(() => undefined);
    });

    await renderSummary();
    expect(signal).toBeInstanceOf(AbortSignal);
    expect(signal?.aborted).toBe(false);

    await act(async () => {
      root.render(<PlatformQueryProvider scopeKey="user-1">{null}</PlatformQueryProvider>);
    });
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 60));
    });

    expect(signal?.aborted).toBe(true);
  });
});
