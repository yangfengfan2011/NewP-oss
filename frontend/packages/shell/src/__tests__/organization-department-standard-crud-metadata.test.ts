import { describe, expect, it } from 'vitest';
import { normalizeStandardCrudSubmitValues } from '@icp-platform/sdk';
import { departmentStandardCrudResource } from '../../../../modules/organization/src/standard-crud/departmentStandardCrudResource';
import departmentModelDocument from '../../../../../modules/Organization/metadata/models/Department.model.json';
import departmentListViewDocument from '../../../../../modules/Organization/metadata/views/DepartmentList.view.json';
import { buildStandardCrudListSchema } from '../metadata/standardCrudSchemaResolver';
import { buildStandardCrudFormSchemaFromModel } from '../standard-crud/standardCrudMetadataForm';

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

  it('keeps the server-owned UpdatedAt field out of Department mutation forms and payloads', () => {
    const create = buildStandardCrudFormSchemaFromModel(departmentModelDocument.Department, {
      mode: 'create',
      enumOptions: {},
    });
    const edit = buildStandardCrudFormSchemaFromModel(departmentModelDocument.Department, {
      mode: 'edit',
      enumOptions: {},
    });
    const values = Object.fromEntries(create.fields.map((field) => [field.key, field.defaultValue ?? '']));
    const payload = departmentStandardCrudResource.buildMutationValues?.(values)
      ?? normalizeStandardCrudSubmitValues(create.fields, values);

    expect(create.fields.some((field) => field.modelField === 'UpdatedAt')).toBe(false);
    expect(edit.fields.some((field) => field.modelField === 'UpdatedAt')).toBe(false);
    expect(payload).not.toHaveProperty('UpdatedAt');
  });
});
