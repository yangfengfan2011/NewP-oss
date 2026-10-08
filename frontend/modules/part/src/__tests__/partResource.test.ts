import { expect, it, vi } from 'vitest';
import { partResource } from '../partResource';

it('keeps the stable entity identity and canonical display fields in the standard adapter', async () => {
  const fetcher = vi.fn(async (_input: RequestInfo | URL) => new Response(JSON.stringify({ items: [{ EID: 'part-a', ECODE: 'A', ENAME: '零组件', UpdatedBy: 'actor', UpdatedBy_DisplayValue: '账号' }], total: 1, page: 1, pageSize: 20 }), { headers: { 'Content-Type': 'application/json' } }));
  vi.stubGlobal('fetch', fetcher);
  try {
    const list = await partResource.dataAdapter.list({ page: 1, pageSize: 20 });
    expect(list.items[0]).toMatchObject({ id: 'part-a', EID: 'part-a', UpdatedBy: 'actor', UpdatedBy_DisplayValue: '账号' });
    expect(partResource.dataAdapter.getRowId(list.items[0])).toBe('part-a');
    expect(String(fetcher.mock.calls[0][0])).toContain('/api/part/parts');
  } finally { vi.unstubAllGlobals(); }
});
