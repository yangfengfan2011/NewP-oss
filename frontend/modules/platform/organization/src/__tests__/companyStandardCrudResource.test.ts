import { describe, expect, it, vi } from 'vitest';
import { invalidatePlatformAntiforgeryToken } from '@icp-platform/core';
import {
  companyStandardCrudResource,
  mapCompanyListItem,
} from '../standard-crud/companyStandardCrudResource';

describe('company standard CRUD resource', () => {
  it('hides initialization when the trusted maintenance eligibility is denied', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({ allowed: false }), { status: 200 }));
    const resolve = companyStandardCrudResource.actionEligibilityResolvers!.initialize!;
    await expect(resolve({ action: { key: 'initialize', labelKey: 'Action_Initialize' },
      featurePosition: 'organization.company.initialize', signal: new AbortController().signal,
    })).resolves.toEqual({ visible: false, available: false });
  });

  it('initializes the default organization only through one explicit command', async () => {
    invalidatePlatformAntiforgeryToken();
    const fetchSpy = vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(new Response(JSON.stringify({ requestToken: 'csrf-token' }), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({
      companyId: 'company', departmentId: 'department', companyCreated: true, departmentCreated: true,
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }));
    expect(companyStandardCrudResource.actionHandlers?.initialize).toBeTypeOf('function');
    await companyStandardCrudResource.actionHandlers!.initialize({ featurePosition: 'organization.company.initialize', buttonId: 'actual-initialize-button' });
    const writes = fetchSpy.mock.calls.map(call => call[0]).filter((request): request is Request => request instanceof Request && request.method === 'POST');
    expect(writes).toHaveLength(1);
    expect(writes[0]?.url).toContain('/api/organization/initialization');
    expect(writes[0]?.headers.get('X-NewP-Feature')).toBe('organization.company.initialize');
    expect(writes[0]?.headers.get('X-NewP-Button')).toBe('actual-initialize-button');
  });

  it('binds the company list to the standard mutation lifecycle without handlers or revision', () => {
    expect(companyStandardCrudResource.resourceKey).toBe('organization.company');
    expect(companyStandardCrudResource.listViewKey).toBe('organization.company.list');
    expect(companyStandardCrudResource.actionHandlers?.authorize).toBeTypeOf('function');
    expect(companyStandardCrudResource.buildMutationValues).toBeUndefined();
    expect(companyStandardCrudResource.concurrencyRevisionField).toBeUndefined();
  });

  it('maps the canonical standard REST row without parsing SortOrder', () => {
    expect(mapCompanyListItem({
      EID: 'Company_1',
      ECODE: 'ICP',
      ENAME: '平台公司',
      IsEnabled: false,
      SortOrder: '01001',
      UpdatedAt: '2026-09-04T00:00:00Z',
    })).toEqual({
      id: 'Company_1',
      EID: 'Company_1',
      ECODE: 'ICP',
      ENAME: '平台公司',
      IsEnabled: false,
      SortOrder: '01001',
      UpdatedAt: '2026-09-04T00:00:00Z',
    });
  });

  it('uses the standard paged company endpoint', async () => {
    const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({
      items: [{ EID: 'Company_1', ECODE: 'ICP', ENAME: '平台公司', IsEnabled: true, SortOrder: '0100', UpdatedAt: '2026-09-04T07:24:26.5023291Z', UpdatedAt_DisplayValue: '2026/9/4 15:24:26' }],
      total: 1,
      page: 1,
      pageSize: 20,
      sortBy: 'SortOrder',
      sortDirection: 'asc',
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }));

    const result = await companyStandardCrudResource.dataAdapter.list({
      page: 1,
      pageSize: 20,
      sortBy: 'SortOrder',
      sortDirection: 'asc',
    });

    expect(result.items[0]).toMatchObject({ id: 'Company_1', SortOrder: '0100', UpdatedAt: '2026-09-04T07:24:26.5023291Z', UpdatedAt_DisplayValue: '2026/9/4 15:24:26' });
    expect(String(fetchSpy.mock.calls[0]?.[0])).toContain('/api/organization/companies?');
  });
});
