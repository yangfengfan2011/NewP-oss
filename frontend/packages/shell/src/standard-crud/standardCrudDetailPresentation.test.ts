import { describe, expect, it } from 'vitest';
import type { StandardCrudResolvedDetailSchema } from '../metadata/standardCrudSchemaResolver';
import { resolveStandardCrudDetailPresentation } from './standardCrudDetailPresentation';

const schema = {
  viewKey: 'organization.person.detail',
  resourceKey: 'organization.person',
  dicKey: 'PersonDetailView',
  titleKey: 'Title',
  descriptionKey: 'Description',
  fieldCatalog: [],
  sections: [
    {
      key: 'basic',
      titleKey: 'Section_Basic',
      fields: [
        { key: 'PersonNo', labelKey: 'Field_PersonNo', rendererKey: 'standard.text', rendererSource: 'standard' },
        { key: 'ENAME', labelKey: 'Field_ENAME', rendererKey: 'standard.text', rendererSource: 'standard' },
      ],
    },
    {
      key: 'audit',
      titleKey: 'Section_Audit',
      fields: [
        { key: 'CreatedBy', labelKey: 'Field_CreatedBy', rendererKey: 'standard.text', rendererSource: 'standard' },
        { key: 'CreatedAt', labelKey: 'Field_CreatedAt', rendererKey: 'standard.date', rendererSource: 'standard' },
      ],
    },
  ],
  summaryBlocks: ['PersonNo', 'ENAME'],
  pageActions: [
    {
      key: 'edit',
      labelKey: 'Action_Edit',
      standardAction: 'edit',
      dataDependency: { fields: ['Revision'], reasonKey: 'Action_DataUnavailable' },
    },
  ],
} as StandardCrudResolvedDetailSchema;

const companySchema = {
  ...schema,
  sections: [{
    key: 'organization',
    titleKey: 'Section_Organization',
    fields: [{
      key: 'CompanyId',
      labelKey: 'Field_CompanyId',
      rendererKey: 'standard.text',
      rendererSource: 'standard',
    }],
  }],
} as StandardCrudResolvedDetailSchema;

