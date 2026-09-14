import { describe, expect, it, vi } from 'vitest';
import { createRuntimeWorkloadStandardCrudResource } from './runtimeWorkloadStandardCrudResource';

describe('runtime workload Standard CRUD resource', () => {
  it('maps the module column API and passes its opaque token to the existing list adapter', async () => {
    const fetcher = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify({
        columns: [{
          key: 'current-week.day-1',
          field: 'currentWeekDay1',
          title: '8月17日',
          valueKind: 'number',
          sortable: true,
          hideable: true,
          apiOnly: 'must-not-leak',
        }],
        contextToken: 'runtime-workload:2026-08-17',
      }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
      .mockResolvedValueOnce(new Response(JSON.stringify({
        items: [{ id: 'member-1', memberName: '陈示例', currentWeekDay1: 1 }],
        total: 1,
        page: 1,
        pageSize: 20,
      }), { status: 200, headers: { 'Content-Type': 'application/json' } }));
    const resource = createRuntimeWorkloadStandardCrudResource(fetcher as unknown as typeof fetch);
    const columnAbortController = new AbortController();
    const rowAbortController = new AbortController();

    const columnSet = await resource.columnProvider?.load({
      filters: { keyword: '陈' },
      signal: columnAbortController.signal,
      featurePosition: 'example-entry',
    });
    const rows = await resource.dataAdapter.list(
      { keyword: '陈', fields: 'id,memberName,currentWeekDay1', pageIndex: 1, pageSize: 20 },
      { columnContextToken: columnSet?.contextToken, signal: rowAbortController.signal, featurePosition: 'example-entry' },
    );

    expect(columnSet).toEqual({
      columns: [{
        key: 'current-week.day-1',
        field: 'currentWeekDay1',
        title: '8月17日',
        valueKind: 'number',
        sortable: true,
        hideable: true,
      }],
      contextToken: 'runtime-workload:2026-08-17',
    });
    expect(fetcher.mock.calls[0]?.[0]).toBe('/api/example/runtime-workload/columns?keyword=%E9%99%88');
    expect(fetcher.mock.calls[0]?.[1]).toMatchObject({ method: 'GET', signal: columnAbortController.signal, headers: { 'X-NewP-Feature': 'example-entry' } });
    expect(fetcher.mock.calls[1]?.[0]).toBe(
      '/api/example/runtime-workload/rows?keyword=%E9%99%88&fields=id%2CmemberName%2CcurrentWeekDay1&pageIndex=1&pageSize=20',
    );
    expect(fetcher.mock.calls[1]?.[1]).toMatchObject({
      method: 'GET',
      headers: { 'X-Example-Column-Context': 'runtime-workload:2026-08-17', 'X-NewP-Feature': 'example-entry' },
      signal: rowAbortController.signal,
    });
    expect(rows.items[0]).toMatchObject({ id: 'member-1', memberName: '陈示例', currentWeekDay1: 1 });
  });

  it('fails before issuing a row request when the column context token is missing', async () => {
    const fetcher = vi.fn();
    const resource = createRuntimeWorkloadStandardCrudResource(fetcher as unknown as typeof fetch);

    await expect(resource.dataAdapter.list({ pageIndex: 1, pageSize: 20 }))
      .rejects.toThrow('Runtime workload rows require a column context token');
    expect(fetcher).not.toHaveBeenCalled();
  });
});
