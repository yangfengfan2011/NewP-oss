import detailViewDocument from '../../../../../modules/Organization/metadata/views/CompanyDetail.view.json';
import { describe, expect, it } from 'vitest';
import { companyStandardCrudResource } from '../../../../modules/organization/src/standard-crud/companyStandardCrudResource';
import companyModelDocument from '../../../../../modules/Organization/metadata/models/Company.model.json';
import companyListViewDocument from '../../../../../modules/Organization/metadata/views/CompanyList.view.json';
import organizationRuntimeModels from '../../../../../tests/Kernel.Tests/Fixtures/OrganizationRuntimeModels.contract.json';
import {
  buildStandardCrudListSchema,
  resolveStandardCrudDetailSchema,
} from '../metadata/standardCrudSchemaResolver';
import { buildStandardCrudFormSchemaFromModel } from '../standard-crud/standardCrudMetadataForm';

describe('organization company standard CRUD metadata', () => {
  const companyRuntimeModelDocument = organizationRuntimeModels.company;

  it('compiles the UpdatedAt list column against the projected Company runtime field catalog', () => {
    const schema = buildStandardCrudListSchema(
      companyListViewDocument.view,
      companyStandardCrudResource,
      { modelDocuments: [companyRuntimeModelDocument] },
    );

    expect(companyModelDocument.Company.fields.some((field) => field.name === 'UpdatedAt')).toBe(false);
    expect(companyRuntimeModelDocument.Company.fields.some((field) => field.name === 'UpdatedAt')).toBe(true);
    expect(schema.columns.find((column) => column.key === 'updatedAt')).toMatchObject({
      source: 'model',
      field: 'UpdatedAt',
    });
  });

  it('keeps projected Company fixed fields out of create and edit forms', () => {
    const create = buildStandardCrudFormSchemaFromModel(companyRuntimeModelDocument.Company, {
      mode: 'create',
      enumOptions: {},
    });
    const edit = buildStandardCrudFormSchemaFromModel(companyRuntimeModelDocument.Company, {
      mode: 'edit',
      enumOptions: {},
    });
    const authoringFieldNames = new Set<string>(companyModelDocument.Company.fields.map((field) => field.name));
    const projectedFixedFields: string[] = companyRuntimeModelDocument.Company.fields
      .filter((field) => !authoringFieldNames.has(field.name))
      .map((field) => field.name);

    expect(projectedFixedFields.length).toBeGreaterThan(0);
    expect(create.fields.some((field) => projectedFixedFields.includes(field.modelField))).toBe(false);
    expect(edit.fields.some((field) => projectedFixedFields.includes(field.modelField))).toBe(false);
    expect(create.sections[0]?.fields).toEqual(['ECODE', 'ENAME', 'SortOrder']);
    expect(edit.sections[0]?.fields).toEqual(['ECODE', 'ENAME', 'SortOrder', 'IsEnabled']);
  });

  it('resolves Company detail fields from the projected runtime field catalog', () => {
    const schema = resolveStandardCrudDetailSchema(
      [detailViewDocument],
      { ...companyStandardCrudResource, detailViewKey: 'organization.company.detail' },
      { modelDocuments: [companyRuntimeModelDocument] },
    );

    expect(schema.sections.at(-1)?.fields.map((field) => field.key)).toEqual([
      'CreatedBy', 'CreatedAt', 'UpdatedBy', 'UpdatedAt',
    ]);
  });

  it('rejects detail fields that are absent from the runtime Company field catalog', () => {
    expect(() => resolveStandardCrudDetailSchema(
      [{
        type: 'view',
        view: {
          key: 'organization.company.detail',
          mode: 'detail',
          model: 'Company',
          sections: [{ key: 'audit', titleKey: 'Section_Audit', fields: ['UpdatedAt'] }],
        },
      }],
      { ...companyStandardCrudResource, detailViewKey: 'organization.company.detail' },
      {
        modelDocuments: [{
          type: 'model',
          Company: { kind: 'Entity', fields: [{ name: 'ENAME', storage: { type: 'string' } }] },
        }],
      },
    )).toThrow('detail section "audit" references unknown model field "UpdatedAt"');
  });
});
