import { describe, expect, it, vi } from 'vitest';
import {
  departmentStandardCrudResource,
  mapDepartmentListItem,
} from '../standard-crud/departmentStandardCrudResource';

describe('department standard CRUD resource', () => {
  it('binds the hierarchy list to standard mutations and Organization-owned dependent lookups', () => {
    expect(departmentStandardCrudResource.resourceKey).toBe('organization.department');
    expect(departmentStandardCrudResource.listViewKey).toBe('organization.department.list');
    expect(departmentStandardCrudResource.lookupProviders?.map((provider) => provider.key)).toEqual([
      'organization.companies',
      'organization.department.parents',
      'organization.department.parent-filter',
    ]);
    expect(departmentStandardCrudResource.actionHandlers).toBeUndefined();
    expect(departmentStandardCrudResource.concurrencyRevisionField).toBeUndefined();
  });

  it('maps canonical company display and preserves SortOrder as a string', () => {
    expect(mapDepartmentListItem({
      EID: 'Department_1',
      CompanyId: 'Company_1',
      CompanyId_DisplayValue: '平台公司',
      ParentDepartmentId: 'Department_Parent',
      ParentDepartmentId_DisplayValue: '研发中心',
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
      ParentDepartmentId: 'Department_Parent',
      ParentDepartmentId_DisplayValue: '研发中心',
      ECODE: 'RD',
      ENAME: '研发部',
      IsEnabled: false,
      SortOrder: '01001',
      UpdatedAt: '2026-09-04T00:00:00Z',
    });
  });

  it('loads parent candidates with company, immutable code, keyword, page and current value', async () => {
    const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({
      items: [{ value: 'Department_Parent', label: '研发中心' }],
      currentItems: [{ value: 'Department_Disabled', label: '旧中心（已停用）', disabled: true }],
      page: 2,
      pageSize: 20,
      hasMore: true,
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }));
    const provider = departmentStandardCrudResource.lookupProviders?.find(
      (candidate) => candidate.key === 'organization.department.parents',
    );

    const result = await provider?.load({
      fieldKey: 'ParentDepartmentId',
      keyword: '研发',
      page: 2,
      pageSize: 20,
      currentValueIds: ['Department_Disabled'],
      values: { CompanyId: 'Company_1', ECODE: 'RD' },
      dependencyValues: { CompanyId: 'Company_1' },
    });

    expect(result).toMatchObject({ page: 2, hasMore: true });
    const url = String(fetchSpy.mock.calls[0]?.[0]);
    expect(url).toContain('/api/organization/department-lookups/parents?');
    expect(url).toContain('companyId=Company_1');
    expect(url).toContain('currentDepartmentCode=RD');
    expect(url).toContain('keyword=%E7%A0%94%E5%8F%91');
    expect(url).toContain('currentValueIds=Department_Disabled');
  });

  it('keeps the parent filter empty until a company is selected, then includes disabled values', async () => {
    const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({
      items: [],
      currentItems: [],
      page: 1,
      pageSize: 20,
      hasMore: false,
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }));
    const provider = departmentStandardCrudResource.lookupProviders?.find(
      (candidate) => candidate.key === 'organization.department.parent-filter',
    );
    const request = {
      fieldKey: 'parentDepartmentId',
      keyword: '',
      page: 1,
      pageSize: 20,
      currentValueIds: [],
      values: {},
      dependencyValues: {},
    };

    const empty = await provider?.load(request);
    await provider?.load({ ...request, dependencyValues: { companyId: 'Company_1' } });

    expect(empty?.options).toEqual([]);
    expect(fetchSpy).toHaveBeenCalledTimes(1);
    const url = String(fetchSpy.mock.calls[0]?.[0]);
    expect(url).toContain('companyId=Company_1');
    expect(url).toContain('includeDisabled=true');
    expect(url).not.toContain('currentDepartmentCode');
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
