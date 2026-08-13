import { describe, expect, it } from 'vitest';
import { mapPersonDetailView, mapPersonListItem } from '../../../../modules/organization/src/services/organizationService';
import { personStandardCrudResource } from '../../../../modules/organization/src/standard-crud/personStandardCrudResource';
import personListViewDocument from '../../../../../modules/Organization/metadata/views/PersonList.view.json';

describe('organization person standard CRUD phase 2 registration', () => {
  it('shrinks registration to metadata-first runtime hooks only', () => {
    expect(personStandardCrudResource.fields).toBeUndefined();
    expect(personStandardCrudResource.formSections).toBeUndefined();
    expect(personStandardCrudResource.listActions).toBeUndefined();
    expect(personStandardCrudResource.detailActions).toBeUndefined();
    expect(personStandardCrudResource.buildColumns).toBeUndefined();
    expect(personStandardCrudResource.renderFilters).toBeUndefined();
    expect(personStandardCrudResource.buildMutationValues).toBeUndefined();
    expect(personStandardCrudResource.lookupProviders?.map((provider) => provider.key)).toEqual([
      'organization.companies',
      'organization.departments',
      'organization.positions',
    ]);
    expect(Object.keys(personStandardCrudResource.summaryRenderers ?? {})).toEqual(['userBinding', 'authorizationSummary']);
    expect(Object.keys(personStandardCrudResource.actionHandlers ?? {})).toEqual(['refresh', 'delete', 'disable', 'enable']);
  });

  it('retains metadata-compatible permissions and runtime adapters', () => {
    expect(personStandardCrudResource.permissions).toEqual(['Organization.Person.View']);
    expect(personStandardCrudResource.createEmptyFilters?.()).toEqual({
      keyword: '',
      employmentStatus: '',
      companyId: '',
      departmentId: '',
      positionId: '',
      isEnabled: '',
    });
    expect(personStandardCrudResource.buildListQuery?.({
      keyword: 'P001',
      employmentStatus: 'FullTime',
      companyId: 'company-1',
      departmentId: 'department-1',
      positionId: 'position-1',
      isEnabled: 'false',
    })).toEqual({
      keyword: 'P001',
      employmentStatus: 'FullTime',
      companyId: 'company-1',
      departmentId: 'department-1',
      positionId: 'position-1',
      isEnabled: 'false',
    });
  });

  it('declares the six filters, personnel columns, sort whitelist and guarded actions in metadata', () => {
    const view = personListViewDocument.view;
    const advancedFields = view.advancedFilter.sections.flatMap((section) => section.fields);

    expect(view.query.fields.map((field) => field.key)).toEqual(['keyword', 'employmentStatus']);
    expect(advancedFields.map((field) => field.key)).toEqual(['companyId', 'departmentId', 'positionId', 'isEnabled']);
    expect(view.columns.map((column) => column.key)).toEqual([
      'personNo', 'name', 'companyName', 'departmentName', 'positionName', 'employmentStatus', 'isEnabled',
    ]);
    expect(view.columns.filter((column) => column.sortable).map((column) => column.field)).toEqual([
      'PersonNo', 'ENAME', 'EmploymentStatus', 'IsEnabled',
    ]);
    expect(view.columns.find((column) => column.key === 'name')).toMatchObject({ standardAction: 'view' });
    expect(view.rowActions.find((action) => action.standardAction === 'disable')).toMatchObject({ inputKey: 'disableReason' });
    expect(view.rowActions.find((action) => action.standardAction === 'delete')).toMatchObject({
      disabledWhen: { field: 'AccountEID', operator: 'notEmpty' },
    });
  });

  it('does not present empty employment status as FullTime', () => {
    const row = mapPersonListItem({
      EID: 'Person_1',
      PersonNo: 'P001',
      ENAME: '张三',
      EmploymentStatus: null,
      IsEnabled: true,
    });

    expect(row.EmploymentStatus).toBe('');
    expect(row.employmentStatus).toBe('');
    expect(row.employmentStatusLabel).toBe('');

    const detail = mapPersonDetailView({
      fields: {
        EID: 'Person_1',
        PersonNo: 'P001',
        ENAME: '张三',
        EmploymentStatus: null,
        IsEnabled: true,
      },
      summaries: {},
    });

    expect(detail.employmentStatus).toBe('');
    expect(detail.employmentStatusLabel).toBe('');
  });
});
