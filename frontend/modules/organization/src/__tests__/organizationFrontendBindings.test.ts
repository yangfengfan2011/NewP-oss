import { describe, expect, it, vi } from 'vitest';

const implementationState = vi.hoisted(() => ({ evaluations: 0 }));

vi.mock('../standard-crud/personStandardCrudResource', () => {
  implementationState.evaluations += 1;
  return {
    personStandardCrudResource: {
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
  it('registers only lightweight Personnel descriptors and defers implementation evaluation', async () => {
    const bindings = organizationBindings as typeof organizationBindings & {
      standardCrudCapabilities?: LazyStandardCrudDescriptor[];
    };

    expect(implementationState.evaluations).toBe(0);
    expect(bindings.standardCrudResources).toBeUndefined();
    expect(bindings.standardCrudCapabilities?.map(({ kind, targetKey }) => ({ kind, targetKey }))).toEqual([
      { kind: 'standard-list', targetKey: 'organization.person.list' },
      { kind: 'standard-crud-detail', targetKey: 'organization.person.detail' },
    ]);

    const loaded = await bindings.standardCrudCapabilities?.[0]?.load();
    expect(implementationState.evaluations).toBe(1);
    expect(loaded?.default).toMatchObject({
      resourceKey: 'organization.person',
      listViewKey: 'organization.person.list',
      detailViewKey: 'organization.person.detail',
    });
  });
});
