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
  expect(schema.columns.find(column => column.standardAction === 'view')?.field).toBe('OccurredAt');
});

it('resolves the real Audit detail renderer and summary bindings without mutation actions', () => {
  const schema = resolveStandardCrudDetailSchema([detail], businessChangeResource, { modelDocuments: [model] });
  expect(schema.sections[0].fields.filter(field => field.rendererSource === 'binding').map(field => field.key)).toEqual(['TargetId', 'ActorId']);
  expect(schema.summaryBlocks).toEqual(['changes']);
  expect(schema.pageActions).toEqual([]);
});
