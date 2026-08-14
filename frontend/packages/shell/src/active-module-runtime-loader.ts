import {
  ROUTE_CAPABILITY_KINDS,
  createStandardCrudClient,
  type FrontendBehaviorBindings,
  type FrontendModule,
  type StandardCrudActionHandler,
  type StandardCrudCapabilityBinding,
} from '@icp-platform/sdk';
import { platformFetch } from '@icp-platform/core';
import internalModuleRegistry from 'virtual:newp-frontend-entry-registry';

export const ACTIVE_MODULE_RUNTIME_URL = '/plugins/active-module-runtime.json';

export interface ActiveModuleFrontendEntry {
  moduleId?: string | null;
  url?: string | null;
  projectionUrl?: string | null;
  blueprintUrl?: string | null;
  behaviorKey?: string | null;
  bindingMode: 'behavior-bindings' | 'legacy-module' | 'standard-data';
  bindings: string[];
}

export interface ActiveModuleRuntimeEntry {
  moduleId: string;
  version: string;
  displayName: string;
  description?: string;
  dependencies: string[];
  frontend?: ActiveModuleFrontendEntry | null;
}

export interface ActiveModuleRuntimeCatalog {
  schemaVersion: string;
  snapshotId: string;
  planHash: string;
  modules: ActiveModuleRuntimeEntry[];
}

export interface ActiveModuleRuntimeLoadResult {
  snapshotId: string;
  planHash: string;
  modules: FrontendModule[];
}

export interface ActiveModuleRuntimeLoaderDependencies {
  catalogUrl?: string;
  fetchCatalog?: (url: string) => Promise<{
    ok: boolean;
    status: number;
    statusText?: string;
    json: () => Promise<ActiveModuleRuntimeCatalog>;
  }>;
  importModule?: (url: string) => Promise<Record<string, unknown>>;
  fetchJson?: (url: string) => Promise<unknown>;
  internalModules?: Record<string, () => Promise<Record<string, unknown>>>;
}

interface StandardDataBlueprintField {
  name: string;
  type: 'string' | 'date' | 'dateTime' | 'boolean' | 'integer' | 'decimal';
  required: boolean;
}

interface StandardDataBlueprintResource {
  key: string;
  displayName: string;
  displayField: string;
  fields: StandardDataBlueprintField[];
}

interface StandardDataBlueprint {
  moduleId: string;
  version: string;
  standardData: { resources: StandardDataBlueprintResource[] };
}

interface StandardDataProjectionResource {
  resourceKey: string;
  apiRoute: string;
  pageRoute: string;
  permissionCodes: string[];
}

interface StandardDataProjection {
  moduleSlug: string;
  resources: StandardDataProjectionResource[];
}

function collectBehaviorBindingTargetKeys(bindings: FrontendBehaviorBindings): string[] {
  return [
    ...(bindings.routeCapabilities ?? []).map((binding) => binding.targetKey),
    ...(bindings.standardCrudCapabilities ?? []).map((binding) => binding.targetKey),
    ...(bindings.standardCrudResources ?? []).flatMap((registration) => [
      registration.listViewKey,
      ...(registration.detailViewKey ? [registration.detailViewKey] : []),
    ]),
    ...(bindings.standardListViews ?? []).map((registration) => registration.viewKey),
    ...(bindings.customPages ?? []).map((registration) => registration.pageKey),
  ];
}

function collectModuleCapabilityTargetKeys(module: FrontendModule): string[] {
  return [
    ...(module.routeCapabilities ?? []).map((binding) => binding.targetKey),
    ...(module.standardCrudCapabilities ?? []).map((binding) => binding.targetKey),
    ...(module.standardCrudResources ?? []).flatMap((registration) => [
      registration.listViewKey,
      ...(registration.detailViewKey ? [registration.detailViewKey] : []),
    ]),
    ...(module.standardListViews ?? []).map((registration) => registration.viewKey),
    ...(module.customPages ?? []).map((registration) => registration.pageKey),
    ...(module.routes ?? []).map((route) => route.path),
  ];
}

