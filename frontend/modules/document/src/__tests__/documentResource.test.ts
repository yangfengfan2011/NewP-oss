import { expect, it, vi } from 'vitest';
import { documentResource } from '../documentResource';

it('keeps the stable entity identity and canonical display fields in the standard adapter', async () => {
  const fetcher = vi.fn(async (_input: RequestInfo | URL) => new Response(JSON.stringify({ items: [{ EID: 'document-a', ECODE: 'A', ENAME: '图文档', UpdatedBy: 'actor', UpdatedBy_DisplayValue: '账号' }], total: 1, page: 1, pageSize: 20 }), { headers: { 'Content-Type': 'application/json' } }));
  vi.stubGlobal('fetch', fetcher);
  try {
    const list = await documentResource.dataAdapter.list({ page: 1, pageSize: 20 });
    expect(list.items[0]).toMatchObject({ id: 'document-a', EID: 'document-a', UpdatedBy: 'actor', UpdatedBy_DisplayValue: '账号' });
    expect(documentResource.dataAdapter.getRowId(list.items[0])).toBe('document-a');
    expect(String(fetcher.mock.calls[0][0])).toContain('/api/document/documents');
  } finally { vi.unstubAllGlobals(); }
});
