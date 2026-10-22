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

it('carries the independent readonly browse position without replacing an existing entry position', async () => {
  const requests: Request[] = [];
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    requests.push(input instanceof Request ? input : new Request(new URL(String(input), 'http://localhost'), init));
    return new Response(JSON.stringify({ fields: { EID: 'entity' }, summaries: {} }), { headers: { 'Content-Type': 'application/json' } });
  }));
  try {
    await partResource.dataAdapter.get!('entity', { featurePosition: 'existing-owner-read', queryScope: { sourceFeaturePosition: 'carried-owner-read' } });
    await partResource.dataAdapter.get!('entity', { queryScope: { sourceFeaturePosition: 'carried-owner-read' } });
    await partResource.dataAdapter.get!('entity');
    expect(requests.map(request => request.headers.get('X-NewP-Feature'))).toEqual(['existing-owner-read', 'carried-owner-read', null]);
    expect(requests.every(request => new URL(request.url).pathname === '/api/part/parts/entity')).toBe(true);
  } finally { vi.unstubAllGlobals(); }
});

it('qualifies standard authorization at the entity owner endpoint with its management context', async () => {
  const requests: Request[] = [];
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    requests.push(input instanceof Request ? input : new Request(new URL(String(input), 'http://localhost'), init));
    return new Response(JSON.stringify({ allowed: true }), { headers: { 'Content-Type': 'application/json' } });
  }));
  try {
    const result = await partResource.actionEligibilityResolvers?.authorize?.({
      id: 'entity', row: { EID: 'entity', ENAME: 'Selected entity' }, featurePosition: 'owner-authorize', signal: new AbortController().signal,
      action: { key: 'authorize', labelKey: 'Authorize', handlerKey: 'authorize', inputKey: 'objectAuthorization' },
    });
    expect(result).toEqual({ visible: true, available: true });
    expect(requests).toHaveLength(1);
    expect(new URL(requests[0].url).pathname).toBe('/api/part/parts/entity/permissions/eligibility');
    expect(requests[0].headers.get('X-NewP-Feature')).toBe('owner-authorize');
    expect(requests[0].method).toBe('GET');
    expect(requests[0].body).toBeNull();
    const ownership = await partResource.actionEligibilityResolvers?.initializeOwnership?.({
      id: 'entity', featurePosition: 'owner-initialize', signal: new AbortController().signal,
      action: { key: 'initializeOwnership', labelKey: 'InitializeOwnership', handlerKey: 'initializeOwnership' },
    });
    expect(ownership).toEqual({ visible: true, available: true });
    expect(new URL(requests[1].url).pathname).toBe('/api/part/parts/ownership/eligibility');
  } finally { vi.unstubAllGlobals(); }
});
