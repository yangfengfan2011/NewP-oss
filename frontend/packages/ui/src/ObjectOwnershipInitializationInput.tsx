import { useEffect, useState } from 'react';
import { Alert, Button, Radio, Select, Space, Table } from 'antd';
import { formatRequestError, platformFetch, useLocalization } from '@icp-platform/core';
import { readStandardCrudRequestError, type StandardCrudActionInputRenderContext } from '@icp-platform/sdk';
import { ownershipInitializationHeaders, ownershipInitializationSource, type ObjectOwnershipInitializationTarget, type OwnershipInitializationDraft } from './ownershipInitializationRequest';
export type { ObjectOwnershipInitializationTarget, OwnershipInitializationDraft } from './ownershipInitializationRequest';

interface Candidate { accountId: string; code: string; name: string; departmentId: string | null }
interface PendingItem { id: string; name: string }

/** 拥有者提供真实端点与上下文；共享输入管理显式批次、人工单选、候选和可取消请求。 */
export default function ObjectOwnershipInitializationInput({ target, featurePosition, action, dataAccess, value, onChange: updateDraft, onDraftStateChange, submitting }: StandardCrudActionInputRenderContext & { target: ObjectOwnershipInitializationTarget }) {
  const { t } = useLocalization();
  const binding = action.dataAccess ?? dataAccess;
  const source = ownershipInitializationSource(target, featurePosition, binding);
  const saved = typeof value === 'object' && value !== null ? value as Partial<OwnershipInitializationDraft> : {};
  const draft = saved.source === source ? saved : {};
  const onChange = (next: Omit<OwnershipInitializationDraft, 'source'>) => updateDraft({ ...next, source });
  const selectedId = target.initialId ?? draft.targetId ?? '';
  const [missing, setMissing] = useState<boolean | null>(null);
  const [failure, setFailure] = useState<{ error: unknown } | null>(null);
  const [manual, setManual] = useState(false);
  const [query, setQuery] = useState('');
  const [candidates, setCandidates] = useState<Candidate[]>([]);
  const [pending, setPending] = useState<{ items: PendingItem[]; total: number } | null>(null);
  const [page, setPage] = useState(1);
  useEffect(() => {
    if (selectedId) return;
    const controller = new AbortController(); setFailure(null); setPending(null);
    void (async () => {
      try {
        const response = await platformFetch(`${target.pendingPath}${target.pendingPath.includes('?') ? '&' : '?'}page=${page}&pageSize=20`, {
          method: 'GET', signal: controller.signal,
          headers: ownershipInitializationHeaders(featurePosition, binding),
        });
        if (!response.ok) throw await readStandardCrudRequestError(response);
        const loaded = await response.json() as { items: PendingItem[]; total: number };
        if (!controller.signal.aborted) setPending(loaded);
      } catch (error) { if (!controller.signal.aborted) setFailure({ error }); }
    })();
    return () => controller.abort();
  }, [selectedId, target.pendingPath, page, featurePosition, binding]);
  useEffect(() => {
    if (!selectedId) { setMissing(null); return; }
    const controller = new AbortController(); setMissing(null); setFailure(null);
    void (async () => {
      try {
        const response = await platformFetch(target.statusPath.replace('{id}', encodeURIComponent(selectedId)), {
          method: 'GET', signal: controller.signal, headers: ownershipInitializationHeaders(featurePosition, binding),
        });
        if (!response.ok) throw await readStandardCrudRequestError(response);
        const status = await response.json() as { missing: boolean };
        if (!controller.signal.aborted) setMissing(status.missing);
      } catch (error) { if (!controller.signal.aborted) setFailure({ error }); }
    })();
    return () => controller.abort();
  }, [selectedId, target.statusPath, featurePosition, binding]);
  useEffect(() => {
    if (!manual || !missing) return;
    const controller = new AbortController();
    void (async () => {
      try {
        const response = await platformFetch(`${target.accountsPath}?query=${encodeURIComponent(query)}`, {
          signal: controller.signal, headers: ownershipInitializationHeaders(featurePosition, binding),
        });
        if (!response.ok) throw await readStandardCrudRequestError(response);
        const items = await response.json() as Candidate[];
        if (!controller.signal.aborted) { setCandidates(items); setFailure(null); }
      } catch (error) { if (!controller.signal.aborted) setFailure({ error }); }
    })();
    return () => controller.abort();
  }, [manual, missing, query, target.accountsPath, featurePosition, binding]);
  const ready = selectedId ? missing === true && (!manual || !!draft.accountId) : (draft.targetIds?.length ?? 0) > 0;
  useEffect(() => { onDraftStateChange?.({ dirty: ready, ready: ready && !submitting && !failure }); }, [ready, submitting, failure, onDraftStateChange]);
  // 初始行身份也写入草稿，提交handler随后核对所属页面的固定目标，不接受换对象重用。
  useEffect(() => { if (target.initialId && draft.targetId !== target.initialId) updateDraft({ targetId: target.initialId, source }); }, [target.initialId, draft.targetId, updateDraft, source]);
  return <Space direction="vertical" style={{ width: '100%' }}>
    <Alert type="info" message={t('Platform.OwnershipBackfillExplanation', '仅补齐缺失归属；默认使用创建账号及其当前部门，不代表创建时的历史部门。此操作仅供管理员维护。')} />
    {failure && <div role="alert">{formatRequestError(failure.error, t)}</div>}
    {!selectedId && <><Alert type="info" message={t('Platform.OwnershipAutomaticBatch', '选择对象后确认，按创建账号及其当前部门自动补齐；无法解析的对象继续保留在本列表。')} />
      <Table<PendingItem> size="small" rowKey="id" loading={pending === null && !failure} dataSource={pending?.items ?? []}
      columns={[{ title: t('Platform.OwnershipPendingObjects', '待补齐对象'), dataIndex: 'name' },
        { title: t('Platform.OwnershipManualInitialization', '人工补齐'), render: (_, item) => <Button type="link" disabled={submitting}
          onClick={() => { setManual(true); onChange({ targetId: item.id }); }}>{t('Platform.OwnershipChooseAccount', '人工指定账号')}</Button> }]}
      pagination={{ current: page, pageSize: 20, total: pending?.total ?? 0, showSizeChanger: false, onChange: setPage, disabled: submitting }}
      rowSelection={{ selectedRowKeys: draft.targetIds ?? [], getCheckboxProps: () => ({ disabled: submitting }), onChange: keys => onChange({ targetIds: keys.map(String) }) }} />
      <Button disabled={submitting || !pending?.items.length} onClick={() => onChange({ targetIds: pending?.items.map(item => item.id) ?? [] })}>
        {t('Platform.OwnershipSelectCurrentPage', '选择当前许可页')}</Button></>}
    {selectedId && !target.initialId && <Button disabled={submitting} onClick={() => { onChange({}); setManual(false); }}>{t('Platform.OwnershipChooseAnotherObject', '重新选择待补齐对象')}</Button>}
    {selectedId && missing === null && !failure && <div role="status">{t('Platform.Loading', '加载中…')}</div>}
    {missing === false && <Alert type="success" message={t('Platform.OwnershipComplete', '对象归属已完整，无需补齐。')} />}
    {missing && <>
      <Radio.Group value={manual} disabled={submitting} onChange={event => { setManual(event.target.value as boolean); onChange({ targetId: selectedId }); }}>
        <Radio value={false}>{t('Platform.OwnershipUseCreator', '按创建账号补齐')}</Radio>
        <Radio value>{t('Platform.OwnershipChooseAccount', '人工指定账号')}</Radio>
      </Radio.Group>
      {manual && <Select showSearch filterOption={false} style={{ width: '100%' }} value={draft.accountId} disabled={submitting}
        aria-label={t('Platform.OwnershipOwner', '所属人')} placeholder={t('Platform.OwnershipSearchAccount', '搜索账号或名称')}
        onSearch={setQuery} onChange={accountId => onChange({ targetId: selectedId, accountId })}
        options={candidates.map(candidate => ({ value: candidate.accountId, disabled: !candidate.departmentId,
          label: `${candidate.code} · ${candidate.name}${candidate.departmentId ? '' : ` (${t('Platform.OwnershipDepartmentMissing', '缺少部门')})`}` }))} />}
    </>}
  </Space>;
}
