import navigation from '../../../../../modules/Audit/metadata/navigation/Audit.nav.json';
import auditBindings from '../../../../modules/audit/src';
import { compileStandardCrudRouteProjection } from '../navigation/standardCrudRouteAdapter';
import { expect, it } from 'vitest';
import model from '../../../../../tests/Kernel.Tests/Fixtures/AuditBusinessChangeRuntime.contract.json';
import list from '../../../../../modules/Audit/metadata/views/BusinessChangeList.view.json';
import detail from '../../../../../modules/Audit/metadata/views/BusinessChangeDetail.view.json';
import { businessChangeResource } from '../../../../modules/audit/src/businessChangeResource';
import { buildStandardCrudListSchema, resolveStandardCrudDetailSchema } from '../metadata/standardCrudSchemaResolver';

it('compiles the real Audit filters and read-only list through the shared consumer', () => {
  const schema = buildStandardCrudListSchema(list.view, businessChangeResource, { modelDocuments: [model] });
  expect(schema.queryFields.map(field => field.key)).toEqual(['Resource', 'TargetId', 'ActorId', 'EventType', 'From', 'To']);
  expect(schema.pageActions).toEqual([]);
  expect(schema.rowActions).toEqual([]);
  expect(schema.exportModes).toEqual([]);
  expect(schema.columns.find(column => column.standardAction === 'view')?.field).toBe('EventType');
  expect(schema.editorMode).toBe('page');
});

it('resolves the real Audit detail renderer and summary bindings without mutation actions', () => {
  const schema = resolveStandardCrudDetailSchema([detail], businessChangeResource, { modelDocuments: [model] });
  expect(schema.sections[0].fields.filter(field => field.rendererSource === 'binding').map(field => field.key)).toEqual(['TargetId', 'ActorId']);
  expect(schema.summaryBlocks).toEqual(['changes']);
  expect(schema.pageActions).toEqual([]);
});

it('projects the actual module detail route from its declared navigation and lazy bindings', () => {
  const routes = compileStandardCrudRouteProjection([{ ...auditBindings,
    metadata: { moduleId: 'Audit', displayName: 'Audit', version: '1.0.0' },
  }], [navigation], [list, detail], []);
  expect(routes.detailEntries).toContainEqual({ path: '/audit/business-changes/:id', viewKey: 'audit.change.detail', permissions: ['Audit.BusinessChange.View'] });
});
