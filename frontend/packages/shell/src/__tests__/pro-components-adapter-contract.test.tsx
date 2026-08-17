// @vitest-environment jsdom

import { act } from 'react';
import { createRoot } from 'react-dom/client';
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
