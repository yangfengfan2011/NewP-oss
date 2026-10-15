// @vitest-environment jsdom

import { act, StrictMode } from 'react';
import { createLocalizedRoot as createRoot } from './localizedTestRoot';
import { describe, expect, it, vi } from 'vitest';
import { SearchTablePage, createSearchTableRequest } from '@icp-platform/ui';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;

describe('Pro Components adapter contract', () => {
  it('keeps platform query builder as the request source of truth', async () => {
    const request = createSearchTableRequest({
      buildQuery: (filters: { keyword: string }) => ({ keyword: filters.keyword.trim() }),
      load: async (query) => ({
        items: [{ id: '1', name: `用户-${query.keyword}` }],
        total: 1,
      }),
      mapRow: (row: { id: string; name: string }) => row,
    });

    const result = await request({
      keyword: ' Alice ',
      current: 1,
      pageSize: 20,
      sortBy: 'PersonNo',
      sortDirection: 'desc',
    });

    expect(result.success).toBe(true);
    expect(result.total).toBe(1);
    expect(result.data[0].name).toBe('用户-Alice');
  });

  it('loads an identical initial query once while StrictMode remounts effects', async () => {
    let resolveRequest!: (value: { data: Array<{ EID: string; name: string }>; total: number; success: boolean }) => void;
    const pendingRequest = new Promise<{ data: Array<{ EID: string; name: string }>; total: number; success: boolean }>((resolve) => {
      resolveRequest = resolve;
    });
    const request = vi.fn(() => pendingRequest);
    const container = document.createElement('div');
    document.body.appendChild(container);
    const root = createRoot(container);

    try {
      await act(async () => {
        root.render(
          <StrictMode>
            <SearchTablePage
              rowKey="EID"
              columns={[{ key: 'name', dataIndex: 'name', title: '姓名' }]}
              request={request}
              query={{ current: 1, pageSize: 20 }}
            />
          </StrictMode>,
        );
        await Promise.resolve();
      });

      expect(request).toHaveBeenCalledTimes(1);
      expect(request).toHaveBeenCalledWith({ current: 1, pageSize: 20 });
    } finally {
      await act(async () => {
        resolveRequest({ data: [{ EID: 'Person_1', name: '张三' }], total: 1, success: true });
        await pendingRequest;
      });
      await act(async () => root.unmount());
      container.remove();
    }
  });

  it('starts the same query again after a failed StrictMode load is retried', async () => {
    const request = vi.fn()
      .mockRejectedValueOnce(new Error('临时加载失败'))
      .mockResolvedValueOnce({ data: [{ EID: 'Person_1', name: '恢复结果' }], total: 1, success: true });
    const container = document.createElement('div');
    document.body.appendChild(container);
    const root = createRoot(container);

    try {
      await act(async () => {
        root.render(
          <StrictMode>
            <SearchTablePage
              rowKey="EID"
              columns={[{ key: 'name', dataIndex: 'name', title: '姓名' }]}
              request={request}
              query={{ current: 1, pageSize: 20 }}
            />
          </StrictMode>,
        );
        await Promise.resolve();
        await Promise.resolve();
      });

      expect(request).toHaveBeenCalledTimes(1);
      expect(container.textContent).toContain('临时加载失败');

      const retryButton = Array.from(container.querySelectorAll('button'))
        .find((button) => button.textContent?.replaceAll(' ', '') === '重试');
      expect(retryButton).toBeDefined();

      await act(async () => {
        retryButton?.click();
        await Promise.resolve();
        await Promise.resolve();
      });

      expect(request).toHaveBeenCalledTimes(2);
      expect(container.textContent).toContain('恢复结果');
    } finally {
      await act(async () => root.unmount());
      container.remove();
    }
  });

  it('only commits the latest response and exposes sorting only for declared columns', async () => {
    let resolveSlow: ((value: { data: Array<{ EID: string; name: string }>; total: number; success: boolean }) => void) | undefined;
    const request = vi.fn((params: Record<string, unknown>) => {
      if (params.current === 1) {
        return new Promise<{ data: Array<{ EID: string; name: string }>; total: number; success: boolean }>((resolve) => {
          resolveSlow = resolve;
        });
      }

      return Promise.resolve({ data: [{ EID: 'Person_2', name: '最新结果' }], total: 1, success: true });
    });
    const onLoadResult = vi.fn();
    const container = document.createElement('div');
    document.body.appendChild(container);
    const root = createRoot(container);
    const commonProps = {
      rowKey: 'EID',
      columns: [
        { key: 'name', dataIndex: 'name', title: '姓名' },
        { key: 'personNo', dataIndex: 'PersonNo', title: '人员编号', sorter: true },
      ],
      request,
      onQueryChange: vi.fn(),
      onLoadResult,
    };

    await act(async () => {
      root.render(<SearchTablePage {...commonProps} query={{ current: 1, pageSize: 20 }} />);
    });
    await act(async () => {
      root.render(<SearchTablePage {...commonProps} query={{ current: 2, pageSize: 20 }} />);
      await Promise.resolve();
    });
    expect(request).toHaveBeenCalledTimes(2);
    expect(request).toHaveBeenLastCalledWith({ current: 2, pageSize: 20 });
    await act(async () => {
      resolveSlow?.({ data: [{ EID: 'Person_1', name: '过期结果' }], total: 40, success: true });
      await Promise.resolve();
    });

    expect(container.textContent).toContain('最新结果');
    expect(container.textContent).not.toContain('过期结果');
    expect(onLoadResult).toHaveBeenCalledTimes(1);
    expect(container.querySelectorAll('.ant-table-column-has-sorters')).toHaveLength(1);
    expect(container.querySelector('.ant-pagination')).not.toBeNull();

    await act(async () => root.unmount());
    container.remove();
  });

  it('reflects controlled server sorting and emits the next direction', async () => {
    const onQueryChange = vi.fn();
    const container = document.createElement('div');
    document.body.appendChild(container);
    const root = createRoot(container);

    await act(async () => {
      root.render(
        <SearchTablePage
          rowKey="EID"
          columns={[{ key: 'personNo', dataIndex: 'PersonNo', title: '人员编号', sorter: true }]}
          request={async () => ({
            data: [{ EID: 'Person_1', PersonNo: 'P001' }],
            total: 1,
            success: true,
          })}
          query={{ current: 1, pageSize: 20, sortBy: 'PersonNo', sortDirection: 'asc' }}
          onQueryChange={onQueryChange}
          columnState={{ personNo: { show: true, order: 0 } }}
          defaultColumnState={{ personNo: { show: true, order: 0 } }}
          onColumnStateChange={vi.fn()}
        />,
      );
      await Promise.resolve();
    });

    const header = container.querySelector('.ant-table-thead th.ant-table-column-has-sorters');
    expect(header?.querySelector('.ant-table-column-sorter-up.active')).not.toBeNull();

    await act(async () => {
      header?.querySelector<HTMLElement>('.ant-table-column-sorters')?.click();
    });
    expect(onQueryChange).toHaveBeenCalledWith(expect.objectContaining({
      sortBy: 'PersonNo',
      sortDirection: 'desc',
    }));

    await act(async () => root.unmount());
    container.remove();
  });

  it('disables the broken density control while keeping column settings', async () => {
    const container = document.createElement('div');
    document.body.appendChild(container);
    const root = createRoot(container);

    await act(async () => {
      root.render(
        <SearchTablePage
          rowKey="EID"
          columns={[{ key: 'name', dataIndex: 'name', title: '姓名' }]}
          request={async () => ({ data: [], total: 0, success: true })}
          query={{ current: 1, pageSize: 20 }}
          toolbar={<span>列表操作</span>}
        />,
      );
      await Promise.resolve();
    });

    expect(container.querySelector('[data-icon="column-height"]')).toBeNull();
    expect(container.querySelector('[data-icon="setting"]')).not.toBeNull();

    await act(async () => root.unmount());
    container.remove();
  });
});

