import { afterEach, describe, expect, it, vi } from 'vitest';
import { mapPersonListItem } from '../services/organizationService';
import { personStandardCrudResource } from '../standard-crud/personStandardCrudResource';

describe('personnel center first vertical slice', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('keeps the resource route readable with View while metadata gates write actions', () => {
    expect(personStandardCrudResource.permissions).toEqual(['Organization.Person.View']);
  });

  it('includes disabled organization only for query lookup and marks its state', async () => {
    vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(new Response(JSON.stringify([
        { id: 'company-1', name: '总部', isEnabled: true },
        { id: 'company-2', name: '停用公司', isEnabled: false },
      ]), { status: 200, headers: { 'Content-Type': 'application/json' } }))
      .mockResolvedValueOnce(new Response(JSON.stringify([
        { id: 'department-1', companyId: 'company-1', name: '研发部', isEnabled: true },
        { id: 'department-2', companyId: 'company-1', name: '停用部门', isEnabled: false },
      ]), { status: 200, headers: { 'Content-Type': 'application/json' } }))
      .mockResolvedValueOnce(new Response(JSON.stringify([
        { id: 'company-1', name: '总部', isEnabled: true },
        { id: 'company-2', name: '停用公司', isEnabled: false },
      ]), { status: 200, headers: { 'Content-Type': 'application/json' } }));

    const companyProvider = personStandardCrudResource.lookupProviders?.find(
      provider => provider.key === 'organization.companies',
    );
    const departmentProvider = personStandardCrudResource.lookupProviders?.find(
      provider => provider.key === 'organization.departments',
    );

    await expect(companyProvider?.load({ fieldKey: 'companyId', values: {}, dependencyValues: {} })).resolves.toEqual([
      { value: 'company-1', label: '总部' },
      { value: 'company-2', label: '停用公司（已停用）' },
    ]);
    await expect(departmentProvider?.load({
      fieldKey: 'departmentId',
      values: { companyId: 'company-1' },
      dependencyValues: { companyId: 'company-1' },
    })).resolves.toEqual([
      { value: 'department-1', label: '研发部' },
      { value: 'department-2', label: '停用部门（已停用）' },
    ]);

    await expect(companyProvider?.load({ fieldKey: 'CompanyId', values: {}, dependencyValues: {} })).resolves.toEqual([
      { value: 'company-1', label: '总部' },
    ]);
  });

  it('projects display names without falling back to raw organization ids', () => {
    const row = mapPersonListItem({
      EID: 'person-1',
      PersonNo: 'P001',
      ENAME: '张三',
      CompanyId: 'company-1',
      CompanyName: '总部',
      DepartmentId: 'department-1',
      DepartmentName: '研发部',
      EmploymentStatus: 'FullTime',
      IsEnabled: true,
    }) as unknown as Record<string, unknown>;
    const unresolved = mapPersonListItem({
      EID: 'person-2',
      PersonNo: 'P002',
      ENAME: '李四',
      CompanyId: 'company-secret-id',
      DepartmentId: 'department-secret-id',
      EmploymentStatus: 'FullTime',
      IsEnabled: true,
    });

    expect(row.CompanyName).toBe('总部');
    expect(row.DepartmentName).toBe('研发部');
    expect(unresolved.companyName).toBe('');
    expect(unresolved.departmentName).toBe('');
  });

  it('requires an explicit disable reason before invoking the lifecycle command', async () => {
    const handler = personStandardCrudResource.actionHandlers?.disable;

    await expect(handler?.({
      id: 'person-1',
      row: { id: 'person-1', revision: 4 } as never,
      input: '   ',
      refresh: vi.fn(),
    })).rejects.toThrow('请填写停用原因');
  });
});
