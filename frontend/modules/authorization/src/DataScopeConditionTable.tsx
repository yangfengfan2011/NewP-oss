import { useLocalization } from '@icp-platform/core';
import { useState, type ComponentProps } from 'react';
import { Alert, Button, Drawer, Dropdown, Flex, Form, Modal, Popconfirm, Select, Space, Table, Tag, Typography, theme } from 'antd';
import DataScopeConditionEditor, { describeDataScopeCondition, type DataScopeCondition } from './DataScopeConditionEditor';
import { asConditionGroup, conditionAt, groupConditions, moveConditionOut, parentConditionPath, removeCondition, replaceCondition, ungroupCondition } from './dataScopeConditionTree';

type Props = Omit<ComponentProps<typeof DataScopeConditionEditor>, 'path' | 'onRemove'> & {
  disabled: boolean; onEditingChange: (editing: boolean) => void;
};
interface ConditionRow { key: string; condition: DataScopeCondition; children?: ConditionRow[] }
interface Editing { path: string; value: DataScopeCondition; append: boolean; variants?: Record<string, DataScopeCondition> }
interface ProposedChange { title: string; next: DataScopeCondition; description: string }
const rows = (children: DataScopeCondition[], parent = '1'): ConditionRow[] => children.map((condition, index) => {
  const key = `${parent}.${index + 1}`;
  return { key, condition, children: condition.type === 'group' ? rows(condition.children ?? [], key) : undefined };
});
const displayPath = (path: string) => path.slice(2);
// 首列按可见层级预留缩进和标签空间，避免深层节点换行后看似回到最外层。
const typeColumnWidth = (items: ConditionRow[]): number => items.reduce((width, item) => Math.max(width,
  128 + displayPath(item.key).length * 8 + (item.key.split('.').length - 2) * 20,
  item.children ? typeColumnWidth(item.children) : 0), 180);
const operators = (t: ReturnType<typeof useLocalization>['t']) => [{ value: 'and', label: t('DataScope.AllConditions', '全部条件 AND') }, { value: 'or', label: t('DataScope.AnyConditions', '任一条件 OR') }];

