import { describe, expect, it } from 'vitest';
import {
  buildStandardCrudDefaultFilters,
  buildStandardCrudEmptyFilters,
  buildStandardCrudListQuery,
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
});
