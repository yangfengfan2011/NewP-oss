import { useEffect, useState } from 'react';
import { Alert, Button, Drawer, Flex, Form, Popconfirm, Space, Table, Tag, Typography } from 'antd';
import { OpinionCheckbox } from '@icp-platform/ui';
import DataScopeSubjectEditor, { type DataScopeSubject, type SubjectType } from './DataScopeSubjectEditor';

import { orderDataScopeSubjects, rankDataScopeSubjects } from './dataScopeSubjectOrder';

interface Action { key: string; title: string; enabled: boolean }
interface SubjectName { id: string; name: string; path?: { name: string }[] | null }
interface DirectoryPage { currentValues: SubjectName[] }
const nameBatchSize = 10;

/** 主体完整展示，名称按类型分批读取；抽屉确认前不改根规则草稿。 */
export default function DataScopeSubjectTable({ subjects, types, actions, read, disabled, onChange, onEditingChange }: {
  subjects: DataScopeSubject[]; types: SubjectType[]; actions: Action[]; disabled: boolean;
  read: (path: string, signal?: AbortSignal) => Promise<unknown>;
  onChange: (subjects: DataScopeSubject[]) => void; onEditingChange: (editing: boolean) => void;
}) {
  const [names, setNames] = useState<Record<string, string>>({});
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);
  const [editing, setEditing] = useState<DataScopeSubject>();
  const [editorError, setEditorError] = useState('');
  const ordered = orderDataScopeSubjects(subjects);
  const commit = (next: DataScopeSubject[]) => onChange(rankDataScopeSubjects(next));
  const nameRequest = JSON.stringify(ordered.filter(subject => types.find(type => type.key === subject.type)?.requiresId && subject.subjectId)
    .map(subject => [subject.type, subject.subjectId]));
  useEffect(() => {
    const controller = new AbortController();
    const entries = JSON.parse(nameRequest) as [string, string][];
    setLoading(entries.length > 0); setError(''); setNames({});
    const load = async () => {
      const next: Record<string, string> = {};
      // 列表不分页；目录请求仍限定每批身份数量，并串行发送，避免大 URL 和无界并发。
      for (const type of new Set(entries.map(([type]) => type))) {
        const ids = [...new Set(entries.filter(([key]) => key === type).map(([, id]) => id))];
        for (let offset = 0; offset < ids.length; offset += nameBatchSize) {
          if (controller.signal.aborted) return;
          const parameters = new URLSearchParams({ page: '1' });
          for (const id of ids.slice(offset, offset + nameBatchSize)) parameters.append('currentIds', id);
          const result = await read(`/subjects/${encodeURIComponent(type)}?${parameters}`, controller.signal) as DirectoryPage;
          for (const item of result.currentValues) next[`${type}:${item.id}`] = [...(item.path?.map(part => part.name) ?? []), item.name].join(' / ');
        }
      }
      if (!controller.signal.aborted) setNames(next);
    };
    void load().catch(failure => { if (!controller.signal.aborted) setError(failure instanceof Error ? failure.message : '主体名称读取失败'); })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [nameRequest, read]);
  const open = (subject: DataScopeSubject) => { setEditing(subject); setEditorError(''); onEditingChange(true); };
  const close = () => { setEditing(undefined); onEditingChange(false); };
  const confirm = () => {
    if (!editing || disabled) return;
    if (types.find(type => type.key === editing.type)?.requiresId && !editing.subjectId) { setEditorError('请选择授权主体。'); return; }
    const existing = ordered.some(subject => subject.id === editing.id);
    commit(existing ? ordered.map(subject => subject.id === editing.id ? editing : subject) : [...ordered, editing]);
    close();
  };
  const move = (id: string, offset: -1 | 1) => {
    const index = ordered.findIndex(subject => subject.id === id);
    const target = index + offset;
    if (disabled || index < 0 || target < 0 || target >= ordered.length) return;
    const next = [...ordered];
    [next[index], next[target]] = [next[target], next[index]];
    commit(next);
  };
  return <>
    <Flex justify="space-between" align="center" wrap gap={12} style={{ marginBottom: 16 }}>
      <Button type="primary" disabled={disabled} onClick={() => open({ id: `DataScopeSubject_${crypto.randomUUID()}`,
        type: 'all', subjectId: null, priority: 0, actions: { read: 'unset' } })}>添加主体项</Button>
      <Typography.Text type="secondary">□ 不选  ✓ 同意  ✕ 拒绝 · 点击循环切换</Typography.Text>
    </Flex>
    <Typography.Paragraph type="secondary">从上到下优先；每个动作分别匹配，“不选”时继续查看下一项。</Typography.Paragraph>
    {error && <Alert type="error" showIcon message={error} style={{ marginBottom: 12 }} />}
    <Table<DataScopeSubject> size="small" rowKey="id" dataSource={ordered} loading={loading}
      onRow={(_subject, index) => ({ 'aria-label': `主体授权项 ${(index ?? 0) + 1}` })}
      pagination={false}
      scroll={{ x: 495 + actions.length * 50 }} locale={{ emptyText: '暂无授权主体，点击“添加主体项”配置。' }}
      columns={[
        { title: '授权主体', key: 'subject', width: 240, render: (_, subject) => {
          const type = types.find(type => type.key === subject.type);
          return subject.subjectId ? <Space size={6} wrap><Tag>{type?.title ?? subject.type}</Tag>
            <span>{names[`${subject.type}:${subject.subjectId}`] ?? (loading ? '读取中…' : `无法解析（${subject.subjectId}）`)}</span>
          </Space> : type?.title ?? subject.type;
        } },
        { title: '包含下级', key: 'descendants', width: 75, render: (_, subject) => types.find(type => type.key === subject.type)?.supportsDescendants
          ? subject.matchOptions?.includeDescendants ? '是' : '否' : '—' },
        ...actions.map(action => ({ title: <span>{action.title ?? action.key}{!action.enabled && <Typography.Text type="secondary">（目录已停用）</Typography.Text>}</span>,
          key: action.key, width: action.enabled ? 50 : 110, align: 'center' as const,
          render: (_: unknown, subject: DataScopeSubject) => <OpinionCheckbox value={subject.actions[action.key] ?? 'unset'}
            label={`${action.title ?? action.key}意见`} disabled={disabled} onChange={value => commit(ordered.map(item => item.id === subject.id
              ? { ...item, actions: { ...item.actions, [action.key]: value } } : item))} /> })),
        { title: '操作', key: 'operations', width: 180, fixed: 'right', render: (_, subject) => <Space size={0}>
          <Button type="link" size="small" disabled={disabled || ordered[0]?.id === subject.id} onClick={() => move(subject.id, -1)}>上移</Button>
          <Button type="link" size="small" disabled={disabled || ordered[ordered.length - 1]?.id === subject.id} onClick={() => move(subject.id, 1)}>下移</Button>
          <Button type="link" size="small" disabled={disabled} onClick={() => open(subject)}>编辑</Button>
          <Popconfirm title="从草稿中删除此主体项？" onConfirm={() => commit(ordered.filter(item => item.id !== subject.id))} okText="删除" cancelText="取消" disabled={disabled}>
            <Button type="link" size="small" danger disabled={disabled}>删除</Button>
          </Popconfirm>
        </Space> },
      ]} />
    <Drawer title={editing && subjects.some(subject => subject.id === editing.id) ? '编辑授权主体' : '添加授权主体'}
      open={!!editing} width={560} maskClosable={false} onClose={close} destroyOnClose
      extra={<Space><Button onClick={close}>取消</Button><Button type="primary" disabled={disabled} onClick={confirm}>确定</Button></Space>}>
      {editing && <Form layout="vertical" disabled={disabled}>
        {editorError && <Alert type="error" message={editorError} showIcon style={{ marginBottom: 16 }} />}
        <DataScopeSubjectEditor subject={editing} index={0} types={types} actions={[]} read={read} showPriority={false}
          onChange={change => { setEditorError(''); setEditing({ ...editing, ...change }); }} />
        <Space size={24} wrap>{actions.map(action => <Form.Item key={action.key} label={`${action.title ?? action.key}意见`}>
          <OpinionCheckbox value={editing.actions[action.key] ?? 'unset'} label={`${action.title ?? action.key}意见`} disabled={disabled}
            onChange={value => setEditing({ ...editing, actions: { ...editing.actions, [action.key]: value } })} />
        </Form.Item>)}</Space>
        <Typography.Paragraph type="secondary">□ 不选  ✓ 同意  ✕ 拒绝。确定后加入草稿，保存根规则后生效。</Typography.Paragraph>
      </Form>}
    </Drawer>
  </>;
}
