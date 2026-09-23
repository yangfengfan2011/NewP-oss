import { useCallback, useEffect, useRef } from 'react';
import { Alert, Button, Card, Spin, Typography } from 'antd';
import { platformFetch, usePlatformQuery } from '@icp-platform/core';
import { DataScopeSubjectEditor, type DataScopeSubject, type SubjectType } from '@icp-platform/ui';
import type { StandardCrudActionInputRenderContext } from '@icp-platform/sdk';

interface ObjectConfiguration { resourceKey: string; objectId: string; subjects: DataScopeSubject[] }
interface ObjectCatalog { actions: { key: string; title: string; enabled: boolean }[]; subjectTypes: SubjectType[] }

interface Target { resourceKey: string; title: string; id?: string; name?: string }

/** 组织资源共用已提交配置读取、草稿及固定目标的对象授权输入。 */
export default function OrganizationObjectAuthorizationInput({ target, featurePosition, value, onChange,
  onDraftStateChange }: StandardCrudActionInputRenderContext & { target: Target }) {
  const instance = useRef(crypto.randomUUID());
  const configuration = value as ObjectConfiguration | undefined;
  const base = target.id
    ? `/api/authorization/data-scope/objects/${target.resourceKey}/${encodeURIComponent(target.id)}` : '';
  const read = useCallback(async (path: string, signal?: AbortSignal) => {
    const response = await platformFetch(base + path, { headers: { 'X-NewP-Feature': featurePosition ?? '' }, signal });
    if (!response.ok) throw new Error(response.status === 403 ? '当前没有对象授权资格。'
      : response.status === 404 ? `${target.title}已不存在或不可读取。` : '对象授权读取失败。');
    return response.json() as Promise<unknown>;
  }, [base, featurePosition, target.title]);
  const query = usePlatformQuery({
    key: ['data-scope-object-editor', target.resourceKey, target.id ?? '', featurePosition ?? '', instance.current],
    enabled: Boolean(target.id && featurePosition),
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
  const error = !target.id || !featurePosition ? `缺少${target.title}或授权入口身份。`
    : query.status === 'error' ? query.error instanceof Error ? query.error.message : '对象授权读取失败。' : '';
  return <div style={{ minWidth: 0, maxHeight: '65vh', overflowY: 'auto' }}>
    <Card size="small" style={{ marginBottom: 16 }} aria-label="授权目标">
      <Typography.Text strong>目标{target.title}：{target.name ?? ''}</Typography.Text>
      <Typography.Paragraph type="secondary" style={{ marginBottom: 0 }}>{target.title}标识：{target.id ?? ''}</Typography.Paragraph>
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
