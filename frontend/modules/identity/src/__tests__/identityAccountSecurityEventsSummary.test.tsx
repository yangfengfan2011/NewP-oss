// @vitest-environment jsdom

import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const listAccountSecurityEventsMock = vi.hoisted(() => vi.fn());
vi.mock('../api/identityClient', async (importOriginal) => ({
  ...await importOriginal<typeof import('../api/identityClient')>(),
  listAccountSecurityEvents: listAccountSecurityEventsMock,
}));

import { IdentityAccountSecurityEventsSummary } from '../standard-crud/IdentityAccountSecurityEventsSummary';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
const nativeGetComputedStyle = window.getComputedStyle.bind(window);

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
      root.render(<IdentityAccountSecurityEventsSummary accountEid="User_2" title="安全事件" />);
    });
  }

  it('renders a stable empty state without changing ordinary detail fields', async () => {
    listAccountSecurityEventsMock.mockResolvedValue({ items: [], page: 1, pageSize: 20, totalCount: 0 });

    await renderSummary();
    await vi.waitFor(() => expect(container.textContent).toContain('暂无安全事件'));

    expect(listAccountSecurityEventsMock).toHaveBeenCalledWith('User_2', 1, 20);
    expect(container.querySelector('.ant-empty')).not.toBeNull();
  });

  it('renders an isolated failure state when the event query fails', async () => {
    listAccountSecurityEventsMock.mockRejectedValue(new Error('IDENTITY_SECURITY_EVENTS_UNAVAILABLE'));

    await renderSummary();
    await vi.waitFor(() => expect(container.textContent).toContain('安全事件加载失败'));

    expect(container.querySelector('.ant-alert-error')).not.toBeNull();
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
        items: [{
          eid: 'event-1', eventType: 'AccountCreated', reason: 'created', occurredAt: '2030-01-01T00:00:00Z',
          traceId: 'trace-1', actorKind: 'IdentityAccount', actorAccountEID: 'User_1',
        }],
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

    expect(listAccountSecurityEventsMock).toHaveBeenNthCalledWith(2, 'User_2', 2, 20);
  });
});
