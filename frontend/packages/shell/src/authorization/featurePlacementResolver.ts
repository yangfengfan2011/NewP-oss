import { createFeatureVisibility } from './featureVisibility';

export interface FeaturePlacement {
  entryPosition: string;
  staticScope?: string;
  actionPosition?: string;
  actionKey?: string;
}

export type FeatureResolution =
  | { kind: 'allowed'; position: string }
  | { kind: 'denied'; position: string }
  | { kind: 'unresolved' };

interface FeaturePoint {
  kind: string;
  key?: string;
  position: string;
  parentPosition?: string | null;
  staticScope?: string | null;
  actionScope?: string | null;
  container?: boolean;
}

interface CreateFeaturePlacementResolverOptions {
  placement: FeaturePlacement | null;
  points: readonly FeaturePoint[];
  navigationPositions: ReadonlyMap<string, string>;
  hasFeature: (position: string) => boolean;
}

interface FeaturePlacementLocator {
  placement: FeaturePlacement | null;
  points: readonly FeaturePoint[];
  readPosition: string | undefined;
  actionPosition: (key: string, surface: string, scope?: string | null) => string | undefined;
  navigationPosition: (key: string) => string | undefined;
  tabPosition: (key: string) => string | undefined;
}

/** 目录定位独立于当前授权，授权刷新只重投影已绑定的位置。 */
export function createFeaturePlacementLocator({
  placement,
  points,
  navigationPositions,
}: Omit<CreateFeaturePlacementResolverOptions, 'hasFeature'>): FeaturePlacementLocator {
  const actionPositions = new Map<string, string | undefined>();
  const tabPositions = new Map<string, string | undefined>();
  return {
    placement,
    points,
    readPosition: placement?.actionPosition ?? placement?.entryPosition,
    actionPosition(key, surface, scope) {
      if (!placement) return undefined;
      if (placement.actionPosition) return placement.actionKey === key ? placement.actionPosition : undefined;
      const staticScope = scope ?? placement.staticScope;
      const identity = JSON.stringify([key, surface, staticScope ?? null]);
      if (!actionPositions.has(identity)) {
        actionPositions.set(identity, points.find(candidate => candidate.kind === 'action'
          && candidate.parentPosition === placement.entryPosition
          && (candidate.staticScope ?? undefined) === (staticScope ?? undefined)
          && candidate.actionScope === surface
          && candidate.key === key)?.position);
      }
      return actionPositions.get(identity);
    },
    navigationPosition: key => navigationPositions.get(key),
    tabPosition(key) {
      if (!placement) return undefined;
      if (!tabPositions.has(key)) {
        tabPositions.set(key, points.find(candidate => candidate.kind === 'tab'
          && candidate.parentPosition === placement.entryPosition
          && (candidate.staticScope ?? undefined) === (placement.staticScope ?? undefined)
          && candidate.key === key)?.position);
      }
      return tabPositions.get(key);
    },
  };
}

/** Shell 私有解析器只向 consumer 返回语义操作的显式三态。 */
export function projectFeaturePlacement(
  locator: FeaturePlacementLocator,
  hasFeature: (position: string) => boolean,
) {
  const visible = createFeatureVisibility(locator.points, hasFeature);
  const decide = (position: string | undefined): FeatureResolution => {
    if (!position) return { kind: 'unresolved' };
    return hasFeature(position) ? { kind: 'allowed', position } : { kind: 'denied', position };
  };

  return {
    placement: locator.placement,
    read: () => decide(locator.readPosition),
    action: (key: string, surface: string, scope?: string | null): FeatureResolution => decide(locator.actionPosition(key, surface, scope)),
    navigation: (key: string) => decide(locator.navigationPosition(key)),
    tab: (key: string): FeatureResolution => {
      const position = locator.tabPosition(key);
      if (!position) return { kind: 'unresolved' };
      return visible(position) ? { kind: 'allowed', position } : { kind: 'denied', position };
    },
    menu: (position: string): FeatureResolution => {
      if (!locator.points.some(point => point.position === position)) return { kind: 'unresolved' };
      return visible(position) ? { kind: 'allowed', position } : { kind: 'denied', position };
    },
  };
}

export function createFeaturePlacementResolver({
  hasFeature,
  ...locationOptions
}: CreateFeaturePlacementResolverOptions) {
  return projectFeaturePlacement(createFeaturePlacementLocator(locationOptions), hasFeature);
}

export function resolvedFeaturePosition(resolution: FeatureResolution): string | undefined {
  return resolution.kind === 'unresolved' ? undefined : resolution.position;
}
