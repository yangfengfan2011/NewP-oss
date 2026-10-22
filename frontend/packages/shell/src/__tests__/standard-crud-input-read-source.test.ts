import { describe, expect, it } from 'vitest';
import { standardCrudInputReadSource, type ActionDataAccessRequirement } from '@icp-platform/sdk';
import { buildStandardCrudListSchema } from '../metadata/standardCrudSchemaResolver';

const source: ActionDataAccessRequirement = {
  id: 'Part/view:part.part.permissions/read', moduleId: 'Part', viewKey: 'part.part.permissions', surface: 'read', actionKey: '',
  execution: 'part.part.authorization.view', actions: ['read'], resources: ['part.part'], featurePositions: ['input-position'],
  provider: 'authorization.object.view', requirements: [{ role: 'view', stage: 'prepare', shape: 'object', resources: ['part.part'], anyOf: [['read']], minimumCount: 1, maximumCount: 1 }],
};
const action = { key: 'authorize', labelKey: 'Authorize', inputReadSourceKey: 'part.part.permissions' };

describe('explicit standard CRUD input read source', () => {
  it('keeps a behavior-only feature action outside the presented action schema', () => {
    const schema = buildStandardCrudListSchema({ key: 'items', featureActions: [{ key: 'authorize', titleKey: 'Authorize',
      behaviorKey: 'part.part.authorize', dataAccess: { object: 'Part', actions: ['read', 'authorize'] } }] }, {
      resourceKey: 'part.part', listViewKey: 'items', dataAdapter: { list: async () => ({ items: [], total: 0, page: 1, pageSize: 20 }),
        get: async () => ({ value: {}, normalized: { fields: {}, summaries: {} } }), getRowId: () => '' },
    });
    expect(schema.featureActions).toEqual([]);
  });

  it('keeps the original PageRef read role and prepare stage at its exact feature position', () => {
    expect(standardCrudInputReadSource({ action, featurePosition: 'input-position', dataAccessDeclarations: [source] })).toBe(source);
    expect(standardCrudInputReadSource({ action: { key: 'generic', labelKey: 'Generic' } })).toBeUndefined();
  });

  it.each([
    { action, dataAccessDeclarations: [source] },
    { action, featurePosition: 'wrong-position', dataAccessDeclarations: [source] },
    { action, featurePosition: 'input-position', dataAccessDeclarations: [] },
    { action, featurePosition: 'input-position', dataAccessDeclarations: [source, source] },
    { action, featurePosition: 'input-position', dataAccessDeclarations: [{ ...source, surface: 'rowActions' }] },
    { action, featurePosition: 'input-position', dataAccessDeclarations: [{ ...source, actions: ['read', 'authorize'] }] },
    { action, featurePosition: 'input-position', dataAccessDeclarations: [{ ...source, requirements: source.requirements!.map(item => ({ ...item, stage: 'submit' as const })) }] },
  ])('rejects missing, borrowed, mixed or ambiguous sources without a button fallback (%#)', context => {
    expect(() => standardCrudInputReadSource(context)).toThrow('INPUT_READ_SOURCE_INVALID');
  });

  it('preserves the explicit reference through the real action schema', () => {
    const schema = buildStandardCrudListSchema({ key: 'items', pageActions: [{ ...action, handlerKey: 'authorize' }] }, {
      resourceKey: 'part.part', listViewKey: 'items', dataAdapter: { list: async () => ({ items: [], total: 0, page: 1, pageSize: 20 }),
        get: async () => ({ value: {}, normalized: { fields: {}, summaries: {} } }), getRowId: () => '' }, actionHandlers: { authorize: async () => undefined },
    });
    expect(schema.pageActions[0].inputReadSourceKey).toBe(source.viewKey);
  });

  it.each(['', null, [], 42])('rejects malformed input references in the action schema (%j)', inputReadSourceKey => {
    expect(() => buildStandardCrudListSchema({ key: 'items', pageActions: [{ ...action, inputReadSourceKey }] }, {
      resourceKey: 'part.part', listViewKey: 'items', dataAdapter: { list: async () => ({ items: [], total: 0, page: 1, pageSize: 20 }),
        get: async () => ({ value: {}, normalized: { fields: {}, summaries: {} } }), getRowId: () => '' },
    })).toThrow('INPUT_READ_SOURCE_INVALID');
  });
});
