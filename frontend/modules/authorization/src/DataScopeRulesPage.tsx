import { useCallback, useEffect, useRef, useState } from 'react';
import { Alert, Button, Card, Flex, Form, Input, Modal, Select, Space, Spin, Switch, Tag, Tree, Typography } from 'antd';
import useMessage from 'antd/es/message/useMessage';
import { platformFetch, useAuthorization } from '@icp-platform/core';
import type { PageProps } from '@icp-platform/sdk';
import { requestRole } from './roleResource';
import DataScopeSubjectEditor, { type DataScopeSubject, type SubjectType } from './DataScopeSubjectEditor';
import DataScopeConditionEditor, { type DataScopeBusinessType, type DataScopeCondition, type DataScopeResourceField } from './DataScopeConditionEditor';

type Subject = DataScopeSubject;
interface Rule { id: string; rootId: string; parentId: null; name: string; enabled: boolean; resourceKey: string;
  condition: DataScopeCondition; subjects: Subject[]; children: Rule[] }
interface Catalog { subjectTypes?: SubjectType[]; contextValues?: { key: string; title: string; valueType: string }[];
  contextBindings?: { modelKey: string; field: string; contextKey: string }[]; actions: { key: string; title: string; enabled: boolean }[];
  businessConditions?: DataScopeBusinessType[];
  features: { behaviorKey: string; position: string }[];
  resources: { resource: { resourceKey: string; modelName: string; modelKey: string }; fields: DataScopeResourceField[] }[] }
const endpoint = '/api/authorization/data-scope';
const errorText = (error: unknown) => error instanceof Error ? error.message : '规则操作失败';