/** 整体组合位于工具栏；分组是对同层条件的组织操作，不属于新建条件的类型。 */
export default function DataScopeConditionTable({ disabled, onEditingChange, ...props }: Props) {
  const { condition, onChange, fields, contexts, businessTypes, errorPath, errorMessage } = props;
  const { t, getEnumerationLabel } = useLocalization();
  const { token } = theme.useToken();
  const [editing, setEditing] = useState<Editing>();
  const [collapsed, setCollapsed] = useState<string[]>([]);
  const [selection, setSelection] = useState<string[]>([]);
  const [proposal, setProposal] = useState<ProposedChange>();
  const overall = asConditionGroup(condition);
  const data = rows(overall.children ?? []);
  const hierarchyWidth = typeColumnWidth(data);
  const expanded = (items: ConditionRow[]): string[] => items.flatMap(item => item.children ? [item.key, ...expanded(item.children)] : []);
  const describe = (value: DataScopeCondition) => describeDataScopeCondition(value, contexts, businessTypes, fields, t, getEnumerationLabel);
  const commit = (next: DataScopeCondition) => { onChange(next); setSelection([]); setCollapsed([]); };
  const open = (next: Editing) => { setEditing(next); onEditingChange(true); };
  const close = () => { setEditing(undefined); onEditingChange(false); };
  const initial = (type: string): DataScopeCondition => type === 'business'
    ? { type, field: '', operator: '', value: null, key: businessTypes[0]?.key }
    : { type: 'attribute', field: fields[0]?.name ?? '', operator: 'equal', value: '' };
  const add = (path: string) => open({ path, value: initial('attribute'), append: true });
  const changeType = (type: string) => {
    if (!editing) return;
    const variants = { ...editing.variants, [editing.value.type]: editing.value };
    setEditing({ ...editing, variants, value: variants[type] ?? initial(type) });
  };
  const confirm = () => {
    if (!editing || disabled) return;
    commit(replaceCondition(overall, editing.path, node => editing.append
      ? { ...node, children: [...node.children ?? [], editing.value] } : editing.value));
    close();
  };
  const requestChange = (next: DataScopeCondition, title: string, description: string, confirm: boolean) => {
    if (!confirm) { commit(next); return; }
    setProposal({ next, title, description }); onEditingChange(true);
  };
  const closeProposal = () => { setProposal(undefined); onEditingChange(false); };
  const moveOut = (path: string) => {
    const parent = parentConditionPath(path);
    const parentNode = conditionAt(overall, parent);
    const grandparent = conditionAt(overall, parentConditionPath(parent));
    requestChange(moveConditionOut(overall, path), 'DataScope.MoveOut', 'DataScope.MoveOutHelp',
      parentNode.operator !== grandparent.operator && (parentNode.children?.length ?? 0) > 1);
  };
  const ungroup = (row: ConditionRow) => requestChange(ungroupCondition(overall, row.key), 'DataScope.Ungroup',
    'DataScope.UngroupHelp',
    row.condition.operator !== conditionAt(overall, parentConditionPath(row.key)).operator && (row.condition.children?.length ?? 0) > 1);
  const menu = (row: ConditionRow) => ({ items: [
    { key: 'out', label: t('DataScope.MoveOut', '移出此组'), disabled: parentConditionPath(row.key) === '1' },
    ...(row.condition.type === 'group' ? [
      { key: 'ungroup', label: t('DataScope.Ungroup', '解除分组') }, { key: 'delete', label: t('DataScope.DeleteGroup', '删除整组'), danger: true },
    ] : []),
  ], onClick: ({ key }: { key: string }) => {
    if (key === 'out') moveOut(row.key);
    if (key === 'ungroup') ungroup(row);
    if (key === 'delete') requestChange(removeCondition(overall, row.key), 'DataScope.DeleteGroup', 'DataScope.DeleteGroupHelp', true);
  } });
  const rowError = (row: ConditionRow) => errorPath === row.key || condition.type !== 'group' && errorPath === '1';
  return <>
    <Flex justify="space-between" align="center" wrap gap={12} style={{ marginBottom: 16 }}>
      <Space wrap><Typography.Text>{t('DataScope.Match', '符合以下')}</Typography.Text>
        <Select aria-label={t('DataScope.Overall', '整体组合方式')} value={overall.operator} options={operators(t)} style={{ width: 190 }} disabled={disabled}
          onChange={operator => commit({ ...overall, operator })} />
      </Space>
      <Button type="primary" disabled={disabled} onClick={() => add('1')}>{t('DataScope.NewCondition', '新建条件')}</Button>
    </Flex>
    {selection.length > 0 && <Flex wrap gap={8} align="center" style={{ padding: 12, marginBottom: 12, background: token.colorPrimaryBg, borderRadius: token.borderRadius }}>
      <Typography.Text>{t('DataScope.Selected', '已选 {count} 项（同层）', { count: selection.length })}</Typography.Text>
      <Button size="small" disabled={disabled || selection.length < 2} onClick={() => commit(groupConditions(overall, selection, 'and'))}>{t('DataScope.GroupAnd', '组合为全部满足')}</Button>
      <Button size="small" disabled={disabled || selection.length < 2} onClick={() => commit(groupConditions(overall, selection, 'or'))}>{t('DataScope.GroupOr', '组合为任一满足')}</Button>
      <Button type="link" size="small" onClick={() => setSelection([])}>{t('DataScope.ClearSelection', '取消选择')}</Button>
    </Flex>}
    {condition.type === 'group' && errorPath === '1' && errorMessage && <Alert type="error" showIcon message={errorMessage} style={{ marginBottom: 12 }} />}
    <Table<ConditionRow> size="small" rowKey="key" dataSource={data} pagination={false} scroll={{ x: hierarchyWidth + 480 }}
      locale={{ emptyText: t('DataScope.NoConditions', '暂无条件，点击“新建条件”开始配置。') }}
      onRow={row => ({ 'aria-label': t('DataScope.ConditionItem', '条件项 {path}', { path: condition.type === 'group' ? row.key : '1' }),
        style: row.condition.type === 'group' ? { background: token.colorFillAlter } : undefined })}
      rowSelection={{ selectedRowKeys: selection, checkStrictly: true, hideSelectAll: true, columnWidth: 40,
        onChange: keys => setSelection(keys.map(String)),
        getCheckboxProps: row => ({ 'aria-label': t('DataScope.SelectCondition', '选择条件 {path}', { path: displayPath(row.key) }), disabled: disabled ||
          selection.length > 0 && parentConditionPath(row.key) !== parentConditionPath(selection[0]) }) }}
      expandable={{ indentSize: 20, expandedRowKeys: expanded(data).filter(path => !collapsed.includes(path) || errorPath?.startsWith(`${path}.`)),
        onExpand: (open, row) => setCollapsed(current => open ? current.filter(path => path !== row.key) : [...current, row.key]) }}
      columns={[
        { title: t('DataScope.Condition', '条件'), key: 'type', width: hierarchyWidth, onCell: () => ({ style: { whiteSpace: 'nowrap' } }), render: (_, row) => <Space size={6}>
          <Typography.Text type="secondary">{displayPath(row.key)}</Typography.Text>
          <Tag>{row.condition.type === 'group' ? t('DataScope.ConditionGroup', '条件组') : row.condition.type === 'business' ? t('DataScope.Business', '业务条件') : t('DataScope.AttributeCondition', '属性条件')}</Tag>
        </Space> },
        { title: t('DataScope.ConditionContent', '条件内容'), key: 'description', render: (_, row) => <>
          {row.condition.type === 'group' ? <Space wrap>
            <Select aria-label={t('DataScope.GroupCombination', '条件组 {path} 组合方式', { path: displayPath(row.key) })} size="small" value={row.condition.operator} options={operators(t)}
              style={{ width: 190 }} disabled={disabled} onChange={operator => commit(replaceCondition(overall, row.key, node => ({ ...node, operator })))} />
            <Typography.Text type="secondary">{t('DataScope.ConditionCount', '{count} 项条件', { count: row.condition.children?.length ?? 0 })}</Typography.Text>
          </Space> : <Typography.Text>{describe(row.condition)}</Typography.Text>}
          {rowError(row) && errorMessage && <Alert showIcon type="error" message={errorMessage} style={{ marginTop: 8 }} />}
        </> },
        { title: t('DataScope.Operations', '操作'), key: 'operations', width: 200, fixed: 'right', render: (_, row) => <Space size={4}>
          {row.condition.type === 'group' ? <Button size="small" type="link" disabled={disabled} onClick={() => add(row.key)}>{t('DataScope.AddCondition', '添加条件')}</Button>
            : <><Button size="small" type="link" disabled={disabled} onClick={() => open({ path: row.key, value: row.condition, append: false })}>{t('DataScope.Edit', '编辑')}</Button>
              <Popconfirm title={t('DataScope.DeleteConditionConfirm', '从草稿中删除此条件？')} disabled={disabled} onConfirm={() => commit(removeCondition(overall, row.key))} okText={t('DataScope.Delete', '删除')} cancelText={t('DataScope.Cancel', '取消')}>
                <Button size="small" type="link" danger disabled={disabled}>{t('DataScope.Delete', '删除')}</Button>
              </Popconfirm></>}
          {(row.condition.type === 'group' || parentConditionPath(row.key) !== '1') && <Dropdown menu={menu(row)} trigger={['click']} disabled={disabled}>
            <Button size="small" type="link" aria-label={t('DataScope.MoreCondition', '条件 {path} 更多操作', { path: displayPath(row.key) })} disabled={disabled}>{t('DataScope.More', '更多 ▾')}</Button>
          </Dropdown>}
        </Space> },
      ]} />
    {!!overall.children?.length && <details style={{ marginTop: 16 }}><summary style={{ cursor: 'pointer', color: token.colorTextSecondary }}>{t('DataScope.Preview', '完整条件预览')}</summary>
      <Typography.Paragraph style={{ marginTop: 8, overflowWrap: 'anywhere' }}>{describe(overall)}</Typography.Paragraph>
    </details>}
    <Drawer title={editing?.append ? t('DataScope.NewCondition', '新建条件') : t('DataScope.EditCondition', '编辑条件')} width={620} open={!!editing} onClose={close} maskClosable={false} destroyOnClose
      extra={<Space><Button onClick={close}>{t('DataScope.Cancel', '取消')}</Button><Button type="primary" onClick={confirm} disabled={disabled}>{t('DataScope.Confirm', '确定')}</Button></Space>}>
      {editing && <Form layout="vertical" disabled={disabled}>
        <Form.Item label={t('DataScope.ConditionType', '条件类型')} required><Select aria-label={t('DataScope.ConditionType', '条件类型')} value={editing.value.type} onChange={changeType}
          options={[{ value: 'attribute', label: t('DataScope.AttributeCondition', '属性条件') }, { value: 'business', label: t('DataScope.Business', '业务条件'), disabled: businessTypes.length === 0 }]} /></Form.Item>
        <DataScopeConditionEditor {...props} condition={editing.value} path="1" leafOnly errorPath={undefined} errorMessage={undefined}
          onChange={value => setEditing({ ...editing, value })} />
        <Typography.Paragraph type="secondary">{t('DataScope.ConditionHelp', '确定后更新草稿，保存根规则后生效。需要分组时，在列表中勾选同层条件进行组合。')}</Typography.Paragraph>
      </Form>}
    </Drawer>
    <Modal title={proposal && t(proposal.title)} open={!!proposal} onCancel={closeProposal} okText={t('DataScope.ConfirmChange', '确认修改')} cancelText={t('DataScope.Cancel', '取消')} okButtonProps={{ disabled }}
      onOk={() => { if (proposal && !disabled) { commit(proposal.next); closeProposal(); } }}>
      {proposal && <><Typography.Paragraph>{t(proposal.description)}</Typography.Paragraph>
        <Typography.Text strong>{t('DataScope.Before', '修改前')}</Typography.Text><Typography.Paragraph>{describe(overall)}</Typography.Paragraph>
        <Typography.Text strong>{t('DataScope.After', '修改后')}</Typography.Text><Typography.Paragraph>{proposal.next.children?.length ? describe(proposal.next) : t('DataScope.NoConditionSave', '无条件（保存前需要添加条件）')}</Typography.Paragraph>
      </>}
    </Modal>
  </>;
}
