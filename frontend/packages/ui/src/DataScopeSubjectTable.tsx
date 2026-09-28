import { formatRequestError, useLocalization } from '@icp-platform/core';
import { useEffect, useState } from 'react';
import { Alert, Button, Drawer, Flex, Form, Popconfirm, Space, Table, Tag, Typography } from 'antd';
import OpinionCheckbox from './OpinionCheckbox';
import DataScopeSubjectEditor, { type DataScopeSubject, type SubjectType } from './DataScopeSubjectEditor';

import { orderDataScopeSubjects, rankDataScopeSubjects } from './dataScopeSubjectOrder';
import { resolveDataScopeSubjects, type DataScopeSubjectDirectoryRead, type DataScopeSubjectNames } from './dataScopeSubjectDirectory';

interface Action { key: string; title: string; enabled: boolean }

/** 主体完整展示，名称按类型分批读取；抽屉确认前不改根规则草稿。 */
export default function DataScopeSubjectTable({ subjects, types, actions, read, disabled, onChange, onEditingChange }: {
  subjects: DataScopeSubject[]; types: SubjectType[]; actions: Action[]; disabled: boolean;
  read: DataScopeSubjectDirectoryRead;
  onChange: (subjects: DataScopeSubject[]) => void; onEditingChange: (editing: boolean) => void;
}) {
  const { t } = useLocalization();
  const [names, setNames] = useState<DataScopeSubjectNames>(new Map());
  const [error, setError] = useState<unknown>();
  const [loading, setLoading] = useState(false);
  const [editing, setEditing] = useState<DataScopeSubject>();
  const [editorError, setEditorError] = useState('');
  const ordered = orderDataScopeSubjects(subjects);
  const commit = (next: DataScopeSubject[]) => onChange(rankDataScopeSubjects(next));
  const nameRequest = JSON.stringify(ordered.filter(subject => types.find(type => type.key === subject.type)?.requiresId && subject.subjectId)
    .map(subject => ({ type: subject.type, subjectId: subject.subjectId })));
  useEffect(() => {
    const controller = new AbortController();
    const entries = JSON.parse(nameRequest) as Pick<DataScopeSubject, 'type' | 'subjectId'>[];
    setLoading(entries.length > 0); setError(''); setNames(new Map());
    void resolveDataScopeSubjects(read, entries, controller.signal).then(next => {
      if (!controller.signal.aborted) setNames(next);
    }).catch(failure => { if (!controller.signal.aborted) setError(failure); })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [nameRequest, read]);
  const open = (subject: DataScopeSubject) => { setEditing(subject); setEditorError(''); onEditingChange(true); };
  const close = () => { setEditing(undefined); onEditingChange(false); };
  const confirm = () => {
    if (!editing || disabled) return;
    if (types.find(type => type.key === editing.type)?.requiresId && !editing.subjectId) { setEditorError('DataScope.SubjectRequired'); return; }
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
        type: 'all', subjectId: null, priority: 0, actions: { read: 'unset' } })}>{t('DataScope.AddSubject', '添加主体项')}</Button>
      <Typography.Text type="secondary">{t('DataScope.OpinionHelp', '□ 不选  ✓ 同意  ✕ 拒绝 · 点击循环切换')}</Typography.Text>
    </Flex>
    <Typography.Paragraph type="secondary">{t('DataScope.PriorityHelp', '从上到下优先；每个动作分别匹配，“不选”时继续查看下一项。')}</Typography.Paragraph>
    {!!error && <Alert type="error" showIcon message={formatRequestError(error, t)} style={{ marginBottom: 12 }} />}
    <Table<DataScopeSubject> size="small" rowKey="id" dataSource={ordered} loading={loading}
      onRow={(_subject, index) => ({ 'aria-label': t('DataScope.SubjectItem', '主体授权项 {index}', { index: (index ?? 0) + 1 }) })}
      pagination={false}
      scroll={{ x: 515 + actions.length * 80 }} locale={{ emptyText: t('DataScope.NoSubjects', '暂无授权主体，点击“添加主体项”配置。') }}
      columns={[
        { title: t('DataScope.Subject', '授权主体'), key: 'subject', width: 240, render: (_, subject) => {
          const type = types.find(type => type.key === subject.type);
          const name = subject.subjectId ? names.get(subject.type)?.get(subject.subjectId) : undefined;
          return subject.subjectId ? <Space size={6} wrap><Tag>{t(`DataScope.SubjectType_${subject.type}`, type?.title ?? subject.type)}</Tag>
            <span>{name ? [...(name.path?.map(part => part.name) ?? []), name.name].join(' / ') : (loading ? t('DataScope.Loading', '读取中…') : t('DataScope.Unresolved', '无法解析（{id}）', { id: subject.subjectId }))}</span>
          </Space> : t(`DataScope.SubjectType_${subject.type}`, type?.title ?? subject.type);
        } },
        { title: t('DataScope.IncludeDescendants', '包含下级'), key: 'descendants', width: 95, render: (_, subject) => types.find(type => type.key === subject.type)?.supportsDescendants
          ? subject.matchOptions?.includeDescendants ? t('DataScope.Yes', '是') : t('DataScope.No', '否') : '—' },
        ...actions.map(action => ({ title: <span>{t(`DataScope.Action_${action.key}`, action.title ?? action.key)}{!action.enabled && <Typography.Text type="secondary">{t('DataScope.CatalogDisabled', '（目录已停用）')}</Typography.Text>}</span>,
          key: action.key, width: action.enabled ? 80 : 140, align: 'center' as const,
          render: (_: unknown, subject: DataScopeSubject) => <OpinionCheckbox value={subject.actions[action.key] ?? 'unset'}
            label={t(`DataScope.Action_${action.key}`, action.title ?? action.key)} disabled={disabled} onChange={value => commit(ordered.map(item => item.id === subject.id
              ? { ...item, actions: { ...item.actions, [action.key]: value } } : item))} /> })),
        { title: t('DataScope.Operations', '操作'), key: 'operations', width: 180, fixed: 'right', render: (_, subject) => <Space size={0} wrap>
          <Button type="link" size="small" disabled={disabled || ordered[0]?.id === subject.id} onClick={() => move(subject.id, -1)}>{t('DataScope.MoveUp', '上移')}</Button>
          <Button type="link" size="small" disabled={disabled || ordered[ordered.length - 1]?.id === subject.id} onClick={() => move(subject.id, 1)}>{t('DataScope.MoveDown', '下移')}</Button>
          <Button type="link" size="small" disabled={disabled} onClick={() => open(subject)}>{t('DataScope.Edit', '编辑')}</Button>
          <Popconfirm title={t('DataScope.DeleteSubjectConfirm', '从草稿中删除此主体项？')} onConfirm={() => commit(ordered.filter(item => item.id !== subject.id))} okText={t('DataScope.Delete', '删除')} cancelText={t('DataScope.Cancel', '取消')} disabled={disabled}>
            <Button type="link" size="small" danger disabled={disabled}>{t('DataScope.Delete', '删除')}</Button>
          </Popconfirm>
        </Space> },
      ]} />
    <Drawer title={editing && subjects.some(subject => subject.id === editing.id) ? t('DataScope.EditSubject', '编辑授权主体') : t('DataScope.AddSubjectTitle', '添加授权主体')}
      open={!!editing} width={560} maskClosable={false} onClose={close} destroyOnClose
      extra={<Space><Button onClick={close}>{t('DataScope.Cancel', '取消')}</Button><Button type="primary" disabled={disabled} onClick={confirm}>{t('DataScope.Confirm', '确定')}</Button></Space>}>
      {editing && <Form layout="vertical" disabled={disabled}>
        {editorError && <Alert type="error" message={t(editorError)} showIcon style={{ marginBottom: 16 }} />}
        <DataScopeSubjectEditor subject={editing} index={0} types={types} actions={[]} read={read} showPriority={false}
          onChange={change => { setEditorError(''); setEditing({ ...editing, ...change }); }} />
        <Space size={24} wrap>{actions.map(action => <Form.Item key={action.key} label={t(`DataScope.Action_${action.key}`, action.title ?? action.key)}>
          <OpinionCheckbox value={editing.actions[action.key] ?? 'unset'} label={t(`DataScope.Action_${action.key}`, action.title ?? action.key)} disabled={disabled}
            onChange={value => setEditing({ ...editing, actions: { ...editing.actions, [action.key]: value } })} />
        </Form.Item>)}</Space>
        <Typography.Paragraph type="secondary">{t('DataScope.SubjectHelp', '□ 不选  ✓ 同意  ✕ 拒绝。确定后加入草稿，保存后生效。')}</Typography.Paragraph>
      </Form>}
    </Drawer>
  </>;
}
