import { useCallback, useEffect, useRef, useState } from 'react';
import { Alert, Button, Card, ConfigProvider, Dropdown, Empty, Flex, Form, Input, Modal, Select, Space, Spin, Switch, Tabs, Tag, Tree, Typography, theme } from 'antd';
import useMessage from 'antd/es/message/useMessage';
import { platformFetch, useAuthorization, useLocalization } from '@icp-platform/core';
import type { PageProps } from '@icp-platform/sdk';
import { requestRole } from './roleResource';
import { type DataScopeSubject, type SubjectType } from './DataScopeSubjectEditor';
import { describeDataScopeCondition, type DataScopeBusinessType, type DataScopeCondition, type DataScopeResourceField } from './DataScopeConditionEditor';

import DataScopeConditionTable from './DataScopeConditionTable';
import DataScopeSubjectTable from './DataScopeSubjectTable';
import './dataScopeRules.css';
import { orderDataScopeSubjects, rankDataScopeSubjects } from './dataScopeSubjectOrder';
import { dataScopeLabels, loadDataScopeViewLabels, type DataScopeViewLabels } from './dataScopeLabels';

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
// 整树保存覆盖各节点；回显按优先级排序，保存时由当前顺序生成数值。
const orderRuleSubjects = (node: Rule, rank = false): Rule => ({ ...node,
  subjects: rank ? rankDataScopeSubjects(orderDataScopeSubjects(node.subjects)) : orderDataScopeSubjects(node.subjects),
  children: node.children.map(child => orderRuleSubjects(child, rank)),
});

