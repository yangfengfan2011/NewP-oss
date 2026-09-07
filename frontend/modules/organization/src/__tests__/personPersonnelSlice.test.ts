import { afterEach, describe, expect, it, vi } from 'vitest';
import { mapPersonDetailView, mapPersonListItem } from '../services/organizationService';
import { personStandardCrudResource } from '../standard-crud/personStandardCrudResource';

const lookupRequest = (
  fieldKey: string,
  values: Record<string, unknown> = {},
  dependencyValues: Record<string, unknown> = {},
) => ({ fieldKey, keyword: '', page: 1, pageSize: 20, currentValueIds: [], values, dependencyValues });

describe('personnel center first vertical slice', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('leaves route authorization to the runtime metadata projection', () => {
    expect(personStandardCrudResource.permissions).toBeUndefined();
    expect(personStandardCrudResource.listRoutePath).toBeUndefined();
    expect(personStandardCrudResource.detailRoutePath).toBeUndefined();
  });

  it('pages organization lookups and preserves disabled current values within the selected company', async () => {
    const fetchSpy = vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(new Response(JSON.stringify({
        items: [{ value: 'company-1', label: '总部', disabled: false }],
        currentItems: [{ value: 'company-2', label: '停用公司（已停用）', disabled: true }],
        page: 2,
        pageSize: 10,
        hasMore: true,
      }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
      .mockResolvedValueOnce(new Response(JSON.stringify({
        items: [{ value: 'department-1', label: '研发部', disabled: false }],
        currentItems: [{ value: 'department-2', label: '停用部门（已停用）', disabled: true }],
        page: 1,
        pageSize: 20,
        hasMore: false,
      }), { status: 200, headers: { 'Content-Type': 'application/json' } }));

    const companyProvider = personStandardCrudResource.lookupProviders?.find(
      provider => provider.key === 'organization.companies',
    );
    const departmentProvider = personStandardCrudResource.lookupProviders?.find(
      provider => provider.key === 'organization.departments',
    );

    await expect(companyProvider?.load({
      ...lookupRequest('CompanyId'),
      keyword: '总部',
      page: 2,
      pageSize: 10,
      currentValueIds: ['company-2'],
    })).resolves.toEqual({
      options: [{ value: 'company-1', label: '总部', disabled: false }],
      currentOptions: [{ value: 'company-2', label: '停用公司（已停用）', disabled: true }],
      page: 2,
      pageSize: 10,
      hasMore: true,
    });
    await expect(departmentProvider?.load({
      ...lookupRequest(
        'DepartmentId',
        { CompanyId: 'company-1' },
        { CompanyId: 'company-1' },
      ),
      currentValueIds: ['department-2'],
    })).resolves.toEqual({
      options: [{ value: 'department-1', label: '研发部', disabled: false }],
      currentOptions: [{ value: 'department-2', label: '停用部门（已停用）', disabled: true }],
      page: 1,
      pageSize: 20,
      hasMore: false,
    });

    expect(String(fetchSpy.mock.calls[0]?.[0])).toContain('/api/organization/person-lookups/companies?');
    expect(String(fetchSpy.mock.calls[0]?.[0])).toContain('keyword=%E6%80%BB%E9%83%A8');
    expect(String(fetchSpy.mock.calls[0]?.[0])).toContain('page=2&pageSize=10');
    expect(String(fetchSpy.mock.calls[0]?.[0])).toContain('currentValueIds=company-2');
    expect(String(fetchSpy.mock.calls[1]?.[0])).toContain('/api/organization/person-lookups/departments?');
    expect(String(fetchSpy.mock.calls[1]?.[0])).toContain('companyId=company-1');
    expect(String(fetchSpy.mock.calls[1]?.[0])).toContain('currentValueIds=department-2');
  });

  it('forwards the lookup execution signal through every organization GET', async () => {
    const fetchSpy = vi.spyOn(globalThis, 'fetch')
      .mockImplementation(async () => new Response(
        JSON.stringify({ items: [], currentItems: [], page: 1, pageSize: 20, hasMore: false }),
        {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        },
      ));
    const controller = new AbortController();
    const providers = personStandardCrudResource.lookupProviders ?? [];
    const companyProvider = providers.find(provider => provider.key === 'organization.companies');
    const departmentProvider = providers.find(provider => provider.key === 'organization.departments');
    const positionProvider = providers.find(provider => provider.key === 'organization.positions');

    await companyProvider?.load(
      lookupRequest('companyId'),
      { signal: controller.signal },
    );
    await departmentProvider?.load(
      lookupRequest(
        'departmentId',
        { companyId: 'company-1' },
        { companyId: 'company-1' },
      ),
      { signal: controller.signal },
    );
    await positionProvider?.load(
      lookupRequest('positionId'),
      { signal: controller.signal },
    );

    expect(fetchSpy).toHaveBeenCalledTimes(3);
    fetchSpy.mock.calls.forEach(([, init]) => {
      expect(init).toEqual(expect.objectContaining({
        signal: controller.signal,
      }));
      expect(init?.method ?? 'GET').toBe('GET');
    });
    expect(String(fetchSpy.mock.calls[2]?.[0])).toContain('/api/organization/positions/options?page=1&pageSize=20');
  });

  it('uses the narrow lookup for disabled list filters without exposing disabled choices in forms', async () => {
    const fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async () => new Response(JSON.stringify({
      items: [{ value: 'company-disabled', label: '停用公司（已停用）', disabled: true }],
      currentItems: [],
      page: 1,
      pageSize: 20,
      hasMore: false,
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }));
    const provider = personStandardCrudResource.lookupProviders?.find(
      item => item.key === 'organization.companies',
    );

    await provider?.load(lookupRequest('companyId'));
    await provider?.load(lookupRequest('CompanyId'));

    expect(String(fetchSpy.mock.calls[0]?.[0])).toContain('includeDisabled=true');
    expect(String(fetchSpy.mock.calls[1]?.[0])).not.toContain('includeDisabled=true');
  });

  it('can retry a failed organization lookup with the same paged request', async () => {
    const fetchSpy = vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(new Response('temporarily unavailable', { status: 503 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({
        items: [{ value: 'company-1', label: '总部', disabled: false }],
        currentItems: [],
        page: 1,
        pageSize: 20,
        hasMore: false,
      }), { status: 200, headers: { 'Content-Type': 'application/json' } }));
    const provider = personStandardCrudResource.lookupProviders?.find(
      item => item.key === 'organization.companies',
    );
    const request = lookupRequest('CompanyId');

    await expect(provider?.load(request)).rejects.toThrow('temporarily unavailable');
    await expect(provider?.load(request)).resolves.toMatchObject({
      options: [{ value: 'company-1', label: '总部', disabled: false }],
    });
    expect(fetchSpy).toHaveBeenCalledTimes(2);
  });

  it('offers only enabled positions while preserving and marking the current disabled value', async () => {
    const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({
      items: [{ value: 'Position_Enabled', label: '平台主管', disabled: false }],
      currentItems: [{ value: 'Position_Disabled', label: '旧岗位（已停用）', disabled: true }],
      page: 1,
      pageSize: 20,
      hasMore: false,
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }));
    const provider = personStandardCrudResource.lookupProviders?.find(
      item => item.key === 'organization.positions',
    );

    const result = await provider?.load({
      ...lookupRequest('PositionId'),
      currentValueIds: ['Position_Disabled'],
    });

    expect(result).toEqual({
      options: [{ value: 'Position_Enabled', label: '平台主管', disabled: false }],
      currentOptions: [{ value: 'Position_Disabled', label: '旧岗位（已停用）', disabled: true }],
      page: 1,
      pageSize: 20,
      hasMore: false,
    });
    expect(String(fetchSpy.mock.calls[0]?.[0])).toContain('currentValueIds=Position_Disabled');
    expect(String(fetchSpy.mock.calls[0]?.[0])).not.toContain('CompanyId');
    expect(String(fetchSpy.mock.calls[0]?.[0])).not.toContain('DepartmentId');
  });

  it('preserves the canonical CompanyId raw/display pair without a CompanyName alias', () => {
    const row = mapPersonListItem({
      EID: 'person-1',
      PersonNo: 'P001',
      ENAME: '张三',
      CompanyId: 'company-1',
      CompanyId_DisplayValue: '总部',
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
    const nullableAffiliation = mapPersonListItem({
      EID: 'person-3',
      PersonNo: 'P003',
      ENAME: '王五',
      CompanyId: null,
      CompanyId_DisplayValue: null,
      EmploymentStatus: 'FullTime',
      IsEnabled: true,
    });

    expect(row).toMatchObject({
      CompanyId: 'company-1',
      CompanyId_DisplayValue: '总部',
      companyId: 'company-1',
    });
    expect(row).not.toHaveProperty('CompanyName');
    expect(row).not.toHaveProperty('companyName');
    expect(row.DepartmentName).toBe('研发部');
    expect(unresolved.CompanyId_DisplayValue).toBeNull();
    expect(unresolved.departmentName).toBe('');
    expect(nullableAffiliation.CompanyId).toBeNull();
    expect(nullableAffiliation.CompanyId_DisplayValue).toBeNull();
    expect(nullableAffiliation.companyId).toBe('');
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
        BirthDate: '1990-02-03',
        BirthDate_DisplayValue: '1990/2/3',
        EmploymentStartedAt: '2026-08-14T01:02:03Z',
        EmploymentStartedAt_DisplayValue: '2026/8/14 09:02:03',
        EmploymentEndedAt: null,
        EmploymentEndedAt_DisplayValue: null,
        CompanyId: 'company-1',
        CompanyId_DisplayValue: '总部',
        EmploymentStatus: 'FullTime',
        IsEnabled: true,
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
      BirthDate: '1990-02-03',
      BirthDate_DisplayValue: '1990/2/3',
      EmploymentStartedAt: '2026-08-14T01:02:03Z',
      EmploymentStartedAt_DisplayValue: '2026/8/14 09:02:03',
      EmploymentEndedAt: null,
      EmploymentEndedAt_DisplayValue: null,
      companyId: 'company-1',
      createdBy: 'creator-1',
      createdAt: '2026-08-14T01:02:03Z',
      updatedBy: 'editor-2',
      updatedAt: '2026-08-15T04:05:06Z',
      userBindingSummary: { status: 'notConfigured', data: null },
      authorizationSummary: { status: 'ready', data: ['Organization.Person.View'] },
    });
    expect(detail).not.toHaveProperty('companyName');
  });

  it('requires an explicit disable reason before invoking the lifecycle command', async () => {
    const handler = personStandardCrudResource.actionHandlers?.disable;

    await expect(handler?.({
      id: 'person-1',
      row: { id: 'person-1' } as never,
      input: '   ',
    })).rejects.toThrow('请填写停用原因');
  });
});
