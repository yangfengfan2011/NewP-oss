import { describe, expect, it } from 'vitest';
import { buildStandardCrudFormSchemaFromModel } from '../standard-crud/standardCrudMetadataForm';

describe('standard CRUD metadata form schema', () => {
  const personModel = {
    builtinFields: ['ENAME'],
    sections: [
      { key: 'basic', titleKey: 'Section_Basic', fields: ['PersonNo', 'ENAME'] },
      { key: 'organization', titleKey: 'Section_Organization', fields: ['DepartmentId', 'PositionId', 'EmploymentStatus', 'IsEnabled'] },
    ],
    fields: [
      { name: 'ENAME', uiType: 'text', form: { create: { mode: 'editable', required: true }, edit: { mode: 'editable' } } },
      { name: 'PersonNo', storage: { type: 'string' }, uiType: 'text', form: { create: { mode: 'editable', required: true }, edit: { mode: 'readonly' } } },
      {
        name: 'DepartmentId',
        storage: { type: 'guid', nullable: true },
        uiType: 'lookup',
        form: { create: { mode: 'editable' }, edit: { mode: 'editable' } },
        dataSource: { type: 'provider', key: 'organization.departments' },
      },
      {
        name: 'PositionId',
        storage: { type: 'guid', nullable: true },
        uiType: 'lookup',
        form: { create: { mode: 'editable' }, edit: { mode: 'editable' } },
        dataSource: { type: 'provider', key: 'organization.positions', dependsOn: ['DepartmentId'] },
      },
      {
        name: 'EmploymentStatus',
        storage: { type: 'string', nullable: true },
        contract: { valueType: 'enum', clrType: 'EmploymentStatus' },
        uiType: 'select',
        form: { create: { mode: 'editable', defaultValue: 'FullTime' }, edit: { mode: 'editable' } },
        dataSource: { type: 'enum', key: 'EmploymentStatus' },
      },
      {
        name: 'IsEnabled',
        storage: { type: 'bool' },
        uiType: 'switch',
        form: { create: { mode: 'editable', defaultValue: true }, edit: { mode: 'editable' } },
      },
    ],
  };

  it('normalizes platform field keys and form sections without metadata aliases', () => {
    const schema = buildStandardCrudFormSchemaFromModel(personModel, {
      mode: 'create',
      enumOptions: {
        EmploymentStatus: [{ value: 'FullTime', label: '正式' }],
      },
    });

    expect(schema.sections).toEqual([
      { key: 'basic', titleKey: 'Section_Basic', fields: ['PersonNo', 'ENAME'] },
      { key: 'organization', titleKey: 'Section_Organization', fields: ['DepartmentId', 'PositionId', 'EmploymentStatus', 'IsEnabled'] },
    ]);
    expect(schema.fields.map((field) => [field.key, field.modelField, field.kind])).toEqual([
      ['PersonNo', 'PersonNo', 'text'],
      ['ENAME', 'ENAME', 'text'],
      ['DepartmentId', 'DepartmentId', 'lookup'],
      ['PositionId', 'PositionId', 'lookup'],
      ['EmploymentStatus', 'EmploymentStatus', 'enum'],
      ['IsEnabled', 'IsEnabled', 'boolean'],
    ]);
    expect(schema.fields.find((field) => field.key === 'PersonNo')).toMatchObject({ required: true, readOnly: false });
    expect(schema.fields.find((field) => field.key === 'EmploymentStatus')).toMatchObject({ defaultValue: 'FullTime' });
    expect(schema.fields.find((field) => field.key === 'PositionId')?.lookup?.dependsOn).toEqual(['DepartmentId']);
  });

  it('applies edit mode readonly rules from model metadata', () => {
    const schema = buildStandardCrudFormSchemaFromModel(personModel, { mode: 'edit', enumOptions: {} });
    expect(schema.fields.find((field) => field.key === 'PersonNo')).toMatchObject({ readOnly: true });
  });

  it('rejects unsupported uiType before rendering', () => {
    expect(() => buildStandardCrudFormSchemaFromModel(
      { fields: [{ name: 'DepartmentId', uiType: 'treeSelect' }] },
      { mode: 'create', enumOptions: {} },
    )).toThrow('unsupported uiType "treeSelect"');
  });

  it('rejects lookup providers that are not registered when providers are supplied', () => {
    expect(() => buildStandardCrudFormSchemaFromModel(
      {
        fields: [{
          name: 'DepartmentId',
          uiType: 'lookup',
          dataSource: { type: 'provider', key: 'organization.departments' },
        }],
      },
      { mode: 'create', enumOptions: {}, lookupProviders: [] },
    )).toThrow('dataSource provider "organization.departments" for field "DepartmentId" is not registered');
  });
});
