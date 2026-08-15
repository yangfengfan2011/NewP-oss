import { describe, expect, it, vi } from 'vitest';
import type { FrontendModule, RouteConfig } from '@icp-platform/sdk';
import type {
  RuntimeNavItemDocument,
  RuntimeViewDocument,
} from '../metadata/runtimeMetadataManifest';
import {
  compileStandardCrudRouteProjection,
} from '../navigation/standardCrudRouteAdapter';

const modules: FrontendModule[] = [
  {
    metadata: { moduleId: 'organization', displayName: 'Organization', version: '1.0.0' },
    standardCrudResources: [
      {
        resourceKey: 'organization.person',
        apiRoute: '/api/organization/persons',
        listViewKey: 'organization.person.list',
        detailViewKey: 'organization.person.detail',
        createEmptyFilters: () => ({}),
        buildListQuery: () => ({}),
        mapListItem: (value: unknown) => value,
        getRowId: () => 'row-1',
        buildColumns: () => [],
      },
    ],
  },
];

const navDocuments: RuntimeNavItemDocument[] = [{
  type: 'nav',
  navigation: {
    items: [{
      key: 'organization.person.center',
      path: '/organization/persons',
      entryType: 'standard-list',
      targetKey: 'organization.person.list',
      permissions: ['Organization.Person.View'],
    }],
  },
}];

const viewDocuments: RuntimeViewDocument[] = [{
  type: 'view',
  view: {
    key: 'organization.person.list',
    mode: 'list',
    detailViewKey: 'organization.person.detail',
  },
}, {
  type: 'view',
  view: {
    key: 'organization.person.detail',
    mode: 'detail',
    model: 'Person',
    sections: [{ key: 'basic', titleKey: 'Section_Basic', fields: ['ENAME'] }],
  },
}];

const fallbackRoutes: RouteConfig[] = [
  { path: '/organization/persons', component: () => null, permissions: ['Organization.Person.View'] },
  { path: '/organization/persons/new', component: () => null, permissions: ['Organization.Person.View'] },
  { path: '/organization/persons/:id', component: () => null, permissions: ['Organization.Person.View'] },
];

describe('standard CRUD route adapter', () => {
  it('builds detail route entries and inherits module route permissions', () => {
    const projection = compileStandardCrudRouteProjection(modules, navDocuments, viewDocuments, []);

    expect(projection.detailEntries).toEqual([
      {
        path: '/organization/persons/:id',
        viewKey: 'organization.person.detail',
        permissions: ['Organization.Person.View'],
      },
    ]);
  });

  it('removes module fallback routes owned by standard CRUD detail routes and drawer redirects', () => {
    const projection = compileStandardCrudRouteProjection(
      modules,
      navDocuments,
      viewDocuments,
      fallbackRoutes,
    );

    expect(projection.fallbackRoutes.map((route) => route.path)).toEqual([
      '/organization/persons',
    ]);
  });

  it('为抽屉入口兼容构建旧新建路由重定向', () => {
    expect(compileStandardCrudRouteProjection(modules, navDocuments, viewDocuments, []).drawerRedirects).toEqual([
      {
        from: '/organization/persons/new',
        to: '/organization/persons?drawer=create',
      },
    ]);
  });

  it('projects lazy list/detail descriptors from metadata without executing their loaders', () => {
    const load = vi.fn();
    const lazyModules: FrontendModule[] = [{
      metadata: { moduleId: 'organization', displayName: 'Organization', version: '1.0.0' },
      standardCrudCapabilities: [
        { kind: 'standard-list', targetKey: 'organization.person.list', load },
        { kind: 'standard-crud-detail', targetKey: 'organization.person.detail', load },
      ],
    }];

    const projection = compileStandardCrudRouteProjection(lazyModules, navDocuments, viewDocuments, []);
    expect(projection.detailEntries).toEqual([{
      path: '/organization/persons/:id',
      viewKey: 'organization.person.detail',
      permissions: ['Organization.Person.View'],
    }]);
    expect(projection.drawerRedirects).toEqual([{
      from: '/organization/persons/new',
      to: '/organization/persons?drawer=create',
    }]);
    expect(load).not.toHaveBeenCalled();
  });

  it('fails closed on duplicate detail route paths instead of relying on registration order', () => {
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
            createEmptyFilters: () => ({}),
            buildListQuery: () => ({}),
            mapListItem: (value: unknown) => value,
            getRowId: () => 'row-1',
            buildColumns: () => [],
          },
        ],
      },
    ];

    const duplicateNavDocuments: RuntimeNavItemDocument[] = [{
      type: 'nav',
      navigation: {
        items: [
          ...navDocuments[0].navigation.items,
          {
            key: 'employee.person.center',
            path: '/organization/persons',
            entryType: 'standard-list',
            targetKey: 'employee.person.list',
          },
        ],
      },
    }];
    const duplicateViewDocuments: RuntimeViewDocument[] = [
      ...viewDocuments,
      {
        type: 'view',
        view: {
          key: 'employee.person.list',
          mode: 'list',
          detailViewKey: 'employee.person.detail',
        },
      },
      {
        type: 'view',
        view: {
          key: 'employee.person.detail',
          mode: 'detail',
          model: 'Person',
          sections: [{ key: 'basic', titleKey: 'Section_Basic', fields: ['ENAME'] }],
        },
      },
    ];

    expect(() => compileStandardCrudRouteProjection(
      duplicateModules,
      duplicateNavDocuments,
      duplicateViewDocuments,
      [],
    )).toThrowError(/MODULE_PROJECTION_FAILED: STANDARD_CRUD_DETAIL_PATH_CONFLICT/);
  });
});

