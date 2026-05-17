import { describe, expect, it, vi } from 'vitest';
import type { FrontendModule, RouteConfig } from '@icp-platform/sdk';
import {
  buildStandardCrudDetailRouteEntries,
  buildStandardCrudDrawerRouteRedirects,
  filterFallbackRoutesByStandardCrudRoutes,
} from '../navigation/standardCrudRouteAdapter';

const modules: FrontendModule[] = [
  {
    metadata: { moduleId: 'organization', displayName: 'Organization', version: '1.0.0' },
    standardCrudResources: [
      {
        resourceKey: 'organization.person',
        apiRoute: '/api/organization/persons',
        listViewKey: 'organization.person.list',
        listRoutePath: '/organization/persons',
        detailViewKey: 'organization.person.detail',
        detailRoutePath: '/organization/persons/:id',
        createEmptyFilters: () => ({}),
        buildListQuery: () => ({}),
        mapListItem: (value: unknown) => value,
        getRowId: () => 'row-1',
        buildColumns: () => [],
      },
    ],
  },
];

const fallbackRoutes: RouteConfig[] = [
  { path: '/organization/persons', component: () => null, permissions: ['Organization.Person.View'] },
  { path: '/organization/persons/:id', component: () => null, permissions: ['Organization.Person.View'] },
];

describe('standard CRUD route adapter', () => {
  it('builds detail route entries and inherits module route permissions', () => {
    const entries = buildStandardCrudDetailRouteEntries(modules, fallbackRoutes);

    expect(entries).toEqual([
      {
        path: '/organization/persons/:id',
        viewKey: 'organization.person.detail',
        permissions: ['Organization.Person.View'],
      },
    ]);
  });

  it('removes module fallback routes owned by standard CRUD detail routes', () => {
    const entries = buildStandardCrudDetailRouteEntries(modules, fallbackRoutes);

    expect(filterFallbackRoutesByStandardCrudRoutes(fallbackRoutes, entries).map((route) => route.path)).toEqual([
      '/organization/persons',
    ]);
  });

  it('为抽屉入口兼容构建旧新建路由重定向', () => {
    expect(buildStandardCrudDrawerRouteRedirects(modules)).toEqual([
      {
        from: '/organization/persons/new',
        to: '/organization/persons?drawer=create',
      },
    ]);
  });

  it('drops duplicate detail route paths instead of relying on registration order', () => {
    const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    const duplicateModules: FrontendModule[] = [
      ...modules,
      {
        metadata: { moduleId: 'employee', displayName: 'Employee', version: '1.0.0' },
        standardCrudResources: [
          {
            resourceKey: 'employee.person',
            apiRoute: '/api/employees',
            listViewKey: 'employee.person.list',
            detailViewKey: 'employee.person.detail',
            detailRoutePath: '/organization/persons/:id',
            createEmptyFilters: () => ({}),
            buildListQuery: () => ({}),
            mapListItem: (value: unknown) => value,
            getRowId: () => 'row-1',
            buildColumns: () => [],
          },
        ],
      },
    ];

    expect(buildStandardCrudDetailRouteEntries(duplicateModules, fallbackRoutes)).toEqual([]);
    expect(errorSpy).toHaveBeenCalledWith(
      'Duplicate standard CRUD detail route path "/organization/persons/:id" was found. All conflicting entries are ignored.',
    );

    errorSpy.mockRestore();
  });
});
