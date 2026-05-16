import { describe, expect, it, vi } from 'vitest';
import type { StandardCrudFieldDefinition, StandardCrudLookupProvider, StandardCrudOption } from '@icp-platform/sdk';
import { loadStandardCrudLookupOptions } from '../standard-crud/standardCrudLookupRuntime';

function lookupField(overrides: Partial<StandardCrudFieldDefinition> = {}): StandardCrudFieldDefinition {
  return {
    key: 'departmentId',
    labelKey: '部门',
    modelField: 'DEPARTMENT_ID',
    kind: 'lookup',
    lookup: {
      providerKey: 'departments',
      dependsOn: ['tenantId'],
      currentValueLabelField: 'departmentName',
    },
    ...overrides,
  };
}

function provider(options: StandardCrudOption[]): StandardCrudLookupProvider {
  return {
    key: 'departments',
    load: vi.fn(async () => options),
  };
}

describe('standard CRUD lookup runtime', () => {
  it('loads lookup options from the matching provider', async () => {
    const options: StandardCrudOption[] = [{ value: 'finance', label: '财务部' }];
    const departments = provider(options);
    const values = { tenantId: 'tenant-a' };

    const result = await loadStandardCrudLookupOptions({
      field: lookupField(),
      providers: [departments],
      values,
      detailFields: {},
    });

    expect(result).toEqual({ options, error: null });
    expect(departments.load).toHaveBeenCalledWith({
      fieldKey: 'departmentId',
      values,
      dependencyValues: { tenantId: 'tenant-a' },
    });
  });

  it('preserves the current value when provider results do not include it', async () => {
    const departments = provider([{ value: 'finance', label: '财务部' }]);

    const result = await loadStandardCrudLookupOptions({
      field: lookupField(),
      providers: [departments],
      values: {
        tenantId: 'tenant-a',
        departmentId: 'existing',
      },
      detailFields: { departmentName: '既有部门' },
    });

    expect(result).toEqual({
      options: [
        { value: 'existing', label: '既有部门' },
        { value: 'finance', label: '财务部' },
      ],
      error: null,
    });
  });

  it('skips provider loading when any dependency value is empty', async () => {
    const departments = provider([{ value: 'finance', label: '财务部' }]);

    const result = await loadStandardCrudLookupOptions({
      field: lookupField(),
      providers: [departments],
      values: { tenantId: '' },
      detailFields: {},
    });

    expect(result).toEqual({ options: [], error: null });
    expect(departments.load).not.toHaveBeenCalled();
  });

  it('reports missing provider before dependency short circuit', async () => {
    const result = await loadStandardCrudLookupOptions({
      field: lookupField({ lookup: { providerKey: 'missing', dependsOn: ['tenantId'] } }),
      providers: [],
      values: { tenantId: '' },
      detailFields: {},
    });

    expect(result).toEqual({ options: [], error: '未找到 lookup provider：missing' });
  });

  it('returns stable error and preserves current value when provider fails', async () => {
    const departments: StandardCrudLookupProvider = {
      key: 'departments',
      load: vi.fn(async () => {
        throw new Error('部门加载失败');
      }),
    };

    const result = await loadStandardCrudLookupOptions({
      field: lookupField(),
      providers: [departments],
      values: {
        tenantId: 'tenant-a',
        departmentId: 'existing',
      },
      detailFields: { departmentName: '既有部门' },
    });

    expect(result).toEqual({
      options: [{ value: 'existing', label: '既有部门' }],
      error: '部门加载失败',
    });
  });

  it('returns field options for non lookup fields', async () => {
    const options: StandardCrudOption[] = [{ value: 'enabled', label: '启用' }];

    const result = await loadStandardCrudLookupOptions({
      field: {
        key: 'status',
        labelKey: '状态',
        modelField: 'STATUS',
        kind: 'enum',
        options,
      },
      providers: [],
      values: {},
      detailFields: {},
    });

    expect(result).toEqual({ options, error: null });
  });
});
