import { useCallback, useEffect, useRef, useState } from 'react';
import { Alert, Button, Card, Flex, Form, Input, Modal, Select, Space, Spin, Switch, Tag, Tree, Typography } from 'antd';
import useMessage from 'antd/es/message/useMessage';
import { platformFetch, useAuthorization } from '@icp-platform/core';
import type { PageProps } from '@icp-platform/sdk';
import { requestRole } from './roleResource';
import DataScopeSubjectEditor, { type DataScopeSubject, type SubjectType } from './DataScopeSubjectEditor';
import DataScopeConditionEditor, { describeDataScopeCondition, type DataScopeBusinessType, type DataScopeCondition, type DataScopeResourceField } from './DataScopeConditionEditor';

type Subject = DataScopeSubject;
interface Rule { id: string; rootId: string; parentId: string | null; name: string; enabled: boolean; resourceKey: string;
  condition: DataScopeCondition; subjects: Subject[]; children: Rule[] }
interface Catalog { subjectTypes?: SubjectType[]; contextValues?: { key: string; title: string; valueType: string }[];
  contextBindings?: { modelKey: string; field: string; contextKey: string }[]; actions: { key: string; title: string; enabled: boolean }[];
  businessConditions?: DataScopeBusinessType[];
  features: { behaviorKey: string; position: string }[];
  resources: { resource: { resourceKey: string; modelName: string; modelKey: string }; fields: DataScopeResourceField[] }[] }
const endpoint = '/api/authorization/data-scope';
const errorText = (error: unknown) => error instanceof Error ? error.message : '规则操作失败';
const findNode = (node: Rule, id: string): Rule | undefined => node.id === id ? node
  : node.children.map(child => findNode(child, id)).find(Boolean);
const updateNode = (node: Rule, id: string, update: (current: Rule) => Rule): Rule => node.id === id ? update(node)
  : { ...node, children: node.children.map(child => updateNode(child, id, update)) };
const removeNode = (node: Rule, id: string): Rule => ({ ...node, children: node.children
  .filter(child => child.id !== id).map(child => removeNode(child, id)) });
const pathTo = (node: Rule, id: string, path: Rule[] = []): Rule[] => {
  const next = [...path, node];
  if (node.id === id) return next;
  for (const child of node.children) {
    const found = pathTo(child, id, next);
    if (found.length) return found;
  }
  return [];
};
interface RuleTreeNode { key: string; title: string; children: RuleTreeNode[] }
const treeNode = (node: Rule): RuleTreeNode =>
  ({ key: node.id, title: node.enabled ? node.name : `${node.name}（已停用）`, children: node.children.map(treeNode) });
const treeIds = (node: Rule): string[] => [node.id, ...node.children.flatMap(treeIds)];

