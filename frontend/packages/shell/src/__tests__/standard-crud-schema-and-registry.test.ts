import { describe, expect, it, vi } from 'vitest';
import type { FrontendModule } from '@icp-platform/sdk';
import { resolveDetailViewDocument, resolveListViewDocument } from '../metadata/viewMetadataResolver';
import {
  buildStandardCrudDetailSchema,
  buildStandardCrudListSchema,
  resolveStandardCrudDetailSchema,
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
  lookupProviders: [
    { key: 'organization.departments', load: async () => [] },
    { key: 'organization.positions', load: async () => [] },
  ],
  actionHandlers: {
    disable: async () => undefined,
  },
};

describe('standard CRUD schema resolver and registry', () => {
  it('resolves ordered detail sections against the canonical field catalog and renderer bindings', () => {
    const schema = resolveStandardCrudDetailSchema(
      [{
        type: 'view',
        view: {
          key: 'organization.person.detail',
          mode: 'detail',
          model: 'Person',
          sections: [
            {
              key: 'basic',
              titleKey: 'Section_Basic',
              fields: ['PersonNo', 'ENAME'],
            },
            {
              key: 'audit',
              titleKey: 'Section_Audit',
              fields: [
                { field: 'CreatedBy', rendererKey: 'organization.actor' },
                'CreatedAt',
              ],
            },
            {
              key: 'organization',
              titleKey: 'Section_Organization',
              fields: ['CompanyId'],
            },
          ],
        },
      }],
      {
        ...personRegistration,
        detailFieldRenderers: {
          'organization.actor': () => null,
        },
      },
      {
        modelDocuments: [{
          type: 'model',
          Person: {
            kind: 'Entity',
            builtinFields: ['ENAME', 'ECODE'],
            fields: [
              { name: 'PersonNo', storage: { type: 'string' } },
              { name: 'CompanyId', uiType: 'lookup' },
            ],
          },
        }],
        detailResponseFields: ['EID', 'PersonNo', 'ENAME', 'CompanyId', 'CompanyName', 'CreatedBy', 'CreatedAt'],
      },
    );

    expect(schema.sections).toEqual([
      {
        key: 'basic',
        titleKey: 'Section_Basic',
        fields: [
          { key: 'PersonNo', labelKey: 'Field_PersonNo', rendererKey: 'standard.text', rendererSource: 'standard' },
          { key: 'ENAME', labelKey: 'Field_ENAME', rendererKey: 'standard.text', rendererSource: 'standard' },
        ],
      },
      {
        key: 'audit',
        titleKey: 'Section_Audit',
        fields: [
          { key: 'CreatedBy', labelKey: 'Field_CreatedBy', rendererKey: 'organization.actor', rendererSource: 'binding' },
          { key: 'CreatedAt', labelKey: 'Field_CreatedAt', rendererKey: 'standard.date', rendererSource: 'standard' },
        ],
      },
      {
        key: 'organization',
        titleKey: 'Section_Organization',
        fields: [
          { key: 'CompanyId', labelKey: 'Field_CompanyId', rendererKey: 'standard.lookup', rendererSource: 'standard' },
        ],
      },
    ]);
    expect(schema.fieldCatalog.map((field) => field.key)).toEqual([
      'PersonNo',
      'CompanyId',
      'EID',
      'CreatedBy',
      'CreatedAt',
      'UpdatedBy',
      'UpdatedAt',
      'ENAME',
      'ECODE',
    ]);
  });

  it.each([
    {
      name: 'missing view',
      viewDocuments: [],
      expected: 'detail view "organization.person.detail" is missing',
    },
    {
      name: 'string section',
      sections: ['basic'],
      expected: 'detail view "organization.person.detail" sections must be objects',
    },
    {
      name: 'no sections',
      sections: [],
      expected: 'detail view "organization.person.detail" must contain at least one section and one field',
    },
    {
      name: 'empty section',
      sections: [{ key: 'basic', titleKey: 'Section_Basic', fields: [] }],
      expected: 'detail view "organization.person.detail" contains an empty detail section',
    },
    {
      name: 'unknown model',
      model: 'Ghost',
      expected: 'view "organization.person.detail" references unknown model "Ghost"',
    },
    {
      name: 'unknown field and DTO alias',
      sections: [{ key: 'basic', titleKey: 'Section_Basic', fields: ['CompanyName'] }],
      expected: 'detail section "basic" references unknown model field "CompanyName"',
    },
    {
      name: 'duplicate field',
      sections: [
        { key: 'basic', titleKey: 'Section_Basic', fields: ['PersonNo'] },
        { key: 'other', titleKey: 'Section_Other', fields: ['PersonNo'] },
      ],
      expected: 'detail view "organization.person.detail" contains duplicate field "PersonNo"',
    },
    {
      name: 'field unavailable in response',
      sections: [{ key: 'basic', titleKey: 'Section_Basic', fields: ['CreatedAt'] }],
      expected: 'detail field "CreatedAt" is not provided by resource "organization.person" response',
    },
    {
      name: 'unknown renderer binding',
      sections: [{ key: 'basic', titleKey: 'Section_Basic', fields: [{ field: 'PersonNo', rendererKey: 'missing' }] }],
      expected: 'detail field renderer "missing" is not registered on resource "organization.person"',
    },
  ])('rejects $name with a stable diagnostic', ({ viewDocuments, sections, model = 'Person', expected }) => {
    const documents = viewDocuments ?? [{
      type: 'view',
      view: {
        key: 'organization.person.detail',
        mode: 'detail',
        model,
        sections: sections ?? [{ key: 'basic', titleKey: 'Section_Basic', fields: ['PersonNo'] }],
      },
    }];
    expect(() => resolveStandardCrudDetailSchema(
      documents,
      {
        ...personRegistration,
      },
      {
        modelDocuments: [{
          type: 'model',
          Person: {
            kind: 'Entity',
            builtinFields: ['ENAME'],
            fields: [{ name: 'PersonNo', storage: { type: 'string' } }],
          },
        }],
        detailResponseFields: ['PersonNo', 'CompanyName'],
      },
    )).toThrow(expected);
  });

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
        columns: [{ key: 'name', labelKey: 'Column_Name', source: 'projection', field: 'name' }],
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
    expect(schema.columns).toEqual([{ key: 'name', labelKey: 'Column_Name', source: 'projection', field: 'name' }]);
    expect(schema.exportModes).toEqual(['current']);
  });

  it('builds list schema with metadata-owned columns actions defaults and localization checks', () => {
    const schema = buildStandardCrudListSchema(
      {
        key: 'organization.person.list',
        dicKey: 'PersonListView',
        titleKey: 'Title',
        descriptionKey: 'Description',
        mode: 'list',
        model: 'Person',
        query: {
          fields: [
            { key: 'keyword', labelKey: 'Query_Keyword', source: 'preset', preset: 'keyword', operator: 'like' },
            {
              key: 'employmentStatus',
              labelKey: 'Query_EmploymentStatus',
              source: 'model',
              field: 'EmploymentStatus',
              operator: '=',
            },
          ],
          defaults: [{ field: 'isEnabled', value: true }],
          defaultSort: { field: 'PersonNo', direction: 'asc' },
          defaultPageSize: 20,
        },
        advancedFilter: {
          enabled: true,
          sections: [{
            key: 'organization',
            titleKey: 'AdvancedFilter_Organization',
            fields: [
              { key: 'departmentId', labelKey: 'Query_Department', source: 'model', field: 'DepartmentId', operator: '=' },
              { key: 'positionId', labelKey: 'Query_Position', source: 'model', field: 'PositionId', operator: '=' },
              { key: 'isEnabled', labelKey: 'Query_IsEnabled', source: 'model', field: 'IsEnabled', operator: '=' },
            ],
          }],
        },
        columns: [
          { key: 'personNo', labelKey: 'Column_PersonNo', source: 'model', field: 'PersonNo', width: 140, sortable: true },
          { key: 'departmentName', labelKey: 'Column_DepartmentName', source: 'projection', field: 'departmentName' },
        ],
        pageActions: [
          {
            key: 'create',
            labelKey: 'Action_Create',
            standardAction: 'create',
            variant: 'primary',
            permissions: ['Organization.Person.Create'],
          },
        ],
        rowActions: [
          {
            key: 'disable',
            labelKey: 'Action_Disable',
            standardAction: 'disable',
            variant: 'danger',
            confirmMessageKey: 'Action_Disable_Confirm',
            handlerKey: 'disable',
            visibleWhen: { field: 'IsEnabled', operator: 'equals', value: true },
          },
        ],
      },
      personRegistration,
      {
        modelDocuments: [{
          type: 'model',
          Person: {
            builtinFields: ['ENAME'],
            fields: [
              { name: 'PersonNo', storage: { type: 'string' } },
              { name: 'DepartmentId', storage: { type: 'guid' }, dataSource: { type: 'provider', key: 'organization.departments' } },
              {
                name: 'PositionId',
                storage: { type: 'guid' },
                dataSource: { type: 'provider', key: 'organization.positions', dependsOn: ['DepartmentId'] },
              },
              {
                name: 'EmploymentStatus',
                storage: { type: 'string' },
                contract: { valueType: 'enum', clrType: 'EmploymentStatus' },
                dataSource: { type: 'enum', key: 'EmploymentStatus' },
              },
              { name: 'IsEnabled', storage: { type: 'bool' } },
            ],
          },
        }],
        defaultLanguageCatalog: {
          'PersonListView.Title': '人员中心',
          'PersonListView.Description': '维护组织模块中的人员基础信息。',
          'PersonListView.Query_Keyword': '关键字',
          'PersonListView.Query_EmploymentStatus': '任职状态',
          'PersonListView.Query_Department': '部门',
          'PersonListView.Query_Position': '岗位',
          'PersonListView.Query_IsEnabled': '启用状态',
          'PersonListView.AdvancedFilter_Organization': '组织信息',
          'PersonListView.Column_PersonNo': '人员编号',
          'PersonListView.Column_DepartmentName': '部门',
          'PersonListView.Action_Create': '新建',
          'PersonListView.Action_Disable': '停用',
          'PersonListView.Action_Disable_Confirm': '确认停用该人员？',
        },
      },
    );

    expect(schema.query.defaults).toEqual([{ field: 'isEnabled', value: true }]);
    expect(schema.query.defaultSort).toEqual({ field: 'PersonNo', direction: 'asc' });
    expect(schema.query.defaultPageSize).toBe(20);
    expect(schema.columns).toEqual([
      { key: 'personNo', labelKey: 'Column_PersonNo', source: 'model', field: 'PersonNo', width: 140, sortable: true },
      { key: 'departmentName', labelKey: 'Column_DepartmentName', source: 'projection', field: 'departmentName' },
    ]);
    expect(schema.pageActions[0]).toMatchObject({
      key: 'create',
      standardAction: 'create',
      variant: 'primary',
      requiredPermissions: ['Organization.Person.Create'],
    });
    expect(schema.rowActions[0]).toMatchObject({
      key: 'disable',
      confirmMessageKey: 'Action_Disable_Confirm',
      handlerKey: 'disable',
      visibleWhen: { field: 'IsEnabled', operator: 'equals', value: true },
    });
  });

  it('fails when row action visibleWhen uses an unsupported operator', () => {
    expect(() => buildStandardCrudListSchema(
      {
        key: 'organization.person.list',
        dicKey: 'PersonListView',
        titleKey: 'Title',
        descriptionKey: 'Description',
        mode: 'list',
        model: 'Person',
        columns: [{ key: 'isEnabled', labelKey: 'Column_IsEnabled', source: 'model', field: 'IsEnabled' }],
        rowActions: [{
          key: 'disable',
          labelKey: 'Action_Disable',
          standardAction: 'disable',
          handlerKey: 'disable',
          visibleWhen: { field: 'IsEnabled', operator: 'notEquals', value: true },
        }],
      },
      personRegistration,
      {
        modelDocuments: [{
          type: 'model',
          Person: { fields: [{ name: 'IsEnabled', storage: { type: 'bool' } }] },
        }],
        defaultLanguageCatalog: {
          'PersonListView.Title': '人员中心',
          'PersonListView.Description': '维护组织模块中的人员基础信息。',
          'PersonListView.Column_IsEnabled': '启用状态',
          'PersonListView.Action_Disable': '停用',
        },
      },
    )).toThrow('row action "disable" visibleWhen uses unsupported operator "notEquals"');
  });

  it('fails when row action visibleWhen references an unknown model field', () => {
    expect(() => buildStandardCrudListSchema(
      {
        key: 'organization.person.list',
        dicKey: 'PersonListView',
        titleKey: 'Title',
        descriptionKey: 'Description',
        mode: 'list',
        model: 'Person',
        columns: [{ key: 'isEnabled', labelKey: 'Column_IsEnabled', source: 'model', field: 'IsEnabled' }],
        rowActions: [{
          key: 'disable',
          labelKey: 'Action_Disable',
          standardAction: 'disable',
          handlerKey: 'disable',
          visibleWhen: { field: 'MissingState', operator: 'equals', value: true },
        }],
      },
      personRegistration,
      {
        modelDocuments: [{
          type: 'model',
          Person: { fields: [{ name: 'IsEnabled', storage: { type: 'bool' } }] },
        }],
        defaultLanguageCatalog: {
          'PersonListView.Title': '人员中心',
          'PersonListView.Description': '维护组织模块中的人员基础信息。',
          'PersonListView.Column_IsEnabled': '启用状态',
          'PersonListView.Action_Disable': '停用',
        },
      },
    )).toThrow('row action "disable" visibleWhen references unknown model field "MissingState"');
  });

  it('fails when row action visibleWhen declares an empty field', () => {
    expect(() => buildStandardCrudListSchema(
      {
        key: 'organization.person.list',
        dicKey: 'PersonListView',
        titleKey: 'Title',
        descriptionKey: 'Description',
        mode: 'list',
        model: 'Person',
        rowActions: [{
          key: 'disable',
          labelKey: 'Action_Disable',
          standardAction: 'disable',
          handlerKey: 'disable',
          visibleWhen: { field: '', operator: 'equals', value: true },
        }],
      },
      personRegistration,
    )).toThrow('row action "disable" visibleWhen must declare field');
  });

  it('fails when row action visibleWhen declares an empty operator', () => {
    expect(() => buildStandardCrudListSchema(
      {
        key: 'organization.person.list',
        dicKey: 'PersonListView',
        titleKey: 'Title',
        descriptionKey: 'Description',
        mode: 'list',
        model: 'Person',
        rowActions: [{
          key: 'disable',
          labelKey: 'Action_Disable',
          standardAction: 'disable',
          handlerKey: 'disable',
          visibleWhen: { field: 'IsEnabled', operator: '', value: true },
        }],
      },
      personRegistration,
    )).toThrow('row action "disable" visibleWhen must declare operator');
  });

  it('fails when row action visibleWhen value is not primitive or null', () => {
    expect(() => buildStandardCrudListSchema(
      {
        key: 'organization.person.list',
        dicKey: 'PersonListView',
        titleKey: 'Title',
        descriptionKey: 'Description',
        mode: 'list',
        model: 'Person',
        rowActions: [{
          key: 'disable',
          labelKey: 'Action_Disable',
          standardAction: 'disable',
          handlerKey: 'disable',
          visibleWhen: { field: 'IsEnabled', operator: 'equals', value: { enabled: true } },
        }],
      },
      personRegistration,
    )).toThrow('row action "disable" visibleWhen value must be a boolean, string, number or null');
  });

  it('fails when row action visibleWhen is declared without required properties', () => {
    expect(() => buildStandardCrudListSchema(
      {
        key: 'organization.person.list',
        dicKey: 'PersonListView',
        titleKey: 'Title',
        descriptionKey: 'Description',
        mode: 'list',
        model: 'Person',
        rowActions: [{
          key: 'disable',
          labelKey: 'Action_Disable',
          standardAction: 'disable',
          handlerKey: 'disable',
          visibleWhen: {},
        }],
      },
      personRegistration,
    )).toThrow('row action "disable" visibleWhen must declare field');
  });

  it('fails when projection column has no field', () => {
    expect(() => buildStandardCrudListSchema(
      {
        key: 'organization.person.list',
        dicKey: 'PersonListView',
        titleKey: 'Title',
        mode: 'list',
        model: 'Person',
        columns: [{ key: 'departmentName', labelKey: 'Column_DepartmentName', source: 'projection' }],
      },
      personRegistration,
      {
        modelDocuments: [{ type: 'model', Person: { fields: [] } }],
        defaultLanguageCatalog: {
          'PersonListView.Title': '人员中心',
          'PersonListView.Description': '维护组织模块中的人员基础信息。',
          'PersonListView.Column_DepartmentName': '部门',
        },
      },
    )).toThrow('projection column "departmentName" must declare field');
  });

  it('fails when a metadata label key is missing from default language catalog', () => {
    expect(() => buildStandardCrudListSchema(
      {
        key: 'organization.person.list',
        dicKey: 'PersonListView',
        titleKey: 'Title',
        mode: 'list',
        model: 'Person',
        pageActions: [{ key: 'create', labelKey: 'Action_Create', standardAction: 'create' }],
      },
      personRegistration,
      {
        modelDocuments: [{ type: 'model', Person: { fields: [] } }],
        defaultLanguageCatalog: {
          'PersonListView.Title': '人员中心',
          'PersonListView.Description': '维护组织模块中的人员基础信息。',
        },
      },
    )).toThrow('missing dictionary key "PersonListView.Action_Create"');
  });

  it('fails when query defaults reference an undeclared query field', () => {
    expect(() => buildStandardCrudListSchema(
      {
        key: 'organization.person.list',
        dicKey: 'PersonListView',
        titleKey: 'Title',
        mode: 'list',
        model: 'Person',
        query: {
          fields: [{ key: 'keyword', labelKey: 'Query_Keyword', source: 'preset', preset: 'keyword' }],
          defaults: [{ field: 'isEnabled', value: true }],
        },
      },
      personRegistration,
      {
        modelDocuments: [{ type: 'model', Person: { fields: [] } }],
        defaultLanguageCatalog: {
          'PersonListView.Title': '人员中心',
          'PersonListView.Description': '维护组织模块中的人员基础信息。',
          'PersonListView.Query_Keyword': '关键字',
        },
      },
    )).toThrow('query default "isEnabled" does not reference a declared query field');
  });

  it('fails when a list view references an unknown model', () => {
    expect(() => buildStandardCrudListSchema(
      {
        key: 'organization.person.list',
        dicKey: 'PersonListView',
        titleKey: 'Title',
        mode: 'list',
        model: 'MissingPerson',
      },
      personRegistration,
      {
        modelDocuments: [{ type: 'model', Person: { fields: [] } }],
        defaultLanguageCatalog: {
          'PersonListView.Title': '人员中心',
          'PersonListView.Description': '维护组织模块中的人员基础信息。',
        },
      },
    )).toThrow('view "organization.person.list" references unknown model "MissingPerson"');
  });

  it('fails when a query field uses an unsupported preset', () => {
    expect(() => buildStandardCrudListSchema(
      {
        key: 'organization.person.list',
        dicKey: 'PersonListView',
        titleKey: 'Title',
        mode: 'list',
        model: 'Person',
        query: {
          fields: [{ key: 'quick', labelKey: 'Query_Quick', source: 'preset', preset: 'quickSearch' }],
        },
      },
      personRegistration,
      {
        modelDocuments: [{ type: 'model', Person: { fields: [] } }],
        defaultLanguageCatalog: {
          'PersonListView.Title': '人员中心',
          'PersonListView.Description': '维护组织模块中的人员基础信息。',
          'PersonListView.Query_Quick': '快速搜索',
        },
      },
    )).toThrow('unsupported query preset "quickSearch"');
  });

  it.each(['fields', 'page', 'pageIndex', 'pageSize', 'sortBy', 'sortDirection'])(
    'fails when query metadata uses reserved standard CRUD query key "%s"',
    (reservedKey) => {
      expect(() => buildStandardCrudListSchema(
        {
          key: 'organization.person.list',
          dicKey: 'PersonListView',
          titleKey: 'Title',
          mode: 'list',
          model: 'Person',
          query: {
            fields: [{ key: reservedKey, labelKey: 'Query_Reserved', source: 'preset', preset: 'keyword' }],
          },
        },
        personRegistration,
        {
          modelDocuments: [{ type: 'model', Person: { fields: [] } }],
          defaultLanguageCatalog: {
            'PersonListView.Title': '人员中心',
            'PersonListView.Description': '维护组织模块中的人员基础信息。',
            'PersonListView.Query_Reserved': '保留字段',
          },
        },
      )).toThrow(`query field key "${reservedKey}" is reserved for standard CRUD requests`);
    },
  );

  it('fails when a metadata action uses an unsupported standard action', () => {
    expect(() => buildStandardCrudListSchema(
      {
        key: 'organization.person.list',
        dicKey: 'PersonListView',
        titleKey: 'Title',
        mode: 'list',
        model: 'Person',
        rowActions: [{ key: 'archive', labelKey: 'Action_Archive', standardAction: 'archive' }],
      },
      personRegistration,
      {
        modelDocuments: [{ type: 'model', Person: { fields: [] } }],
        defaultLanguageCatalog: {
          'PersonListView.Title': '人员中心',
          'PersonListView.Description': '维护组织模块中的人员基础信息。',
          'PersonListView.Action_Archive': '归档',
        },
      },
    )).toThrow('unsupported standard action "archive"');
  });

  it('fails when an action handlerKey is not registered on the current resource', () => {
    expect(() => buildStandardCrudListSchema(
      {
        key: 'organization.person.list',
        dicKey: 'PersonListView',
        titleKey: 'Title',
        mode: 'list',
        model: 'Person',
        rowActions: [{ key: 'disable', labelKey: 'Action_Disable', standardAction: 'disable', handlerKey: 'missingDisable' }],
      },
      { ...personRegistration, actionHandlers: { disable: async () => undefined } },
      {
        modelDocuments: [{ type: 'model', Person: { fields: [] } }],
        defaultLanguageCatalog: {
          'PersonListView.Title': '人员中心',
          'PersonListView.Description': '维护组织模块中的人员基础信息。',
          'PersonListView.Action_Disable': '停用',
        },
      },
    )).toThrow('action handler "missingDisable" is not registered on resource "organization.person"');
  });

  it('accepts a module registered action input and rejects a missing renderer', () => {
    const view = {
      key: 'organization.person.list',
      dicKey: 'PersonListView',
      titleKey: 'Title',
      mode: 'list',
      model: 'Person',
      rowActions: [{
        key: 'disable',
        labelKey: 'Action_Disable',
        standardAction: 'disable',
        handlerKey: 'disable',
        inputKey: 'lifecycle-reason',
      }],
    };
    const options = {
      modelDocuments: [{ type: 'model', Person: { fields: [] } }],
      defaultLanguageCatalog: {
        'PersonListView.Title': '人员中心',
        'PersonListView.Description': '维护组织模块中的人员基础信息。',
        'PersonListView.Action_Disable': '停用',
      },
    };

    expect(() => buildStandardCrudListSchema(
      view,
      { ...personRegistration, actionHandlers: { disable: async () => undefined } },
      options,
    )).toThrow('action input renderer "lifecycle-reason" is not registered on resource "organization.person"');

    const schema = buildStandardCrudListSchema(
      view,
      {
        ...personRegistration,
        actionHandlers: { disable: async () => undefined },
        actionInputRenderers: { 'lifecycle-reason': () => null },
      },
      options,
    );
    expect(schema.rowActions[0]).toMatchObject({ inputKey: 'lifecycle-reason', handlerKey: 'disable' });
  });

  it('fails when metadata declares an unsupported dataSource type', () => {
    expect(() => buildStandardCrudListSchema(
      {
        key: 'organization.person.list',
        dicKey: 'PersonListView',
        titleKey: 'Title',
        mode: 'list',
        model: 'Person',
        query: {
          fields: [{ key: 'departmentId', labelKey: 'Query_Department', source: 'model', field: 'DepartmentId' }],
        },
      },
      personRegistration,
      {
        modelDocuments: [{
          type: 'model',
          Person: {
            fields: [{ name: 'DepartmentId', dataSource: { type: 'tree' } }],
          },
        }],
        defaultLanguageCatalog: {
          'PersonListView.Title': '人员中心',
          'PersonListView.Description': '维护组织模块中的人员基础信息。',
          'PersonListView.Query_Department': '部门',
        },
      },
    )).toThrow('unsupported dataSource type "tree"');
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
        columns: [undefined, { key: 'name', labelKey: 'Column_Name', source: 'projection', field: 'name' }],
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
    expect(schema.columns).toEqual([{ key: 'name', labelKey: 'Column_Name', source: 'projection', field: 'name' }]);
    expect(schema.pageActions).toEqual([{ key: 'refresh', labelKey: 'Action_Refresh' }]);
    expect(schema.rowActions).toEqual([{ key: 'view', labelKey: 'Action_View' }]);
  });

  it('fails when duplicate model documents declare the same model key', () => {
    expect(() => buildStandardCrudListSchema(
      {
        key: 'organization.person.list',
        dicKey: 'PersonListView',
        titleKey: 'Title',
        mode: 'list',
        model: 'Person',
      },
      personRegistration,
      {
        modelDocuments: [
          { type: 'model', Person: { fields: [] } },
          { type: 'model', Person: { fields: [] } },
        ],
        defaultLanguageCatalog: {
          'PersonListView.Title': '人员中心',
          'PersonListView.Description': '维护组织模块中的人员基础信息。',
        },
      },
    )).toThrow('model "Person" is declared by multiple metadata documents');
  });

  it('fails when an action uses an unsupported variant', () => {
    expect(() => buildStandardCrudListSchema(
      {
        key: 'organization.person.list',
        dicKey: 'PersonListView',
        titleKey: 'Title',
        mode: 'list',
        model: 'Person',
        pageActions: [{ key: 'create', labelKey: 'Action_Create', standardAction: 'create', variant: 'quiet' }],
      },
      personRegistration,
      {
        modelDocuments: [{ type: 'model', Person: { fields: [] } }],
        defaultLanguageCatalog: {
          'PersonListView.Title': '人员中心',
          'PersonListView.Description': '维护组织模块中的人员基础信息。',
          'PersonListView.Action_Create': '新建',
        },
      },
    )).toThrow('unsupported action variant "quiet"');
  });

  it('builds a detail schema from runtime view metadata and registration', () => {
    const schema = buildStandardCrudDetailSchema(
      {
        key: 'organization.person.detail',
        dicKey: 'PersonDetailView',
        titleKey: 'Title',
        descriptionKey: 'Description',
        mode: 'detail',
        sections: ['basic', { key: 'organization', titleKey: '组织信息', fields: ['departmentId'] }],
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
    expect(schema.sections).toEqual([
      { key: 'basic', titleKey: 'basic', fields: [] },
      { key: 'organization', titleKey: '组织信息', fields: ['departmentId'] },
    ]);
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
