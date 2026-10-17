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
  featurePosition?: string;
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
  | 'STANDARD_CRUD_DETAIL_ACTION_MISSING'
  | 'STANDARD_CRUD_DETAIL_VIEW_KEY_MISMATCH'
  | 'STANDARD_CRUD_DETAIL_VIEW_NOT_UNIQUE'
  | 'STANDARD_CRUD_DETAIL_CAPABILITY_NOT_UNIQUE'
  | 'STANDARD_CRUD_DETAIL_CAPABILITY_ORPHAN'
  | 'STANDARD_CRUD_DETAIL_PATH_CONFLICT'
  | 'STANDARD_CRUD_DRAWER_PATH_CONFLICT'
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
  projectsFullDetail?: boolean;
  path: string;
  featurePosition?: string;
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
  // 显式导航与组合 View 都通过受控 PageRef 消费绑定，不因此生成另一套独立路由。
  const composedPages = [
    ...navDocuments.flatMap(document => document.navigation.items).map(item => item.page),
    ...viewDocuments.flatMap(document => [
      ...(Array.isArray(document.view.tabs) ? document.view.tabs : []),
      ...(Array.isArray(document.view.nodeTypes) ? document.view.nodeTypes : []),
      ...(Array.isArray(document.view.detailContributions) ? document.view.detailContributions : []),
    ]).map(item => item.pageRef),
  ];

  for (const module of modules) {
    for (const resource of module.standardCrudResources ?? []) {
      owners.push(compileLegacyResourceOwner(resource, navDocuments, viewDocuments));
    }

    for (const listBinding of (module.standardCrudCapabilities ?? []).filter(
      (binding) => binding.kind === 'standard-list',
    )) {
      const navigationMatches = navDocuments.flatMap(document => document.navigation.items)
        .filter(item => item.entryType === 'standard-list' && item.targetKey === listBinding.targetKey);
      const listView = requireUniqueView(viewDocuments, listBinding.targetKey, 'STANDARD_CRUD_LIST_VIEW_NOT_UNIQUE');
      const detailViewKey = readNonEmptyString(listView.view.detailViewKey);
      const composedList = composedPages.some(page => page?.pageKey === 'platform.list' && page.parameters?.viewKey === listBinding.targetKey);
      const composedDetail = detailViewKey && composedPages.some(page => page?.pageKey === 'platform.detail' && page.parameters?.viewKey === detailViewKey);
      // 仅内嵌详情仍使用同一资源的装载关联，但不因此虚构列表导航或新建/详情地址。
      const nav = navigationMatches.length === 0 && (composedList || composedDetail) ? undefined : requireUniqueListNav(navDocuments, listBinding.targetKey);
      if (!detailViewKey) {
        if (readNonEmptyString(listView.view.model)) {
          fail('STANDARD_CRUD_DETAIL_VIEW_KEY_MISSING', listBinding.targetKey);
        }
        if (nav) owners.push({
          path: nav.path,
          featurePosition: nav.featurePosition,
          projectsDrawer: false,
        });
        continue;
      }

      requireUniqueView(viewDocuments, detailViewKey, 'STANDARD_CRUD_DETAIL_VIEW_NOT_UNIQUE');
      if (nav || composedList) requireObjectViewAction(listView);
      const matchingDetailTargets = lazyDetailTargets.filter((targetKey) => targetKey === detailViewKey);
      if (matchingDetailTargets.length > 1) {
        fail('STANDARD_CRUD_DETAIL_CAPABILITY_NOT_UNIQUE', detailViewKey);
      }
      if (matchingDetailTargets.length && consumedLazyDetailTargets.has(detailViewKey)) {
        fail('STANDARD_CRUD_DETAIL_CAPABILITY_NOT_UNIQUE', detailViewKey);
      }
      if (matchingDetailTargets.length) consumedLazyDetailTargets.add(detailViewKey);
      if (nav) owners.push({
        detailViewKey,
        projectsFullDetail: matchingDetailTargets.length === 1,
        path: nav.path,
        featurePosition: nav.featurePosition,
        projectsDrawer: true,
      });
    }
  }

  for (const detailTarget of lazyDetailTargets) {
    if (!consumedLazyDetailTargets.has(detailTarget)) {
      fail('STANDARD_CRUD_DETAIL_CAPABILITY_ORPHAN', detailTarget);
    }
  }

  const explicitEntries = navDocuments.flatMap(document => document.navigation.items)
    .filter(item => item.entryType === 'standard-crud-detail').map(item => {
      const targetCount = lazyDetailTargets.filter(key => key === item.targetKey).length
        + modules.flatMap(module => module.standardCrudResources ?? []).filter(resource => resource.detailViewKey === item.targetKey && resource.detailRoutePath).length;
      if (!item.targetKey || !item.path || targetCount !== 1)
        fail('STANDARD_CRUD_DETAIL_CAPABILITY_ORPHAN', item.targetKey ?? item.key);
      resolveExplicitDetailRoute(navDocuments, item.targetKey);
      return { path: item.path!, viewKey: item.targetKey!, featurePosition: item.featurePosition };
    });
  const detailEntries = [...explicitEntries, ...owners.flatMap((owner) => owner.detailViewKey && owner.projectsFullDetail
    && !explicitEntries.some(entry => entry.viewKey === owner.detailViewKey)
    ? [{
        path: `${normalizeListPath(owner.path)}/:id`,
        viewKey: owner.detailViewKey,
        featurePosition: owner.featurePosition,
      }]
    : [])];
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
      featurePosition: entry.featurePosition,
      diagnostic: 'STANDARD_CRUD_DETAIL_FALLBACK_CONFLICT' as const,
    })),
    ...drawerRedirects.map((entry, index) => ({
      path: entry.from,
      featurePosition: drawerOwners[index].featurePosition,
      diagnostic: 'STANDARD_CRUD_DRAWER_FALLBACK_CONFLICT' as const,
    })),
  ];

  for (const owned of ownedRoutes) {
    const matchingFallbacks = moduleRoutes.filter((route) => route.path === owned.path);
    if (matchingFallbacks.length > 1) {
      fail('STANDARD_CRUD_FALLBACK_OWNERSHIP_CONFLICT', owned.path);
    }
    if (matchingFallbacks.length === 1
      && matchingFallbacks[0].featurePosition !== owned.featurePosition) {
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

  const listView = requireUniqueView(viewDocuments, resource.listViewKey, 'STANDARD_CRUD_LIST_VIEW_NOT_UNIQUE');
  const metadataDetailViewKey = readNonEmptyString(listView.view.detailViewKey);
  if (!metadataDetailViewKey && readNonEmptyString(listView.view.model)) {
    fail('STANDARD_CRUD_DETAIL_VIEW_KEY_MISSING', resource.listViewKey);
  }
  if (resource.detailViewKey !== metadataDetailViewKey) {
    fail('STANDARD_CRUD_DETAIL_VIEW_KEY_MISMATCH', resource.listViewKey);
  }
  if (resource.detailViewKey) {
    requireUniqueView(viewDocuments, resource.detailViewKey, 'STANDARD_CRUD_DETAIL_VIEW_NOT_UNIQUE');
    requireObjectViewAction(listView);
  }

  return {
    detailViewKey: resource.detailViewKey,
    projectsFullDetail: Boolean(resource.detailRoutePath),
    path: nav.path,
    featurePosition: nav.featurePosition,
    projectsDrawer: true,
  };
}

// 有 Model 的对象必须能从列表进入详情；纯统计视图没有对象操作要求。
function requireObjectViewAction(document: RuntimeViewDocument): void {
  if (!readNonEmptyString(document.view.model)) return;
  const actions = [document.view.rowActions, document.view.columns]
    .flatMap((value) => Array.isArray(value) ? value : []);
  if (!actions.some((value: unknown) => typeof value === 'object' && value !== null
    && 'standardAction' in value && value.standardAction === 'view')) {
    fail('STANDARD_CRUD_DETAIL_ACTION_MISSING', String(document.view.key));
  }
}

/** 完整页入口只消费显式 capability；Detail View 本身仅保证查看抽屉可用。 */
export function hasStandardCrudFullDetailPage(modules: FrontendModule[], detailViewKey: string): boolean {
  return modules.some((module) =>
    (module.standardCrudCapabilities ?? []).some((binding) =>
      binding.kind === 'standard-crud-detail' && binding.targetKey === detailViewKey)
    || (module.standardCrudResources ?? []).some((resource) =>
      resource.detailViewKey === detailViewKey && Boolean(resource.detailRoutePath)));
}

function requireUniqueListNav(
  navDocuments: RuntimeNavItemDocument[],
  listViewKey: string,
): { path: string; featurePosition?: string } {
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
    featurePosition: matches[0].featurePosition,
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

export function resolveStandardCrudListRoute(
  navDocuments: RuntimeNavItemDocument[],
  listViewKey: string,
): { path: string; featurePosition?: string } | null {
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
    path: matches[0].redirectTo ?? matches[0].path!,
    featurePosition: matches[0].featurePosition,
  };
}

/** 完整详情由显式路由声明拥有；历史独立列表仍沿用原有派生约定。 */
export function resolveExplicitDetailRoute(navDocuments: RuntimeNavItemDocument[], detailViewKey: string) {
  const matches = navDocuments.flatMap(document => document.navigation.items)
    .filter(item => item.entryType === 'standard-crud-detail' && item.targetKey === detailViewKey);
  if (matches.length > 1 || (matches.length === 1 && !matches[0].path))
    fail('STANDARD_CRUD_DETAIL_PATH_CONFLICT', detailViewKey);
  return matches[0];
}
