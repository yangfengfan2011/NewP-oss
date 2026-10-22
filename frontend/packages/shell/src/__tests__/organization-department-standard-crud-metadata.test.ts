import type { ActionDataAccessRequirement } from '@icp-platform/sdk';
import compiledRequirements from '../../../../../tests/Kernel.Tests/Fixtures/OrganizationDataAccess.contract.json';
import { bindActionDataAccess } from '../metadata/runtimeMetadataLoader';
import detailViewDocument from '../../../../modules/platform/organization/metadata/views/DepartmentDetail.view.json';
import { describe, expect, it } from 'vitest';
import { departmentStandardCrudResource } from '../../../../modules/platform/organization/src/standard-crud/departmentStandardCrudResource';
import departmentModelDocument from '../../../../modules/platform/organization/metadata/models/Department.model.json';
import departmentListViewDocument from '../../../../modules/platform/organization/metadata/views/DepartmentList.view.json';
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

  it('binds the actual read-only Department detail without inventing unused form lookup sources', () => {
    const requirements = compiledRequirements as ActionDataAccessRequirement[];
    expect(requirements.filter(item => item.viewKey === detailViewDocument.view.key && item.surface === 'lookupBindings')).toEqual([]);
    const bound = bindActionDataAccess({ ...detailViewDocument, type: 'view' }, requirements);
    expect(bound.view.dataAccess).toMatchObject({ id: expect.any(String), execution: 'organization.department.read' });
    expect(bound.view.lookupBindings).toEqual({
      'organization.companies': 'organization.department.company.lookup',
      'organization.department.parents': 'organization.department.parent.lookup',
      'organization.department.parent-filter': 'organization.department.parent.lookup',
    });
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
