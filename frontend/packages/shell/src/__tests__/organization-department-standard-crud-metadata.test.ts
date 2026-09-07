import detailViewDocument from '../../../../../modules/Organization/metadata/views/DepartmentDetail.view.json';
import { describe, expect, it } from 'vitest';
import { departmentStandardCrudResource } from '../../../../modules/organization/src/standard-crud/departmentStandardCrudResource';
import departmentModelDocument from '../../../../../modules/Organization/metadata/models/Department.model.json';
import departmentListViewDocument from '../../../../../modules/Organization/metadata/views/DepartmentList.view.json';
import organizationRuntimeModels from '../../../../../tests/Kernel.Tests/Fixtures/OrganizationRuntimeModels.contract.json';
import {
  buildStandardCrudListSchema,
  resolveStandardCrudDetailSchema,
} from '../metadata/standardCrudSchemaResolver';
import { buildStandardCrudFormSchemaFromModel } from '../standard-crud/standardCrudMetadataForm';

describe('organization department standard CRUD metadata', () => {
  const departmentRuntimeModelDocument = organizationRuntimeModels.department;

  it('compiles the UpdatedAt list column against the projected Department runtime field catalog', () => {
    const schema = buildStandardCrudListSchema(
      departmentListViewDocument.view,
      departmentStandardCrudResource,
      { modelDocuments: [departmentRuntimeModelDocument] },
    );

    expect(departmentModelDocument.Department.fields.some((field) => field.name === 'UpdatedAt')).toBe(false);
    expect(departmentRuntimeModelDocument.Department.fields.some((field) => field.name === 'UpdatedAt')).toBe(true);
    expect(schema.columns.find((column) => column.key === 'updatedAt')).toMatchObject({
      source: 'model',
      field: 'UpdatedAt',
    });
  });

  it('keeps projected Department fixed fields out of create and edit forms', () => {
    const create = buildStandardCrudFormSchemaFromModel(departmentRuntimeModelDocument.Department, {
      mode: 'create',
      enumOptions: {},
    });
    const edit = buildStandardCrudFormSchemaFromModel(departmentRuntimeModelDocument.Department, {
      mode: 'edit',
      enumOptions: {},
    });
    const authoringFieldNames = new Set<string>(departmentModelDocument.Department.fields.map((field) => field.name));
    const projectedFixedFields: string[] = departmentRuntimeModelDocument.Department.fields
      .filter((field) => !authoringFieldNames.has(field.name))
      .map((field) => field.name);

    expect(projectedFixedFields.length).toBeGreaterThan(0);
    expect(create.fields.some((field) => projectedFixedFields.includes(field.modelField))).toBe(false);
    expect(edit.fields.some((field) => projectedFixedFields.includes(field.modelField))).toBe(false);
    expect(create.sections[0]?.fields).toEqual(['CompanyId', 'ParentDepartmentId', 'ECODE', 'ENAME', 'SortOrder']);
    expect(edit.sections[0]?.fields).toEqual([
      'CompanyId',
      'ParentDepartmentId',
      'ECODE',
      'ENAME',
      'SortOrder',
      'IsEnabled',
    ]);
  });

  it('resolves Department detail fields from the projected runtime field catalog', () => {
    const schema = resolveStandardCrudDetailSchema(
      [detailViewDocument],
      { ...departmentStandardCrudResource, detailViewKey: 'organization.department.detail' },
      { modelDocuments: [departmentRuntimeModelDocument] },
    );

    expect(schema.sections.at(-1)?.fields.map((field) => field.key)).toEqual([
      'CreatedBy', 'CreatedAt', 'UpdatedBy', 'UpdatedAt',
    ]);
  });
});
