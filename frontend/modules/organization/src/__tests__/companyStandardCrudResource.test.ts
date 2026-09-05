import { describe, expect, it, vi } from 'vitest';
import {
  companyStandardCrudResource,
  mapCompanyListItem,
} from '../standard-crud/companyStandardCrudResource';

describe('company standard CRUD resource', () => {
  it('binds the company list to the standard mutation lifecycle without handlers or revision', () => {
    expect(companyStandardCrudResource.resourceKey).toBe('organization.company');
    expect(companyStandardCrudResource.listViewKey).toBe('organization.company.list');
    expect(companyStandardCrudResource.actionHandlers).toBeUndefined();
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
