import { createContext, useContext, useMemo } from 'react';
import { useAuthorization } from '@icp-platform/core';
import { useRuntimeMetadata } from '../metadata/runtimeMetadataContext';
import {
  createFeaturePlacementLocator,
  projectFeaturePlacement,
  type FeaturePlacement,
} from './featurePlacementResolver';

export type { FeaturePlacement } from './featurePlacementResolver';

export const FeaturePlacementContext = createContext<FeaturePlacement | null>(null);

function useOptionalFeatureDecision(): (position: string) => boolean {
  try { return useAuthorization().hasFeature; }
  catch { return () => false; }
}

/** 位置来自正式目录与外层声明；运行时记录和 URL 都不参与身份生成。 */
export function useFeaturePlacement() {
  const placement = useContext(FeaturePlacementContext);
  const metadata = useRuntimeMetadata();
  const hasFeature = useOptionalFeatureDecision();
  const points = useMemo(() => metadata?.manifest.modules.flatMap(module => module.features ?? []) ?? [], [metadata]);
  const navigationPositions = useMemo(() => new Map(
    metadata?.navDocuments
      .flatMap(document => document.navigation.items)
      .flatMap(item => item.featurePosition ? [[item.key, item.featurePosition] as const] : []) ?? [],
  ), [metadata]);
  const locator = useMemo(() => createFeaturePlacementLocator({
    placement,
    points,
    navigationPositions,
  }), [navigationPositions, placement, points]);
  return useMemo(() => projectFeaturePlacement(locator, hasFeature), [hasFeature, locator]);
}
