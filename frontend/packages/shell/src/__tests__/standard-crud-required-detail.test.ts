import { describe, expect, it, vi } from 'vitest';
import type { FrontendModule } from '@icp-platform/sdk';
import organizationRuntimeModels from '../../../../../tests/Kernel.Tests/Fixtures/OrganizationRuntimeModels.contract.json';
import { companyStandardCrudResource } from '../../../../modules/platform/organization/src/standard-crud/companyStandardCrudResource';
import { resolveStandardCrudDetailSchema } from '../metadata/standardCrudSchemaResolver';
import { compileStandardCrudRouteProjection } from '../navigation/standardCrudRouteAdapter';
import type { RuntimeViewDocument } from '../metadata/runtimeMetadataManifest';

const auditFields = ['CreatedBy', 'CreatedAt', 'UpdatedBy', 'UpdatedAt'];
const basic = { key: 'basic', titleKey: 'Section_Basic', fields: ['ENAME'] };
const audit = { key: 'audit', titleKey: 'Section_Audit', fields: auditFields };
const detailView = (sections = [basic, audit]): RuntimeViewDocument => ({
  type: 'view',
  view: { key: 'organization.company.detail', mode: 'detail', model: 'Company', sections },
});

describe('required object detail', () => {
  const load = vi.fn();
  const module: FrontendModule = {
    metadata: { moduleId: 'Organization', displayName: 'Organization', version: '1.0.0' },
    standardCrudCapabilities: [{ kind: 'standard-list', targetKey: 'organization.company.list', load }],
  };
  const nav = [{ type: 'nav' as const, navigation: { items: [{
    key: 'companies', path: '/organization/companies', entryType: 'standard-list' as const,
    targetKey: 'organization.company.list', permissions: ['Organization.Company.View'],
  }] } }];
  const listView: RuntimeViewDocument = {
    type: 'view', view: { key: 'organization.company.list', mode: 'list', model: 'Company', detailViewKey: 'organization.company.detail', rowActions: [{ key: 'view', standardAction: 'view' }] },
  };

  it('supports a real Company drawer without registering or loading a full page', () => {
    const projection = compileStandardCrudRouteProjection([module], nav, [listView, detailView()], []);
    expect(projection.detailEntries).toEqual([]);
    expect(projection.drawerRedirects).toEqual([{ from: '/organization/companies/new', to: '/organization/companies?drawer=create' }]);
    expect(load).not.toHaveBeenCalled();
  });

  it('rejects an actual view action without its Detail View', () => {
    expect(() => compileStandardCrudRouteProjection([module], nav, [{
      ...listView, view: { ...listView.view, detailViewKey: undefined },
    }], [])).toThrow('STANDARD_CRUD_DETAIL_VIEW_KEY_MISSING');
  });

  it.each([false, true])('keeps a model list without a detail consumer or invented detail/create routes (legacy: %s)', legacy => {
    const withoutDetail: RuntimeViewDocument = { ...listView, view: {
      ...listView.view, detailViewKey: undefined, rowActions: [], pageActions: [],
    } };
    const registration = { ...companyStandardCrudResource, detailViewKey: undefined, detailRoutePath: undefined };
    const owner: FrontendModule = { ...module, standardCrudCapabilities: legacy ? [] : module.standardCrudCapabilities,
      standardCrudResources: legacy ? [registration] : [] };
    expect(compileStandardCrudRouteProjection([owner], nav, [withoutDetail], [])).toEqual({
      detailEntries: [], drawerRedirects: [], fallbackRoutes: [],
    });
    expect(load).not.toHaveBeenCalled();
  });

  it('rejects an object list that hides every detail entry', () => {
    expect(() => compileStandardCrudRouteProjection([module], nav, [{
      ...listView, view: { ...listView.view, rowActions: [] },
    }, detailView()], [])).toThrow('STANDARD_CRUD_DETAIL_ACTION_MISSING');
  });

  it.each([
    ['missing', [basic]],
    ['not last', [audit, basic]],
    ['wrong order', [basic, { ...audit, fields: [...auditFields].reverse() }]],
    ['incomplete', [basic, { ...audit, fields: auditFields.slice(0, 2) }]],
    ['mixed with business fields', [{ ...audit, fields: ['ENAME', ...auditFields] }]],
  ])('rejects an audit section that is %s', (_, sections) => {
    expect(() => resolveStandardCrudDetailSchema(
      [detailView(sections as typeof basic[])],
      { ...companyStandardCrudResource, detailViewKey: 'organization.company.detail' },
      { modelDocuments: [organizationRuntimeModels.company] },
    )).toThrow('must end with an audit section');
  });

  it('preserves the canonical audit field order from the explicit Detail View', () => {
    const schema = resolveStandardCrudDetailSchema(
      [detailView()],
      { ...companyStandardCrudResource, detailViewKey: 'organization.company.detail' },
      { modelDocuments: [organizationRuntimeModels.company] },
    );
    expect(schema.sections.at(-1)?.fields.map((field) => field.key)).toEqual(auditFields);
  });
});
