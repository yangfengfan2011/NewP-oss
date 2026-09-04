import { describe, expect, it, vi } from 'vitest';
import {
  departmentStandardCrudResource,
  mapDepartmentListItem,
} from '../standard-crud/departmentStandardCrudResource';

describe('department standard CRUD resource', () => {
  it('binds the root department list to standard mutations and the company lookup only', () => {
    expect(departmentStandardCrudResource.resourceKey).toBe('organization.department');
    expect(departmentStandardCrudResource.listViewKey).toBe('organization.department.list');
    expect(departmentStandardCrudResource.lookupProviders?.map((provider) => provider.key)).toEqual([
      'organization.companies',
    ]);
    expect(departmentStandardCrudResource.actionHandlers).toBeUndefined();
    expect(departmentStandardCrudResource.concurrencyRevisionField).toBeUndefined();
  });

  it('maps canonical company display and preserves SortOrder as a string', () => {
    expect(mapDepartmentListItem({
      EID: 'Department_1',
      CompanyId: 'Company_1',
      CompanyId_DisplayValue: '平台公司',
      ECODE: 'RD',
      ENAME: '研发部',
      IsEnabled: false,
      SortOrder: '01001',
      UpdatedAt: '2026-09-04T00:00:00Z',
    })).toEqual({
      id: 'Department_1',
      EID: 'Department_1',
      CompanyId: 'Company_1',
      CompanyId_DisplayValue: '平台公司',
      ECODE: 'RD',
      ENAME: '研发部',
      IsEnabled: false,
      SortOrder: '01001',
      UpdatedAt: '2026-09-04T00:00:00Z',
    });
  });

  it('uses the standard paged department endpoint', async () => {
    const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({
      items: [{ EID: 'Department_1', CompanyId: 'Company_1', ECODE: 'RD', ENAME: '研发部', SortOrder: '0100' }],
      total: 1,
      page: 1,
      pageSize: 20,
      sortBy: 'SortOrder',
      sortDirection: 'asc',
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }));

    const result = await departmentStandardCrudResource.dataAdapter.list({
      page: 1,
      pageSize: 20,
      sortBy: 'SortOrder',
      sortDirection: 'asc',
      companyId: 'Company_1',
    });

    expect(result.items[0]).toMatchObject({ id: 'Department_1', SortOrder: '0100' });
    expect(String(fetchSpy.mock.calls[0]?.[0])).toContain('/api/organization/departments?');
  });
});