describe('standard CRUD detail presentation', () => {
  it.each(['CreatedBy', 'CreatedAt', 'UpdatedBy', 'UpdatedAt'])('preserves raw audit %s while presenting its companion', (field) => {
    const result = resolveStandardCrudDetailPresentation({
      schema: { ...companySchema, sections: [{ key: 'audit', titleKey: 'Audit', fields: [{ key: field, labelKey: field, rendererKey: 'standard.text', rendererSource: 'standard' }] }] },
      expectedId: 'person-1',
      fields: { EID: 'person-1', [field]: 'raw-audit', [`${field}_DisplayValue`]: '服务端显示值' },
    });
    expect(result.kind).toBe('ready');
    if (result.kind !== 'ready') return;
    expect(result.sections[0].fields[0].value).toBe('服务端显示值');
    expect(result.sourceFields[field]).toBe('raw-audit');
  });
  it('keeps present null distinct from a missing field', () => {
    const result = resolveStandardCrudDetailPresentation({
      schema,
      expectedId: 'person-1',
      fields: {
        EID: 'person-1',
        PersonNo: 'P001',
        ENAME: null,
        CreatedBy: 'admin',
        CreatedAt: '2026-08-15T00:00:00Z',
        Revision: 1,
      },
    });

    expect(result.kind).toBe('ready');
    if (result.kind !== 'ready') return;
    expect(result.sections[0].fields[1]).toMatchObject({ key: 'ENAME', state: 'present', value: null });
    expect(result.diagnostics).toEqual([]);
  });

  it('presents a companion display value while keeping the raw field in source fields', () => {
    const result = resolveStandardCrudDetailPresentation({
      schema: companySchema,
      expectedId: 'person-1',
      fields: {
        EID: 'person-1',
        CompanyId: 'company-raw-id',
        CompanyId_DisplayValue: '示例公司',
        Revision: 1,
      },
    });

    expect(result.kind).toBe('ready');
    if (result.kind !== 'ready') return;
    expect(result.sections[0].fields[0]).toMatchObject({
      key: 'CompanyId',
      state: 'present',
      value: '示例公司',
    });
    expect(result.sourceFields.CompanyId).toBe('company-raw-id');
  });

  it.each([
    ['missing', {}],
    ['null', { CompanyId_DisplayValue: null }],
    ['empty', { CompanyId_DisplayValue: '' }],
    ['blank', { CompanyId_DisplayValue: '   ' }],
  ])('falls back to the raw field when its companion display value is %s', (_, companionField) => {
    const result = resolveStandardCrudDetailPresentation({
      schema: companySchema,
      expectedId: 'person-1',
      fields: {
        EID: 'person-1',
        CompanyId: 'company-raw-id',
        Revision: 1,
        ...companionField,
      },
    });

    expect(result.kind).toBe('ready');
    if (result.kind !== 'ready') return;
    expect(result.sections[0].fields[0]).toMatchObject({
      key: 'CompanyId',
      state: 'present',
      value: 'company-raw-id',
    });
    expect(result.sourceFields.CompanyId).toBe('company-raw-id');
  });

  it('degrades only the missing field while rendering other fields in the section', () => {
    const result = resolveStandardCrudDetailPresentation({
      schema,
      expectedId: 'person-1',
      fields: {
        EID: 'person-1',
        PersonNo: 'P001',
        CreatedBy: 'admin',
        CreatedAt: '2026-08-15T00:00:00Z',
        Revision: 1,
      },
    });

    expect(result.kind).toBe('ready');
    if (result.kind !== 'ready') return;
    expect(result.sections[0]).toMatchObject({ state: 'available' });
    expect(result.sections[0].fields[1]).toMatchObject({ key: 'ENAME', state: 'unavailable' });
    expect(result.diagnostics).toContainEqual(expect.objectContaining({
      code: 'STANDARD_CRUD_DETAIL_FIELD_UNAVAILABLE',
      fieldKey: 'ENAME',
      sectionKey: 'basic',
    }));
  });

  it('treats a non-JSON undefined property as unavailable', () => {
    const result = resolveStandardCrudDetailPresentation({
      schema,
      expectedId: 'person-1',
      fields: {
        EID: 'person-1',
        PersonNo: 'P001',
        ENAME: undefined,
        CreatedBy: 'admin',
        CreatedAt: '2026-08-15T00:00:00Z',
        Revision: undefined,
      },
    });

    expect(result.kind).toBe('ready');
    if (result.kind !== 'ready') return;
    expect(result.sections[0].fields[1]).toMatchObject({ key: 'ENAME', state: 'unavailable' });
    expect(result.pageActions[0]).toMatchObject({ key: 'edit', disabled: true });
  });

  it('degrades the whole section only when every declared field is missing', () => {
    const result = resolveStandardCrudDetailPresentation({
      schema,
      expectedId: 'person-1',
      fields: { EID: 'person-1', PersonNo: 'P001', ENAME: 'Alice', Revision: 1 },
    });

    expect(result.kind).toBe('ready');
    if (result.kind !== 'ready') return;
    expect(result.sections[1]).toMatchObject({ state: 'unavailable', key: 'audit' });
    expect(result.diagnostics).toContainEqual(expect.objectContaining({
      code: 'STANDARD_CRUD_DETAIL_SECTION_UNAVAILABLE',
      sectionKey: 'audit',
    }));
  });

  it.each([
    [{ PersonNo: 'P001' }, 'STANDARD_CRUD_DETAIL_CRITICAL_ID_MISSING'],
    [{ EID: '', PersonNo: 'P001' }, 'STANDARD_CRUD_DETAIL_CRITICAL_ID_INVALID'],
    [{ EID: 'person-2', PersonNo: 'P001' }, 'STANDARD_CRUD_DETAIL_CRITICAL_ID_MISMATCH'],
  ])('fails the whole detail for an invalid critical EID', (fields, expectedCode) => {
    const result = resolveStandardCrudDetailPresentation({ schema, expectedId: 'person-1', fields });

    expect(result).toMatchObject({ kind: 'fatal', diagnostic: { code: expectedCode } });
  });

  it('disables an action when its declared data dependency is absent', () => {
    const result = resolveStandardCrudDetailPresentation({
      schema,
      expectedId: 'person-1',
      fields: {
        EID: 'person-1',
        PersonNo: 'P001',
        ENAME: 'Alice',
        CreatedBy: 'admin',
        CreatedAt: '2026-08-15T00:00:00Z',
      },
    });

    expect(result.kind).toBe('ready');
    if (result.kind !== 'ready') return;
    expect(result.pageActions[0]).toMatchObject({
      key: 'edit',
      disabled: true,
      disabledReasonKey: 'Action_DataUnavailable',
    });
    expect(result.diagnostics).toContainEqual(expect.objectContaining({
      code: 'STANDARD_CRUD_DETAIL_ACTION_DEPENDENCY_UNAVAILABLE',
      actionKey: 'edit',
      fieldKey: 'Revision',
    }));
  });
});