/** 根树草稿与正式保存分离；页面离开使用既有 Page 生命周期。 */
export default function DataScopeRulesPage({ input, reportState }: PageProps) {
  const { hasFeature } = useAuthorization();
  const [catalog, setCatalog] = useState<Catalog>();
  const [rules, setRules] = useState<Rule[]>([]);
  const [draft, setDraft] = useState<Rule>();
  const [saved, setSaved] = useState<Rule>();
  const [selectedId, setSelectedId] = useState<string>();
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const [conditionErrorPath, setConditionErrorPath] = useState('');
  const [pendingRule, setPendingRule] = useState<Rule>();
  const [deleteRoot, setDeleteRoot] = useState(false);
  const pending = useRef(false);
  const [message, messageHolder] = useMessage();
  const entry = String(input.parameters.featurePosition ?? '');
  const dirty = JSON.stringify(draft) !== JSON.stringify(saved);
  const selected = draft && findNode(draft, selectedId ?? draft.id);
  const ancestors = draft && selected ? pathTo(draft, selected.id).slice(0, -1) : [];
  const shownRules = draft ? rules.some(rule => rule.id === draft.id)
    ? rules.map(rule => rule.id === draft.id ? draft : rule) : [...rules, draft] : rules;
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
  const reset = useCallback(() => { setDraft(saved); setSelectedId(saved?.id); setConditionErrorPath(''); }, [saved]);
  const edit = (change: (node: Rule) => Rule) => {
    if (draft && selected) setDraft(updateNode(draft, selected.id, change));
  };
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
      const nested = /^节点 ([^：]+)：条件 (\d+(?:\.\d+)*)：/.exec(message);
      if (nested) setSelectedId(nested[1]);
      setConditionErrorPath(nested?.[2] ?? /^条件 (\d+(?:\.\d+)*)：/.exec(message)?.[1] ?? '');
      return false;
    }
    finally { pending.current = false; setSaving(false); }
  }, [draft, savePosition, readOnly, message]);
  useEffect(() => { reportState({ dirty, submitting: saving }, reset, save); }, [dirty, saving, reset, save, reportState]);
  const choose = (rule: Rule) => {
    if (dirty) setPendingRule(rule);
    else { setDraft(rule); setSaved(rules.find(value => value.id === rule.id)); setSelectedId(rule.id); setConditionErrorPath(''); }
  };
  const initialCondition = (resourceKey: string): DataScopeCondition => ({ type: 'attribute',
    field: catalog?.resources.find(item => item.resource.resourceKey === resourceKey)?.fields[0]?.name ?? '',
    operator: 'equal', value: '' });
  const create = () => {
    const resource = catalog?.resources[0];
    if (!resource) return;
    const id = `DataScopeRule_${crypto.randomUUID()}`;
    choose({ id, rootId: id, parentId: null, name: '新规则', enabled: true, resourceKey: resource.resource.resourceKey,
      condition: initialCondition(resource.resource.resourceKey),
      subjects: [{ id: `DataScopeSubject_${crypto.randomUUID()}`, type: 'all', subjectId: null, priority: 0, actions: { read: 'unset' } }], children: [] });
  };
  const addChild = () => {
    if (!draft || !selected) return;
    const id = `DataScopeRule_${crypto.randomUUID()}`;
    const child: Rule = { id, rootId: draft.id, parentId: selected.id, name: '新子规则', enabled: true,
      resourceKey: draft.resourceKey, condition: initialCondition(draft.resourceKey), subjects: [], children: [] };
    edit(node => ({ ...node, children: [...node.children, child] }));
    setSelectedId(id);
  };
  const addSibling = () => {
    if (!draft || !selected?.parentId) return;
    const parentId = selected.parentId;
    const id = `DataScopeRule_${crypto.randomUUID()}`;
    const sibling: Rule = { id, rootId: draft.id, parentId, name: '新同级规则', enabled: true,
      resourceKey: draft.resourceKey, condition: initialCondition(draft.resourceKey), subjects: [], children: [] };
    setDraft(updateNode(draft, parentId, parent => ({ ...parent, children: [...parent.children, sibling] })));
    setSelectedId(id);
  };
  const deleteSelected = () => {
    if (!draft || !selected?.parentId) return;
    setDraft(removeNode(draft, selected.id));
    setSelectedId(selected.parentId);
  };
  const deleteSavedRoot = async () => {
    if (!draft || pending.current || !savePosition || readOnly) return;
    if (!saved) { setDraft(undefined); setSelectedId(undefined); setDeleteRoot(false); return; }
    pending.current = true; setSaving(true); setError('');
    try {
      await requestRole(platformFetch as typeof fetch, `${endpoint}/rules/${encodeURIComponent(draft.id)}`, {
        method: 'DELETE', headers: { 'X-NewP-Feature': savePosition },
      }, '规则');
      setRules(current => current.filter(rule => rule.id !== draft.id));
      setDraft(undefined); setSaved(undefined); setSelectedId(undefined); setDeleteRoot(false);
      void message.success('根规则已删除');
    } catch (failure) { setError(errorText(failure)); }
    finally { pending.current = false; setSaving(false); }
  };
  const fields = catalog?.resources.find(resource => resource.resource.resourceKey === selected?.resourceKey)?.fields ?? [];
  const modelKey = catalog?.resources.find(item => item.resource.resourceKey === selected?.resourceKey)?.resource.modelKey ?? '';
  const businessTypes = catalog?.businessConditions?.filter(type => type.modelKeys.includes(modelKey)) ?? [];
  return <div style={{ padding: 24 }}>
    <Typography.Title level={3}>数据范围规则</Typography.Title>
    <Typography.Paragraph type="secondary">按根规则整体保存。支持多主体显式优先级、当前身份值和读取三态。</Typography.Paragraph>
    {messageHolder}
    {error && <Alert type="error" showIcon message={error} style={{ marginBottom: 16 }} />}
    <Spin spinning={loading}>
      <Flex gap={20} wrap align="stretch">
        <Card title="规则树" style={{ width: 280, maxWidth: '100%', flexShrink: 0 }} extra={<Button disabled={readOnly || saving} onClick={create}>新增规则</Button>}>
          <Tree aria-label="规则树" selectedKeys={selectedId ? [selectedId] : []} expandedKeys={shownRules.flatMap(treeIds)}
            treeData={shownRules.map(treeNode)} onSelect={keys => {
              const id = String(keys[0] ?? '');
              if (draft && findNode(draft, id)) { setSelectedId(id); setConditionErrorPath(''); return; }
              const rule = rules.find(value => value.id === id);
              if (rule) choose(rule);
            }} disabled={saving} />
          {shownRules.length === 0 && <Typography.Text type="secondary">暂无规则</Typography.Text>}
          <Space wrap style={{ marginTop: 12 }}>
            <Button disabled={!selected || readOnly || saving} onClick={addChild}>添加子规则</Button>
            <Button disabled={!selected?.parentId || readOnly || saving} onClick={addSibling}>添加同级规则</Button>
          </Space>
          <Space wrap style={{ marginTop: 16 }}><Button disabled={page === 1 || dirty || saving} onClick={() => setPage(page - 1)}>上一页</Button>
            <Button disabled={rules.length < 20 || dirty || saving} onClick={() => setPage(page + 1)}>下一页</Button></Space>
        </Card>
        <Card title={selected?.name ?? '选择或新增规则'} style={{ flex: '1 1 460px', minWidth: 0 }}>
          {selected && <Form layout="vertical" disabled={readOnly || saving}>
            <Typography.Text type="secondary">当前节点：{selected.name}</Typography.Text>
            {ancestors.length > 0 && <details open style={{ margin: '12px 0', padding: 12, border: '1px solid var(--ant-color-border-secondary)', borderRadius: 6 }}>
              <summary>完整条件还包含 {ancestors.length} 个祖先条件</summary>
              <ol style={{ marginBottom: 0, paddingLeft: 24 }}>{ancestors.map(node => <li key={node.id} style={{ marginTop: 8 }}>
                <Space wrap><Typography.Text strong>{node.name}</Typography.Text>{!node.enabled && <Tag>已停用</Tag>}</Space>
                <div><Typography.Text type="secondary">{describeDataScopeCondition(node.condition, catalog?.contextValues ?? [], businessTypes)}</Typography.Text></div>
              </li>)}</ol>
            </details>}
            <Form.Item label="规则名称"><Input aria-label="规则名称" value={selected.name} onChange={event => edit(node => ({ ...node, name: event.target.value }))} /></Form.Item>
            <Form.Item label="启用规则"><Switch aria-label="启用规则" checked={selected.enabled} onChange={enabled => edit(node => ({ ...node, enabled }))} /></Form.Item>
            <Form.Item label="资源"><Select aria-label="资源" value={draft?.resourceKey} disabled={!!selected.parentId}
              options={catalog?.resources.map(({ resource }) => ({ value: resource.resourceKey, label: `${resource.modelName} · ${resource.resourceKey}` }))}
              onChange={resourceKey => {
                if (!draft) return;
                const replace = (node: Rule): Rule => ({ ...node, resourceKey,
                  condition: { type: 'attribute', field: '', operator: 'equal', value: '' }, children: node.children.map(replace) });
                setDraft(replace(draft));
              }} /></Form.Item>
            <Typography.Title level={5}>适用条件</Typography.Title>
            <DataScopeConditionEditor condition={selected.condition} path="1" fields={fields} modelKey={modelKey}
              contexts={catalog?.contextValues ?? []} bindings={catalog?.contextBindings ?? []} businessTypes={businessTypes}
              errorPath={conditionErrorPath} errorMessage={error} onChange={condition => {
                setConditionErrorPath(''); edit(node => ({ ...node, condition }));
              }} />
            <Typography.Title level={5}>主体授权项</Typography.Title>
            <Tag>{catalog?.actions.find(action => action.key === 'read')?.enabled ? '读取目录已启用' : '读取目录已停用'}</Tag>
            {selected.subjects.map((subject, index) => <DataScopeSubjectEditor key={subject.id} subject={subject} index={index}
              types={catalog?.subjectTypes ?? [{ key: 'all', title: '所有已认证用户', requiresId: false }]} read={read}
              onChange={change => edit(node => ({ ...node, subjects: node.subjects.map(item => item.id === subject.id ? { ...item, ...change } : item) }))}
              onRemove={() => edit(node => ({ ...node, subjects: node.subjects.filter(item => item.id !== subject.id) }))} />)}
            <Button style={{ marginBottom: 16 }} onClick={() => edit(node => ({ ...node, subjects: [...node.subjects,
              { id: `DataScopeSubject_${crypto.randomUUID()}`, type: 'all', subjectId: null, priority: 0, actions: { read: 'unset' } }] }))}>添加主体项</Button>
            <Flex gap="small" wrap align="center"><Button danger disabled={readOnly || saving} onClick={() => selected.parentId ? deleteSelected() : setDeleteRoot(true)}>
              {selected.parentId ? '删除节点' : '删除根树'}</Button></Flex>
          </Form>}
        </Card>
      </Flex>
      {draft && <Card size="small" style={{ marginTop: 16 }}><Flex gap="small" wrap align="center">
        <Typography.Text>当前根树：{draft.name}</Typography.Text>
        <Button type="primary" loading={saving} disabled={!dirty || readOnly} onClick={() => void save()}>保存根规则</Button>
        <Button disabled={!dirty || saving} onClick={reset}>放弃修改</Button>
        {dirty && <Typography.Text type="warning">有未保存的草稿</Typography.Text>}
      </Flex></Card>}
    </Spin>
    <Modal title="放弃未保存的草稿？" open={!!pendingRule} onCancel={() => setPendingRule(undefined)} onOk={() => {
      setDraft(pendingRule); setSaved(rules.find(rule => rule.id === pendingRule?.id)); setSelectedId(pendingRule?.id); setPendingRule(undefined);
    }} okText="放弃并切换" cancelText="继续编辑">当前根规则的修改尚未保存。</Modal>
    <Modal title="删除整棵根规则？" open={deleteRoot} onCancel={() => setDeleteRoot(false)} onOk={() => void deleteSavedRoot()}
      okText="删除整棵根树" okButtonProps={{ danger: true, loading: saving }} cancelText="取消">
      根规则及全部子节点会一起删除并记录审计。
    </Modal>
  </div>;
}