/** 根树草稿与正式保存分离；页面离开使用既有 Page 生命周期。 */
export default function DataScopeRulesPage({ input, reportState }: PageProps) {
  const { hasFeature } = useAuthorization();
  const { token } = theme.useToken();
  const { t } = useLocalization();
  const [viewLabels, setViewLabels] = useState<DataScopeViewLabels[]>([]);
  const [activeTab, setActiveTab] = useState('conditions');
  const [collapsedKeys, setCollapsedKeys] = useState<string[]>([]);
  const [editing, setEditing] = useState(false);
  const [editorEpoch, setEditorEpoch] = useState(0);
  const [pendingSelection, setPendingSelection] = useState<string>();
  const [catalog, setCatalog] = useState<Catalog>();
  const [rules, setRules] = useState<Rule[]>([]);
  const [draft, setDraft] = useState<Rule>();
  const [saved, setSaved] = useState<Rule>();
  const [selectedId, setSelectedId] = useState<string>();
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
    // 树不展示分页；沿既有 API 串行读取根节点批次，离开页面即取消后续读取。
    const load = async () => {
      const nextCatalog = await read('/catalog', controller.signal) as Catalog;
      const nextLabels = await loadDataScopeViewLabels(platformFetch as typeof fetch, nextCatalog.resources.map(item => item.resource), controller.signal);
      const allRules: Rule[] = [];
      let nextPage = 1;
      while (!controller.signal.aborted) {
        const batch = await read(`/rules?page=${nextPage}`, controller.signal) as Rule[];
        allRules.push(...batch.map(rule => orderRuleSubjects(rule)));
        if (batch.length < 20) break;
        nextPage += 1;
      }
      if (!controller.signal.aborted) { setCatalog(nextCatalog); setViewLabels(nextLabels); setRules(allRules); }
    };
    void load().catch(failure => { if (!controller.signal.aborted) setError(errorText(failure)); })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [read]);
  const reset = useCallback(() => { setDraft(saved); setSelectedId(saved?.id); setConditionErrorPath(''); setEditing(false); setEditorEpoch(value => value + 1); }, [saved]);
  const edit = (change: (node: Rule) => Rule) => {
    if (draft && selected) setDraft(updateNode(draft, selected.id, change));
  };
  const save = useCallback(async () => {
    if (editing) { void message.warning('请先确定或取消抽屉中的编辑。'); return false; }
    if (!draft || !savePosition || readOnly || pending.current) return false;
    pending.current = true; setSaving(true); setError(''); setConditionErrorPath('');
    try {
      const candidate = orderRuleSubjects(draft, true);
      await requestRole(platformFetch as typeof fetch, `${endpoint}/rules/${encodeURIComponent(draft.id)}`, {
        method: 'PUT', headers: { 'Content-Type': 'application/json', 'X-NewP-Feature': savePosition }, body: JSON.stringify(candidate),
      }, '规则');
      setDraft(candidate); setSaved(candidate);
      setRules(current => current.some(rule => rule.id === candidate.id) ? current.map(rule => rule.id === candidate.id ? candidate : rule) : [...current, candidate]);
      void message.success('规则已保存');
      return true;
    } catch (failure) {
      const message = errorText(failure);
      setError(message);
      const nested = /^节点 ([^：]+)：条件 (\d+(?:\.\d+)*)：/.exec(message);
      if (nested) {
        setSelectedId(nested[1]);
        const path = pathTo(draft, nested[1]).map(node => node.id);
        setCollapsedKeys(current => current.filter(id => !path.includes(id)));
      }
      setActiveTab('conditions');
      setConditionErrorPath(nested?.[2] ?? /^条件 (\d+(?:\.\d+)*)：/.exec(message)?.[1] ?? '');
      return false;
    }
    finally { pending.current = false; setSaving(false); }
  }, [draft, savePosition, readOnly, message, editing]);
  useEffect(() => { reportState({ dirty: dirty || editing, submitting: saving }, reset, save); }, [dirty, editing, saving, reset, save, reportState]);
  const choose = (rule: Rule, nodeId = rule.id) => {
    if (dirty) { setPendingRule(rule); setPendingSelection(nodeId); }
    else { setDraft(rule); setSaved(rules.find(value => value.id === rule.id)); setSelectedId(nodeId); setConditionErrorPath(''); }
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
    setActiveTab('conditions');
    const ancestors = pathTo(draft, selected.id).map(node => node.id);
    setCollapsedKeys(current => current.filter(id => !ancestors.includes(id)));
  };
  const addSibling = () => {
    if (!draft || !selected?.parentId) return;
    const parentId = selected.parentId;
    const id = `DataScopeRule_${crypto.randomUUID()}`;
    const sibling: Rule = { id, rootId: draft.id, parentId, name: '新同级规则', enabled: true,
      resourceKey: draft.resourceKey, condition: initialCondition(draft.resourceKey), subjects: [], children: [] };
    setDraft(updateNode(draft, parentId, parent => ({ ...parent, children: [...parent.children, sibling] })));
    setSelectedId(id);
    setActiveTab('conditions');
    const ancestors = pathTo(draft, parentId).map(node => node.id);
    setCollapsedKeys(current => current.filter(id => !ancestors.includes(id)));
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
  const labels = dataScopeLabels(viewLabels, selected?.resourceKey ?? '', t);
  const fields = (catalog?.resources.find(resource => resource.resource.resourceKey === selected?.resourceKey)?.fields ?? [])
    .map(field => ({ ...field, title: labels.field(field.name) }));
  const modelKey = catalog?.resources.find(item => item.resource.resourceKey === selected?.resourceKey)?.resource.modelKey ?? '';
  const businessTypes = catalog?.businessConditions?.filter(type => type.modelKeys.includes(modelKey)) ?? [];
  const disabled = readOnly || saving;
  const selectNode = (id: string) => {
    if (saving || !id) return;
    if (draft && findNode(draft, id)) { setSelectedId(id); setConditionErrorPath(''); return; }
    const root = rules.find(rule => findNode(rule, id));
    if (root) choose(root, id);
  };
  const nodeMenu = { items: [
    { key: 'child', label: '添加子规则', disabled: !selected || disabled },
    { key: 'sibling', label: '添加同级规则', disabled: !selected?.parentId || disabled },
    { key: 'delete', label: selected?.parentId ? '删除节点' : '删除根树', danger: true, disabled: !selected || disabled },
  ], onClick: ({ key }: { key: string }) => {
    if (key === 'child') addChild();
    if (key === 'sibling') addSibling();
    if (key === 'delete') setDeleteRoot(true);
  } };
  return <div className="data-scope-page">
    {messageHolder}
    {error && <Alert type="error" showIcon message={error} style={{ marginBottom: 16 }} />}
    <Spin spinning={loading}>
      <div className="data-scope-workspace">
        <aside aria-label="规则树面板">
          <Card size="small" className="data-scope-sidebar" styles={{ header: { minHeight: token.controlHeightLG, padding: `${token.paddingXXS}px ${token.paddingSM}px` }, body: { padding: token.paddingSM } }}
            title={<Space size={token.marginXS} wrap><Button size="small" disabled={disabled} onClick={create}>＋ 新增规则</Button>
              <Dropdown menu={nodeMenu} trigger={['click']}><Button size="small" disabled={!selected || disabled}>节点操作 ▾</Button></Dropdown></Space>}>
            <ConfigProvider theme={{ components: { Tree: { titleHeight: token.controlHeight, nodeSelectedBg: token.colorPrimaryBg, nodeSelectedColor: token.colorPrimaryText } } }}>
              <Tree aria-label="规则树" blockNode showLine={{ showLeafIcon: false }} switcherIcon={<span aria-hidden="true">⌄</span>}
                selectedKeys={selectedId ? [selectedId] : []} expandedKeys={shownRules.flatMap(treeIds).filter(id => !collapsedKeys.includes(id))}
                onExpand={keys => setCollapsedKeys(shownRules.flatMap(treeIds).filter(id => !keys.includes(id)))}
                treeData={shownRules.map(treeNode)} onSelect={keys => selectNode(String(keys[0] ?? ''))}
                onRightClick={({ node }) => selectNode(String(node.key))}
                titleRender={node => <Dropdown trigger={['contextMenu']} menu={{ ...nodeMenu, items: nodeMenu.items.map(item => ({ ...item, disabled: item.disabled || selectedId !== node.key })) }} disabled={disabled}>
                  <span style={{ whiteSpace: 'normal', overflowWrap: 'anywhere' }}>{node.title}</span>
                </Dropdown>} disabled={saving} />
            </ConfigProvider>
            {shownRules.length === 0 && <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="暂无规则" />}
          </Card>
        </aside>
        <Card size="small" className="data-scope-detail" title={selected ? <div className="data-scope-title" aria-label="规则路径">
            {ancestors.length > 0 && <Typography.Text type="secondary">{ancestors.map(node => node.name).join(' / ')} /</Typography.Text>}
            <Input aria-label="规则名称" title="规则名称，可直接编辑" variant="borderless" disabled={disabled} value={selected.name}
              onChange={event => edit(node => ({ ...node, name: event.target.value }))} />
          </div> : '规则详情'}
          extra={selected && <Switch aria-label="启用规则" checkedChildren="启用" unCheckedChildren="停用" disabled={disabled}
            checked={selected.enabled} onChange={enabled => edit(node => ({ ...node, enabled }))} />}>
          {selected ? <>
            <Form layout="vertical" disabled={disabled}>
              <div className="data-scope-basics">
                <Form.Item label="资源"><Select aria-label="资源" value={draft?.resourceKey} disabled={!!selected.parentId || disabled}
                  options={catalog?.resources.map(({ resource }) => ({ value: resource.resourceKey, label: dataScopeLabels(viewLabels, resource.resourceKey, t).resource(resource.modelName) }))}
                  onChange={resourceKey => {
                    if (!draft) return;
                    const replace = (node: Rule): Rule => ({ ...node, resourceKey,
                      condition: { type: 'attribute', field: '', operator: 'equal', value: '' }, children: node.children.map(replace) });
                    setDraft(replace(draft));
                  }} /></Form.Item>
              </div>
            </Form>
            <Tabs activeKey={activeTab} onChange={setActiveTab} items={[
              { key: 'conditions', label: '规则条件', children: <>
                {ancestors.length > 0 && <details className="data-scope-ancestors">
                  <summary>完整条件还包含 {ancestors.length} 个祖先条件</summary>
                  <ol>{ancestors.map(node => <li key={node.id}>
                    <Space><Typography.Text strong>{node.name}</Typography.Text>{!node.enabled && <Tag>已停用</Tag>}</Space>
                    <div><Typography.Text type="secondary">{describeDataScopeCondition(node.condition, catalog?.contextValues ?? [], businessTypes, fields)}</Typography.Text></div>
                  </li>)}</ol>
                </details>}
                <DataScopeConditionTable key={`${selected.id}:${editorEpoch}`} condition={selected.condition} fields={fields} modelKey={modelKey}
                  contexts={catalog?.contextValues ?? []} bindings={catalog?.contextBindings ?? []} businessTypes={businessTypes}
                  disabled={disabled} onEditingChange={setEditing} errorPath={conditionErrorPath} errorMessage={error}
                  onChange={condition => { setConditionErrorPath(''); edit(node => ({ ...node, condition })); }} />
              </> },
              { key: 'subjects', label: `授权主体（${selected.subjects.length}）`, children: <DataScopeSubjectTable key={`${selected.id}:${editorEpoch}`}
                subjects={selected.subjects} types={catalog?.subjectTypes ?? [{ key: 'all', title: '所有用户', requiresId: false }]}
                actions={catalog?.actions ?? []} read={read} disabled={disabled} onEditingChange={setEditing}
                onChange={subjects => edit(node => ({ ...node, subjects }))} /> },
            ]} />
          </> : <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="从左侧选择规则，或新增规则" />}
          {draft && <Flex className="data-scope-savebar" style={{ borderTop: `1px solid ${token.colorBorderSecondary}` }} gap={12} justify="space-between" align="center" wrap>
            <div><Typography.Text>根规则：{draft.name}</Typography.Text><br />
              <Typography.Text type="secondary">保存包含该根规则下的全部修改</Typography.Text></div>
            <Space wrap>{dirty && <Typography.Text type="warning">未保存</Typography.Text>}
              <Button disabled={!dirty || saving || editing} onClick={reset}>放弃修改</Button>
              <Button type="primary" loading={saving} disabled={!dirty || readOnly || editing} onClick={() => void save()}>保存根规则</Button>
            </Space>
          </Flex>}
        </Card>
      </div>
    </Spin>
    <Modal title="放弃未保存的草稿？" open={!!pendingRule} onCancel={() => setPendingRule(undefined)} onOk={() => {
      setDraft(pendingRule); setSaved(rules.find(rule => rule.id === pendingRule?.id)); setSelectedId(pendingSelection ?? pendingRule?.id); setPendingRule(undefined); setConditionErrorPath('');
    }} okText="放弃并切换" cancelText="继续编辑">当前根规则的修改尚未保存。</Modal>
    <Modal title={selected?.parentId ? '删除当前节点及其子规则？' : '删除整棵根规则？'} open={deleteRoot} onCancel={() => setDeleteRoot(false)} onOk={() => {
      if (selected?.parentId) { deleteSelected(); setDeleteRoot(false); } else void deleteSavedRoot();
    }} okText={selected?.parentId ? '删除节点' : '删除整棵根树'} okButtonProps={{ danger: true, loading: saving }} cancelText="取消">
      {selected?.parentId ? '当前节点及全部子规则将从草稿中删除，保存根规则后生效。' : '根规则及全部子节点会立即删除并记录审计。'}
    </Modal>
  </div>;
}
