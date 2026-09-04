import { describe, expect, it } from 'vitest';
import { departmentStandardCrudResource } from '../../../../modules/organization/src/standard-crud/departmentStandardCrudResource';
import departmentModelDocument from '../../../../../modules/Organization/metadata/models/Department.model.json';
import departmentListViewDocument from '../../../../../modules/Organization/metadata/views/DepartmentList.view.json';
import { buildStandardCrudListSchema } from '../metadata/standardCrudSchemaResolver';

describe('organization department standard CRUD metadata', () => {
  it('compiles the UpdatedAt list column against the Department model authoring document', () => {
    const schema = buildStandardCrudListSchema(
      departmentListViewDocument.view,
      departmentStandardCrudResource,
      { modelDocuments: [departmentModelDocument] },
    );

    expect(schema.columns.find((column) => column.key === 'updatedAt')).toMatchObject({
      source: 'model',
      field: 'UpdatedAt',
    });
  });
});
