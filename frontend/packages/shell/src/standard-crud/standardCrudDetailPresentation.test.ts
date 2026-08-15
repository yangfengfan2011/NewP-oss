import { describe, expect, it } from 'vitest';
import type { StandardCrudResolvedDetailSchema } from '../metadata/standardCrudSchemaResolver';
import { resolveStandardCrudDetailPresentation } from './standardCrudDetailPresentation';

const schema = {
  viewKey: 'organization.person.detail',
  resourceKey: 'organization.person',
  apiRoute: '/api/organization/persons',
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

describe('standard CRUD detail presentation', () => {
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
