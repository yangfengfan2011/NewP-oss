import type { RouteCapabilityBinding, StandardCrudResourceRegistration } from '@icp-platform/sdk';
import { useParams } from 'react-router-dom';

/** 标准列表宿主只在 capability 命中后动态导入，targetKey 已由绑定闭包固定。 */
export function createStandardListRouteCapabilityBinding(
  targetKey: string,
  registration?: StandardCrudResourceRegistration,
): RouteCapabilityBinding {
  return {
    kind: 'standard-list',
    targetKey,
    load: async () => {
      const { default: StandardListPageHost } = await import('../pages/StandardListPageHost');
      return {
        default: function StandardListRouteCapability() {
          return <StandardListPageHost viewKey={targetKey} registration={registration} />;
        },
      };
    },
  };
}

/** 标准详情 Adapter 在路由树内部读取 :id，调用者仍只提供 kind 与 targetKey。 */
export function createStandardCrudDetailRouteCapabilityBinding(
  targetKey: string,
  registration?: StandardCrudResourceRegistration,
): RouteCapabilityBinding {
  return {
    kind: 'standard-crud-detail',
    targetKey,
    load: async () => {
      const { default: StandardCrudDetailPageHost } = await import('../pages/StandardCrudDetailPageHost');
      return {
        default: function StandardCrudDetailRouteCapability() {
          const params = useParams();
          return (
            <StandardCrudDetailPageHost
              viewKey={targetKey}
              id={params.id ?? null}
              registration={registration}
            />
          );
        },
      };
    },
  };
}
