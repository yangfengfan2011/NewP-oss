import { describe, expect, it } from 'vitest';
import exampleRecordModelDocument from '../../../../modules/example/metadata/models/ExampleRecord.model.json';
import positionModelDocument from '../../../../modules/organization/metadata/models/Position.model.json';
import personModelDocument from '../../../../modules/organization/metadata/models/Person.model.json';
import standardDataRuntimeModelDocument from '../../../../../tests/Kernel.Tests/Fixtures/StandardDataRuntimeModel.contract.json';
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

  it('projects Person calendar date and instant semantics into three form sections', () => {
    const schema = buildStandardCrudFormSchemaFromModel(personModelDocument.Person, {
      mode: 'edit',
      enumOptions: { EmploymentStatus: [] },
      timeZoneId: 'Asia/Taipei',
    });
    const createSchema = buildStandardCrudFormSchemaFromModel(personModelDocument.Person, {
      mode: 'create',
      enumOptions: { EmploymentStatus: [] },
      timeZoneId: 'Asia/Taipei',
    });

    expect(schema.sections.map((section) => section.key)).toEqual(['basic', 'organization', 'employment']);
    expect(schema.fields.find((field) => field.key === 'BirthDate')).toMatchObject({ kind: 'date' });
    expect(schema.fields.find((field) => field.key === 'EmploymentStartedAt')).toMatchObject({
      kind: 'dateTime',
      timeZoneId: 'Asia/Taipei',
    });
    expect(schema.fields.find((field) => field.key === 'EmploymentEndedAt')).toMatchObject({ kind: 'dateTime' });
    expect(personModelDocument.Person.fields.some((field) => field.name === 'Revision')).toBe(false);
    expect(createSchema.fields.some((field) => field.key === 'Revision')).toBe(false);
    expect(schema.fields.some((field) => field.key === 'Revision')).toBe(false);
  });

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
    expect(schema.fields.find((field) => field.key === 'PersonNo')).toMatchObject({ trim: true, maxLength: 256 });
    expect(schema.fields.find((field) => field.key === 'ENAME')).toMatchObject({ trim: true, maxLength: 256 });
    expect(schema.fields.find((field) => field.key === 'EmploymentStatus')).toMatchObject({ defaultValue: 'FullTime' });
    expect(schema.fields.find((field) => field.key === 'PositionId')?.lookup?.dependsOn).toEqual(['DepartmentId']);
  });

  it('applies edit mode readonly rules from model metadata', () => {
    const schema = buildStandardCrudFormSchemaFromModel(personModel, { mode: 'edit', enumOptions: {} });
    expect(schema.fields.find((field) => field.key === 'PersonNo')).toMatchObject({ readOnly: true });
  });

  it('consumes runtime hidden and readonly form modes without inferring field ownership', () => {
    const runtimeModel = {
      sections: [{ key: 'basic', titleKey: 'Section_Basic', fields: ['Name', 'CreatedAt', 'UpdatedAt'] }],
      fields: [
        {
          name: 'Name',
          uiType: 'text',
          form: { create: { mode: 'editable' }, edit: { mode: 'editable' } },
        },
        {
          name: 'CreatedAt',
          uiType: 'dateTime',
          form: { create: { mode: 'hidden' }, edit: { mode: 'hidden' } },
        },
        {
          name: 'UpdatedAt',
          uiType: 'dateTime',
          form: { create: { mode: 'hidden' }, edit: { mode: 'readonly' } },
        },
      ],
    };

    const create = buildStandardCrudFormSchemaFromModel(runtimeModel, { mode: 'create', enumOptions: {} });
    const edit = buildStandardCrudFormSchemaFromModel(runtimeModel, { mode: 'edit', enumOptions: {} });

    expect(create.fields.map((field) => field.modelField)).toEqual(['Name']);
    expect(create.sections[0]?.fields).toEqual(['Name']);
    expect(edit.fields.map((field) => field.modelField)).toEqual(['Name', 'UpdatedAt']);
    expect(edit.fields.find((field) => field.modelField === 'UpdatedAt')).toMatchObject({ readOnly: true });
    expect(edit.sections[0]?.fields).toEqual(['Name', 'UpdatedAt']);
  });

  it('keeps position enablement hidden on create and renders it in the edit section', () => {
    const create = buildStandardCrudFormSchemaFromModel(positionModelDocument.Position, {
      mode: 'create',
      enumOptions: {},
    });
    const edit = buildStandardCrudFormSchemaFromModel(positionModelDocument.Position, {
      mode: 'edit',
      enumOptions: {},
    });

    expect(create.fields.some((field) => field.key === 'IsEnabled')).toBe(false);
    expect(create.sections[0]?.fields).toEqual(['ENAME', 'Description']);
    expect(edit.fields.find((field) => field.key === 'IsEnabled')).toMatchObject({
      kind: 'boolean',
      required: false,
      readOnly: false,
    });
    expect(edit.sections[0]?.fields).toEqual(['ENAME', 'Description', 'IsEnabled']);
  });

  it('projects the formal Example RecordSource create default separately from create and edit requiredness', () => {
    const create = buildStandardCrudFormSchemaFromModel(exampleRecordModelDocument.ExampleRecord, {
      mode: 'create',
      enumOptions: {},
    });
    const edit = buildStandardCrudFormSchemaFromModel(exampleRecordModelDocument.ExampleRecord, {
      mode: 'edit',
      enumOptions: {},
    });

    expect(create.fields.find((field) => field.key === 'RecordSource')).toMatchObject({
      required: true,
      readOnly: false,
      defaultValue: 'Manual',
    });
    expect(edit.fields.find((field) => field.key === 'RecordSource')).toMatchObject({
      required: true,
      readOnly: false,
      defaultValue: undefined,
    });
  });

  it('consumes standard-data runtime requiredness in create and edit without a frontend default', () => {
    const standardDataAssetModel = standardDataRuntimeModelDocument.Asset;

    const create = buildStandardCrudFormSchemaFromModel(standardDataAssetModel, {
      mode: 'create',
      enumOptions: {},
    });
    const edit = buildStandardCrudFormSchemaFromModel(standardDataAssetModel, {
      mode: 'edit',
      enumOptions: {},
    });

    expect(create.fields.find((field) => field.key === 'Name')).toMatchObject({
      required: true,
      readOnly: false,
      defaultValue: undefined,
    });
    expect(edit.fields.find((field) => field.key === 'Name')).toMatchObject({
      required: true,
      readOnly: false,
      defaultValue: undefined,
    });
    expect(create.fields.find((field) => field.key === 'IsEnabled')).toMatchObject({ required: false });
    expect(edit.fields.find((field) => field.key === 'IsEnabled')).toMatchObject({ required: false });
    expect(create.fields.some((field) => field.key === 'EID' || field.key === 'CreatedAt')).toBe(false);
    expect(edit.fields.some((field) => field.key === 'EID' || field.key === 'CreatedAt')).toBe(false);
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
