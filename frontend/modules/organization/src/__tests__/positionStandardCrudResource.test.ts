import { describe, expect, it, vi } from 'vitest';
import {
  mapPositionListItem,
  positionStandardCrudResource,
} from '../standard-crud/positionStandardCrudResource';

describe('position standard CRUD resource', () => {
  it('binds the metadata list view to the standard mutation lifecycle without handlers or revision', () => {
    expect(positionStandardCrudResource.resourceKey).toBe('organization.position');
    expect(positionStandardCrudResource.listViewKey).toBe('organization.position.list');
    expect(positionStandardCrudResource.actionHandlers).toBeUndefined();
    expect(positionStandardCrudResource.buildMutationValues).toBeUndefined();
    expect(positionStandardCrudResource.concurrencyRevisionField).toBeUndefined();
  });

  it('maps the standard REST row shape', () => {
    expect(mapPositionListItem({
      EID: 'Position_1',
      ENAME: '平台主管',
      Description: '负责平台规划',
      IsEnabled: false,
    })).toEqual({
      id: 'Position_1',
      EID: 'Position_1',
      ENAME: '平台主管',
      Description: '负责平台规划',
      IsEnabled: false,
    });
  });

  it('uses the standard REST list endpoint', async () => {
    const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({
      items: [{ EID: 'Position_1', ENAME: '平台主管', Description: null, IsEnabled: true }],
      total: 1,
      page: 1,
      pageSize: 20,
      sortBy: 'ENAME',
      sortDirection: 'asc',
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }));

    const result = await positionStandardCrudResource.dataAdapter.list({
      page: 1,
      pageSize: 20,
      sortBy: 'ENAME',
      sortDirection: 'asc',
    });

    expect(result.items[0]).toMatchObject({ id: 'Position_1', ENAME: '平台主管' });
    expect(String(fetchSpy.mock.calls[0]?.[0])).toContain('/api/organization/positions?');
  });

  it('uses the standard REST update endpoint without a revision field', async () => {
    const fetchSpy = vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(new Response(JSON.stringify({ requestToken: 'csrf-token' }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }))
      .mockResolvedValueOnce(new Response(JSON.stringify({
        fields: {
          EID: 'Position_1',
          ENAME: '平台主管',
          Description: null,
          IsEnabled: false,
        },
        summaries: {},
      }), { status: 200, headers: { 'Content-Type': 'application/json' } }));

    await positionStandardCrudResource.dataAdapter.update?.('Position_1', {
      ENAME: '平台主管',
      Description: null,
      IsEnabled: false,
    });

    const request = fetchSpy.mock.calls[1]?.[0] as Request;
    expect(request.url).toContain('/api/organization/positions/Position_1');
    expect(request.method).toBe('PUT');
    await expect(request.clone().json()).resolves.toEqual({
      ENAME: '平台主管',
      Description: null,
      IsEnabled: false,
    });
  });

  it('uses the standard REST delete endpoint', async () => {
    const fetchSpy = vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(new Response(JSON.stringify({ requestToken: 'csrf-token' }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }))
      .mockResolvedValueOnce(new Response(null, { status: 204 }));

    await positionStandardCrudResource.dataAdapter.remove?.('Position_1');

    const request = fetchSpy.mock.calls[fetchSpy.mock.calls.length - 1]?.[0] as Request;
    expect(request.url).toContain('/api/organization/positions/Position_1');
    expect(request.method).toBe('DELETE');
  });
});
