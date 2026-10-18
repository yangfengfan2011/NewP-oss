import { useEffect, useState, useSyncExternalStore } from 'react';
import { ownershipTransferOutcomeUnknown, ownershipTransferSubmission, ownershipTransferSource, type ObjectOwnershipTransferTarget, type OwnershipTransferDraft } from './ownershipTransferRequest';
import { Alert, Select, Space } from 'antd';
import { formatRequestError, platformFetch, useLocalization } from '@icp-platform/core';
import { readStandardCrudRequestError, StandardCrudRequestError, type StandardCrudActionInputRenderContext } from '@icp-platform/sdk';

export { ownershipTransferSource } from './ownershipTransferRequest';
export type { ObjectOwnershipTransferTarget, OwnershipTransferDraft } from './ownershipTransferRequest';
interface Candidate { accountId: string; code: string; name: string; departmentId: string | null }
const noSubscription = () => () => {};
const noUnknown = () => false;

/** 标准动作宿主持有草稿和提交；本输入只读取当前资格与最小账号候选，不推断接收者 read。 */
export default function ObjectOwnershipTransferInput(context: StandardCrudActionInputRenderContext & { target: ObjectOwnershipTransferTarget }) {
  const source = ownershipTransferSource(context.target, context.featurePosition, context.instance);
  return <TransferSession key={source} {...context} source={source} />;
}

function TransferSession({ target, source, featurePosition, value, onChange, onDraftStateChange, submitting, instance, submissionFailure }:
  StandardCrudActionInputRenderContext & { target: ObjectOwnershipTransferTarget; source: string }) {
  const { t } = useLocalization();
  const submission = ownershipTransferSubmission(instance, source);
  const unknown = ownershipTransferOutcomeUnknown(submissionFailure);
  const accessDenied = submissionFailure instanceof StandardCrudRequestError && [401, 403, 404].includes(submissionFailure.status);
  const frozen = useSyncExternalStore(submission?.subscribe ?? noSubscription, submission?.getSnapshot ?? noUnknown) || unknown;
  useEffect(() => { if (unknown) submission?.freeze(); }, [unknown, submission]);
  const [query, setQuery] = useState('');
  const [allowed, setAllowed] = useState(false);
  const [loading, setLoading] = useState(true);
  const [searching, setSearching] = useState(false);
  const [candidates, setCandidates] = useState<Candidate[]>([]);
  const [failure, setFailure] = useState<unknown>();
  const draft = value as OwnershipTransferDraft | undefined;
  const recipientId = draft?.source === source ? draft.recipientId : undefined;
  useEffect(() => {
    if (!featurePosition || !instance?.active) { setLoading(false); return; }
    const controller = new AbortController();
    // 搜索不撤销本来源已核资格，避免Select禁用关闭时清空查询；提交仍等待searching完成。
    setSearching(true); setFailure(undefined); setCandidates([]);
    void (async () => {
      try {
        const headers = { 'X-NewP-Feature': featurePosition };
        const eligible = await platformFetch(target.eligibilityPath, { method: 'GET', signal: controller.signal, headers });
        if (!eligible.ok) throw await readStandardCrudRequestError(eligible);
        const eligibility = await eligible.json() as { allowed: boolean };
        if (!eligibility.allowed) { if (!controller.signal.aborted && instance.active) setAllowed(false); return; }
        const url = new URL(target.accountsPath, window.location.origin); url.searchParams.set('query', query);
        const response = await platformFetch(url.pathname + url.search, { method: 'GET', signal: controller.signal, headers });
        if (!response.ok) throw await readStandardCrudRequestError(response);
        const items = await response.json() as Candidate[];
        if (!controller.signal.aborted && instance.active) { setCandidates(items); setAllowed(true); }
      } catch (error) { if (!controller.signal.aborted && instance.active) { setAllowed(false); setFailure(error); } }
      finally { if (!controller.signal.aborted && instance.active) { setLoading(false); setSearching(false); } }
    })();
    return () => controller.abort();
  }, [target.eligibilityPath, target.accountsPath, query, featurePosition, instance]);
  useEffect(() => {
    onDraftStateChange?.({ dirty: !!recipientId, ready: !!recipientId && allowed && !loading && !searching && !failure && !submitting && !frozen && !accessDenied && !!instance?.active });
  }, [recipientId, allowed, loading, searching, failure, submitting, frozen, accessDenied, instance, onDraftStateChange]);
  return <Space direction="vertical" style={{ width: '100%' }}>
    <Alert type="info" message={t('Platform.TransferExplanation', '转交会改变所属人及所属部门，创建人和手工授权保持不变；接收后是否可读仍按正常权限决定。')} />
    {frozen && <Alert type="warning" message={t('Platform.TransferOutcomeUnknown', '转交结果未知，不能重复提交。请核对当前对象归属及通知。')} />}
    {!!failure && <div role="alert">{formatRequestError(failure, t)}</div>}
    <label>{t('Platform.TransferRecipient', '接收人')}
      <Select style={{ width: '100%' }} showSearch filterOption={false} loading={loading || searching} value={recipientId}
        disabled={!!submitting || frozen || accessDenied || !allowed || loading || !instance?.active}
        onSearch={setQuery} onChange={(accountId: string) => onChange({ recipientId: accountId, source })}
        options={candidates.map(candidate => ({ value: candidate.accountId, label: `${candidate.name} (${candidate.code})` }))} />
    </label>
  </Space>;
}
