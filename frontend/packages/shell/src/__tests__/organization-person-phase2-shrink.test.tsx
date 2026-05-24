import { describe, expect, it } from 'vitest';
import { mapPersonDetailView, mapPersonListItem } from '../../../../modules/organization/src/services/organizationService';
import { personStandardCrudResource } from '../../../../modules/organization/src/standard-crud/personStandardCrudResource';

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
    expect(Object.keys(personStandardCrudResource.summaryRenderers ?? {})).toEqual(['userLink', 'authorizationSummary']);
    expect(Object.keys(personStandardCrudResource.actionHandlers ?? {})).toEqual(['refresh', 'delete', 'disable', 'enable']);
  });

  it('retains metadata-compatible permissions and runtime adapters', () => {
    expect(personStandardCrudResource.permissions).toEqual([
      'Organization.Person.View',
      'Organization.Person.Create',
    ]);
    expect(personStandardCrudResource.createEmptyFilters?.()).toEqual({
      keyword: '',
      employmentStatus: '',
    });
    expect(personStandardCrudResource.buildListQuery?.({
      keyword: 'P001',
      employmentStatus: 'FullTime',
    })).toEqual({
      keyword: 'P001',
      employmentStatus: 'FullTime',
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
