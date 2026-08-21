import type {
  FrontendModule,
  RouteConfig,
  StandardCrudResourceRegistration,
} from '@icp-platform/sdk';
import type {
  RuntimeNavItemDocument,
  RuntimeViewDocument,
} from '../metadata/runtimeMetadataManifest';

export interface StandardCrudDetailRouteEntry {
  path: string;
  viewKey: string;
  permissions?: string[];
}

export interface StandardCrudDrawerRouteRedirect {
  from: string;
  to: string;
}

export interface StandardCrudRouteProjection {
  detailEntries: StandardCrudDetailRouteEntry[];
  drawerRedirects: StandardCrudDrawerRouteRedirect[];
  fallbackRoutes: RouteConfig[];
}

export type StandardCrudProjectionDiagnostic =
  | 'STANDARD_CRUD_NAV_NOT_UNIQUE'
  | 'STANDARD_CRUD_LIST_VIEW_NOT_UNIQUE'
  | 'STANDARD_CRUD_DETAIL_VIEW_KEY_MISSING'
  | 'STANDARD_CRUD_DETAIL_VIEW_KEY_MISMATCH'
  | 'STANDARD_CRUD_DETAIL_VIEW_NOT_UNIQUE'
  | 'STANDARD_CRUD_DETAIL_CAPABILITY_NOT_UNIQUE'
  | 'STANDARD_CRUD_DETAIL_CAPABILITY_ORPHAN'
  | 'STANDARD_CRUD_DETAIL_PATH_CONFLICT'
  | 'STANDARD_CRUD_DRAWER_PATH_CONFLICT'
  | 'STANDARD_CRUD_PERMISSION_CONFLICT'
  | 'STANDARD_CRUD_DETAIL_FALLBACK_CONFLICT'
  | 'STANDARD_CRUD_DRAWER_FALLBACK_CONFLICT'
  | 'STANDARD_CRUD_FALLBACK_OWNERSHIP_CONFLICT';

export class ModuleProjectionError extends Error {
  readonly name = 'ModuleProjectionError';
  readonly code = 'MODULE_PROJECTION_FAILED';

  constructor(
    public readonly diagnostic: StandardCrudProjectionDiagnostic,
    public readonly subject: string,
  ) {
    super(`MODULE_PROJECTION_FAILED: ${diagnostic}: ${subject}`);
  }
}

interface StandardCrudProjectionOwner {
  detailViewKey?: string;
  path: string;
  permissions?: string[];
  projectsDrawer: boolean;
}

/**
 * 编译标准 CRUD 的路由所有权。任何歧义都会在路由树发布和 capability loader 执行前失败。
 */
export function compileStandardCrudRouteProjection(
  modules: FrontendModule[],
  navDocuments: RuntimeNavItemDocument[],
  viewDocuments: RuntimeViewDocument[],
  moduleRoutes: RouteConfig[],
): StandardCrudRouteProjection {
  const owners: StandardCrudProjectionOwner[] = [];
  const lazyDetailTargets = modules.flatMap((module) =>
    (module.standardCrudCapabilities ?? [])
      .filter((binding) => binding.kind === 'standard-crud-detail')
      .map((binding) => binding.targetKey),
  );
  const consumedLazyDetailTargets = new Set<string>();

  for (const module of modules) {
    for (const resource of module.standardCrudResources ?? []) {
      owners.push(compileLegacyResourceOwner(resource, navDocuments, viewDocuments));
    }

    for (const listBinding of (module.standardCrudCapabilities ?? []).filter(
      (binding) => binding.kind === 'standard-list',
    )) {
      const nav = requireUniqueListNav(navDocuments, listBinding.targetKey);
      const listView = requireUniqueView(viewDocuments, listBinding.targetKey, 'STANDARD_CRUD_LIST_VIEW_NOT_UNIQUE');
      const detailViewKey = readNonEmptyString(listView.view.detailViewKey);
      if (!detailViewKey) {
        owners.push({
          path: nav.path,
          permissions: nav.permissions,
          projectsDrawer: false,
        });
        continue;
      }

      requireUniqueView(viewDocuments, detailViewKey, 'STANDARD_CRUD_DETAIL_VIEW_NOT_UNIQUE');
      const matchingDetailTargets = lazyDetailTargets.filter((targetKey) => targetKey === detailViewKey);
      if (matchingDetailTargets.length !== 1) {
        fail('STANDARD_CRUD_DETAIL_CAPABILITY_NOT_UNIQUE', detailViewKey);
      }
      if (consumedLazyDetailTargets.has(detailViewKey)) {
        fail('STANDARD_CRUD_DETAIL_CAPABILITY_NOT_UNIQUE', detailViewKey);
      }
      consumedLazyDetailTargets.add(detailViewKey);
      owners.push({
        detailViewKey,
        path: nav.path,
        permissions: nav.permissions,
        projectsDrawer: true,
      });
    }
  }

  for (const detailTarget of lazyDetailTargets) {
    if (!consumedLazyDetailTargets.has(detailTarget)) {
      fail('STANDARD_CRUD_DETAIL_CAPABILITY_ORPHAN', detailTarget);
    }
  }

  const detailEntries = owners.flatMap((owner) => owner.detailViewKey
    ? [{
        path: `${normalizeListPath(owner.path)}/:id`,
        viewKey: owner.detailViewKey,
        permissions: owner.permissions,
      }]
    : []);
  requireUniquePaths(
    detailEntries.map((entry) => entry.path),
    'STANDARD_CRUD_DETAIL_PATH_CONFLICT',
  );

  const drawerOwners = owners.filter((owner) => owner.projectsDrawer);
  const drawerRedirects = drawerOwners.map((owner) => ({
    from: `${normalizeListPath(owner.path)}/new`,
    to: `${owner.path}?drawer=create`,
  }));
  requireUniquePaths(
    drawerRedirects.map((entry) => entry.from),
    'STANDARD_CRUD_DRAWER_PATH_CONFLICT',
  );

  const ownedRoutes = [
    ...detailEntries.map((entry) => ({
      path: entry.path,
      permissions: entry.permissions,
      diagnostic: 'STANDARD_CRUD_DETAIL_FALLBACK_CONFLICT' as const,
    })),
    ...drawerRedirects.map((entry, index) => ({
      path: entry.from,
      permissions: drawerOwners[index].permissions,
      diagnostic: 'STANDARD_CRUD_DRAWER_FALLBACK_CONFLICT' as const,
    })),
  ];

  for (const owned of ownedRoutes) {
    const matchingFallbacks = moduleRoutes.filter((route) => route.path === owned.path);
    if (matchingFallbacks.length > 1) {
      fail('STANDARD_CRUD_FALLBACK_OWNERSHIP_CONFLICT', owned.path);
    }
    if (matchingFallbacks.length === 1
      && !permissionSetsEqual(matchingFallbacks[0].permissions, owned.permissions)) {
      fail(owned.diagnostic, owned.path);
    }
  }

  const ownedPaths = new Set(ownedRoutes.map((route) => route.path));
  return {
    detailEntries,
    drawerRedirects,
    fallbackRoutes: moduleRoutes.filter((route) => !ownedPaths.has(route.path)),
  };
}

