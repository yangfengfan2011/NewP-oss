import { afterEach, describe, expect, it, vi } from 'vitest';
import { mapPersonDetailView, mapPersonListItem } from '../services/organizationService';
import { personStandardCrudResource } from '../standard-crud/personStandardCrudResource';

describe('personnel center first vertical slice', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('leaves route authorization to the runtime metadata projection', () => {
    expect(personStandardCrudResource.permissions).toBeUndefined();
    expect(personStandardCrudResource.listRoutePath).toBeUndefined();
    expect(personStandardCrudResource.detailRoutePath).toBeUndefined();
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

  it('forwards the lookup execution signal through every organization GET', async () => {
    const fetchSpy = vi.spyOn(globalThis, 'fetch')
      .mockImplementation(async () => new Response('[]', {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }));
    const controller = new AbortController();
    const providers = personStandardCrudResource.lookupProviders ?? [];
    const companyProvider = providers.find(provider => provider.key === 'organization.companies');
    const departmentProvider = providers.find(provider => provider.key === 'organization.departments');
    const positionProvider = providers.find(provider => provider.key === 'organization.positions');

    await companyProvider?.load(
      { fieldKey: 'companyId', values: {}, dependencyValues: {} },
      { signal: controller.signal },
    );
    await departmentProvider?.load(
      {
        fieldKey: 'departmentId',
        values: { companyId: 'company-1' },
        dependencyValues: { companyId: 'company-1' },
      },
      { signal: controller.signal },
    );
    await positionProvider?.load(
      {
        fieldKey: 'positionId',
        values: { departmentId: 'department-1' },
        dependencyValues: { departmentId: 'department-1' },
      },
      { signal: controller.signal },
    );

    expect(fetchSpy).toHaveBeenCalledTimes(3);
    fetchSpy.mock.calls.forEach(([, init]) => {
      expect(init).toEqual(expect.objectContaining({
        method: 'GET',
        signal: controller.signal,
      }));
    });
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

  it('renders reference projections through the explicit detail field binding', () => {
    const renderer = personStandardCrudResource.detailFieldRenderers?.['organization.person.reference'];

    expect(renderer?.({
      fieldKey: 'DepartmentId',
      value: 'department-1',
      detailFields: {
        DepartmentId: 'department-1',
        DepartmentName: '研发部',
      },
    })).toBe('研发部');
  });

  it('maps all entity audit fields and preserves person detail summaries', () => {
    const detail = mapPersonDetailView({
      fields: {
        EID: 'person-1',
        PersonNo: 'P001',
        ENAME: '张三',
        EmploymentStatus: 'FullTime',
        IsEnabled: true,
        Revision: 7,
        CreatedBy: 'creator-1',
        CreatedAt: '2026-08-14T01:02:03Z',
        UpdatedBy: 'editor-2',
        UpdatedAt: '2026-08-15T04:05:06Z',
      },
      summaries: {
        userBinding: { status: 'notConfigured', data: null },
        authorizationSummary: { status: 'ready', data: ['Organization.Person.View'] },
      },
    });

    expect(detail).toMatchObject({
      createdBy: 'creator-1',
      createdAt: '2026-08-14T01:02:03Z',
      updatedBy: 'editor-2',
      updatedAt: '2026-08-15T04:05:06Z',
      userBindingSummary: { status: 'notConfigured', data: null },
      authorizationSummary: { status: 'ready', data: ['Organization.Person.View'] },
    });
  });

  it('requires an explicit disable reason before invoking the lifecycle command', async () => {
    const handler = personStandardCrudResource.actionHandlers?.disable;

    await expect(handler?.({
      id: 'person-1',
      row: { id: 'person-1', revision: 4 } as never,
      input: '   ',
    })).rejects.toThrow('请填写停用原因');
  });
});