function validateRouteCapabilityDescriptors(
  moduleId: string,
  bindings: FrontendBehaviorBindings['routeCapabilities'],
): void {
  const supportedKinds = new Set<string>(ROUTE_CAPABILITY_KINDS);
  for (const binding of bindings ?? []) {
    if (!supportedKinds.has(binding.kind)) {
      throw new Error(`unknown route capability kind '${String(binding.kind)}' for '${moduleId}'`);
    }
    if (typeof binding.targetKey !== 'string'
      || binding.targetKey.trim().length === 0
      || typeof binding.load !== 'function') {
      throw new Error(`invalid route capability descriptor for '${moduleId}'`);
    }
  }
}

function validateStandardCrudCapabilityDescriptors(
  moduleId: string,
  bindings: StandardCrudCapabilityBinding[] | undefined,
): void {
  for (const binding of bindings ?? []) {
    if ((binding.kind !== 'standard-list' && binding.kind !== 'standard-crud-detail')
      || typeof binding.targetKey !== 'string'
      || binding.targetKey.trim().length === 0
      || typeof binding.load !== 'function') {
      throw new Error(`invalid standard CRUD capability descriptor for '${moduleId}'`);
    }
  }
}

function validateRouteCapabilityCandidate(modules: FrontendModule[]): void {
  const ownerByTargetKey = new Map<string, string>();
  for (const module of modules) {
    const moduleId = module.metadata.moduleId;
    validateRouteCapabilityDescriptors(moduleId, module.routeCapabilities);
    validateStandardCrudCapabilityDescriptors(moduleId, module.standardCrudCapabilities);
    for (const targetKey of collectModuleCapabilityTargetKeys(module)) {
      const owner = ownerByTargetKey.get(targetKey);
      if (owner) {
        throw new Error(
          `duplicate route capability target '${targetKey}' for '${owner}' and '${moduleId}'`,
        );
      }
      ownerByTargetKey.set(targetKey, moduleId);
    }
  }
}

function validateBehaviorBindingCoverage(
  entry: ActiveModuleRuntimeEntry,
  frontend: ActiveModuleFrontendEntry,
  bindings: FrontendBehaviorBindings,
): void {
  validateRouteCapabilityDescriptors(entry.moduleId, bindings.routeCapabilities);
  validateStandardCrudCapabilityDescriptors(entry.moduleId, bindings.standardCrudCapabilities);

  const implementedTargetKeys = collectBehaviorBindingTargetKeys(bindings);
  const duplicateDeclared = frontend.bindings.find(
    (targetKey, index) => frontend.bindings.indexOf(targetKey) !== index,
  );
  const duplicateImplemented = implementedTargetKeys.find(
    (targetKey, index) => implementedTargetKeys.indexOf(targetKey) !== index,
  );
  if (duplicateDeclared || duplicateImplemented) {
    throw new Error(
      `duplicate binding target for '${entry.moduleId}': ${duplicateDeclared ?? duplicateImplemented}`,
    );
  }

  const declared = new Set(frontend.bindings);
  const implemented = new Set(implementedTargetKeys);
  const missing = frontend.bindings.filter((targetKey) => !implemented.has(targetKey));
  const unexpected = [...implemented].filter((targetKey) => !declared.has(targetKey));
  if (missing.length > 0 || unexpected.length > 0) {
    throw new Error(
      `binding coverage mismatch for '${entry.moduleId}': missing [${missing.join(', ')}], unexpected [${unexpected.join(', ')}]`,
    );
  }
}

function defaultImporter(url: string): Promise<Record<string, unknown>> {
  return import(/* @vite-ignore */ url);
}

async function defaultJsonFetcher(url: string): Promise<unknown> {
  const response = await fetch(url, { credentials: 'same-origin' });
  if (!response.ok) {
    throw new Error(`${url} returned ${response.status} ${response.statusText}`.trim());
  }

  return response.json();
}

function toRouteToken(value: string): string {
  return value.replace(/([a-z0-9])([A-Z])/g, '$1-$2').toLowerCase();
}

