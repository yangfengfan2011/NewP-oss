import { useEffect, useRef, useState } from 'react';
import { Alert, Button, Collapse, Select, Space, Spin, Tag, Typography } from 'antd';
import { formatRequestError, useLocalization, usePlatformQuery } from '@icp-platform/core';
import type { DataScopeSubject } from './DataScopeSubjectEditor';
import type { ObjectCatalog } from './ObjectAuthorizationInput';
import { resolveDataScopeSubjects, searchDataScopeSubjects, type DataScopeSubjectDirectoryRead } from './dataScopeSubjectDirectory';

interface Source { kind: string; id: string; name: string; opinion: string; status: string; subjects: DataScopeSubject[] }
interface ActionResult { key: string; title: string; allowed: boolean; reason: string; sources: Source[] }
interface Explanation { accountId: string; accountName: string; actions: ActionResult[];
  subjectNames?: { type: string; id: string; name: string | null; status: 'resolved' | 'unavailable' }[] }
const reasons: Record<string, string> = { administrator: '管理员豁免', disabled: '该动作的数据权限检查已停用',
  readRequired: '没有读取权限，因此不能执行此动作', object: '由对象授权决定', rule: '由命中规则决定；独立分支拒绝优先', default: '没有明确意见，默认允许' };
const statuses: Record<string, string> = { effective: '参与最终裁决', descendantOverride: '被子规则覆盖', objectOverride: '被对象授权覆盖' };

