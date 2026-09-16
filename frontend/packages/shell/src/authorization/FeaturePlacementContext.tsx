import { createContext, useContext, useMemo } from 'react';
import { useAuthorization } from '@icp-platform/core';
import { useRuntimeMetadata } from '../metadata/runtimeMetadataContext';
import { createFeatureVisibility } from './featureVisibility';

export interface FeaturePlacement {
  entryPosition: string;
  staticScope?: string;
  actionPosition?: string;
  actionKey?: string;
}

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
  const visible = useMemo(() => createFeatureVisibility(points, hasFeature), [points, hasFeature]);
  return {
    placement, points, hasFeature, visible,
    readPosition: placement?.actionPosition ?? placement?.entryPosition,
    action(key: string, surface: string, scope?: string): string | undefined {
      if (!placement) return undefined;
      if (placement.actionPosition) return placement.actionKey === key ? placement.actionPosition : undefined;
      const staticScope = scope ?? placement.staticScope;
      return points.find(point => point.kind === 'action' && point.parentPosition === placement.entryPosition
        && (point.staticScope ?? undefined) === (staticScope ?? undefined) && point.actionScope === surface && point.key === key)?.position;
    },
    navigation(key: string): string | undefined {
      return metadata?.navDocuments.flatMap(document => document.navigation.items).find(item => item.key === key)?.featurePosition;
    },
    tab(key: string): string | undefined {
      return placement ? points.find(point => point.kind === 'tab' && point.parentPosition === placement.entryPosition
        && (point.staticScope ?? undefined) === placement.staticScope && point.key === key)?.position : undefined;
    },
  };
}
