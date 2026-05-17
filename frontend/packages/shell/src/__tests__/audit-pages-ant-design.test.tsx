// @vitest-environment jsdom

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import OperationLogs from '../../../../modules/audit/src/pages/OperationLogs';
import AccessLogs from '../../../../modules/audit/src/pages/AccessLogs';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;

async function flushProTableRequest(): Promise<void> {
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 0));
  });
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 0));
  });
}

function installAntdTestShims(): void {
  const getComputedStyle = window.getComputedStyle.bind(window);
  Object.defineProperty(window, 'matchMedia', {
    writable: true,
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
  Object.defineProperty(window, 'getComputedStyle', {
    writable: true,
    value: (element: Element, pseudoElement?: string | null) => {
      if (pseudoElement) {
        return {
          getPropertyValue: () => '',
          width: '0px',
          height: '0px',
        };
      }

      return getComputedStyle(element);
    },
  });
}

function renderPage(element: React.ReactNode, container: HTMLDivElement): Root {
  const root = createRoot(container);
  root.render(element);
  return root;
}

describe('audit pages Ant Design rebuild', () => {
  let container: HTMLDivElement;
  let root: Root | null;

  beforeEach(() => {
    vi.restoreAllMocks();
    installAntdTestShims();
    container = document.createElement('div');
    document.body.appendChild(container);
    root = null;
  });

  afterEach(async () => {
    if (root) {
      await act(async () => {
        root.unmount();
      });
    }

    container.remove();
    vi.clearAllMocks();
    root = null;
  });

  it('renders operation logs through the Pro table adapter', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({
      items: [{
        id: 1,
        operationTime: '2026-05-17T10:00:00Z',
        userName: '审计员',
        moduleId: 'organization',
        operationType: 'Update',
        operationTarget: 'Person',
        result: 'Success',
        clientIp: '127.0.0.1',
      }],
      total: 1,
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }));

    await act(async () => {
      root = renderPage(<OperationLogs />, container);
    });
    await flushProTableRequest();

    expect(fetch).toHaveBeenCalledWith('/api/audit/operation-logs?pageIndex=1&pageSize=20');
    expect(container.querySelector('.ant-pro-table')).not.toBeNull();
    expect(container.querySelector('.ant-table')).not.toBeNull();
    expect(container.querySelector('.ant-tag')).not.toBeNull();
    expect(container.textContent).toContain('操作日志');
    expect(container.textContent).toContain('成功');
  });

  it('renders access logs through the Pro table adapter', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({
      items: [{
        id: 2,
        accessTime: '2026-05-17T10:01:00Z',
        userName: '审计员',
        requestPath: '/api/audit/access-logs',
        httpMethod: 'GET',
        statusCode: 200,
        responseTime: 18,
        clientIp: '127.0.0.1',
      }],
      total: 1,
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }));

    await act(async () => {
      root = renderPage(<AccessLogs />, container);
    });
    await flushProTableRequest();

    expect(fetch).toHaveBeenCalledWith('/api/audit/access-logs?pageIndex=1&pageSize=20');
    expect(container.querySelector('.ant-pro-table')).not.toBeNull();
    expect(container.querySelector('.ant-table')).not.toBeNull();
    expect(container.querySelector('.ant-tag')).not.toBeNull();
    expect(container.textContent).toContain('访问日志');
    expect(container.textContent).toContain('18ms');
  });

  it('uses Ant Design loading, empty, and error states for audit tables', async () => {
    const fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(() => new Promise<Response>(() => undefined));

    await act(async () => {
      root = renderPage(<OperationLogs />, container);
    });
    await flushProTableRequest();

    expect(container.querySelector('.ant-spin')).not.toBeNull();

    await act(async () => {
      root.unmount();
    });
    container.innerHTML = '';

    fetchSpy.mockReset();
    fetchSpy.mockResolvedValueOnce(new Response(JSON.stringify({ items: [], total: 0 }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    }));

    await act(async () => {
      root = renderPage(<OperationLogs />, container);
    });
    await flushProTableRequest();

    expect(container.querySelector('.ant-empty')).not.toBeNull();

    await act(async () => {
      root.unmount();
    });
    container.innerHTML = '';

    fetchSpy.mockReset();
    fetchSpy.mockRejectedValueOnce(new Error('审计日志加载失败'));

    await act(async () => {
      root = renderPage(<AccessLogs />, container);
    });
    await flushProTableRequest();

    expect(container.querySelector('.ant-alert')).not.toBeNull();
    expect(container.textContent).toContain('审计日志加载失败');
  });
});
