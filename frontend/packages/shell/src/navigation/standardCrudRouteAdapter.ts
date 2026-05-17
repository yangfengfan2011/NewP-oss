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
): RouteConfig[] {
  const standardCrudPaths = new Set(standardCrudRoutes.map((entry) => entry.path));
  return moduleRoutes.filter((route) => !standardCrudPaths.has(route.path));
}
