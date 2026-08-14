import {
  ModuleRegistry,
  type RouteCapabilityBinding,
  type RouteCapabilityRef,
  type StandardCrudCapabilityBinding,
  type StandardCrudResourceRegistration,
} from '@icp-platform/sdk';
import type { ComponentType } from 'react';
import { createLegacyRouteCapabilityBinding } from './LegacyRouteCapabilityAdapter';
import {
  createStandardCrudDetailRouteCapabilityBinding,
  createStandardListRouteCapabilityBinding,
} from './StandardRouteCapabilityAdapters';

export interface RouteCapabilityRuntime {
  load(ref: RouteCapabilityRef): Promise<ComponentType>;
}

export type RouteCapabilityErrorCode =
  | 'ROUTE_CAPABILITY_SNAPSHOT_UNAVAILABLE'
  | 'ROUTE_CAPABILITY_NOT_FOUND'
  | 'ROUTE_CAPABILITY_CONFLICT'
  | 'ROUTE_CAPABILITY_KIND_MISMATCH'
  | 'ROUTE_CAPABILITY_LOAD_FAILED'
  | 'ROUTE_CAPABILITY_STALE'
  | 'ROUTE_CAPABILITY_RENDER_FAILED';

export class RouteCapabilityError extends Error {
  readonly name = 'RouteCapabilityError';

  constructor(
    public readonly code: RouteCapabilityErrorCode,
    ref?: RouteCapabilityRef,
    public readonly cause?: unknown,
  ) {
    super(ref ? `${code}: ${ref.kind}:${ref.targetKey}` : code);
  }
}

class ModuleRegistryRouteCapabilityRuntime implements RouteCapabilityRuntime {
  private readonly loads = new Map<string, Promise<ComponentType>>();

  load(ref: RouteCapabilityRef): Promise<ComponentType> {
    const snapshotId = ModuleRegistry.getActiveSnapshotId();
    if (!snapshotId) {
      return Promise.reject(new RouteCapabilityError('ROUTE_CAPABILITY_SNAPSHOT_UNAVAILABLE'));
    }

    const cacheKey = `${snapshotId}\u0000${ref.kind}\u0000${ref.targetKey}`;
    const existing = this.loads.get(cacheKey);
    if (existing) {
      return existing;
    }

    const trackedLoad = this.loadBinding(ref, snapshotId).catch((error: unknown) => {
      // 失败只清理当前 Promise，避免旧请求在 retry 已开始后误删新 snapshot/key 的 single-flight。
      if (this.loads.get(cacheKey) === trackedLoad) {
        this.loads.delete(cacheKey);
      }
      throw error;
    });
    this.loads.set(cacheKey, trackedLoad);
    return trackedLoad;
  }

  private async loadBinding(ref: RouteCapabilityRef, snapshotId: string): Promise<ComponentType> {
    const allBindings = ModuleRegistry.getAllModules()
      .flatMap((module) => [
        ...(module.routeCapabilities ?? []),
        ...(module.standardCrudCapabilities ?? []).map((binding) => (
          createLazyStandardCrudRouteCapabilityBinding(binding)
        )),
        ...(module.customPages ?? []).map((registration) => createLegacyRouteCapabilityBinding(
          { kind: 'custom', targetKey: registration.pageKey },
          registration.component,
        )),
        ...(module.routes ?? []).map((route) => createLegacyRouteCapabilityBinding(
          { kind: 'custom', targetKey: route.path },
          route.component,
        )),
        ...(module.standardListViews ?? []).map((registration) => (
          createStandardListRouteCapabilityBinding(registration.viewKey)
        )),
        ...(module.standardCrudResources ?? []).flatMap((registration) => [
          createStandardListRouteCapabilityBinding(registration.listViewKey),
          ...(registration.detailViewKey
            ? [createStandardCrudDetailRouteCapabilityBinding(registration.detailViewKey)]
            : []),
        ]),
      ]);
    const targetBindings = allBindings.filter((binding) => binding.targetKey === ref.targetKey);
    const bindings = targetBindings.filter((binding) => binding.kind === ref.kind);
    if (bindings.length > 1) {
      throw new RouteCapabilityError('ROUTE_CAPABILITY_CONFLICT', ref);
    }
    const binding: RouteCapabilityBinding | undefined = bindings[0];
    if (!binding) {
      if (targetBindings.length > 0) {
        throw new RouteCapabilityError('ROUTE_CAPABILITY_KIND_MISMATCH', ref);
      }
      throw new RouteCapabilityError('ROUTE_CAPABILITY_NOT_FOUND', ref);
    }

    let loaded;
    try {
      loaded = await binding.load();
    } catch (error) {
      throw error instanceof RouteCapabilityError
        ? error
        : new RouteCapabilityError('ROUTE_CAPABILITY_LOAD_FAILED', ref, error);
    }
    if (ModuleRegistry.getActiveSnapshotId() !== snapshotId) {
      throw new RouteCapabilityError('ROUTE_CAPABILITY_STALE', ref);
    }
    const component = typeof loaded === 'object' && loaded !== null
      ? (loaded as { default?: unknown }).default
      : undefined;
    if (!isRenderableComponent(component)) {
      throw new RouteCapabilityError(
        'ROUTE_CAPABILITY_LOAD_FAILED',
        ref,
        new TypeError('Route capability module must have a renderable default export.'),
      );
    }
    return component;
  }
}

export const routeCapabilityRuntime: RouteCapabilityRuntime = new ModuleRegistryRouteCapabilityRuntime();

function createLazyStandardCrudRouteCapabilityBinding(
  binding: StandardCrudCapabilityBinding,
): RouteCapabilityBinding {
  return {
    kind: binding.kind,
    targetKey: binding.targetKey,
    load: async () => {
      const loaded = await binding.load();
      const registration = typeof loaded === 'object' && loaded !== null
        ? (loaded as { default?: unknown }).default
        : undefined;
      if (!isStandardCrudResourceRegistration(registration)) {
        throw new TypeError('Standard CRUD capability module must have a resource registration default export.');
      }

      const implementedTargetKey = binding.kind === 'standard-list'
        ? registration.listViewKey
        : registration.detailViewKey;
      if (implementedTargetKey !== binding.targetKey) {
        throw new TypeError(
          `Standard CRUD capability target mismatch: expected '${binding.targetKey}', got '${implementedTargetKey ?? ''}'.`,
        );
      }

      const routeBinding = binding.kind === 'standard-list'
        ? createStandardListRouteCapabilityBinding(binding.targetKey, registration)
        : createStandardCrudDetailRouteCapabilityBinding(binding.targetKey, registration);
      return routeBinding.load();
    },
  };
}

function isStandardCrudResourceRegistration(value: unknown): value is StandardCrudResourceRegistration {
  if (typeof value !== 'object' || value === null) {
    return false;
  }

  const resource = value as Partial<StandardCrudResourceRegistration>;
  return typeof resource.resourceKey === 'string'
    && typeof resource.apiRoute === 'string'
    && typeof resource.listViewKey === 'string'
    && typeof resource.createEmptyFilters === 'function'
    && typeof resource.buildListQuery === 'function'
    && typeof resource.mapListItem === 'function'
    && typeof resource.getRowId === 'function';
}

function isRenderableComponent(value: unknown): value is ComponentType {
  return typeof value === 'function';
}
