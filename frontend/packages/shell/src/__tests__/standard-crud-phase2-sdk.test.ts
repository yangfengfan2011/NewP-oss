import { describe, expect, it, vi } from 'vitest';
import {
  buildStandardCrudI18nLabel,
  clearStandardCrudDependentLookupValues,
  loadStandardCrudLookupOptions,
  normalizeStandardCrudSubmitValues,
  preserveStandardCrudCurrentOption,
  readStandardCrudFieldValue,
  resolveStandardCrudSummaryMessage,
  type StandardCrudActionDefinition,
  type StandardCrudFieldDefinition,
  type StandardCrudOption,
  type StandardCrudResourceRegistration,
} from '@icp-platform/sdk';

describe('standard CRUD phase 2 SDK helpers', () => {
  it('builds i18n labels from dictionary and local keys', () => {
    expect(buildStandardCrudI18nLabel('Person', 'Fields.PersonNo')).toBe('Person.Fields.PersonNo');
    expect(buildStandardCrudI18nLabel('Person', '')).toBe('Person');
    expect(buildStandardCrudI18nLabel('', 'Fields.PersonNo')).toBe('Fields.PersonNo');
  });

  it('reads field values by model field with fallback fields', () => {
    const field: StandardCrudFieldDefinition = {
      key: 'personNo',
      labelKey: 'Fields.PersonNo',
      modelField: 'PERSON_NO',
      fallbackFields: ['PersonNo'],
      kind: 'text',
    };

    expect(readStandardCrudFieldValue({ PERSON_NO: 'P-001' }, field)).toBe('P-001');
    expect(readStandardCrudFieldValue({ PersonNo: 'P-002' }, field)).toBe('P-002');
    expect(readStandardCrudFieldValue({}, field)).toBeNull();
  });

  it('normalizes submit values with trim, emptyAsNull, readOnly skip and default values', () => {
    const fields: StandardCrudFieldDefinition[] = [
      { key: 'personNo', labelKey: 'Fields.PersonNo', modelField: 'PERSON_NO', kind: 'text', trim: true },
      { key: 'displayName', labelKey: 'Fields.DisplayName', modelField: 'DISPLAY_NAME', kind: 'text', trim: true, emptyAsNull: true },
      { key: 'status', labelKey: 'Fields.Status', modelField: 'STATUS', kind: 'enum', defaultValue: 'enabled' },
      { key: 'createdAt', labelKey: 'Fields.CreatedAt', modelField: 'CREATED_AT', kind: 'readonly', readOnly: true },
    ];

    expect(
      normalizeStandardCrudSubmitValues(
        fields,
        {
          personNo: ' P-001 ',
          displayName: '   ',
          createdAt: '2026-05-16',
        },
      ),
    ).toEqual({
      PERSON_NO: 'P-001',
      DISPLAY_NAME: null,
      STATUS: 'enabled',
    });

    expect(normalizeStandardCrudSubmitValues(fields, { status: null })).toEqual({
      PERSON_NO: null,
      DISPLAY_NAME: null,
      STATUS: null,
    });
  });

  it('preserves the current option before provider results when missing', () => {
    const current: StandardCrudOption = { value: 'current', label: '当前值' };
    const options: StandardCrudOption[] = [{ value: 'next', label: '新值' }];

    expect(preserveStandardCrudCurrentOption(options, current)).toEqual([current, ...options]);
    expect(preserveStandardCrudCurrentOption([current, ...options], current)).toEqual([current, ...options]);
  });

  it('resolves standard summary status messages', () => {
    expect(resolveStandardCrudSummaryMessage({ status: 'notConfigured', data: null })).toBe('暂未配置');
    expect(resolveStandardCrudSummaryMessage({ status: 'dependencyUnavailable', data: null })).toBe('依赖不可用');
  });

  it('loads lookup options through the shared standard CRUD helper', async () => {
    const load = vi.fn(async () => [{ value: 'Dept_2', label: '二部' }]);
    const field: StandardCrudFieldDefinition = {
      key: 'departmentId',
      labelKey: '所属部门',
      modelField: 'DepartmentId',
      kind: 'lookup',
      lookup: { providerKey: 'departments', dependsOn: ['companyId'], currentValueLabelField: 'DepartmentName' },
    };

    await expect(
      loadStandardCrudLookupOptions({
        field,
        providers: [{ key: 'departments', load }],
        values: { companyId: '', departmentId: 'Dept_1' },
        detailFields: { DepartmentName: '一部' },
      }),
    ).resolves.toEqual({ options: [], error: null });
    expect(load).not.toHaveBeenCalled();

    await expect(
      loadStandardCrudLookupOptions({
        field,
        providers: [{ key: 'departments', load }],
        values: { companyId: 'Company_1', departmentId: 'Dept_1' },
        detailFields: { DepartmentName: '一部' },
      }),
    ).resolves.toEqual({
      options: [
        { value: 'Dept_1', label: '一部' },
        { value: 'Dept_2', label: '二部' },
      ],
      error: null,
    });
    expect(load).toHaveBeenCalledWith({
      fieldKey: 'departmentId',
      values: { companyId: 'Company_1', departmentId: 'Dept_1' },
      dependencyValues: { companyId: 'Company_1' },
    });
  });

  it('clears lookup descendants from dependsOn declarations', () => {
    const fields: StandardCrudFieldDefinition[] = [
      { key: 'companyId', labelKey: '公司', modelField: 'CompanyId', kind: 'lookup', lookup: { providerKey: 'companies' } },
      {
        key: 'departmentId',
        labelKey: '部门',
        modelField: 'DepartmentId',
        kind: 'lookup',
        lookup: { providerKey: 'departments', dependsOn: ['companyId'] },
      },
      {
        key: 'positionId',
        labelKey: '岗位',
        modelField: 'PositionId',
        kind: 'lookup',
        lookup: { providerKey: 'positions', dependsOn: ['departmentId'] },
      },
    ];

    expect(
      clearStandardCrudDependentLookupValues(
        { companyId: 'Company_2', departmentId: 'Dept_1', positionId: 'Position_1', name: '张三' },
        'companyId',
        fields,
      ),
    ).toEqual({ companyId: 'Company_2', departmentId: '', positionId: '', name: '张三' });
  });

  it('allows metadata action keys and enum option bridge on resource registrations', () => {
    const action: StandardCrudActionDefinition = {
      key: 'disable',
      labelKey: 'Actions.Disable',
      standardAction: 'disable',
      confirmMessage: '确认停用？',
      confirmMessageKey: 'Actions.DisableConfirm',
      handlerKey: 'disable',
    };
    const registration: StandardCrudResourceRegistration = {
      resourceKey: 'organization.person',
      apiRoute: '/api/organization/persons',
      listViewKey: 'organization.person.list',
      createEmptyFilters: () => ({}),
      buildListQuery: () => ({}),
      mapListItem: (value) => value as Record<string, unknown>,
      getRowId: () => 'row-1',
      listActions: [action],
      enumOptions: {
        EmploymentStatus: [{ value: 'FullTime', label: '正式' }],
      },
    };

    expect(registration.buildColumns).toBeUndefined();
    expect(registration.renderFilters).toBeUndefined();
    expect(registration.enumOptions?.EmploymentStatus[0]).toEqual({ value: 'FullTime', label: '正式' });
    expect(registration.listActions?.[0]).toMatchObject({
      confirmMessage: '确认停用？',
      confirmMessageKey: 'Actions.DisableConfirm',
      handlerKey: 'disable',
    });
  });
});