/** 只消费服务端解释，选择用户不修改授权草稿或当前登录身份。 */
export default function ObjectAuthorizationDetails({ read, catalog, unavailable = false }: {
  read: DataScopeSubjectDirectoryRead; catalog: ObjectCatalog; unavailable?: boolean;
}) {
  const { t } = useLocalization();
  const instance = useRef(crypto.randomUUID());
  const [accountId, setAccountId] = useState<string>();
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const [revision, setRevision] = useState(0);
  const [expanded, setExpanded] = useState<string[]>([]);
  const directory = usePlatformQuery({ key: ['object-explanation-users', instance.current, search, page],
    load: ({ signal }) => searchDataScopeSubjects(read, { type: 'user', query: search, page }, signal) });
  const explanation = usePlatformQuery({ key: ['object-explanation', instance.current, accountId ?? '', revision],
    load: async ({ signal }) => {
      const result = await read(`/explanation${accountId ? `?accountId=${encodeURIComponent(accountId)}` : ''}`, signal) as Explanation;
      const entries = result.actions.flatMap(action => action.sources.flatMap(source => source.subjects));
      const projectedNames = result.subjectNames;
      const names = projectedNames === undefined ? await resolveDataScopeSubjects(read, entries, signal)
        : new Map([...new Set(projectedNames.map(item => item.type))].map(type => [type,
          new Map(projectedNames.filter(item => item.type === type).map(item => [item.id,
            { id: item.id, name: item.status === 'resolved' && item.name !== null ? item.name : t('DataScope.SubjectUnavailable', '不可见或已不存在') }]))]));
      return { ...result, names };
    } });
  const identity = JSON.stringify([accountId, revision]);
  const [snapshot, setSnapshot] = useState<{ identity: string; data: typeof explanation.data }>();
  useEffect(() => {
    if (explanation.status === 'success') setSnapshot({ identity, data: explanation.data });
    else if (explanation.status === 'error') setSnapshot(undefined);
  }, [explanation.status, explanation.data, identity]);
  // 仅保留同一分析请求的显示与 Collapse 实例；新结果更新名称，拒绝不展示旧裁决。
  const data = unavailable || explanation.status === 'error' ? undefined : explanation.data
    ?? (snapshot?.identity === identity ? snapshot.data : undefined);
  const options = [...new Map([{ value: '', label: t('DataScope.CurrentUser', '当前用户') }, ...(directory.data?.items ?? []).map(user => ({ value: user.id, label: user.name })),
    ...(data ? [{ value: data.accountId, label: data.accountName }] : [])].map(option => [option.value, option])).values()];
  return <>
    <Space wrap style={{ marginBottom: 12 }}>
      <span>{t('DataScope.AnalyzeUser', '分析用户')}</span><Select aria-label={t('DataScope.AnalyzeUser', '分析用户')} disabled={unavailable} showSearch filterOption={false} value={accountId ?? ''} options={options}
        style={{ width: 220 }} onSearch={value => { setSearch(value); setPage(1); }}
        onChange={value => setAccountId(value || undefined)} loading={directory.isFetching} />
      {(page > 1 || directory.data?.hasMore) && <>
        <Button disabled={unavailable || page === 1 || directory.isFetching} onClick={() => setPage(page - 1)}>{t('DataScope.PreviousUsers', '上一页用户')}</Button>
        <Button disabled={unavailable || !directory.data?.hasMore || directory.isFetching} onClick={() => setPage(page + 1)}>{t('DataScope.NextUsers', '下一页用户')}</Button>
      </>}
      <Button disabled={unavailable} onClick={() => setRevision(value => value + 1)}>{t('DataScope.Reanalyze', '重新分析')}</Button>
    </Space>
    <Typography.Paragraph type="secondary">{t('DataScope.ExplanationHelp', '展示已保存配置下的数据权限。页面功能授权和业务校验仍独立生效。新建权限需按新对象内容判定。')}</Typography.Paragraph>
    {directory.status === 'error' && <Alert type="error" showIcon message={t('DataScope.DirectoryReadFailed', '用户目录读取失败。')} />}
    {explanation.status === 'error' && <Alert type="error" showIcon message={formatRequestError(explanation.error, t)} />}
    <Spin spinning={explanation.isFetching}>
      {data && <>
        <Typography.Paragraph>{t('DataScope.AnalyzeUser', '分析用户')}：<strong>{data.accountName}</strong></Typography.Paragraph>
        <Collapse activeKey={expanded} onChange={keys => setExpanded(typeof keys === 'string' ? [keys] : keys)} items={data.actions.map(action => ({ key: action.key,
          label: <Space><strong>{t(`DataScope.Action_${action.key}`, action.title)}</strong><Tag color={action.allowed ? 'success' : 'error'}>{action.allowed ? t('DataScope.Allowed', '允许') : t('DataScope.Deny', '拒绝')}</Tag></Space>,
          children: <>
            <Typography.Paragraph>{t(`DataScope.Reason_${action.reason}`, reasons[action.reason] ?? action.reason)}</Typography.Paragraph>
            {action.sources.map(source => <div key={`${source.kind}:${source.id}`} style={{ marginBottom: 16 }}>
              <Space wrap><Tag>{source.kind === 'object' ? t('DataScope.ObjectAuthorization', '对象授权') : t('DataScope.Rule', '规则')}</Tag><strong>{source.kind === 'object' ? t('DataScope.ObjectAuthorization', '对象授权') : source.name}</strong>
                <Tag color={source.opinion === 'deny' ? 'error' : 'success'}>{source.opinion === 'deny' ? t('DataScope.Deny', '拒绝') : t('DataScope.Allow', '同意')}</Tag></Space>
              <Typography.Paragraph type="secondary">{t(`DataScope.Status_${source.status}`, statuses[source.status] ?? source.status)}</Typography.Paragraph>
              {source.subjects.map(subject => <Typography.Paragraph key={subject.id}>
                {t(`DataScope.SubjectType_${subject.type}`, catalog.subjectTypes.find(type => type.key === subject.type)?.title ?? subject.type)}
                {subject.subjectId && ` · ${data.names.get(subject.type)?.get(subject.subjectId)?.name ?? subject.subjectId}`}
                {subject.matchOptions?.includeDescendants && t('DataScope.DescendantsSuffix', '（包含下级）')}
                {t('DataScope.PriorityValue', ' · 优先级 {priority} · {opinion}', { priority: subject.priority, opinion: subject.actions[action.key] === 'deny' ? t('DataScope.Deny', '拒绝') : t('DataScope.Allow', '同意') })}
              </Typography.Paragraph>)}
            </div>)}
          </>,
        }))} />
      </>}
    </Spin>
  </>;
}
