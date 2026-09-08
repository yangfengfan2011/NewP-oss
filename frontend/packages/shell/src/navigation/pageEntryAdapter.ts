import type { RuntimeNavItemDocument } from '../metadata/runtimeMetadataManifest';
import type { RuntimePageEntry } from './pageEntryTypes';
import { hasRuntimePageEntryShape } from './pageEntryTypes';
import type { RouteConfig } from '@icp-platform/sdk';

export function buildPageEntriesFromNavDocuments(
  navDocuments: RuntimeNavItemDocument[],
): RuntimePageEntry[] {
  return navDocuments.flatMap((document) =>
    [...document.navigation.items.filter(hasRuntimePageEntryShape).map((item) => ({
      path: item.path,
      entryType: item.entryType,
      targetKey: item.targetKey,
      permissions: item.permissions,
      ...(item.localAdministratorOnly ? { localAdministratorOnly: true } : {}),
    })), ...document.navigation.items.filter(item => item.path && item.page).map(item => ({
      path: item.path!, entryType: 'page' as const, targetKey: item.page!.pageKey, page: item.page, permissions: item.permissions,
    }))],
  );
}

export function resolveRouteableMetadataEntries(
  metadataEntries: RuntimePageEntry[],
  moduleRoutes: RouteConfig[],
): RuntimePageEntry[] {
  const moduleRouteByPath = new Map(moduleRoutes.map((route) => [route.path, route] as const));
  const entriesByPath = new Map<string, RuntimePageEntry[]>();

  for (const entry of metadataEntries) {
    const existingEntries = entriesByPath.get(entry.path) ?? [];
    existingEntries.push(entry);
    entriesByPath.set(entry.path, existingEntries);
  }

  const resolvedEntries: RuntimePageEntry[] = [];

  for (const [path, entries] of entriesByPath) {
    if (entries.length > 1) {
      // 同一路径出现多个 metadata 入口时不能靠顺序决定最终结果；这里直接报错并交回模块路由兜底。
      console.error(
        `Duplicate metadata page entries for path "${path}" were found. All conflicting entries are ignored.`,
      );
      continue;
    }

    const entry = entries[0];
    const moduleRoute = moduleRouteByPath.get(path);
    const inheritedPermissions = entry.permissions && entry.permissions.length > 0
      ? entry.permissions
      : moduleRoute?.permissions;
    const localAdministratorOnly = entry.localAdministratorOnly || moduleRoute?.localAdministratorOnly;
    const access = localAdministratorOnly ? { localAdministratorOnly: true } : {};

    resolvedEntries.push(
      inheritedPermissions && inheritedPermissions.length > 0
        ? { ...entry, permissions: inheritedPermissions, ...access }
        : { ...entry, permissions: undefined, ...access },
    );
  }

  return resolvedEntries;
}

export function filterFallbackRoutesByMetadataEntries(
  moduleRoutes: RouteConfig[],
  routeableMetadataEntries: RuntimePageEntry[],
): RouteConfig[] {
  if (routeableMetadataEntries.length === 0) {
    return moduleRoutes;
  }

  const routeablePaths = new Set(routeableMetadataEntries.map((entry) => entry.path));
  return moduleRoutes.filter((route) => !routeablePaths.has(route.path));
}
