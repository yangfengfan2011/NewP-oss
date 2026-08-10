import { describe, expect, it, vi } from 'vitest';
import type { RouteConfig } from '@icp-platform/sdk';
import {
  buildPageEntriesFromNavDocuments,
  filterFallbackRoutesByMetadataEntries,
  resolveRouteableMetadataEntries,
} from '../navigation/pageEntryAdapter';

function createRoute(path: string, permissions?: string[]): RouteConfig {
  return {
    path,
    component: function DummyRoute() {
      return null;
    },
    permissions,
  };
}

function snapshotRoutes(routes: RouteConfig[]) {
  return routes.map((route) => ({
    path: route.path,
    permissions: route.permissions,
  }));
}

describe('page entry routing', () => {
  it('projects valid nav metadata items into runtime page entries', () => {
    const entries = buildPageEntriesFromNavDocuments([
      {
        type: 'nav',
        navigation: {
          items: [
            {
              key: 'organization.personCenter',
              path: '/organization/persons',
              entryType: 'standard-list',
              targetKey: 'organization.person.list',
              permissions: ['Organization.Person.View'],
            },
            {
              key: 'organization.tree',
              path: '/organization/tree',
              entryType: 'custom',
              targetKey: 'organization.tree.page',
              localAdministratorOnly: true,
            },
          ],
        },
      },
    ]);

    expect(entries).toEqual([
      {
        path: '/organization/persons',
        entryType: 'standard-list',
        targetKey: 'organization.person.list',
        permissions: ['Organization.Person.View'],
      },
      {
        path: '/organization/tree',
        entryType: 'custom',
        targetKey: 'organization.tree.page',
        localAdministratorOnly: true,
      },
    ]);
  });

  it('ignores nav metadata items without a complete page entry contract', () => {
    const entries = buildPageEntriesFromNavDocuments([
      {
        type: 'nav',
        navigation: {
          items: [
            {
              key: 'organization.personCenter',
              path: '/organization/persons',
              entryType: 'standard-list',
              targetKey: 'organization.person.list',
            },
            {
              key: 'organization.tree',
              path: '/organization/tree',
              targetKey: 'organization.tree.page',
            },
            {
              key: 'organization.positions',
              path: '/organization/positions',
              entryType: 'custom',
            },
            {
              key: 'organization.missingPath',
              entryType: 'custom',
              targetKey: 'organization.missing.page',
            },
          ],
        },
      },
    ]);

    expect(entries).toEqual([
      {
        path: '/organization/persons',
        entryType: 'standard-list',
        targetKey: 'organization.person.list',
      },
    ]);
  });

  it('inherits module permissions when metadata route reuses a module path without permissions', () => {
    const rawEntries = buildPageEntriesFromNavDocuments([
      {
        type: 'nav',
        navigation: {
          items: [
            {
              key: 'organization.personCenter',
              path: '/organization/persons',
              entryType: 'standard-list',
              targetKey: 'organization.person.list',
            },
          ],
        },
      },
    ]);

    const moduleRoutes = [
      createRoute('/organization/persons', ['Organization.Person.View']),
      createRoute('/organization/persons/new'),
    ];

    const resolvedEntries = resolveRouteableMetadataEntries(rawEntries, moduleRoutes);

    expect(resolvedEntries).toEqual([
      {
        path: '/organization/persons',
        entryType: 'standard-list',
        targetKey: 'organization.person.list',
        permissions: ['Organization.Person.View'],
      },
    ]);
    expect(snapshotRoutes(filterFallbackRoutesByMetadataEntries(moduleRoutes, resolvedEntries))).toEqual([
      {
        path: '/organization/persons/new',
        permissions: undefined,
      },
    ]);
  });

  it('keeps non-navigation fallback routes when metadata routes take over a path', () => {
    const rawEntries = buildPageEntriesFromNavDocuments([
      {
        type: 'nav',
        navigation: {
          items: [
            {
              key: 'organization.personCenter',
              path: '/organization/persons',
              entryType: 'standard-list',
              targetKey: 'organization.person.list',
              permissions: ['Organization.Person.View'],
            },
          ],
        },
      },
    ]);

    const moduleRoutes = [
      createRoute('/organization/persons', ['Organization.Person.View']),
      createRoute('/organization/persons/new', ['Organization.Person.View']),
      createRoute('/organization/persons/:id', ['Organization.Person.View']),
    ];

    const resolvedEntries = resolveRouteableMetadataEntries(rawEntries, moduleRoutes);
    const fallbackRoutes = filterFallbackRoutesByMetadataEntries(moduleRoutes, resolvedEntries);

    expect(resolvedEntries).toEqual([
      {
        path: '/organization/persons',
        entryType: 'standard-list',
        targetKey: 'organization.person.list',
        permissions: ['Organization.Person.View'],
      },
    ]);
    expect(snapshotRoutes(fallbackRoutes)).toEqual([
      {
        path: '/organization/persons/new',
        permissions: ['Organization.Person.View'],
      },
      {
        path: '/organization/persons/:id',
        permissions: ['Organization.Person.View'],
      },
    ]);
  });

  it('drops conflicting metadata routes for the same path and keeps module fallback routes', () => {
    const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => undefined);

    const rawEntries = buildPageEntriesFromNavDocuments([
      {
        type: 'nav',
        navigation: {
          items: [
            {
              key: 'organization.personCenter',
              path: '/organization/persons',
              entryType: 'standard-list',
              targetKey: 'organization.person.list',
            },
            {
              key: 'organization.personArchive',
              path: '/organization/persons',
              entryType: 'custom',
              targetKey: 'organization.person.archive.page',
            },
          ],
        },
      },
    ]);

    const moduleRoutes = [
      createRoute('/organization/persons', ['Organization.Person.View']),
      createRoute('/organization/persons/new', ['Organization.Person.View']),
    ];

    const resolvedEntries = resolveRouteableMetadataEntries(rawEntries, moduleRoutes);
    const fallbackRoutes = filterFallbackRoutesByMetadataEntries(moduleRoutes, resolvedEntries);

    expect(errorSpy).toHaveBeenCalledTimes(1);
    expect(String(errorSpy.mock.calls[0]?.[0])).toContain('/organization/persons');
    expect(resolvedEntries).toEqual([]);
    expect(snapshotRoutes(fallbackRoutes)).toEqual(snapshotRoutes(moduleRoutes));

    errorSpy.mockRestore();
  });
});
