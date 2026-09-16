import { describe, expect, it } from 'vitest';
import { createFeaturePlacementResolver } from './featurePlacementResolver';

const points = [
  { kind: 'navigation', key: 'people', position: 'entry.people' },
  { kind: 'navigation', key: 'roles', position: 'entry.roles' },
  { kind: 'action', key: 'create', actionScope: 'list.pageActions', position: 'entry.people.action.create', parentPosition: 'entry.people' },
  { kind: 'action', key: 'edit', actionScope: 'list.rowActions', position: 'entry.people.action.edit.company', parentPosition: 'entry.people', staticScope: 'company' },
  { kind: 'action', key: 'edit', actionScope: 'list.rowActions', position: 'entry.people.action.edit.department', parentPosition: 'entry.people', staticScope: 'department' },
  { kind: 'action', key: 'edit', actionScope: 'list.rowActions', position: 'entry.roles.action.edit', parentPosition: 'entry.roles' },
  { kind: 'tab', key: 'members', position: 'entry.people.tab.members', parentPosition: 'entry.people', staticScope: 'company' },
] as const;

describe('feature placement resolver', () => {
  it('returns explicit tri-state decisions and rejects wrong entry, scope, or denied grants', () => {
    const resolver = createFeaturePlacementResolver({
      placement: { entryPosition: 'entry.people', staticScope: 'company' },
      points,
      navigationPositions: new Map([['people', 'entry.people'], ['roles', 'entry.roles']]),
      hasFeature: position => position !== 'entry.people.action.edit.company',
    });

    expect(resolver.read()).toEqual({ kind: 'allowed', position: 'entry.people' });
    expect(resolver.action('edit', 'list.rowActions')).toEqual({ kind: 'denied', position: 'entry.people.action.edit.company' });
    expect(resolver.action('edit', 'list.rowActions', 'department')).toEqual({ kind: 'allowed', position: 'entry.people.action.edit.department' });
    expect(resolver.action('edit', 'list.rowActions', 'missing')).toEqual({ kind: 'unresolved' });
    expect(resolver.tab('members')).toEqual({ kind: 'allowed', position: 'entry.people.tab.members' });
  });

  it('normalizes null scope to the bound scope and lets an explicit node scope override it', () => {
    const resolver = createFeaturePlacementResolver({
      placement: { entryPosition: 'entry.people', staticScope: 'company' },
      points,
      navigationPositions: new Map(),
      hasFeature: () => true,
    });

    expect(resolver.action('edit', 'list.rowActions', null)).toEqual({ kind: 'allowed', position: 'entry.people.action.edit.company' });
    expect(resolver.action('edit', 'list.rowActions', 'department')).toEqual({ kind: 'allowed', position: 'entry.people.action.edit.department' });
  });

  it('uses an origin action override only for the same execution key', () => {
    const resolver = createFeaturePlacementResolver({
      placement: {
        entryPosition: 'entry.people',
        staticScope: 'company',
        actionPosition: 'entry.people.action.create',
        actionKey: 'create',
      },
      points,
      navigationPositions: new Map(),
      hasFeature: position => position === 'entry.people.action.create',
    });

    expect(resolver.read()).toEqual({ kind: 'allowed', position: 'entry.people.action.create' });
    expect(resolver.action('create', 'list.pageActions')).toEqual({ kind: 'allowed', position: 'entry.people.action.create' });
    expect(resolver.action('edit', 'list.rowActions')).toEqual({ kind: 'unresolved' });
  });

  it('resolves navigation from the target identity instead of the current entry', () => {
    const resolver = createFeaturePlacementResolver({
      placement: { entryPosition: 'entry.people' },
      points,
      navigationPositions: new Map([['roles', 'entry.roles']]),
      hasFeature: position => position === 'entry.roles',
    });

    expect(resolver.navigation('roles')).toEqual({ kind: 'allowed', position: 'entry.roles' });
    expect(resolver.navigation('missing')).toEqual({ kind: 'unresolved' });
  });
});
