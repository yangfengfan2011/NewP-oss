import { fetchObjectAuthorization, objectAuthorizationError, objectAuthorizationPath, objectAuthorizationOutcomeUnknown, objectAuthorizationSubmission } from './objectAuthorizationRequest';
import { lazy, Suspense } from 'react';
import { platformFetch } from '@icp-platform/core';
import type { ObjectAuthorizationTarget } from './ObjectAuthorizationInput';
import { StandardCrudRequestError, type StandardCrudActionContext, StandardCrudActionEligibilityResolver, StandardCrudActionInputRenderContext } from '@icp-platform/sdk';

const Input = lazy(() => import('./ObjectAuthorizationInput'));

type TargetContext = Pick<StandardCrudActionContext, 'id' | 'detail' | 'row' | 'queryScope' | 'instance'>;

/** owner 查询沿浏览目录，编辑仍独立核管理资格；旧 generic 入口保留既有管理合同。 */
export function createObjectAuthorizationActions(
  resourceKey: string,
  getTarget: (context: TargetContext) => Omit<ObjectAuthorizationTarget, 'resourceKey'>,
  fetcher: typeof fetch = platformFetch as typeof fetch,
) {
  const eligibility = (inspect: boolean): StandardCrudActionEligibilityResolver => async context => {
    const { id, featurePosition, signal } = context;
    if (!id) return { visible: false, available: false };
    const selected = getTarget(context);
    const target = { ...selected, resourceKey, id: selected.id ?? id };
    const response = await fetchObjectAuthorization(fetcher, objectAuthorizationPath(target, inspect && target.basePath ? '/catalog' : '/eligibility'),
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
    actionEligibilityResolvers: { authorize: eligibility(false), viewAuthorization: eligibility(true) },
    actionHandlers: {
      authorize: async (context: StandardCrudActionContext) => {
        const { id, input, featurePosition } = context;
        if (!id || !featurePosition || !input || typeof input !== 'object') throw new StandardCrudRequestError('对象授权尚未加载。', 0, {}, { reasonKey: 'DataScope.ObjectNotLoaded' });
        const config = input as { resourceKey: string; objectId: string; subjects: unknown[] };
        const selected = getTarget(context);
        const target = { ...selected, resourceKey, id: selected.id ?? id };
        if (config.resourceKey !== resourceKey || config.objectId !== target.id || !Array.isArray(config.subjects))
          throw new StandardCrudRequestError('对象授权目标已变化，请重新打开。', 0, {}, { reasonKey: 'DataScope.ObjectChanged' });
        const submission = objectAuthorizationSubmission(context.instance, target, featurePosition);
        if (target.basePath && !submission)
          throw new StandardCrudRequestError('对象授权上下文已结束，请重新打开。', 400, {}, { reasonKey: 'DataScope.ObjectNotLoaded' });
        if (submission?.getSnapshot())
          throw new StandardCrudRequestError('原保存结果未知，不能重复提交。', 0, {}, { reasonKey: 'AuthorizationMessages.OperationUnknown' });
        try {
          const response = await fetchObjectAuthorization(fetcher, objectAuthorizationPath(target), {
            method: 'PUT', headers: { 'Content-Type': 'application/json', 'X-NewP-Feature': featurePosition },
            body: JSON.stringify(config),
          });
          if (!response.ok) throw await objectAuthorizationError(response);
          if (response.status !== 204)
            throw new StandardCrudRequestError('保存回执不符合对象授权合同。', 0, {}, { reasonKey: 'AuthorizationMessages.OperationUnknown' });
        } catch (failure) {
          if (objectAuthorizationOutcomeUnknown(failure)) { submission?.freeze(); context.instance?.changed(); }
          throw failure;
        }
      },
    },
  };
}
