import { describe, expect, it, vi } from 'vitest';
import type { FrontendModule } from '@icp-platform/sdk';
import { resolveDetailViewDocument, resolveListViewDocument } from '../metadata/viewMetadataResolver';
import {
  buildStandardCrudDetailSchema,
  buildStandardCrudListSchema,
} from '../metadata/standardCrudSchemaResolver';
import { buildStandardCrudRegistry } from '../registries/standardCrudRegistry';

const personRegistration = {
  resourceKey: 'organization.person',
  apiRoute: '/api/organization/persons',
  listViewKey: 'organization.person.list',
  detailViewKey: 'organization.person.detail',
  createEmptyFilters: () => ({ keyword: '' }),
  buildListQuery: (filters: { keyword: string }) => filters,
  mapListItem: (value: unknown) => value,
  getRowId: () => 'row-1',
  buildColumns: () => [],
};

describe('standard CRUD schema resolver and registry', () => {
  it('builds a list schema from runtime view metadata and registration', () => {
    const schema = buildStandardCrudListSchema(
      {
        key: 'organization.person.list',
        dicKey: 'PersonListView',
        titleKey: 'Title',
        descriptionKey: 'Description',
        mode: 'list',
        query: {
          fields: [
            {
              key: 'keyword',
              labelKey: 'Query_Keyword',
              source: 'preset',
              preset: 'keyword',
              operator: 'like',
              dataSourceKey: 'person.keyword',
            },
          ],
        },
        advancedFilter: {
          enabled: true,
          titleKey: 'AdvancedFilter_Title',
          sections: [
            {
              key: 'organization',
              titleKey: 'AdvancedFilter_Organization',
              fields: [
                {
                  key: 'companyId',
                  labelKey: 'Query_Company',
                  source: 'model',
                  field: 'CompanyId',
                  dataSourceKey: 'organization.companies',
                },
              ],
            },
          ],
        },
        columns: [{ key: 'name', labelKey: 'Column_Name' }],
        pageActions: [{ key: 'refresh', labelKey: 'Action_Refresh' }],
        rowActions: [{ key: 'view', labelKey: 'Action_View' }],
        export: { modes: ['current'] },
      },
      personRegistration,
    );

    expect(schema).toMatchObject({
      viewKey: 'organization.person.list',
      resourceKey: 'organization.person',
      apiRoute: '/api/organization/persons',
      dicKey: 'PersonListView',
      titleKey: 'Title',
      detailViewKey: 'organization.person.detail',
    });
    expect(schema.queryFields).toHaveLength(1);
    expect(schema.queryFields[0].dataSourceKey).toBe('person.keyword');
    expect(schema.advancedFilter).toEqual({
      enabled: true,
      titleKey: 'AdvancedFilter_Title',
      sections: [
        {
          key: 'organization',
          titleKey: 'AdvancedFilter_Organization',
          fields: [
            {
              key: 'companyId',
              labelKey: 'Query_Company',
              source: 'model',
              field: 'CompanyId',
              dataSourceKey: 'organization.companies',
            },
          ],
        },
      ],
    });
    expect(schema.columns).toEqual([{ key: 'name', labelKey: 'Column_Name' }]);
    expect(schema.exportModes).toEqual(['current']);
  });

  it('filters malformed metadata elements instead of throwing while building schemas', () => {
    const schema = buildStandardCrudListSchema(
      {
        key: 'organization.person.list',
        query: { fields: [null, { key: 'keyword', labelKey: 'Query_Keyword' }] },
        advancedFilter: {
          enabled: true,
          sections: [
            null,
            {
              key: 'status',
              titleKey: 'AdvancedFilter_Status',
              fields: [undefined, { key: 'employmentStatus', labelKey: 'Query_EmploymentStatus' }],
            },
          ],
        },
        columns: [undefined, { key: 'name', labelKey: 'Column_Name' }],
        pageActions: [null, { key: 'refresh', labelKey: 'Action_Refresh' }],
        rowActions: [false, { key: 'view', labelKey: 'Action_View' }],
      },
      personRegistration,
    );

    expect(schema.queryFields).toEqual([{ key: 'keyword', labelKey: 'Query_Keyword', source: 'preset' }]);
    expect(schema.advancedFilter.sections).toEqual([
      {
        key: 'status',
        titleKey: 'AdvancedFilter_Status',
        fields: [{ key: 'employmentStatus', labelKey: 'Query_EmploymentStatus', source: 'preset' }],
      },
    ]);
    expect(schema.columns).toEqual([{ key: 'name', labelKey: 'Column_Name' }]);
    expect(schema.pageActions).toEqual([{ key: 'refresh', labelKey: 'Action_Refresh' }]);
    expect(schema.rowActions).toEqual([{ key: 'view', labelKey: 'Action_View' }]);
  });

  it('builds a detail schema from runtime view metadata and registration', () => {
    const schema = buildStandardCrudDetailSchema(
      {
        key: 'organization.person.detail',
        dicKey: 'PersonDetailView',
        titleKey: 'Title',
        descriptionKey: 'Description',
        mode: 'detail',
        sections: ['basic', 'organization'],
        summaryBlocks: ['userLink'],
        pageActions: [{ key: 'disable', labelKey: 'Action_Disable' }],
      },
      personRegistration,
    );

    expect(schema).toMatchObject({
      viewKey: 'organization.person.detail',
      resourceKey: 'organization.person',
      apiRoute: '/api/organization/persons',
      dicKey: 'PersonDetailView',
      titleKey: 'Title',
    });
    expect(schema.sections).toEqual(['basic', 'organization']);
    expect(schema.summaryBlocks).toEqual(['userLink']);
  });

  it('resolves only detail view documents for detail mode', () => {
    const detail = resolveDetailViewDocument(
      [
        { type: 'view', view: { key: 'organization.person.list', mode: 'list' } },
        { type: 'view', view: { key: 'organization.person.detail', mode: 'detail' } },
      ],
      'organization.person.detail',
    );

    expect(detail?.view.key).toBe('organization.person.detail');
  });

  it('ignores malformed view document envelopes while resolving metadata', () => {
    const list = resolveListViewDocument(
      [
        { type: 'view' } as never,
        { type: 'view', view: null } as never,
        { type: 'view', view: { key: 'organization.person.list', mode: 'list' } },
      ],
      'organization.person.list',
    );
    const detail = resolveDetailViewDocument([{ type: 'view', view: null } as never], 'organization.person.detail');

    expect(list?.view.key).toBe('organization.person.list');
    expect(detail).toBeNull();
  });

  it('indexes resources by resource key, list view key and detail view key', () => {
    const registry = buildStandardCrudRegistry([
      {
        metadata: { moduleId: 'organization', displayName: 'Organization', version: '1.0.0' },
        standardCrudResources: [personRegistration],
      } satisfies FrontendModule,
    ]);

    expect(registry.findByResourceKey('organization.person')).toBe(personRegistration);
    expect(registry.findByListViewKey('organization.person.list')).toBe(personRegistration);
    expect(registry.findByDetailViewKey('organization.person.detail')).toBe(personRegistration);
  });

  it('drops duplicate resource registrations and reports the conflict', () => {
    const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => undefined);

    const registry = buildStandardCrudRegistry([
      {
        metadata: { moduleId: 'organization-a', displayName: 'A', version: '1.0.0' },
        standardCrudResources: [personRegistration],
      },
      {
        metadata: { moduleId: 'organization-b', displayName: 'B', version: '1.0.0' },
        standardCrudResources: [{ ...personRegistration, apiRoute: '/api/other' }],
      },
    ]);

    expect(errorSpy).toHaveBeenCalledTimes(1);
    expect(registry.findByResourceKey('organization.person')).toBeUndefined();
    expect(registry.findByListViewKey('organization.person.list')).toBeUndefined();

    errorSpy.mockRestore();
  });

  it('drops duplicate list view registrations even when resource keys differ', () => {
    const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => undefined);

    const registry = buildStandardCrudRegistry([
      {
        metadata: { moduleId: 'organization-a', displayName: 'A', version: '1.0.0' },
        standardCrudResources: [personRegistration],
      },
      {
        metadata: { moduleId: 'organization-b', displayName: 'B', version: '1.0.0' },
        standardCrudResources: [{ ...personRegistration, resourceKey: 'organization.employee', detailViewKey: 'organization.employee.detail' }],
      },
    ]);

    expect(errorSpy).toHaveBeenCalledWith(
      'Duplicate standard CRUD view registration detected for viewKey "organization.person.list". The key is ignored.',
    );
    expect(registry.findByResourceKey('organization.person')).toBeUndefined();
    expect(registry.findByResourceKey('organization.employee')).toBeUndefined();
    expect(registry.findByListViewKey('organization.person.list')).toBeUndefined();
    expect(registry.findByDetailViewKey('organization.person.detail')).toBeUndefined();
    expect(registry.findByDetailViewKey('organization.employee.detail')).toBeUndefined();

    errorSpy.mockRestore();
  });

  it('drops duplicate detail view registrations even when resource keys differ', () => {
    const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => undefined);

    const registry = buildStandardCrudRegistry([
      {
        metadata: { moduleId: 'organization-a', displayName: 'A', version: '1.0.0' },
        standardCrudResources: [personRegistration],
      },
      {
        metadata: { moduleId: 'organization-b', displayName: 'B', version: '1.0.0' },
        standardCrudResources: [{ ...personRegistration, resourceKey: 'organization.employee', listViewKey: 'organization.employee.list' }],
      },
    ]);

    expect(errorSpy).toHaveBeenCalledWith(
      'Duplicate standard CRUD view registration detected for viewKey "organization.person.detail". The key is ignored.',
    );
    expect(registry.findByResourceKey('organization.person')).toBeUndefined();
    expect(registry.findByResourceKey('organization.employee')).toBeUndefined();
    expect(registry.findByListViewKey('organization.person.list')).toBeUndefined();
    expect(registry.findByListViewKey('organization.employee.list')).toBeUndefined();
    expect(registry.findByDetailViewKey('organization.person.detail')).toBeUndefined();

    errorSpy.mockRestore();
  });
});
