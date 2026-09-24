import { useRef, useState } from 'react';
import { Alert, Button, Collapse, Select, Space, Spin, Tag, Typography } from 'antd';
import { usePlatformQuery } from '@icp-platform/core';
import type { DataScopeSubject } from './DataScopeSubjectEditor';
import type { ObjectCatalog } from './ObjectAuthorizationInput';

interface Source { kind: string; id: string; name: string; opinion: string; status: string; subjects: DataScopeSubject[] }
interface ActionResult { key: string; title: string; allowed: boolean; reason: string; sources: Source[] }
interface Explanation { accountId: string; accountName: string; actions: ActionResult[] }
interface Directory { items: { id: string; name: string }[]; hasMore: boolean; currentValues: { id: string; name: string }[] }
const reasons: Record<string, string> = { administrator: '管理员豁免', disabled: '该动作的数据权限检查已停用',
  readRequired: '没有读取权限，因此不能执行此动作', object: '由对象授权决定', rule: '由命中规则决定；独立分支拒绝优先', default: '没有明确意见，默认允许' };
const statuses: Record<string, string> = { effective: '参与最终裁决', descendantOverride: '被子规则覆盖', objectOverride: '被对象授权覆盖' };

/** 只消费服务端解释，选择用户不修改授权草稿或当前登录身份。 */
export default function ObjectAuthorizationDetails({ read, catalog }: {
  read: (path: string, signal?: AbortSignal) => Promise<unknown>; catalog: ObjectCatalog;
}) {
  const instance = useRef(crypto.randomUUID());
  const [accountId, setAccountId] = useState<string>();
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const [revision, setRevision] = useState(0);
  const directory = usePlatformQuery({ key: ['object-explanation-users', instance.current, search, page],
    load: ({ signal }) => read(`/subjects/user?${new URLSearchParams({ query: search, page: String(page) })}`, signal) as Promise<Directory> });
  const explanation = usePlatformQuery({ key: ['object-explanation', instance.current, accountId ?? '', revision],
    load: async ({ signal }) => {
      const result = await read(`/explanation${accountId ? `?accountId=${encodeURIComponent(accountId)}` : ''}`, signal) as Explanation;
      const names: Record<string, string> = {};
      const entries = result.actions.flatMap(action => action.sources.flatMap(source => source.subjects));
      // 名称按类型串行批量解析；不向普通业务目录发起逐主体查询。
      for (const type of new Set(entries.filter(entry => entry.subjectId).map(entry => entry.type))) {
        const ids = [...new Set(entries.filter(entry => entry.type === type).map(entry => entry.subjectId!))];
        for (let offset = 0; offset < ids.length; offset += 10) {
          const parameters = new URLSearchParams({ page: '1' });
          ids.slice(offset, offset + 10).forEach(id => parameters.append('currentIds', id));
          const values = await read(`/subjects/${encodeURIComponent(type)}?${parameters}`, signal) as Directory;
          values.currentValues.forEach(value => { names[`${type}:${value.id}`] = value.name; });
        }
      }
      return { ...result, names };
    } });
  const data = explanation.status === 'success' ? explanation.data : undefined;
  const options = [...new Map([{ value: '', label: '当前用户' }, ...(directory.data?.items ?? []).map(user => ({ value: user.id, label: user.name })),
    ...(data ? [{ value: data.accountId, label: data.accountName }] : [])].map(option => [option.value, option])).values()];
  return <>
    <Space wrap style={{ marginBottom: 12 }}>
      <span>分析用户</span><Select aria-label="分析用户" showSearch filterOption={false} value={accountId ?? ''} options={options}
        style={{ width: 220 }} onSearch={value => { setSearch(value); setPage(1); }}
        onChange={value => setAccountId(value || undefined)} loading={directory.isFetching} />
      {(page > 1 || directory.data?.hasMore) && <>
        <Button disabled={page === 1 || directory.isFetching} onClick={() => setPage(page - 1)}>上一页用户</Button>
        <Button disabled={!directory.data?.hasMore || directory.isFetching} onClick={() => setPage(page + 1)}>下一页用户</Button>
      </>}
      <Button onClick={() => setRevision(value => value + 1)}>重新分析</Button>
    </Space>
    <Typography.Paragraph type="secondary">展示已保存配置下的数据权限。页面功能授权和业务校验仍独立生效。新建权限需按新对象内容判定。</Typography.Paragraph>
    {directory.status === 'error' && <Alert type="error" showIcon message="用户目录读取失败。" />}
    {explanation.status === 'error' && <Alert type="error" showIcon message={explanation.error instanceof Error ? explanation.error.message : '权限分析失败。'} />}
    <Spin spinning={explanation.isFetching}>
      {data && <>
        <Typography.Paragraph>分析用户：<strong>{data.accountName}</strong></Typography.Paragraph>
        <Collapse items={data.actions.map(action => ({ key: action.key,
          label: <Space><strong>{action.title}</strong><Tag color={action.allowed ? 'success' : 'error'}>{action.allowed ? '允许' : '拒绝'}</Tag></Space>,
          children: <>
            <Typography.Paragraph>{reasons[action.reason] ?? action.reason}</Typography.Paragraph>
            {action.sources.map(source => <div key={`${source.kind}:${source.id}`} style={{ marginBottom: 16 }}>
              <Space wrap><Tag>{source.kind === 'object' ? '对象授权' : '规则'}</Tag><strong>{source.name}</strong>
                <Tag color={source.opinion === 'deny' ? 'error' : 'success'}>{source.opinion === 'deny' ? '拒绝' : '同意'}</Tag></Space>
              <Typography.Paragraph type="secondary">{statuses[source.status] ?? source.status}</Typography.Paragraph>
              {source.subjects.map(subject => <Typography.Paragraph key={subject.id}>
                {catalog.subjectTypes.find(type => type.key === subject.type)?.title ?? subject.type}
                {subject.subjectId && ` · ${data.names[`${subject.type}:${subject.subjectId}`] ?? subject.subjectId}`}
                {subject.matchOptions?.includeDescendants && '（包含下级）'}
                {` · 优先级 ${subject.priority} · ${subject.actions[action.key] === 'deny' ? '拒绝' : '同意'}`}
              </Typography.Paragraph>)}
            </div>)}
          </>,
        }))} />
      </>}
    </Spin>
  </>;
}
