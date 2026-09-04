import { describe, expect, it } from 'vitest';
import { normalizeStandardCrudSubmitValues } from '@icp-platform/sdk';
import { companyStandardCrudResource } from '../../../../modules/organization/src/standard-crud/companyStandardCrudResource';
import companyModelDocument from '../../../../../modules/Organization/metadata/models/Company.model.json';
import companyListViewDocument from '../../../../../modules/Organization/metadata/views/CompanyList.view.json';
import { buildStandardCrudListSchema } from '../metadata/standardCrudSchemaResolver';
import { buildStandardCrudFormSchemaFromModel } from '../standard-crud/standardCrudMetadataForm';

describe('organization company standard CRUD metadata', () => {
  it('compiles the UpdatedAt list column against the Company model authoring document', () => {
    const schema = buildStandardCrudListSchema(
      companyListViewDocument.view,
      companyStandardCrudResource,
      { modelDocuments: [companyModelDocument] },
    );

    expect(schema.columns.find((column) => column.key === 'updatedAt')).toMatchObject({
      source: 'model',
      field: 'UpdatedAt',
    });
  });

  it('keeps the server-owned UpdatedAt field out of Company mutation forms and payloads', () => {
    const create = buildStandardCrudFormSchemaFromModel(companyModelDocument.Company, {
      mode: 'create',
      enumOptions: {},
    });
    const edit = buildStandardCrudFormSchemaFromModel(companyModelDocument.Company, {
      mode: 'edit',
      enumOptions: {},
    });
    const values = Object.fromEntries(create.fields.map((field) => [field.key, field.defaultValue ?? '']));
    const payload = companyStandardCrudResource.buildMutationValues?.(values)
      ?? normalizeStandardCrudSubmitValues(create.fields, values);

    expect(create.fields.some((field) => field.modelField === 'UpdatedAt')).toBe(false);
    expect(edit.fields.some((field) => field.modelField === 'UpdatedAt')).toBe(false);
    expect(payload).not.toHaveProperty('UpdatedAt');
  });
});
