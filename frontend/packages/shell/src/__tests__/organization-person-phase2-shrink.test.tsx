import { describe, expect, it } from 'vitest';
import { personStandardCrudResource } from '../../../../modules/organization/src/standard-crud/personStandardCrudResource';

describe('organization person standard CRUD phase 2 registration', () => {
  it('declares platform fields, form sections, lookup providers and summary renderers', () => {
    expect(personStandardCrudResource.fields?.map((field) => field.key)).toEqual([
      'personNo',
      'name',
      'companyId',
      'departmentId',
      'positionId',
      'employmentStatus',
      'isEnabled',
    ]);
    expect(personStandardCrudResource.fields?.find((field) => field.key === 'personNo')?.readOnly).toBe(true);
    expect(personStandardCrudResource.formSections?.map((section) => section.key)).toEqual(['basic', 'organization']);
    expect(personStandardCrudResource.lookupProviders?.map((provider) => provider.key)).toEqual([
      'organization.companies',
      'organization.departments',
      'organization.positions',
    ]);
    expect(Object.keys(personStandardCrudResource.summaryRenderers ?? {})).toEqual(['userLink', 'authorizationSummary']);
    expect(personStandardCrudResource.listActions?.map((action) => action.key)).toEqual([
      'create',
      'refresh',
      'view',
      'edit',
      'delete',
      'disable',
    ]);
    expect(personStandardCrudResource.detailActions?.map((action) => action.key)).toEqual(['disable']);
  });

  it('normalizes person mutation values through platform field definitions', () => {
    const values = personStandardCrudResource.buildMutationValues?.({
      personNo: ' P001 ',
      name: ' 张三 ',
      companyId: '',
      departmentId: '',
      positionId: '',
      employmentStatus: 'FullTime',
      isEnabled: true,
    });

    expect(values).toEqual({
      PersonNo: 'P001',
      ENAME: '张三',
      CompanyId: null,
      DepartmentId: null,
      PositionId: null,
      EmploymentStatus: 'FullTime',
      IsEnabled: true,
    });
  });
});
