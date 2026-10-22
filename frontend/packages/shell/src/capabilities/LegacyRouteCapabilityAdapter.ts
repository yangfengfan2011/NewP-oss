import type {
  RouteCapabilityBinding,
  RouteCapabilityRef,
} from '@icp-platform/sdk';
import type { ComponentType } from 'react';

/**
 * 迁移期把已在轻量入口中出现的 eager component 包装到统一 loader seam；
 * 它只保证行为兼容，不代表实现字节已经离开启动静态图。
 */
export function createLegacyRouteCapabilityBinding(
  ref: RouteCapabilityRef,
  component: ComponentType,
): RouteCapabilityBinding {
  return {
    ...ref,
    load: async () => ({ default: component }),
  };
}