function compileLegacyResourceOwner(
  resource: StandardCrudResourceRegistration,
  navDocuments: RuntimeNavItemDocument[],
  viewDocuments: RuntimeViewDocument[],
): StandardCrudProjectionOwner {
  const nav = requireUniqueListNav(navDocuments, resource.listViewKey);
  if (resource.permissions && !permissionSetsEqual(resource.permissions, nav.permissions)) {
    fail('STANDARD_CRUD_PERMISSION_CONFLICT', resource.listViewKey);
  }

  const listView = requireUniqueView(viewDocuments, resource.listViewKey, 'STANDARD_CRUD_LIST_VIEW_NOT_UNIQUE');
  const metadataDetailViewKey = readNonEmptyString(listView.view.detailViewKey);
  if (resource.detailViewKey !== metadataDetailViewKey) {
    fail('STANDARD_CRUD_DETAIL_VIEW_KEY_MISMATCH', resource.listViewKey);
  }
  if (resource.detailViewKey) {
    requireUniqueView(viewDocuments, resource.detailViewKey, 'STANDARD_CRUD_DETAIL_VIEW_NOT_UNIQUE');
  }

  return {
    detailViewKey: resource.detailViewKey,
    path: nav.path,
    permissions: nav.permissions,
    projectsDrawer: true,
  };
}

function requireUniqueListNav(
  navDocuments: RuntimeNavItemDocument[],
  listViewKey: string,
): { path: string; permissions?: string[] } {
  const matches = navDocuments
    .flatMap((document) => document.navigation.items)
    .filter((item) => item.entryType === 'standard-list'
      && item.targetKey === listViewKey
      && typeof item.path === 'string'
      && item.path.length > 0);
  if (matches.length !== 1) {
    fail('STANDARD_CRUD_NAV_NOT_UNIQUE', listViewKey);
  }

  return {
    path: matches[0].path!,
    permissions: matches[0].permissions,
  };
}

function requireUniqueView(
  viewDocuments: RuntimeViewDocument[],
  viewKey: string,
  diagnostic: Extract<
    StandardCrudProjectionDiagnostic,
    'STANDARD_CRUD_LIST_VIEW_NOT_UNIQUE' | 'STANDARD_CRUD_DETAIL_VIEW_NOT_UNIQUE'
  >,
): RuntimeViewDocument {
  const matches = viewDocuments.filter((document) => document.view.key === viewKey);
  if (matches.length !== 1) {
    fail(diagnostic, viewKey);
  }
  return matches[0];
}

function requireUniquePaths(
  paths: string[],
  diagnostic: Extract<
    StandardCrudProjectionDiagnostic,
    'STANDARD_CRUD_DETAIL_PATH_CONFLICT' | 'STANDARD_CRUD_DRAWER_PATH_CONFLICT'
  >,
): void {
  const seen = new Set<string>();
  for (const path of paths) {
    if (seen.has(path)) {
      fail(diagnostic, path);
    }
    seen.add(path);
  }
}

function fail(diagnostic: StandardCrudProjectionDiagnostic, subject: string): never {
  throw new ModuleProjectionError(diagnostic, subject);
}

function normalizeListPath(path: string): string {
  return path.replace(/\/$/, '');
}

function readNonEmptyString(value: unknown): string | undefined {
  return typeof value === 'string' && value.length > 0 ? value : undefined;
}

function permissionSetsEqual(left: string[] | undefined, right: string[] | undefined): boolean {
  const normalizedLeft = [...new Set(left ?? [])].sort();
  const normalizedRight = [...new Set(right ?? [])].sort();
  return normalizedLeft.length === normalizedRight.length
    && normalizedLeft.every((permission, index) => permission === normalizedRight[index]);
}

export function resolveStandardCrudListRoute(
  navDocuments: RuntimeNavItemDocument[],
  listViewKey: string,
): { path: string; permissions?: string[] } | null {
  const matches = navDocuments
    .flatMap((document) => document.navigation.items)
    .filter((item) => item.entryType === 'standard-list'
      && item.targetKey === listViewKey
      && typeof item.path === 'string'
      && item.path.length > 0);

  if (matches.length !== 1) {
    return null;
  }

  return {
    path: matches[0].path!,
    permissions: matches[0].permissions,
  };
}
