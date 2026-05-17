import type { FrontendModule, RouteConfig } from '@icp-platform/sdk';

export interface StandardCrudDetailRouteEntry {
  path: string;
  viewKey: string;
  permissions?: string[];
}

export interface StandardCrudDrawerRouteRedirect {
  from: string;
  to: string;
}

export function buildStandardCrudDetailRouteEntries(
  modules: FrontendModule[],
  moduleRoutes: RouteConfig[],
): StandardCrudDetailRouteEntry[] {
  const moduleRouteByPath = new Map(moduleRoutes.map((route) => [route.path, route] as const));
  const entriesByPath = new Map<string, StandardCrudDetailRouteEntry[]>();

  for (const module of modules) {
    for (const resource of module.standardCrudResources ?? []) {
      if (!resource.detailRoutePath || !resource.detailViewKey) {
        continue;
      }

      const inheritedPermissions = resource.permissions && resource.permissions.length > 0
        ? resource.permissions
        : moduleRouteByPath.get(resource.detailRoutePath)?.permissions;

      const entries = entriesByPath.get(resource.detailRoutePath) ?? [];
      entries.push({
        path: resource.detailRoutePath,
        viewKey: resource.detailViewKey,
        permissions: inheritedPermissions,
      });
      entriesByPath.set(resource.detailRoutePath, entries);
    }
  }

  const resolvedEntries: StandardCrudDetailRouteEntry[] = [];

  for (const [path, entries] of entriesByPath) {
    if (entries.length > 1) {
      // 详情路由同路径冲突不能依赖模块注册顺序决胜；忽略全部冲突项，交由明确注册修正后再接管。
      console.error(`Duplicate standard CRUD detail route path "${path}" was found. All conflicting entries are ignored.`);
      continue;
    }

    resolvedEntries.push(entries[0]);
  }

  return resolvedEntries;
}

export function buildStandardCrudDrawerRouteRedirects(modules: FrontendModule[]): StandardCrudDrawerRouteRedirect[] {
  return modules.flatMap((module) =>
    (module.standardCrudResources ?? []).flatMap((resource) => {
      if (!resource.listRoutePath) {
        return [];
      }

      return [{
        from: `${resource.listRoutePath}/new`,
        to: `${resource.listRoutePath}?drawer=create`,
      }];
    }),
  );
}

export function filterFallbackRoutesByStandardCrudRoutes(
  moduleRoutes: RouteConfig[],
  standardCrudRoutes: StandardCrudDetailRouteEntry[],
  drawerRouteRedirects: StandardCrudDrawerRouteRedirect[] = [],
): RouteConfig[] {
  // 标准 CRUD 一旦声明详情路由或旧入口抽屉重定向，即表示这些路径所有权已经被宿主接管；
  // fallback route 必须整体让出，避免同一路径被重复渲染成页面与重定向。
  const standardCrudOwnedPaths = new Set([
    ...standardCrudRoutes.map((entry) => entry.path),
    ...drawerRouteRedirects.map((entry) => entry.from),
  ]);

  return moduleRoutes.filter((route) => !standardCrudOwnedPaths.has(route.path));
}
