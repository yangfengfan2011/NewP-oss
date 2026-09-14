import { createContext, useContext, useMemo } from 'react';
import { useAuthorization } from '@icp-platform/core';
import { useRuntimeMetadata } from '../metadata/runtimeMetadataContext';

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
  return {
    placement, points, hasFeature,
    readPosition: placement?.actionPosition ?? placement?.entryPosition,
    action(key: string, surface: string): string | undefined {
      if (!placement) return undefined;
      if (placement.actionPosition) return placement.actionKey === key ? placement.actionPosition : undefined;
      return points.find(point => point.kind === 'action' && point.parentPosition === placement.entryPosition
        && (point.staticScope ?? undefined) === placement.staticScope && point.actionScope === surface && point.key === key)?.position;
    },
    tab(key: string): string | undefined {
      return placement ? points.find(point => point.kind === 'tab' && point.parentPosition === placement.entryPosition
        && (point.staticScope ?? undefined) === placement.staticScope && point.key === key)?.position : undefined;
    },
  };
}
