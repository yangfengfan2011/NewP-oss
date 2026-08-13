import { describe, expect, it, vi } from 'vitest';
import {
  buildStandardCrudPath,
  createStandardCrudClient,
  normalizeStandardCrudListResult,
  normalizeStandardCrudSummaryBlock,
  readStandardCrudBoolean,
  readStandardCrudString,
  StandardCrudRequestError,
} from '@icp-platform/sdk';

describe('standard CRUD SDK helpers', () => {
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

  it.each([
    [401, '登录状态已失效'],
    [403, '权限已变化'],
  ])('keeps %s authentication feedback explicit', async (status, expectedMessage) => {
    const client = createStandardCrudClient('/api/sample-items', vi.fn().mockResolvedValue(
      new Response(null, { status }),
    ) as unknown as typeof fetch);

    await expect(client.update('item-1', {})).rejects.toThrow(expectedMessage);
  });
});
