import { describe, expect, it, vi } from 'vitest';
import {
  buildStandardCrudPath,
  createStandardCrudClient,
  createStandardCrudOperationResultPresenterRegistry,
  createStandardCrudRestDataAdapter,
  normalizeStandardCrudListResult,
  normalizeStandardCrudSummaryBlock,
  readStandardCrudBoolean,
  readStandardCrudString,
  StandardCrudRequestError,
} from '@icp-platform/sdk';

describe('standard CRUD SDK helpers', () => {
  it('resolves an opaque operation result through a module-neutral presenter registry', () => {
    const result = { operation: 'create' as const, payload: { receipt: 'opaque' } };
    const unsupported = {
      supports: vi.fn(() => false),
      render: vi.fn(() => null),
    };
    const supported = {
      supports: vi.fn(() => true),
      render: vi.fn(() => null),
    };

    const registry = createStandardCrudOperationResultPresenterRegistry([unsupported, supported]);

    expect(registry.resolve(result)).toBe(supported);
    expect(unsupported.supports).toHaveBeenCalledWith(result);
    expect(supported.supports).toHaveBeenCalledWith(result);
  });

  it('normalizes list results with stable paging defaults', () => {
    const result = normalizeStandardCrudListResult(
      {
        items: [{ EID: 'Person_1', ENAME: '张三' }],
        total: 7,
        page: 2,
        pageSize: 20,
        sortBy: 'ENAME',
        sortDirection: 'asc',
      },
      (item) => ({ id: readStandardCrudString(item, ['EID']), name: readStandardCrudString(item, ['ENAME']) }),
    );

    expect(result).toEqual({
      items: [{ id: 'Person_1', name: '张三' }],
      total: 7,
      page: 2,
      pageSize: 20,
      sortBy: 'ENAME',
      sortDirection: 'asc',
    });
  });

  it('loads a canonical page through the standard REST data adapter', async () => {
    const fetcher = vi.fn(async () => new Response(JSON.stringify({
      items: [{ EID: 'Person_1', ENAME: '张三' }],
      total: 7,
      page: 2,
      pageSize: 20,
      sortBy: 'ENAME',
      sortDirection: 'asc',
    }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    }));
    const adapter = createStandardCrudRestDataAdapter({
      apiRoute: '/api/organization/persons',
      fetcher: fetcher as unknown as typeof fetch,
      mapListItem: (item) => ({
        id: readStandardCrudString(item, ['EID']),
        name: readStandardCrudString(item, ['ENAME']),
      }),
      getRowId: (row) => row.id,
    });

    const result = await adapter.list({
      pageIndex: 2,
      pageSize: 20,
      sortBy: 'ENAME',
      sortDirection: 'asc',
    });

    expect(result).toEqual({
      items: [{ id: 'Person_1', name: '张三' }],
      total: 7,
      page: 2,
      pageSize: 20,
      sortBy: 'ENAME',
      sortDirection: 'asc',
    });
    expect(adapter.getRowId(result.items[0])).toBe('Person_1');
  });

  it('passes the list read signal to fetch without serializing it into the request URL', async () => {
    const fetcher = vi.fn(async () => new Response(JSON.stringify({
      items: [],
      total: 0,
      page: 1,
      pageSize: 20,
    }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    }));
    const adapter = createStandardCrudRestDataAdapter({
      apiRoute: '/api/organization/persons',
      fetcher: fetcher as unknown as typeof fetch,
      mapListItem: (item) => item,
      getRowId: () => 'Person_1',
    });
    const abortController = new AbortController();

    await adapter.list(
      { pageIndex: 1, pageSize: 20 },
      { signal: abortController.signal },
    );

    expect(fetcher).toHaveBeenCalledWith(
      '/api/organization/persons?pageIndex=1&pageSize=20',
      { method: 'GET', signal: abortController.signal },
    );
    expect(String(fetcher.mock.calls[0][0])).not.toContain('signal');
  });

  it('rejects a column context token instead of silently dropping it in the standard REST adapter', async () => {
    const fetcher = vi.fn();
    const adapter = createStandardCrudRestDataAdapter({
      apiRoute: '/api/sample-items',
      fetcher: fetcher as unknown as typeof fetch,
      mapListItem: (item) => item,
      getRowId: () => 'sample-1',
    });

    await expect(adapter.list({}, { columnContextToken: 'opaque-token' }))
      .rejects.toThrow('Standard REST data adapter does not support a column context token');
    expect(fetcher).not.toHaveBeenCalled();
  });

  it('loads one normalized and module-mapped detail through the data adapter', async () => {
    const fetcher = vi.fn(async () => new Response(JSON.stringify({
      fields: { EID: 'Person_1', ENAME: '张三' },
      summaries: {},
    }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    }));
    const adapter = createStandardCrudRestDataAdapter({
      apiRoute: '/api/organization/persons',
      fetcher: fetcher as unknown as typeof fetch,
      mapListItem: (item) => item,
      getRowId: () => 'Person_1',
      mapDetail: (detail) => ({
        id: readStandardCrudString(detail.fields, ['EID']),
        name: readStandardCrudString(detail.fields, ['ENAME']),
      }),
    });

    const detail = await adapter.get('Person_1');

    expect(detail).toEqual({
      normalized: {
        fields: { EID: 'Person_1', ENAME: '张三' },
        summaries: {},
      },
      value: { id: 'Person_1', name: '张三' },
    });
  });

  it('passes the detail read signal to the standard REST fetch without changing its URL', async () => {
    const fetcher = vi.fn(async () => new Response(JSON.stringify({
      fields: { EID: 'Person_1', ENAME: '张三' },
      summaries: {},
    }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    }));
    const adapter = createStandardCrudRestDataAdapter({
      apiRoute: '/api/organization/persons',
      fetcher: fetcher as unknown as typeof fetch,
      mapListItem: (item) => item,
      getRowId: () => 'Person_1',
    });
    const abortController = new AbortController();

    await adapter.get('Person_1', { signal: abortController.signal });

    expect(fetcher).toHaveBeenCalledWith(
      '/api/organization/persons/Person_1',
      { method: 'GET', signal: abortController.signal },
    );
  });

  it('returns a module-neutral create outcome through the standard REST data adapter', async () => {
    const fetcher = vi.fn(async () => new Response(JSON.stringify({
      fields: { EID: 'Person_1', ENAME: '张三' },
      summaries: {},
    }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    }));
    const adapter = createStandardCrudRestDataAdapter({
      apiRoute: '/api/organization/persons',
      fetcher: fetcher as unknown as typeof fetch,
      mapListItem: (item) => item,
      getRowId: () => 'Person_1',
    });

    const result = await adapter.create({ ENAME: '张三' });

    expect(result).toEqual({
      operation: 'create',
      change: { operation: 'create', id: 'Person_1', after: { EID: 'Person_1', ENAME: '张三' } },
      payload: {
        fields: { EID: 'Person_1', ENAME: '张三' },
        summaries: {},
      },
    });
  });

  it('returns a module-neutral update outcome through the standard REST data adapter', async () => {
    const fetcher = vi.fn(async () => new Response(JSON.stringify({
      fields: { EID: 'Person_1', ENAME: '李四', Revision: 2 },
      summaries: {},
    }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    }));
    const adapter = createStandardCrudRestDataAdapter({
      apiRoute: '/api/organization/persons',
      fetcher: fetcher as unknown as typeof fetch,
      mapListItem: (item) => item,
      getRowId: () => 'Person_1',
    });

    const result = await adapter.update('Person_1', { ENAME: '李四', Revision: 1 });

    expect(result).toEqual({
      operation: 'update',
      change: { operation: 'update', id: 'Person_1', before: undefined, after: { EID: 'Person_1', ENAME: '李四', Revision: 2 } },
      payload: {
        fields: { EID: 'Person_1', ENAME: '李四', Revision: 2 },
        summaries: {},
      },
    });
  });

  it('delegates remove and export through the standard REST data adapter', async () => {
    const fetcher = vi.fn(async (_input: RequestInfo | URL, init?: RequestInit) => {
      if (init?.method === 'DELETE') {
        return new Response(null, { status: 204 });
      }

      return new Response('EID,ENAME\nPerson_1,张三', {
        status: 200,
        headers: { 'Content-Disposition': 'attachment; filename="persons.csv"' },
      });
    });
    const adapter = createStandardCrudRestDataAdapter({
      apiRoute: '/api/organization/persons',
      fetcher: fetcher as unknown as typeof fetch,
      mapListItem: (item) => item,
      getRowId: () => 'Person_1',
    });

    await adapter.remove('Person_1');
    const exported = await adapter.exportList({
      mode: 'all',
      columns: [{ key: 'name', field: 'ENAME', title: '姓名' }],
      query: { pageIndex: 1, pageSize: 20, filters: {} },
    });

    expect(fetcher).toHaveBeenCalledWith('/api/organization/persons/Person_1', { method: 'DELETE' });
    expect(exported.fileName).toBe('persons.csv');
  });

  it('uses item count as list fallback when paging fields are missing', () => {
    const result = normalizeStandardCrudListResult({ items: [{ EID: 'Person_1' }] }, (item) => item);

    expect(result.total).toBe(1);
    expect(result.page).toBe(1);
    expect(result.pageSize).toBe(1);
  });

  it('normalizes summary blocks and legacy array summaries', () => {
    expect(normalizeStandardCrudSummaryBlock({ status: 'ready', data: ['A'] })).toEqual({
      status: 'ready',
      data: ['A'],
    });
    expect(normalizeStandardCrudSummaryBlock([])).toEqual({
      status: 'notConfigured',
      data: null,
    });
  });

  it('fails closed when summary status is unknown', () => {
    expect(normalizeStandardCrudSummaryBlock({ status: 'unexpected', data: ['A'] })).toEqual({
      status: 'dependencyUnavailable',
      data: null,
    });
  });

  it('reads primitive fields from unknown records', () => {
    const value = { EID: 'Person_1', IsEnabled: true };

    expect(readStandardCrudString(value, ['EID'])).toBe('Person_1');
    expect(readStandardCrudString(value, ['Missing'], 'fallback')).toBe('fallback');
    expect(readStandardCrudBoolean(value, ['IsEnabled'], false)).toBe(true);
    expect(readStandardCrudBoolean(value, ['Missing'], true)).toBe(true);
  });

  it('builds API paths with query parameters', () => {
    expect(buildStandardCrudPath('/api/organization/persons', { keyword: 'zhang', empty: '', ignored: null })).toBe(
      '/api/organization/persons?keyword=zhang',
    );
  });

  it('encodes fields as a comma separated standard CRUD query parameter', () => {
    expect(buildStandardCrudPath('/api/organization/persons', {
      pageIndex: 1,
      pageSize: 20,
      fields: 'EID,PersonNo,CompanyName',
    })).toBe('/api/organization/persons?pageIndex=1&pageSize=20&fields=EID%2CPersonNo%2CCompanyName');
  });

  it('creates a fetch-backed standard CRUD client', async () => {
    const fetcher = vi.fn(async () =>
      new Response(JSON.stringify({ fields: { EID: 'Person_1' }, summaries: {} }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    );
    const client = createStandardCrudClient('/api/organization/persons', fetcher);

    const detail = await client.getDetail('Person_1');

    expect(fetcher).toHaveBeenCalledWith('/api/organization/persons/Person_1', { method: 'GET' });
    expect(detail.fields).toEqual({ EID: 'Person_1' });
  });

  it('通过标准客户端删除标准 CRUD 资源', async () => {
    const fetcher = vi.fn().mockResolvedValue(new Response(null, { status: 204 }));
    const client = createStandardCrudClient('/api/sample-items', fetcher as unknown as typeof fetch);

    await client.delete('item-1');

    expect(fetcher).toHaveBeenCalledWith('/api/sample-items/item-1', { method: 'DELETE' });
  });

  it('surfaces the backend export limit message', async () => {
    const fetcher = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      code: 'StandardCrud.Export.LimitExceeded',
      message: '筛选结果超过同步导出上限 10000 行，请缩小筛选范围后重试。',
    }), {
      status: 400,
      headers: { 'Content-Type': 'application/json' },
    }));
    const client = createStandardCrudClient('/api/sample-items', fetcher as unknown as typeof fetch);

    await expect(client.exportList({
      mode: 'all',
      columns: [{ key: 'name', field: 'ENAME', title: '名称' }],
      query: { pageIndex: 1, pageSize: 20, filters: {} },
    })).rejects.toThrow('筛选结果超过同步导出上限 10000 行');
  });

  it('preserves conflict status and field errors from standard CRUD problem details', async () => {
    const fetcher = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      title: '记录已经变化',
      errors: { ENAME: ['姓名不能为空'] },
    }), {
      status: 409,
      headers: { 'Content-Type': 'application/problem+json' },
    }));
    const client = createStandardCrudClient('/api/sample-items', fetcher as unknown as typeof fetch);

    await expect(client.update('item-1', { ENAME: '' })).rejects.toMatchObject<Partial<StandardCrudRequestError>>({
      status: 409,
      fieldErrors: { ENAME: '姓名不能为空' },
    });
  });

  it('maps the backend standard CRUD fieldErrors array into field feedback', async () => {
    const fetcher = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      code: 'Organization.Person.NameRequired',
      message: '人员姓名不能为空。',
      fieldErrors: [
        { field: 'ENAME', code: 'Organization.Person.NameRequired', message: '人员姓名不能为空。' },
      ],
    }), {
      status: 400,
      headers: { 'Content-Type': 'application/problem+json' },
    }));
    const client = createStandardCrudClient('/api/organization/persons', fetcher as unknown as typeof fetch);

    await expect(client.update('Person_1', { ENAME: '' })).rejects.toMatchObject<Partial<StandardCrudRequestError>>({
      status: 400,
      fieldErrors: { ENAME: '人员姓名不能为空。' },
    });
  });

  it('reports a lost successful write response as uncertain without replaying', async () => {
    const fetcher = vi.fn().mockResolvedValue(new Response('{', { status: 200 }));
    const client = createStandardCrudClient('/api/sample-items', fetcher);
    await expect(client.update('item-1', {})).rejects.toThrow('请先查询实际结果，再决定是否重试');
    expect(fetcher).toHaveBeenCalledOnce();
  });

  it('does not label an authorization dependency outage as a revocation', async () => {
    const fetcher = vi.fn().mockResolvedValue(new Response('{"title":"AUTHORIZATION_UNAVAILABLE"}', { status: 503 }));
    await expect(createStandardCrudClient('/api/sample-items', fetcher).update('item-1', {})).rejects.toThrow('AUTHORIZATION_UNAVAILABLE');
    expect(fetcher).toHaveBeenCalledOnce();
  });

  it.each([
    [401, '登录状态已失效'],
    [403, '当前操作已被拒绝'],
  ])('keeps %s authentication feedback explicit', async (status, expectedMessage) => {
    const client = createStandardCrudClient('/api/sample-items', vi.fn().mockResolvedValue(
      new Response(null, { status }),
    ) as unknown as typeof fetch);

    await expect(client.update('item-1', {})).rejects.toThrow(expectedMessage);
  });
});
