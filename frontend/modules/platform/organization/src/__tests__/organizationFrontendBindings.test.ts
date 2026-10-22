import { describe, expect, it, vi } from 'vitest';

const implementationState = vi.hoisted(() => ({ primaryEvaluations: 0, recoveryEvaluations: 0 }));

vi.mock('../standard-crud/personStandardCrudResource.primary', () => {
  implementationState.primaryEvaluations += 1;
  return {
    primary: {
      resourceKey: 'organization.person',
      listViewKey: 'organization.person.list',
      detailViewKey: 'organization.person.detail',
    },
  };
});

vi.mock('../standard-crud/personStandardCrudResource.recovery', () => {
  implementationState.recoveryEvaluations += 1;
  return {
    recovery: {
      resourceKey: 'organization.person',
      listViewKey: 'organization.person.list',
      detailViewKey: 'organization.person.detail',
    },
  };
});

import organizationBindings from '../index';

interface LazyStandardCrudDescriptor {
  kind: 'standard-list' | 'standard-crud-detail';
  targetKey: string;
  load: () => Promise<{ default: unknown }>;
}

describe('organization frontend bindings', () => {
  it('registers only lightweight Organization descriptors and defers implementation evaluation', async () => {
    const bindings = organizationBindings as typeof organizationBindings & {
      standardCrudCapabilities?: LazyStandardCrudDescriptor[];
    };

    expect(implementationState.primaryEvaluations).toBe(0);
    expect(implementationState.recoveryEvaluations).toBe(0);
    expect(bindings.standardCrudResources).toBeUndefined();
    expect(bindings.standardCrudCapabilities?.map(({ kind, targetKey }) => ({ kind, targetKey }))).toEqual([
      { kind: 'standard-list', targetKey: 'organization.person.list' },
      { kind: 'standard-crud-detail', targetKey: 'organization.person.detail' },
      { kind: 'standard-list', targetKey: 'organization.position.list' },
      { kind: 'standard-list', targetKey: 'organization.company.list' },
      { kind: 'standard-list', targetKey: 'organization.department.list' },
    ]);

    const loaded = await bindings.standardCrudCapabilities?.[0]?.load();
    expect(implementationState.primaryEvaluations).toBe(1);
    expect(implementationState.recoveryEvaluations).toBe(0);
    expect(loaded?.default).toMatchObject({
      resourceKey: 'organization.person',
      listViewKey: 'organization.person.list',
      detailViewKey: 'organization.person.detail',
    });
  });
});
