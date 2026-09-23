import { useCallback, useEffect, useRef } from 'react';
import { Alert, Button, Card, Spin, Typography } from 'antd';
import { platformFetch, usePlatformQuery } from '@icp-platform/core';
import { DataScopeSubjectEditor, type DataScopeSubject, type SubjectType } from '@icp-platform/ui';
import type { StandardCrudActionInputRenderContext } from '@icp-platform/sdk';
import type { PersonDetailView } from '../services/organizationService';

interface ObjectConfiguration { resourceKey: string; objectId: string; subjects: DataScopeSubject[] }
interface ObjectCatalog { actions: { key: string; title: string; enabled: boolean }[]; subjectTypes: SubjectType[] }

/** 对象身份固定为当前人员；草稿由共享动作弹窗持有，取消卸载后重新读取已提交配置。 */
export default function PersonObjectAuthorizationInput({ featurePosition, detail, value, onChange,
  onDraftStateChange }: StandardCrudActionInputRenderContext) {
  const person = detail as PersonDetailView | undefined;
  const id = person?.id;
  const instance = useRef(crypto.randomUUID());
  const configuration = value as ObjectConfiguration | undefined;
  const base = id ? `/api/authorization/data-scope/objects/organization.person/${encodeURIComponent(id)}` : '';
  const read = useCallback(async (path: string, signal?: AbortSignal) => {
    const response = await platformFetch(base + path, { headers: { 'X-NewP-Feature': featurePosition ?? '' }, signal });
    if (!response.ok) throw new Error(response.status === 403 ? '当前没有对象授权资格。'
      : response.status === 404 ? '人员已不存在或不可读取。' : '对象授权读取失败。');
    return response.json() as Promise<unknown>;
  }, [base, featurePosition]);
  const query = usePlatformQuery({
    key: ['data-scope-object-editor', id ?? '', featurePosition ?? '', instance.current],
    enabled: Boolean(id && featurePosition),
    load: async ({ signal }) => {
      const [saved, catalog] = await Promise.all([read('', signal), read('/catalog', signal)]);
      return { saved: saved as ObjectConfiguration, catalog: catalog as ObjectCatalog };
    },
  });
  useEffect(() => {
    if (query.data) onChange(query.data.saved);
  }, [query.data, onChange]);
  useEffect(() => {
    onDraftStateChange?.({ ready: query.status === 'success' && Boolean(configuration),
      dirty: Boolean(query.data && configuration && JSON.stringify(query.data.saved) !== JSON.stringify(configuration)) });
  }, [query.status, query.data, configuration, onDraftStateChange]);
  const catalog = query.data?.catalog;
  const loading = query.status === 'idle' || query.isFetching;
  const error = !id || !featurePosition ? '缺少人员或授权入口身份。'
    : query.status === 'error' ? query.error instanceof Error ? query.error.message : '对象授权读取失败。' : '';
  return <div style={{ minWidth: 0, maxHeight: '65vh', overflowY: 'auto' }}>
    <Card size="small" style={{ marginBottom: 16 }} aria-label="授权目标">
      <Typography.Text strong>目标人员：{person?.name ?? ''}</Typography.Text>
      <Typography.Paragraph type="secondary" style={{ marginBottom: 0 }}>人员标识：{id ?? ''}</Typography.Paragraph>
    </Card>
    {error && <Alert type="error" showIcon message={error} style={{ marginBottom: 12 }} />}
    <Spin spinning={loading}>
      <Typography.Title level={5}>主体授权项</Typography.Title>
      {!loading && configuration?.subjects.length === 0 && <Typography.Paragraph type="secondary">
        暂无主体授权项。添加主体后配置优先级和动作意见。
      </Typography.Paragraph>}
      {configuration?.subjects.map((subject, index) => <DataScopeSubjectEditor key={subject.id} subject={subject} index={index}
        types={catalog?.subjectTypes ?? []} actions={catalog?.actions ?? []}
        read={read}
        onChange={change => onChange({ ...configuration, subjects: configuration.subjects.map(item =>
          item.id === subject.id ? { ...item, ...change } : item) })}
        onRemove={() => onChange({ ...configuration, subjects: configuration.subjects.filter(item => item.id !== subject.id) })} />)}
      {!loading && configuration && <Button onClick={() => onChange({ ...configuration, subjects: [...configuration.subjects,
        { id: `DataScopeObjectSubject_${crypto.randomUUID()}`, type: 'all', subjectId: null,
          priority: 0, actions: Object.fromEntries((catalog?.actions ?? []).map(action => [action.key, 'unset'])) }] })}>
        添加主体项
      </Button>}
      <Typography.Paragraph type="secondary" style={{ marginTop: 12 }}>
        对象的明确意见优先于规则，未表态沿用规则。整份保存后，下一次请求使用新配置。
      </Typography.Paragraph>
    </Spin>
  </div>;
}