it('resizes only the standard table body while reserving toolbar, header and pagination height', async () => {
  let height = 600;
  const observers: Array<{ notify: () => void; targets: Set<Element> }> = [];
  vi.stubGlobal('ResizeObserver', class {
    targets = new Set<Element>();
    constructor(callback: () => void) { observers.push({ notify: callback, targets: this.targets }); }
    observe = (element: Element) => { this.targets.add(element); };
    unobserve = (element: Element) => { this.targets.delete(element); };
    disconnect = () => { this.targets.clear(); };
  });
  const clientHeight = vi.spyOn(HTMLElement.prototype, 'clientHeight', 'get').mockImplementation(function (this: HTMLElement) {
    return this.classList.contains('np-search-table-page') ? height : 0;
  });
  const bounds = vi.spyOn(Element.prototype, 'getBoundingClientRect').mockImplementation(function (this: Element) {
    const measured = this.matches('.ant-pro-table-list-toolbar') ? 40 : this.matches('.ant-table-thead') ? 30
      : this.matches('.np-search-table-footer') ? 44 : 0;
    return { x: 0, y: 0, top: 0, left: 0, right: 800, bottom: measured, width: 800, height: measured, toJSON: () => ({}) };
  });
  const container = document.createElement('div'); document.body.append(container);
  const root = createRoot(container);
  try {
    await act(async () => root.render(<StrictMode><div className="np-crud-table-section"><SearchTablePage rowKey="id"
      columns={[{ key: 'name', dataIndex: 'name', title: 'Name' }]} headerTitle="Actions" toolbar={<span>Refresh</span>}
      request={async () => ({ data: [], total: 0, success: true })}
      controlledResult={{ data: [{ id: 'one', name: 'One' }], total: 1, loading: false, hasLoaded: true, error: null, onRetry: () => undefined }}
    /></div></StrictMode>));
    expect(container.querySelector<HTMLElement>('.ant-table-body')?.style.maxHeight).toBe('486px');
    height = 400;
    await act(async () => { observers.filter(observer => [...observer.targets].some(element => element.classList.contains('np-search-table-page'))).forEach(observer => observer.notify()); });
    expect(container.querySelector<HTMLElement>('.ant-table-body')?.style.maxHeight).toBe('286px');
    expect(container.querySelector('.ant-table-body .np-search-table-footer')).toBeNull();
  } finally {
    await act(async () => root.unmount()); container.remove(); clientHeight.mockRestore(); bounds.mockRestore(); vi.unstubAllGlobals();
  }
  expect(observers.every(observer => observer.targets.size === 0)).toBe(true);
});
