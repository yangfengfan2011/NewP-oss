import { formatRequestError, useLocalization } from '@icp-platform/core';
import { useEffect, useState } from 'react';
import { Alert, Button, Form, InputNumber, Select, Space, Switch, Typography } from 'antd';
import { searchDataScopeSubjects, type DataScopeSubjectDirectoryRead, type DataScopeSubjectSearchResult } from './dataScopeSubjectDirectory';

export interface DataScopeSubject { id: string; type: string; subjectId: string | null; priority: number;
  actions: Record<string, string>; matchOptions?: { includeDescendants: boolean } }
export interface SubjectType { key: string; title: string; requiresId: boolean; supportsDescendants?: boolean }
export interface DataScopeAction { key: string; title: string; enabled: boolean }

/** 主体候选独立于业务页面读取；切换类型取消旧请求，已选名称由同一最小目录解析。 */
export default function DataScopeSubjectEditor({ subject, index, types, actions, read, onChange, onRemove, showPriority = true }: {
  subject: DataScopeSubject; index: number; types: SubjectType[];
  actions?: DataScopeAction[];
  showPriority?: boolean;
  read: DataScopeSubjectDirectoryRead;
  onChange: (change: Partial<DataScopeSubject>) => void; onRemove?: () => void;
}) {
  const { t } = useLocalization();
  const [query, setQuery] = useState('');
  const [page, setPage] = useState(1);
  const [directory, setDirectory] = useState<DataScopeSubjectSearchResult>({ items: [], hasMore: false, options: [] });
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<unknown>();
  const type = types.find(item => item.key === subject.type);
  useEffect(() => {
    if (!type?.requiresId) return;
    const controller = new AbortController();
    setLoading(true); setError('');
    void searchDataScopeSubjects(read, { type: subject.type, query, page, currentId: subject.subjectId }, controller.signal).then(result => {
      if (!controller.signal.aborted) setDirectory(result);
    }).catch(failure => { if (!controller.signal.aborted) setError(failure); })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [read, subject.type, subject.subjectId, type?.requiresId, query, page]);
  return <div role="group" aria-label={t('DataScope.SubjectItem', '主体授权项 {index}', { index: index + 1 })} style={{ borderBottom: '1px solid var(--ant-color-border-secondary)', marginBottom: 12 }}>
    {!!error && <Alert type="error" showIcon message={formatRequestError(error, t)} />}
    <Space wrap align="start">
      <Form.Item label={t('DataScope.SubjectType', '主体类型')}><Select aria-label={t('DataScope.SubjectType', '主体类型')} style={{ width: 150 }} value={subject.type}
        options={types.map(item => ({ value: item.key, label: t(`DataScope.SubjectType_${item.key}`, item.title) }))} onChange={value => {
          setQuery(''); setPage(1); setDirectory({ items: [], options: [], hasMore: false });
          onChange({ type: value, subjectId: null, matchOptions: undefined });
        }} /></Form.Item>
      {type?.requiresId && <Form.Item label={t('DataScope.SubjectSelect', '主体选择')}>
        <Select aria-label={t('DataScope.SubjectSelect', '主体选择')} style={{ minWidth: 220, maxWidth: '100%' }} showSearch filterOption={false} loading={loading}
          value={subject.subjectId} options={directory.options.map(item => ({ value: item.id, label: [...(item.path?.map(segment => segment.name) ?? []), item.name].join(' / ') }))}
          onSearch={value => { setQuery(value); setPage(1); }} onChange={subjectId => onChange({ subjectId })} />
        <Space><Button size="small" disabled={page === 1 || loading} onClick={() => setPage(page - 1)}>{t('DataScope.PreviousSubjects', '上一页主体')}</Button>
          <Button size="small" disabled={!directory.hasMore || loading} onClick={() => setPage(page + 1)}>{t('DataScope.NextSubjects', '下一页主体')}</Button></Space>
      </Form.Item>}
      {type?.supportsDescendants && <Form.Item label={t('DataScope.IncludeDepartments', '包含下级部门')}><Switch aria-label={t('DataScope.IncludeDepartments', '包含下级部门')} checked={subject.matchOptions?.includeDescendants ?? false}
        onChange={includeDescendants => onChange({ matchOptions: { includeDescendants } })} /></Form.Item>}
      {showPriority && <Form.Item label={t('DataScope.Priority', '优先级')}><InputNumber aria-label={t('DataScope.Priority', '优先级')} precision={0} value={subject.priority} onChange={priority => onChange({ priority: priority ?? 0 })} /></Form.Item>}
      {(actions ?? [{ key: 'read', title: '读取', enabled: true }]).map(action =>
        <Form.Item key={action.key} label={t('DataScope.ActionOpinion', '{action}意见', { action: t(`DataScope.Action_${action.key}`, action.title) })}><Select aria-label={t('DataScope.ActionOpinion', '{action}意见', { action: t(`DataScope.Action_${action.key}`, action.title) })} style={{ width: 130 }}
          value={subject.actions[action.key] ?? 'unset'}
          options={[{ value: 'unset', label: t('DataScope.Unset', '不选') }, { value: 'allow', label: t('DataScope.Allow', '同意') }, { value: 'deny', label: t('DataScope.Deny', '拒绝') }]}
          onChange={opinion => onChange({ actions: { ...subject.actions, [action.key]: opinion } })} /></Form.Item>)}
      {onRemove && <Form.Item label={t('DataScope.Operations', '操作')}><Button danger onClick={onRemove}>{t('DataScope.DeleteSubject', '删除主体项')}</Button></Form.Item>}
    </Space>
    {type?.supportsDescendants && <Typography.Paragraph type="secondary">{t('DataScope.DepartmentHelp', '默认仅匹配本部门人员。')}</Typography.Paragraph>}
  </div>;
}
