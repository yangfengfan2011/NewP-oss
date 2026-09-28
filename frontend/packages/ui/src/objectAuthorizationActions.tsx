import { fetchObjectAuthorization, objectAuthorizationError } from './objectAuthorizationRequest';
import { lazy, Suspense } from 'react';
import { platformFetch } from '@icp-platform/core';
import type { ObjectAuthorizationTarget } from './ObjectAuthorizationInput';
import { StandardCrudRequestError, type StandardCrudActionContext, StandardCrudActionEligibilityResolver, StandardCrudActionInputRenderContext } from '@icp-platform/sdk';

const Input = lazy(() => import('./ObjectAuthorizationInput'));

/** 两个独立功能位置共用对象管理资格；查看动作关闭弹窗时不提交配置。 */
export function createObjectAuthorizationActions(
  resourceKey: string,
  getTarget: (context: StandardCrudActionInputRenderContext) => Omit<ObjectAuthorizationTarget, 'resourceKey'>,
  fetcher: typeof fetch = platformFetch as typeof fetch,
) {
  const eligibility: StandardCrudActionEligibilityResolver = async ({ id, featurePosition, signal }) => {
    if (!id) return { visible: false, available: false };
    const response = await fetchObjectAuthorization(fetcher, `/api/authorization/data-scope/objects/${encodeURIComponent(resourceKey)}/${encodeURIComponent(id)}/eligibility`,
      { headers: { 'X-NewP-Feature': featurePosition }, signal });
    if (response.status === 403 || response.status === 404) return { visible: false, available: false };
    if (!response.ok) throw await objectAuthorizationError(response);
    return { visible: true, available: true };
  };
  const render = Object.assign((context: StandardCrudActionInputRenderContext) =>
    <Suspense fallback={null}><Input {...context} target={{ ...getTarget(context), resourceKey }} /></Suspense>, { dialogWidth: 960 });
  const details = Object.assign((context: StandardCrudActionInputRenderContext) => render(context), { dialogWidth: 960, readOnly: true });
  return {
    actionInputRenderers: { objectAuthorization: render, objectAuthorizationDetails: details },
    actionEligibilityResolvers: { authorize: eligibility, viewAuthorization: eligibility },
    actionHandlers: {
      authorize: async ({ id, input, featurePosition }: StandardCrudActionContext) => {
        if (!id || !featurePosition || !input || typeof input !== 'object') throw new StandardCrudRequestError('对象授权尚未加载。', 0, {}, { reasonKey: 'DataScope.ObjectNotLoaded' });
        const config = input as { resourceKey: string; objectId: string; subjects: unknown[] };
        if (config.resourceKey !== resourceKey || config.objectId !== id || !Array.isArray(config.subjects))
          throw new StandardCrudRequestError('对象授权目标已变化，请重新打开。', 0, {}, { reasonKey: 'DataScope.ObjectChanged' });
        const response = await fetchObjectAuthorization(fetcher, `/api/authorization/data-scope/objects/${encodeURIComponent(resourceKey)}/${encodeURIComponent(id)}`, {
          method: 'PUT', headers: { 'Content-Type': 'application/json', 'X-NewP-Feature': featurePosition },
          body: JSON.stringify(config),
        });
        if (!response.ok) throw await objectAuthorizationError(response);
      },
    },
  };
}