/** 根树草稿与正式保存分离；页面离开使用既有 Page 生命周期。 */
export default function DataScopeRulesPage({ input, reportState }: PageProps) {
  const { hasFeature } = useAuthorization();
  const [catalog, setCatalog] = useState<Catalog>();
  const [rules, setRules] = useState<Rule[]>([]);
  const [draft, setDraft] = useState<Rule>();
  const [saved, setSaved] = useState<Rule>();
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const [conditionErrorPath, setConditionErrorPath] = useState('');
  const [pendingRule, setPendingRule] = useState<Rule>();
  const pending = useRef(false);
  const [message, messageHolder] = useMessage();
  const entry = String(input.parameters.featurePosition ?? '');
  const dirty = JSON.stringify(draft) !== JSON.stringify(saved);
  const savePosition = catalog?.features.find(feature => feature.behaviorKey === 'authorization.dataScope.save')?.position;
  const readOnly = !savePosition || !hasFeature(savePosition);
  const read = useCallback((path: string, signal?: AbortSignal) => requestRole(platformFetch as typeof fetch, endpoint + path,
    { headers: { 'X-NewP-Feature': entry }, signal }, '规则'), [entry]);
  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    void Promise.all([read('/catalog', controller.signal), read(`/rules?page=${page}`, controller.signal)]).then(([nextCatalog, nextRules]) => {
      if (!controller.signal.aborted) { setCatalog(nextCatalog as Catalog); setRules(nextRules as Rule[]); }
    }).catch(failure => { if (!controller.signal.aborted) setError(errorText(failure)); })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [read, page]);
  const reset = useCallback(() => setDraft(saved), [saved]);
  const save = useCallback(async () => {
    if (!draft || !savePosition || readOnly || pending.current) return false;
    pending.current = true; setSaving(true); setError(''); setConditionErrorPath('');
    try {
      await requestRole(platformFetch as typeof fetch, `${endpoint}/rules/${encodeURIComponent(draft.id)}`, {
        method: 'PUT', headers: { 'Content-Type': 'application/json', 'X-NewP-Feature': savePosition }, body: JSON.stringify(draft),
      }, '规则');
      setSaved(draft);
      setRules(current => current.some(rule => rule.id === draft.id) ? current.map(rule => rule.id === draft.id ? draft : rule) : [...current, draft]);
      void message.success('规则已保存');
      return true;
    } catch (failure) {
      const message = errorText(failure);
      setError(message);
      setConditionErrorPath(/^条件 (\d+(?:\.\d+)*)：/.exec(message)?.[1] ?? '');
      return false;
    }
    finally { pending.current = false; setSaving(false); }
  }, [draft, savePosition, readOnly, message]);
  useEffect(() => { reportState({ dirty, submitting: saving }, reset, save); }, [dirty, saving, reset, save, reportState]);
  const choose = (rule: Rule) => {
    if (dirty) setPendingRule(rule);
    else { setDraft(rule); setSaved(rules.find(value => value.id === rule.id)); }
  };
  const create = () => {
    const resource = catalog?.resources[0];
    if (!resource) return;
    const id = `DataScopeRule_${crypto.randomUUID()}`;
    choose({ id, rootId: id, parentId: null, name: '新规则', enabled: true, resourceKey: resource.resource.resourceKey,
      condition: { type: 'attribute', field: resource.fields[0]?.name ?? '', operator: 'equal', value: '' },
      subjects: [{ id: `DataScopeSubject_${crypto.randomUUID()}`, type: 'all', subjectId: null, priority: 0, actions: { read: 'unset' } }], children: [] });
  };
  const fields = catalog?.resources.find(resource => resource.resource.resourceKey === draft?.resourceKey)?.fields ?? [];
  const modelKey = catalog?.resources.find(item => item.resource.resourceKey === draft?.resourceKey)?.resource.modelKey ?? '';
  const businessTypes = catalog?.businessConditions?.filter(type => type.modelKeys.includes(modelKey)) ?? [];
  return <div style={{ padding: 24 }}>
    <Typography.Title level={3}>数据范围规则</Typography.Title>
    <Typography.Paragraph type="secondary">按根规则整体保存。支持多主体显式优先级、当前身份值和读取三态。</Typography.Paragraph>
    {messageHolder}
    {error && <Alert type="error" showIcon message={error} style={{ marginBottom: 16 }} />}
    <Spin spinning={loading}>
      <Flex gap={20} wrap align="stretch">
        <Card title="规则" style={{ width: 280, maxWidth: '100%', flexShrink: 0 }} extra={<Button disabled={readOnly || saving} onClick={create}>新增规则</Button>}>
          <Tree selectedKeys={draft ? [draft.id] : []} treeData={rules.map(rule => ({ key: rule.id, title: rule.name }))}
            onSelect={keys => { const rule = rules.find(value => value.id === keys[0]); if (rule) choose(rule); }} disabled={saving} />
          {rules.length === 0 && <Typography.Text type="secondary">暂无规则</Typography.Text>}
          <Space wrap style={{ marginTop: 16 }}><Button disabled={page === 1 || dirty || saving} onClick={() => setPage(page - 1)}>上一页</Button>
            <Button disabled={rules.length < 20 || dirty || saving} onClick={() => setPage(page + 1)}>下一页</Button></Space>
        </Card>
        <Card title={draft?.name ?? '选择或新增规则'} style={{ flex: '1 1 460px', minWidth: 0 }}>
          {draft && <Form layout="vertical" disabled={readOnly || saving}>
            <Form.Item label="规则名称"><Input aria-label="规则名称" value={draft.name} onChange={event => setDraft({ ...draft, name: event.target.value })} /></Form.Item>
            <Form.Item label="启用规则"><Switch aria-label="启用规则" checked={draft.enabled} onChange={enabled => setDraft({ ...draft, enabled })} /></Form.Item>
            <Form.Item label="资源"><Select aria-label="资源" value={draft.resourceKey} options={catalog?.resources.map(({ resource }) => ({ value: resource.resourceKey, label: `${resource.modelName} · ${resource.resourceKey}` }))}
              onChange={resourceKey => setDraft({ ...draft, resourceKey, condition: { type: 'attribute', field: '', operator: 'equal', value: '' } })} /></Form.Item>
            <Typography.Title level={5}>适用条件</Typography.Title>
            <DataScopeConditionEditor condition={draft.condition} path="1" fields={fields} modelKey={modelKey}
              contexts={catalog?.contextValues ?? []} bindings={catalog?.contextBindings ?? []} businessTypes={businessTypes}
              errorPath={conditionErrorPath} errorMessage={error} onChange={condition => {
                setConditionErrorPath(''); setDraft({ ...draft, condition });
              }} />
            <Typography.Title level={5}>主体授权项</Typography.Title>
            <Tag>{catalog?.actions.find(action => action.key === 'read')?.enabled ? '读取目录已启用' : '读取目录已停用'}</Tag>
            {draft.subjects.map((subject, index) => <DataScopeSubjectEditor key={subject.id} subject={subject} index={index}
              types={catalog?.subjectTypes ?? [{ key: 'all', title: '所有已认证用户', requiresId: false }]} read={read}
              onChange={change => setDraft({ ...draft, subjects: draft.subjects.map(item => item.id === subject.id ? { ...item, ...change } : item) })}
              onRemove={() => setDraft({ ...draft, subjects: draft.subjects.filter(item => item.id !== subject.id) })} />)}
            <Button style={{ marginBottom: 16 }} onClick={() => setDraft({ ...draft, subjects: [...draft.subjects,
              { id: `DataScopeSubject_${crypto.randomUUID()}`, type: 'all', subjectId: null, priority: 0, actions: { read: 'unset' } }] })}>添加主体项</Button>
            <Flex gap="small" wrap align="center"><Button type="primary" loading={saving} disabled={!dirty || readOnly} onClick={() => void save()}>保存根规则</Button>
              <Button disabled={!dirty || saving} onClick={reset}>放弃修改</Button>{dirty && <Typography.Text type="warning">有未保存的草稿</Typography.Text>}</Flex>
          </Form>}
        </Card>
      </Flex>
    </Spin>
    <Modal title="放弃未保存的草稿？" open={!!pendingRule} onCancel={() => setPendingRule(undefined)} onOk={() => {
      setDraft(pendingRule); setSaved(rules.find(rule => rule.id === pendingRule?.id)); setPendingRule(undefined);
    }} okText="放弃并切换" cancelText="继续编辑">当前根规则的修改尚未保存。</Modal>
  </div>;
}