function mapStandardFieldKind(type: StandardDataBlueprintField['type']) {
  if (type === 'boolean') return 'boolean' as const;
  if (type === 'integer' || type === 'decimal') return 'number' as const;
  if (type === 'date' || type === 'dateTime') return 'date' as const;
  return 'text' as const;
}

function asRecord(value: unknown): Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
    ? value as Record<string, unknown>
    : {};
}

function createStandardDataStateActionHandlers(apiRoute: string): Record<string, StandardCrudActionHandler> {
  const client = createStandardCrudClient(apiRoute, platformFetch);
  const requireId = (id: string | undefined, action: string): string => {
    if (!id) {
      throw new Error(`缺少资源标识，无法${action}。`);
    }

    return id;
  };

  return {
    disable: async ({ id, refresh }) => {
      await client.disable(requireId(id, '停用'));
      refresh();
    },
    enable: async ({ id, refresh }) => {
      await client.update(requireId(id, '启用'), { IsEnabled: true });
      refresh();
    },
  };
}

async function projectStandardDataModule(
  entry: ActiveModuleRuntimeEntry,
  frontend: ActiveModuleFrontendEntry,
  fetchJson: (url: string) => Promise<unknown>,
): Promise<FrontendModule> {
  if (frontend.behaviorKey !== 'standard-data.v1'
    || !frontend.blueprintUrl
    || !frontend.projectionUrl) {
    throw new Error(`standard-data entry '${entry.moduleId}' is incomplete`);
  }

  const [blueprintValue, projectionValue] = await Promise.all([
    fetchJson(frontend.blueprintUrl),
    fetchJson(frontend.projectionUrl),
  ]);
  const blueprint = blueprintValue as StandardDataBlueprint;
  const projection = projectionValue as StandardDataProjection;
  if (blueprint.moduleId !== entry.moduleId
    || blueprint.version !== entry.version
    || !Array.isArray(blueprint.standardData?.resources)
    || !projection.moduleSlug
    || !Array.isArray(projection.resources)) {
    throw new Error(`standard-data contracts for '${entry.moduleId}' do not match the active catalog`);
  }

  const standardCrudResources = blueprint.standardData.resources.map((resource) => {
    const projected = projection.resources.find((candidate) => candidate.resourceKey === resource.key);
    const bindingKey = `${entry.moduleId}.${resource.key}`;
    if (!projected || !frontend.bindings.includes(bindingKey)) {
      throw new Error(`standard-data resource '${bindingKey}' has no active projection binding`);
    }

    const resourceSlug = toRouteToken(resource.key);
    const viewPermission = projected.permissionCodes.find((code) => code.endsWith('.View'));
    if (!viewPermission) {
      throw new Error(`standard-data resource '${bindingKey}' has no View permission`);
    }

    return {
      resourceKey: bindingKey,
      apiRoute: projected.apiRoute,
      listViewKey: `${projection.moduleSlug}.${resourceSlug}.list`,
      listRoutePath: projected.pageRoute,
      detailViewKey: `${projection.moduleSlug}.${resourceSlug}.detail`,
      detailRoutePath: `${projected.pageRoute.replace(/\/$/, '')}/:id`,
      listPanelTitle: resource.displayName,
      permissions: [viewPermission],
      fields: resource.fields.map((field) => ({
        key: field.name,
        labelKey: field.name,
        modelField: field.name,
        kind: mapStandardFieldKind(field.type),
        required: field.required,
        trim: field.type === 'string',
        emptyAsNull: !field.required,
      })),
      actionHandlers: resource.fields.some((field) => field.name === 'IsEnabled')
        ? createStandardDataStateActionHandlers(projected.apiRoute)
        : undefined,
      createEmptyFilters: () => ({}),
      buildListQuery: (filters: Record<string, string>) => filters,
      mapListItem: asRecord,
      getRowId: (row: Record<string, unknown>) => {
        const id = row.EID ?? row.eid ?? row.Id ?? row.id;
        return typeof id === 'string' ? id : '';
      },
    };
  });
  const permissionCodes = projection.resources
    .flatMap((resource) => resource.permissionCodes)
    .filter((code, index, all) => all.indexOf(code) === index);

  return {
    metadata: {
      moduleId: entry.moduleId,
      displayName: entry.displayName,
      version: entry.version,
      description: entry.description,
    },
    standardCrudResources,
    permissions: permissionCodes.map((code) => ({ code, name: code })),
  } satisfies FrontendModule;
}

