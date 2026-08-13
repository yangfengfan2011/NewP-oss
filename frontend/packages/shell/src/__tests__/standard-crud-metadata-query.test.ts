import { describe, expect, it } from 'vitest';
import {
  buildStandardCrudDefaultFilters,
  buildStandardCrudEmptyFilters,
  buildStandardCrudListQuery,
  parseStandardCrudListState,
  serializeStandardCrudListState,
  collectStandardCrudListRequestFields,
} from '../standard-crud/standardCrudMetadataQuery';

const querySchema = {
  fields: [
    { key: 'keyword', labelKey: 'Query_Keyword', source: 'preset' as const, preset: 'keyword', operator: 'like' },
    { key: 'employmentStatus', labelKey: 'Query_EmploymentStatus', source: 'model' as const, field: 'EmploymentStatus', operator: '=' },
  ],
  advancedFilter: {
    enabled: true,
    sections: [{
      key: 'organization',
      fields: [
        { key: 'departmentId', labelKey: 'Query_Department', source: 'model' as const, field: 'DepartmentId', operator: '=' },
        { key: 'positionId', labelKey: 'Query_Position', source: 'model' as const, field: 'PositionId', operator: '=' },
        { key: 'isEnabled', labelKey: 'Query_IsEnabled', source: 'model' as const, field: 'IsEnabled', operator: '=' },
      ],
    }],
  },
  defaults: [{ field: 'isEnabled', value: true }],
  defaultPageSize: 20,
};

describe('standard CRUD metadata query helpers', () => {
  it('builds empty and default filters from declared query keys', () => {
    expect(buildStandardCrudEmptyFilters(querySchema)).toEqual({
      keyword: '',
      employmentStatus: '',
      departmentId: '',
      positionId: '',
      isEnabled: '',
    });
    expect(buildStandardCrudDefaultFilters(querySchema)).toEqual({
      keyword: '',
      employmentStatus: '',
      departmentId: '',
      positionId: '',
      isEnabled: true,
    });
  });

  it('submits only declared keys and skips empty values', () => {
    expect(buildStandardCrudListQuery(querySchema, {
      keyword: '张',
      employmentStatus: '',
      departmentId: 'Dept_1',
      positionId: '',
      isEnabled: true,
      companyId: 'must-not-leak',
    })).toEqual({
      keyword: '张',
      departmentId: 'Dept_1',
      isEnabled: true,
    });
  });

  it('collects list request fields from columns and row action state dependencies', () => {
    expect(collectStandardCrudListRequestFields(
      [
        { field: ' PersonNo ' },
        { field: 'DepartmentName' },
        { field: 'PersonNo' },
      ],
      [
        { visibleWhen: { field: ' IsEnabled ' } },
        { visibleWhen: { field: 'IsEnabled' } },
        {},
      ],
    )).toEqual(['EID', 'PersonNo', 'DepartmentName', 'IsEnabled']);
  });

  it('restores only declared filters paging and sortable metadata from URL', () => {
    expect(parseStandardCrudListState(
      querySchema,
      new URLSearchParams('keyword=%E5%BC%A0&departmentId=Dept_1&secret=leak&page=3&pageSize=50&sortBy=ENAME&sortDirection=desc'),
      [
        { field: 'PersonNo', sortable: true },
        { field: 'ENAME', sortable: true },
        { field: 'DepartmentName', sortable: false },
      ],
    )).toEqual({
      filters: {
        keyword: '张',
        employmentStatus: '',
        departmentId: 'Dept_1',
        positionId: '',
        isEnabled: true,
      },
      page: 3,
      pageSize: 50,
      sortBy: 'ENAME',
      sortDirection: 'desc',
    });
  });

  it('falls back to metadata defaults for invalid URL paging and sorting', () => {
    expect(parseStandardCrudListState(
      {
        ...querySchema,
        defaultSort: { field: 'PersonNo', direction: 'asc' as const },
      },
      new URLSearchParams('page=0&pageSize=200&sortBy=DepartmentName&sortDirection=sideways'),
      [
        { field: 'PersonNo', sortable: true },
        { field: 'DepartmentName', sortable: false },
      ],
    )).toMatchObject({
      page: 1,
      pageSize: 20,
      sortBy: 'PersonNo',
      sortDirection: 'asc',
    });
  });

  it('serializes committed state without leaking drafts and preserves unrelated route params', () => {
    const params = serializeStandardCrudListState(
      querySchema,
      {
        filters: {
          keyword: '张',
          employmentStatus: '',
          departmentId: 'Dept_1',
          positionId: '',
          isEnabled: true,
          secret: 'must-not-leak',
        },
        page: 2,
        pageSize: 50,
        sortBy: 'ENAME',
        sortDirection: 'desc',
      },
      new URLSearchParams('drawer=view&id=Person_1&oldFilter=remove-me'),
    );

    expect(params.toString()).toBe(
      'drawer=view&id=Person_1&keyword=%E5%BC%A0&departmentId=Dept_1&isEnabled=true&page=2&pageSize=50&sortBy=ENAME&sortDirection=desc',
    );
  });
});
