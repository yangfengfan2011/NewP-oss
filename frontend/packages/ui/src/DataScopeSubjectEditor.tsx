import { useEffect, useState } from 'react';
import { Alert, Button, Form, InputNumber, Select, Space, Switch, Typography } from 'antd';

export interface DataScopeSubject { id: string; type: string; subjectId: string | null; priority: number;
  actions: Record<string, string>; matchOptions?: { includeDescendants: boolean } }
export interface SubjectType { key: string; title: string; requiresId: boolean; supportsDescendants?: boolean }
export interface DataScopeAction { key: string; title: string; enabled: boolean }
interface SubjectName { id: string; name: string; parentId?: string; path?: { type: string; id: string; name: string }[] | null }
interface DirectoryPage { items: SubjectName[]; hasMore: boolean; currentValues: SubjectName[] }

/** 主体候选独立于业务页面读取；切换类型取消旧请求，已选名称由同一最小目录解析。 */
export default function DataScopeSubjectEditor({ subject, index, types, actions, read, onChange, onRemove, showPriority = true }: {
  subject: DataScopeSubject; index: number; types: SubjectType[];
  actions?: DataScopeAction[];
  showPriority?: boolean;
  read: (path: string, signal?: AbortSignal) => Promise<unknown>;
  onChange: (change: Partial<DataScopeSubject>) => void; onRemove?: () => void;
}) {
  const [query, setQuery] = useState('');
  const [page, setPage] = useState(1);
  const [directory, setDirectory] = useState<DirectoryPage>({ items: [], hasMore: false, currentValues: [] });
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const type = types.find(item => item.key === subject.type);
  useEffect(() => {
    if (!type?.requiresId) return;
    const controller = new AbortController();
    setLoading(true); setError('');
    const parameters = new URLSearchParams({ query, page: String(page) });
    if (subject.subjectId) parameters.append('currentIds', subject.subjectId);
    void read(`/subjects/${encodeURIComponent(subject.type)}?${parameters}`, controller.signal).then(result => {
      if (!controller.signal.aborted) setDirectory(result as DirectoryPage);
    }).catch(failure => { if (!controller.signal.aborted) setError(failure instanceof Error ? failure.message : '主体目录读取失败'); })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [read, subject.type, subject.subjectId, type?.requiresId, query, page]);
  const options = [...new Map([...directory.items, ...directory.currentValues].map(item => [item.id, item])).values()];
  return <div role="group" aria-label={`主体授权项 ${index + 1}`} style={{ borderBottom: '1px solid var(--ant-color-border-secondary)', marginBottom: 12 }}>
    {error && <Alert type="error" showIcon message={error} />}
    <Space wrap align="start">
      <Form.Item label="主体类型"><Select aria-label="主体类型" style={{ width: 150 }} value={subject.type}
        options={types.map(item => ({ value: item.key, label: item.title }))} onChange={value => {
          setQuery(''); setPage(1); setDirectory({ items: [], currentValues: [], hasMore: false });
          onChange({ type: value, subjectId: null, matchOptions: undefined });
        }} /></Form.Item>
      {type?.requiresId && <Form.Item label="主体选择">
        <Select aria-label="主体选择" style={{ minWidth: 220, maxWidth: '100%' }} showSearch filterOption={false} loading={loading}
          value={subject.subjectId} options={options.map(item => ({ value: item.id, label: [...(item.path?.map(segment => segment.name) ?? []), item.name].join(' / ') }))}
          onSearch={value => { setQuery(value); setPage(1); }} onChange={subjectId => onChange({ subjectId })} />
        <Space><Button size="small" disabled={page === 1 || loading} onClick={() => setPage(page - 1)}>上一页主体</Button>
          <Button size="small" disabled={!directory.hasMore || loading} onClick={() => setPage(page + 1)}>下一页主体</Button></Space>
      </Form.Item>}
      {type?.supportsDescendants && <Form.Item label="包含下级部门"><Switch aria-label="包含下级部门" checked={subject.matchOptions?.includeDescendants ?? false}
        onChange={includeDescendants => onChange({ matchOptions: { includeDescendants } })} /></Form.Item>}
      {showPriority && <Form.Item label="优先级"><InputNumber aria-label="优先级" precision={0} value={subject.priority} onChange={priority => onChange({ priority: priority ?? 0 })} /></Form.Item>}
      {(actions ?? [{ key: 'read', title: '读取', enabled: true }]).map(action =>
        <Form.Item key={action.key} label={`${action.title}意见`}><Select aria-label={`${action.title}意见`} style={{ width: 130 }}
          value={subject.actions[action.key] ?? 'unset'}
          options={[{ value: 'unset', label: '不选' }, { value: 'allow', label: '同意' }, { value: 'deny', label: '拒绝' }]}
          onChange={opinion => onChange({ actions: { ...subject.actions, [action.key]: opinion } })} /></Form.Item>)}
      {onRemove && <Form.Item label="操作"><Button danger onClick={onRemove}>删除主体项</Button></Form.Item>}
    </Space>
    {type?.supportsDescendants && <Typography.Paragraph type="secondary">默认仅匹配本部门人员。</Typography.Paragraph>}
  </div>;
}