export async function loadActiveModuleRuntimeWith(
  dependencies: ActiveModuleRuntimeLoaderDependencies = {},
): Promise<ActiveModuleRuntimeLoadResult> {
  const catalogUrl = dependencies.catalogUrl ?? ACTIVE_MODULE_RUNTIME_URL;
  const fetchCatalog = dependencies.fetchCatalog ?? ((url: string) => fetch(url));
  const internalModules = dependencies.internalModules ?? internalModuleRegistry;
  const importModule = dependencies.importModule ?? defaultImporter;
  const fetchJson = dependencies.fetchJson ?? defaultJsonFetcher;
  const response = await fetchCatalog(catalogUrl);
  if (!response.ok) {
    throw new Error(
      `ACTIVE_MODULE_RUNTIME_UNAVAILABLE: ${catalogUrl} returned ${response.status} ${response.statusText ?? ''}`.trim(),
    );
  }

  const catalog: ActiveModuleRuntimeCatalog = await response.json();
  if (!catalog.snapshotId || !catalog.planHash || !Array.isArray(catalog.modules)) {
    throw new Error('ACTIVE_MODULE_RUNTIME_INVALID: active catalog is incomplete');
  }

  try {
    const modules = await Promise.all(
      catalog.modules
        .filter((entry) => entry.frontend)
        .map(async (entry) => {
          const frontend = entry.frontend!;
          if (frontend.bindingMode === 'standard-data') {
            return projectStandardDataModule(entry, frontend, fetchJson);
          }

          let exports: Record<string, unknown>;
          let resolvedEntry: string;
          if (frontend.moduleId) {
            const loader = internalModules[frontend.moduleId];
            if (!loader) {
              throw new Error(`internal entry '${frontend.moduleId}' is not present`);
            }
            exports = await loader();
            resolvedEntry = frontend.moduleId;
          } else if (frontend.url) {
            exports = await importModule(frontend.url);
            resolvedEntry = frontend.url;
          } else {
            throw new Error(`module '${entry.moduleId}' has no frontend entry`);
          }

          const implementation = exports.default;
          if (!implementation) {
            throw new Error(`entry '${resolvedEntry}' has no default export`);
          }

          const metadata = {
            moduleId: entry.moduleId,
            displayName: entry.displayName,
            version: entry.version,
            description: entry.description,
          };
          if (frontend.bindingMode === 'behavior-bindings') {
            const bindings = implementation as FrontendBehaviorBindings;
            if (!frontend.behaviorKey
              || bindings.behaviorKey !== frontend.behaviorKey) {
              throw new Error(
                `behavior key mismatch for '${entry.moduleId}': expected '${frontend.behaviorKey ?? ''}', got '${bindings.behaviorKey ?? ''}'`,
              );
            }
            validateBehaviorBindingCoverage(entry, frontend, bindings);
            return {
              metadata,
              routeCapabilities: bindings.routeCapabilities,
              standardCrudCapabilities: bindings.standardCrudCapabilities,
              standardCrudResources: bindings.standardCrudResources,
              standardListViews: bindings.standardListViews,
              customPages: bindings.customPages,
              initialize: bindings.initialize,
              cleanup: bindings.cleanup,
            } satisfies FrontendModule;
          }

          const legacyModule = implementation as FrontendModule;
          return {
            ...legacyModule,
            metadata,
          } satisfies FrontendModule;
        }),
    );
    validateRouteCapabilityCandidate(modules);

    return {
      snapshotId: catalog.snapshotId,
      planHash: catalog.planHash,
      modules,
    };
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    throw new Error(`MODULE_PROJECTION_FAILED: snapshot '${catalog.snapshotId}' could not be loaded: ${message}`);
  }
}

export async function loadActiveModuleRuntime(): Promise<ActiveModuleRuntimeLoadResult> {
  return loadActiveModuleRuntimeWith();
}
