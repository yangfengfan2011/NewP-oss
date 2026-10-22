import { lazy, Suspense } from 'react';
import { platformFetch } from '@icp-platform/core';
import { readStandardCrudRequestError, StandardCrudRequestError, type StandardCrudActionContext, type StandardCrudActionInputRenderContext,
  type StandardCrudActionEligibilityResolver } from '@icp-platform/sdk';
import { ownershipTransferSource, ownershipTransferSubmission, ownershipTransferOutcomeUnknown, ownershipTransferHeaders, type ObjectOwnershipTransferTarget, type OwnershipTransferDraft } from './ownershipTransferRequest';

const Input = lazy(() => import('./ObjectOwnershipTransferInput'));
type TargetContext = Pick<StandardCrudActionContext, 'id' | 'row' | 'detail' | 'selectedRows' | 'queryScope' | 'instance'>;

/** 模块固定来源及真实端点，共享动作只发送接收账号，不接受草稿里的归属字段或判权断言。 */
export function createObjectOwnershipTransferActions(getTarget: (context: TargetContext) => ObjectOwnershipTransferTarget,
  fetcher: typeof fetch = platformFetch as typeof fetch) {
  const eligibility: StandardCrudActionEligibilityResolver = async context => {
    // 页级批次的选择只在点击时成为快照；输入随后对完整选择查询，服务端提交仍独立重验。
    if (!context.id) return { visible: true, available: true };
    const target = getTarget(context);
    const response = await fetcher(target.eligibilityPath, { method: 'GET', signal: context.signal,
      headers: ownershipTransferHeaders(context.featurePosition, context.action.dataAccess ?? context.dataAccess) });
    if ([403, 404, 503].includes(response.status)) return { visible: false, available: false };
    if (!response.ok) throw await readStandardCrudRequestError(response);
    const result = await response.json() as { allowed: boolean };
    return { visible: result.allowed === true, available: result.allowed === true };
  };
  return {
    actionEligibilityResolvers: { transfer: eligibility },
    actionInputRenderers: { transferOwnership: (context: StandardCrudActionInputRenderContext) =>
      <Suspense fallback={null}><Input {...context} target={getTarget(context)} /></Suspense> },
    actionHandlers: { transfer: Object.assign(async (context: StandardCrudActionContext) => {
      const target = getTarget(context);
      const dataAccess = context.action?.dataAccess ?? context.dataAccess;
      const source = ownershipTransferSource(target, context.featurePosition, context.instance, dataAccess);
      const draft = context.input as OwnershipTransferDraft | undefined;
      if (!context.featurePosition || !context.instance?.active || !draft?.recipientId || draft.source !== source)
        throw new StandardCrudRequestError('转交对象或来源已变化，请重新选择。', 409, {}, { reasonKey: 'Platform.TransferSourceChanged' });
      if (ownershipTransferSubmission(context.instance, source)?.getSnapshot())
        throw new StandardCrudRequestError('转交结果未知，不能重复提交。', 0, {}, { code: 'Platform.MutationOutcomeUnknown', reasonKey: 'Platform.TransferOutcomeUnknown' });
      let receipt: { succeeded?: boolean; changed?: number };
      try {
        const response = await fetcher(target.savePath, { method: 'POST', headers: { 'Content-Type': 'application/json', ...ownershipTransferHeaders(context.featurePosition, dataAccess) },
          body: JSON.stringify({ ...target.body, recipientId: draft.recipientId }) });
        if (!response.ok) throw await readStandardCrudRequestError(response);
        receipt = await response.json() as { succeeded?: boolean; changed?: number };
        if (receipt.succeeded !== true || !Number.isSafeInteger(receipt.changed) || receipt.changed! < 0)
          throw new StandardCrudRequestError('转交回执无法确认。', 0, {}, { code: 'Platform.MutationOutcomeUnknown', reasonKey: 'Platform.TransferOutcomeUnknown' });
      } catch (failure) {
        if (ownershipTransferOutcomeUnknown(failure)) { ownershipTransferSubmission(context.instance, source)?.freeze(); context.instance.changed(); }
        throw failure;
      }
      return { operation: 'action' as const, payload: receipt,
        ...(receipt.changed ? { change: { operation: 'update' as const, id: '' } } : { noChange: true as const }),
        notice: { reasonKey: receipt.changed ? 'Platform.OwnershipTransferred' : 'Platform.OwnershipTransferUnchanged' } };
    }, { isCorrectableInputError: (failure: unknown) => failure instanceof StandardCrudRequestError && failure.code === 'EntityOwnership.RecipientUnavailable' }) },
  };
}
