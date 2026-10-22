import { fetchObjectAuthorization, objectAuthorizationError, objectAuthorizationPath, objectAuthorizationOutcomeUnknown, objectAuthorizationSubmission, objectAuthorizationHeaders } from './objectAuthorizationRequest';
import { lazy, Suspense } from 'react';
import { platformFetch } from '@icp-platform/core';
import type { ObjectAuthorizationTarget } from './ObjectAuthorizationInput';
import { standardCrudInputReadSource, StandardCrudRequestError, type StandardCrudActionContext, StandardCrudActionEligibilityResolver, StandardCrudActionInputRenderContext } from '@icp-platform/sdk';

const Input = lazy(() => import('./ObjectAuthorizationInput'));
const inspectionRequestsOwner = Symbol('object-authorization-inspection-requests');

type TargetContext = Pick<StandardCrudActionContext, 'id' | 'detail' | 'row' | 'queryScope' | 'instance'>;

/** owner 查询沿浏览目录，编辑仍独立核管理资格；generic 入口使用自身正式管理按钮。 */
export function createObjectAuthorizationActions(
  resourceKey: string,
  getTarget: (context: TargetContext) => Omit<ObjectAuthorizationTarget, 'resourceKey'>,
  fetcher: typeof fetch = platformFetch as typeof fetch,
) {
  const targetFor = (context: TargetContext & Pick<StandardCrudActionContext, 'action' | 'dataAccess'>, inspect = false): ObjectAuthorizationTarget => {
    const selected = getTarget(context);
    const id = selected.id ?? context.id;
    const inspectionOperation = context.action?.handlerKey === 'viewAuthorization'
      || (context.action?.dataAccess ?? context.dataAccess)?.businessBinding === `${resourceKey}.viewAuthorization`;
    return { ...selected, resourceKey, id, ...(inspect && inspectionOperation && !selected.basePath ? {
      basePath: `/api/authorization/data-scope/objects/${encodeURIComponent(resourceKey)}/${encodeURIComponent(id ?? '')}/inspection`,
    } : {}) };
  };
  const eligibility = (inspect: boolean): StandardCrudActionEligibilityResolver => async context => {
    const { id, featurePosition, signal } = context;
    const dataAccess = inspect && context.action.navigationTargetKey
      ? standardCrudInputReadSource(context) : context.action.dataAccess ?? context.dataAccess;
    if (inspect && context.action.navigationTargetKey && !dataAccess) throw new Error('INPUT_READ_SOURCE_INVALID');
    if (!id) return { visible: false, available: false };
    const target = targetFor(context, inspect);
    const response = await fetchObjectAuthorization(fetcher, objectAuthorizationPath(target, inspect && target.basePath ? '/catalog' : '/eligibility'),
      { headers: objectAuthorizationHeaders(featurePosition, dataAccess), signal });
    if (response.status === 403 || response.status === 404) return { visible: false, available: false };
    if (!response.ok) throw await objectAuthorizationError(response);
    return { visible: true, available: true };
  };
  const render = Object.assign((context: StandardCrudActionInputRenderContext) =>
    <Suspense fallback={null}><Input {...context} target={targetFor(context)} /></Suspense>, { dialogWidth: 1360 });
  const details = Object.assign((context: StandardCrudActionInputRenderContext) =>
    <Suspense fallback={null}><Input {...context} target={targetFor(context, true)} /></Suspense>, { dialogWidth: 960, readOnly: true });
  return {
    actionInputRenderers: { objectAuthorization: render, objectAuthorizationDetails: details },
    actionEligibilityResolvers: { authorize: eligibility(false), viewAuthorization: eligibility(true) },
    actionHandlers: {
      // 只读Review的关闭不执行handler；直接动作仍有真实GET实现，并随实例结束取消。
      viewAuthorization: async (context: StandardCrudActionContext) => {
        const { id, featurePosition, instance } = context;
        if (!id || !featurePosition || !instance?.active)
          throw new StandardCrudRequestError('对象授权上下文已结束。', 400, {}, { reasonKey: 'DataScope.ObjectNotLoaded' });
        const requests = instance.getOrCreate(inspectionRequestsOwner, () => new Set<AbortController>(), pending => {
          for (const request of pending) request.abort();
          pending.clear();
        });
        const controller = new AbortController(); requests.add(controller);
        try {
          const response = await fetchObjectAuthorization(fetcher, objectAuthorizationPath(targetFor(context, true), '/catalog'),
            { headers: objectAuthorizationHeaders(featurePosition, context.action?.dataAccess ?? context.dataAccess), signal: controller.signal });
          controller.signal.throwIfAborted();
          if (!response.ok) throw await objectAuthorizationError(response);
          const payload: unknown = await response.json();
          controller.signal.throwIfAborted();
          return { operation: 'action' as const, payload, noChange: true as const };
        } finally { requests.delete(controller); }
      },
      authorize: async (context: StandardCrudActionContext) => {
        const { id, input, featurePosition } = context;
        const dataAccess = context.action?.dataAccess ?? context.dataAccess;
        if (!id || !featurePosition || !input || typeof input !== 'object') throw new StandardCrudRequestError('对象授权尚未加载。', 0, {}, { reasonKey: 'DataScope.ObjectNotLoaded' });
        const config = input as { resourceKey: string; objectId: string; subjects: unknown[] };
        const selected = getTarget(context);
        const target = { ...selected, resourceKey, id: selected.id ?? id };
        if (config.resourceKey !== resourceKey || config.objectId !== target.id || !Array.isArray(config.subjects))
          throw new StandardCrudRequestError('对象授权目标已变化，请重新打开。', 0, {}, { reasonKey: 'DataScope.ObjectChanged' });
        const submission = objectAuthorizationSubmission(context.instance, target, featurePosition, dataAccess);
        if (target.basePath && !submission)
          throw new StandardCrudRequestError('对象授权上下文已结束，请重新打开。', 400, {}, { reasonKey: 'DataScope.ObjectNotLoaded' });
        if (submission?.getSnapshot())
          throw new StandardCrudRequestError('原保存结果未知，不能重复提交。', 0, {}, { reasonKey: 'AuthorizationMessages.OperationUnknown' });
        try {
          const response = await fetchObjectAuthorization(fetcher, objectAuthorizationPath(target), {
            method: 'PUT', headers: { 'Content-Type': 'application/json', ...objectAuthorizationHeaders(featurePosition, dataAccess) },
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
