import { describe, expect, it } from 'vitest';
import type {
  StandardCrudFieldDefinition,
  StandardCrudListQueryRequest,
  StandardCrudLookupProvider,
} from '@icp-platform/sdk';
import {
  createStandardCrudDetailQueryKey,
  createStandardCrudListQueryKey,
  createStandardCrudLookupQueryKey,
  createStandardCrudRuntimeColumnsQueryKey,
  createStandardCrudRuntimeRowsQueryKey,
} from '../standard-crud/standardCrudQueryKeys';

describe('Standard CRUD query keys', () => {
  it('keys every detail surface only by resource and record identity', () => {
    const pageKey = createStandardCrudDetailQueryKey('organization.person', 'Person_1');
    const drawerKey = createStandardCrudDetailQueryKey('organization.person', 'Person_1');

    expect(pageKey).toEqual(['standard-crud.detail', 'organization.person', 'Person_1']);
    expect(drawerKey).toEqual(pageKey);
    expect(createStandardCrudDetailQueryKey('organization.person', 'Person_2')).not.toEqual(pageKey);
    expect(createStandardCrudDetailQueryKey('identity.account', 'Person_1')).not.toEqual(pageKey);
  });

  it('keys runtime columns by resource and every serializable committed filter', () => {
    const firstKey = createStandardCrudRuntimeColumnsQueryKey({
      resourceKey: 'example.runtime-workload',
      filters: {
        keyword: '陈',
        context: { team: 'north', week: 34 },
        ignoredSignal: new AbortController().signal,
        ignoredCallback: () => 'ignored',
      },
    });
    const reorderedKey = createStandardCrudRuntimeColumnsQueryKey({
      resourceKey: 'example.runtime-workload',
      filters: {
        ignoredCallback: () => 'different',
        context: { week: 34, team: 'north' },
        keyword: '陈',
        ignoredSignal: new AbortController().signal,
      },
    });

    expect(firstKey).toEqual([
      'standard-crud.runtime-columns',
      'example.runtime-workload',
      { context: { team: 'north', week: 34 }, keyword: '陈' },
    ]);
    expect(reorderedKey).toEqual(firstKey);
    expect(createStandardCrudRuntimeColumnsQueryKey({
      resourceKey: 'example.other-workload',
      filters: { keyword: '陈', context: { team: 'north', week: 34 } },
    })).not.toEqual(firstKey);
    expect(createStandardCrudRuntimeColumnsQueryKey({
      resourceKey: 'example.runtime-workload',
      filters: { keyword: '张', context: { team: 'north', week: 34 } },
    })).not.toEqual(firstKey);
    expect(JSON.stringify(firstKey)).not.toContain('ignored');
  });

  it('keys runtime rows by request projection and the successful column generation', () => {
    const createKey = (
      request: StandardCrudListQueryRequest,
      columnContextToken = 'week-token',
      columnGeneration = 7,
    ) => createStandardCrudRuntimeRowsQueryKey({
      resourceKey: 'example.runtime-workload',
      request,
      columnContextToken,
      columnGeneration,
    });
    const request = {
      keyword: '陈',
      fields: 'EID,memberName,currentWeekDay1',
      pageIndex: 2,
      pageSize: 20,
      sortBy: 'memberName',
      sortDirection: 'asc',
    };
    const base = createKey(request);

    expect(base).toEqual([
      'standard-crud.runtime-rows',
      'example.runtime-workload',
      {
        columnContextToken: 'week-token',
        columnGeneration: 7,
        request,
      },
    ]);
    expect(createKey({ ...request, pageIndex: 3 })).not.toEqual(base);
    expect(createKey({ ...request, sortDirection: 'desc' })).not.toEqual(base);
    expect(createKey({ ...request, fields: 'EID,memberName,nextWeekDay1' })).not.toEqual(base);
    expect(createKey(request, 'next-week-token')).not.toEqual(base);
    expect(createKey(request, 'week-token', 8)).not.toEqual(base);
  });

  it('canonicalizes object order and nested undefined values without leaking illegal key values', () => {
    const firstRequest = {
      sequence: [1, undefined, 2],
      pageSize: undefined,
      filters: { ignored: undefined, status: 'active' },
      pageIndex: 1,
    } as unknown as StandardCrudListQueryRequest;
    const secondRequest = {
      pageIndex: 1,
      filters: { status: 'active', ignored: undefined },
      pageSize: undefined,
      sequence: [1, undefined, 2],
    } as unknown as StandardCrudListQueryRequest;

    const firstKey = createStandardCrudListQueryKey('organization.person', firstRequest);
    const secondKey = createStandardCrudListQueryKey('organization.person', secondRequest);

    expect(JSON.stringify(firstKey)).toBe(JSON.stringify(secondKey));
    expect(firstKey).toEqual([
      'standard-crud.list',
      'organization.person',
      {
        filters: { status: 'active' },
        pageIndex: 1,
        sequence: [1, null, 2],
      },
    ]);
  });

  it('canonicalizes only serializable lookup result semantics', () => {
    const field: StandardCrudFieldDefinition = {
      key: 'departmentId',
      labelKey: 'localized.department',
      modelField: 'DEPARTMENT_ID',
      kind: 'lookup',
      lookup: {
        providerKey: 'departments',
        dependsOn: ['tenantId'],
        currentValueLabelField: 'departmentName',
      },
    };
    const provider: StandardCrudLookupProvider = {
      key: 'departments',
      load: async () => [],
    };
    const firstKey = createStandardCrudLookupQueryKey({
      resourceKey: 'organization.person',
      fields: [field],
      providers: [provider],
      values: {
        departmentId: 'department-a',
        tenantId: 'tenant-a',
        ignoredCallback: () => 'ignored',
      },
      detailFields: {
        departmentName: '财务部',
        ignoredSignal: new AbortController().signal,
      },
    });
    const secondKey = createStandardCrudLookupQueryKey({
      resourceKey: 'organization.person',
      fields: [{ ...field, labelKey: 'another.locale.key' }],
      providers: [{ ...provider, load: async () => [{ value: 'ignored', label: 'ignored' }] }],
      values: {
        ignoredCallback: () => 'different',
        tenantId: 'tenant-a',
        departmentId: 'department-a',
      },
      detailFields: {
        ignoredSignal: new AbortController().signal,
        departmentName: '财务部',
      },
    });

    expect(firstKey).toEqual(secondKey);
    expect(JSON.stringify(firstKey)).not.toContain('ignored');
    expect(firstKey[0]).toBe('standard-crud.lookup');
  });

  it('changes lookup keys when resource, provider availability, dependency, or current label semantics change', () => {
    const field: StandardCrudFieldDefinition = {
      key: 'departmentId',
      labelKey: '部门',
      modelField: 'DEPARTMENT_ID',
      kind: 'lookup',
      lookup: {
        providerKey: 'departments',
        dependsOn: ['tenantId'],
        currentValueLabelField: 'departmentName',
      },
    };
    const provider: StandardCrudLookupProvider = { key: 'departments', load: async () => [] };
    const createKey = (
      resourceKey: string,
      tenantId: string,
      providers: StandardCrudLookupProvider[] = [provider],
      departmentName = '财务部',
    ) => createStandardCrudLookupQueryKey({
      resourceKey,
      fields: [field],
      providers,
      values: { departmentId: 'department-a', tenantId },
      detailFields: { departmentName },
    });

    const base = createKey('organization.person', 'tenant-a');
    expect(createKey('identity.account', 'tenant-a')).not.toEqual(base);
    expect(createKey('organization.person', 'tenant-b')).not.toEqual(base);
    expect(createKey('organization.person', 'tenant-a', [])).not.toEqual(base);
    expect(createKey('organization.person', 'tenant-a', [provider], '研发部')).not.toEqual(base);
  });
});