describe('standard CRUD route projection compiler', () => {
  const load = vi.fn();
  const listTarget = 'sample.records.list';
  const detailTarget = 'sample.records.detail';
  const path = '/sample/records';
  const permissions = ['Sample.Record.View'];

  const lazyModule: FrontendModule = {
    metadata: { moduleId: 'sample', displayName: 'Sample', version: '1.0.0' },
    standardCrudCapabilities: [
      { kind: 'standard-list', targetKey: listTarget, load },
      { kind: 'standard-crud-detail', targetKey: detailTarget, load },
    ],
  };
  const lazyNav: RuntimeNavItemDocument[] = [{
    type: 'nav',
    navigation: {
      items: [{
        key: 'sample.records',
        path,
        entryType: 'standard-list',
        targetKey: listTarget,
        permissions,
      }],
    },
  }];
  const lazyViews: RuntimeViewDocument[] = [{
    type: 'view',
    view: { key: listTarget, mode: 'list', detailViewKey: detailTarget },
  }, {
    type: 'view',
    view: {
      key: detailTarget,
      mode: 'detail',
      model: 'Record',
      sections: [{ key: 'basic', titleKey: 'Section_Basic', fields: ['ENAME'] }],
    },
  }];

  function expectProjectionFailure(
    action: () => unknown,
    diagnostic: string,
  ): void {
    expect(action).toThrowError(new RegExp(`MODULE_PROJECTION_FAILED: ${diagnostic}`));
    expect(load).not.toHaveBeenCalled();
  }

  it('fails when a lazy list descriptor has no unique standard-list nav owner', () => {
    expectProjectionFailure(
      () => compileStandardCrudRouteProjection([lazyModule], [], lazyViews, []),
      'STANDARD_CRUD_NAV_NOT_UNIQUE',
    );
  });

  it.each(['missing', 'duplicate'] as const)(
    'fails when the lazy list view is %s',
    (variant) => {
      const views = variant === 'missing'
        ? lazyViews.slice(1)
        : [lazyViews[0], lazyViews[0], lazyViews[1]];

      expectProjectionFailure(
        () => compileStandardCrudRouteProjection([lazyModule], lazyNav, views, []),
        'STANDARD_CRUD_LIST_VIEW_NOT_UNIQUE',
      );
    },
  );

  it.each(['missing-key', 'unknown-view', 'missing-descriptor', 'duplicate-descriptor'] as const)(
    'fails when the list detail projection is %s',
    (variant) => {
      const module = variant === 'missing-descriptor'
        ? { ...lazyModule, standardCrudCapabilities: [lazyModule.standardCrudCapabilities![0]] }
        : variant === 'duplicate-descriptor'
          ? {
              ...lazyModule,
              standardCrudCapabilities: [
                ...lazyModule.standardCrudCapabilities!,
                { kind: 'standard-crud-detail' as const, targetKey: detailTarget, load },
              ],
            }
        : lazyModule;
      const views = variant === 'missing-key'
        ? [{ ...lazyViews[0], view: { key: listTarget, mode: 'list' } }, lazyViews[1]]
        : variant === 'unknown-view'
          ? [lazyViews[0]]
          : lazyViews;
      const diagnostic = variant === 'missing-key'
        ? 'STANDARD_CRUD_DETAIL_VIEW_KEY_MISSING'
        : variant === 'unknown-view'
          ? 'STANDARD_CRUD_DETAIL_VIEW_NOT_UNIQUE'
          : 'STANDARD_CRUD_DETAIL_CAPABILITY_NOT_UNIQUE';

      expectProjectionFailure(
        () => compileStandardCrudRouteProjection([module], lazyNav, views, []),
        diagnostic,
      );
    },
  );

  it('fails when a detail descriptor is not owned by a list descriptor', () => {
    const orphanModule: FrontendModule = {
      ...lazyModule,
      standardCrudCapabilities: [
        ...lazyModule.standardCrudCapabilities!,
        { kind: 'standard-crud-detail', targetKey: 'sample.orphan.detail', load },
      ],
    };

    expectProjectionFailure(
      () => compileStandardCrudRouteProjection([orphanModule], lazyNav, lazyViews, []),
      'STANDARD_CRUD_DETAIL_CAPABILITY_ORPHAN',
    );
  });

  it('fails when one detail descriptor is claimed by multiple list descriptors', () => {
    const secondListTarget = 'sample.secondary.list';
    const sharedDetailModule: FrontendModule = {
      ...lazyModule,
      standardCrudCapabilities: [
        ...lazyModule.standardCrudCapabilities!,
        { kind: 'standard-list', targetKey: secondListTarget, load },
      ],
    };
    const nav: RuntimeNavItemDocument[] = [{
      type: 'nav',
      navigation: {
        items: [
          ...lazyNav[0].navigation.items,
          { key: 'sample.secondary', path: '/sample/secondary', entryType: 'standard-list', targetKey: secondListTarget },
        ],
      },
    }];
    const views: RuntimeViewDocument[] = [
      ...lazyViews,
      { type: 'view', view: { key: secondListTarget, mode: 'list', detailViewKey: detailTarget } },
    ];

    expectProjectionFailure(
      () => compileStandardCrudRouteProjection([sharedDetailModule], nav, views, []),
      'STANDARD_CRUD_DETAIL_CAPABILITY_NOT_UNIQUE',
    );
  });

  it('fails when two resources derive the same detail path', () => {
    const secondListTarget = 'sample.archived.list';
    const secondDetailTarget = 'sample.archived.detail';
    const secondModule: FrontendModule = {
      metadata: { moduleId: 'archived', displayName: 'Archived', version: '1.0.0' },
      standardCrudCapabilities: [
        { kind: 'standard-list', targetKey: secondListTarget, load },
        { kind: 'standard-crud-detail', targetKey: secondDetailTarget, load },
      ],
    };
    const nav: RuntimeNavItemDocument[] = [{
      type: 'nav',
      navigation: {
        items: [
          ...lazyNav[0].navigation.items,
          { key: 'sample.archived', path, entryType: 'standard-list', targetKey: secondListTarget },
        ],
      },
    }];
    const views: RuntimeViewDocument[] = [
      ...lazyViews,
      { type: 'view', view: { key: secondListTarget, mode: 'list', detailViewKey: secondDetailTarget } },
      {
        type: 'view',
        view: {
          key: secondDetailTarget,
          mode: 'detail',
          model: 'Record',
          sections: [{ key: 'basic', titleKey: 'Section_Basic', fields: ['ENAME'] }],
        },
      },
    ];

    expectProjectionFailure(
      () => compileStandardCrudRouteProjection([lazyModule, secondModule], nav, views, []),
      'STANDARD_CRUD_DETAIL_PATH_CONFLICT',
    );
  });

  it('fails when legacy resource permissions conflict with nav truth', () => {
    const legacyModule: FrontendModule = {
      ...modules[0],
      standardCrudResources: [{
        ...modules[0].standardCrudResources![0],
        permissions: ['Sample.Record.Update'],
      }],
    };

    expectProjectionFailure(
      () => compileStandardCrudRouteProjection([legacyModule], navDocuments, viewDocuments, []),
      'STANDARD_CRUD_PERMISSION_CONFLICT',
    );
  });

  it('fails when legacy registration detail truth differs from the list view', () => {
    const legacyModule: FrontendModule = {
      ...modules[0],
      standardCrudResources: [{
        ...modules[0].standardCrudResources![0],
        detailViewKey: 'sample.other.detail',
      }],
    };

    expectProjectionFailure(
      () => compileStandardCrudRouteProjection([legacyModule], navDocuments, viewDocuments, []),
      'STANDARD_CRUD_DETAIL_VIEW_KEY_MISMATCH',
    );
  });

  it('fails when two list-only resources derive the same drawer path', () => {
    const listOnlyModules: FrontendModule[] = ['alpha', 'beta'].map((key) => ({
      metadata: { moduleId: key, displayName: key, version: '1.0.0' },
      standardCrudResources: [{
        resourceKey: `${key}.records`,
        apiRoute: `/api/${key}`,
        listViewKey: `${key}.records.list`,
        createEmptyFilters: () => ({}),
        buildListQuery: () => ({}),
        mapListItem: (value) => value as Record<string, unknown>,
        getRowId: () => 'row-1',
      }],
    }));
    const nav: RuntimeNavItemDocument[] = [{
      type: 'nav',
      navigation: {
        items: ['alpha', 'beta'].map((key) => ({
          key,
          path,
          entryType: 'standard-list' as const,
          targetKey: `${key}.records.list`,
        })),
      },
    }];
    const views: RuntimeViewDocument[] = ['alpha', 'beta'].map((key) => ({
      type: 'view',
      view: { key: `${key}.records.list`, mode: 'list' },
    }));

    expectProjectionFailure(
      () => compileStandardCrudRouteProjection(listOnlyModules, nav, views, []),
      'STANDARD_CRUD_DRAWER_PATH_CONFLICT',
    );
  });

  it('fails when a drawer compatibility fallback claims different permissions', () => {
    const conflictingFallback: RouteConfig = {
      path: `${path}/new`,
      component: () => null,
      permissions: ['Sample.Record.Update'],
    };

    expectProjectionFailure(
      () => compileStandardCrudRouteProjection([lazyModule], lazyNav, lazyViews, [conflictingFallback]),
      'STANDARD_CRUD_DRAWER_FALLBACK_CONFLICT',
    );
  });

  it('fails when multiple fallback routes claim a projected drawer path', () => {
    const fallback: RouteConfig = {
      path: `${path}/new`,
      component: () => null,
      permissions,
    };

    expectProjectionFailure(
      () => compileStandardCrudRouteProjection([lazyModule], lazyNav, lazyViews, [fallback, fallback]),
      'STANDARD_CRUD_FALLBACK_OWNERSHIP_CONFLICT',
    );
  });

  it('compiles detail and drawer ownership without invoking loaders', () => {
    const compatibleFallback: RouteConfig = {
      path: `${path}/new`,
      component: () => null,
      permissions,
    };

    expect(compileStandardCrudRouteProjection(
      [lazyModule],
      lazyNav,
      lazyViews,
      [compatibleFallback, { path: '/unowned', component: () => null }],
    )).toEqual({
      detailEntries: [{ path: `${path}/:id`, viewKey: detailTarget, permissions }],
      drawerRedirects: [{ from: `${path}/new`, to: `${path}?drawer=create` }],
      fallbackRoutes: [{ path: '/unowned', component: expect.any(Function) }],
    });
    expect(load).not.toHaveBeenCalled();
  });
});
