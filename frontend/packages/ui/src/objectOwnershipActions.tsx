import { lazy, Suspense } from 'react';
import { platformFetch } from '@icp-platform/core';
import { StandardCrudRequestError, readStandardCrudRequestError, type StandardCrudActionContext, type StandardCrudActionInputRenderContext, type StandardCrudActionEligibilityResolver } from '@icp-platform/sdk';
import { ownershipInitializationHeaders, ownershipInitializationSource, type ObjectOwnershipInitializationTarget, type OwnershipInitializationDraft } from './ownershipInitializationRequest';

const Input = lazy(() => import('./ObjectOwnershipInitializationInput'));

/** 真实owner只提供端点和目标解析，共享动作保持NULL-only补齐、在途输入及结果回执。 */
export function createObjectOwnershipActions(getTarget: (context: StandardCrudActionContext) => ObjectOwnershipInitializationTarget, eligibilityPath: string) {
  const eligibility: StandardCrudActionEligibilityResolver = async context => {
    const response = await platformFetch(eligibilityPath, {
      signal: context.signal, headers: ownershipInitializationHeaders(context.featurePosition),
    });
    if (response.status === 403) return { visible: false, available: false };
    if (!response.ok) throw await readStandardCrudRequestError(response);
    return { visible: true, available: true };
  };
  return {
    actionEligibilityResolvers: { initializeOwnership: eligibility, initializeOwnershipPending: eligibility },
    actionInputRenderers: { initializeOwnership: (context: StandardCrudActionInputRenderContext) => {
      const target = getTarget(context);
      return <Suspense fallback={null}><Input key={ownershipInitializationSource(target, context.featurePosition, context.action.dataAccess ?? context.dataAccess, context.action.inputReadSourceKey)} {...context} target={target} /></Suspense>;
    } },
    actionHandlers: {
      initializeOwnership: Object.assign(async (context: StandardCrudActionContext) => {
        const target = getTarget(context);
        const binding = context.action?.dataAccess ?? context.dataAccess;
        const input = context.input as OwnershipInitializationDraft | undefined;
        const batch = !target.initialId && (input?.targetIds?.length ?? 0) > 0;
        if (!input || input.source !== ownershipInitializationSource(target, context.featurePosition, binding, context.action?.inputReadSourceKey)
          || !batch && (!input.targetId || target.initialId && target.initialId !== input.targetId))
          throw new StandardCrudRequestError('归属补齐对象已变化，请重新选择。', 409, {}, { reasonKey: 'Platform.OwnershipTargetChanged' });
        const response = await platformFetch(batch ? target.batchPath : target.savePath.replace('{id}', encodeURIComponent(input.targetId!)), {
          method: 'POST', headers: { 'Content-Type': 'application/json', ...ownershipInitializationHeaders(context.featurePosition, binding) },
          body: JSON.stringify({ ...target.body, ...(batch ? { [target.batchIdField ?? 'ids']: input.targetIds } : { accountId: input.accountId ?? null }) }),
        });
        if (!response.ok) throw await readStandardCrudRequestError(response);
        const payload = await response.json() as { changed: boolean | number; unresolved?: number };
        return { operation: 'action' as const, payload, ...(payload.changed ? { change: { operation: 'update' as const, id: '' } } : { noChange: true as const }),
          notice: { reasonKey: batch ? 'Platform.OwnershipBatchCompleted' : payload.changed ? 'Platform.OwnershipBackfilled' : 'Platform.OwnershipComplete',
            ...(batch ? { args: { changed: payload.changed, unresolved: payload.unresolved ?? 0 } } : {}) } };
      }, { isCorrectableInputError: (failure: unknown) => failure instanceof StandardCrudRequestError && failure.code === 'EntityOwnership.AccountUnresolved' }),
    },
  };
}
