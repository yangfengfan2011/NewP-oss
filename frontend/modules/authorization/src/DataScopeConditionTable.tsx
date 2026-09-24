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
const operators = [{ value: 'and', label: '全部条件 AND' }, { value: 'or', label: '任一条件 OR' }];

/** 整体组合位于工具栏；分组是对同层条件的组织操作，不属于新建条件的类型。 */
export default function DataScopeConditionTable({ disabled, onEditingChange, ...props }: Props) {
  const { condition, onChange, fields, contexts, businessTypes, errorPath, errorMessage } = props;
  const { token } = theme.useToken();
  const [editing, setEditing] = useState<Editing>();
  const [collapsed, setCollapsed] = useState<string[]>([]);
  const [selection, setSelection] = useState<string[]>([]);
  const [proposal, setProposal] = useState<ProposedChange>();
  const overall = asConditionGroup(condition);
  const data = rows(overall.children ?? []);
  const hierarchyWidth = typeColumnWidth(data);
  const expanded = (items: ConditionRow[]): string[] => items.flatMap(item => item.children ? [item.key, ...expanded(item.children)] : []);
  const describe = (value: DataScopeCondition) => describeDataScopeCondition(value, contexts, businessTypes, fields);
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
    requestChange(moveConditionOut(overall, path), '移出此组', '所选项将与原父组并列，条件之间的逻辑关系可能改变。',
      parentNode.operator !== grandparent.operator && (parentNode.children?.length ?? 0) > 1);
  };
  const ungroup = (row: ConditionRow) => requestChange(ungroupCondition(overall, row.key), '解除分组',
    '组内条件将保留并移到父层，随后采用父层的组合方式。',
    row.condition.operator !== conditionAt(overall, parentConditionPath(row.key)).operator && (row.condition.children?.length ?? 0) > 1);
  const menu = (row: ConditionRow) => ({ items: [
    { key: 'out', label: '移出此组', disabled: parentConditionPath(row.key) === '1' },
    ...(row.condition.type === 'group' ? [
      { key: 'ungroup', label: '解除分组' }, { key: 'delete', label: '删除整组', danger: true },
    ] : []),
  ], onClick: ({ key }: { key: string }) => {
    if (key === 'out') moveOut(row.key);
    if (key === 'ungroup') ungroup(row);
    if (key === 'delete') requestChange(removeCondition(overall, row.key), '删除整组', '本组及其内部全部条件都会从草稿中删除。', true);
  } });
  const rowError = (row: ConditionRow) => errorPath === row.key || condition.type !== 'group' && errorPath === '1';
  return <>
    <Flex justify="space-between" align="center" wrap gap={12} style={{ marginBottom: 16 }}>
      <Space wrap><Typography.Text>符合以下</Typography.Text>
        <Select aria-label="整体组合方式" value={overall.operator} options={operators} style={{ width: 165 }} disabled={disabled}
          onChange={operator => commit({ ...overall, operator })} />
      </Space>
      <Button type="primary" disabled={disabled} onClick={() => add('1')}>新建条件</Button>
    </Flex>
    {selection.length > 0 && <Flex wrap gap={8} align="center" style={{ padding: 12, marginBottom: 12, background: token.colorPrimaryBg, borderRadius: token.borderRadius }}>
      <Typography.Text>已选 {selection.length} 项（同层）</Typography.Text>
      <Button size="small" disabled={disabled || selection.length < 2} onClick={() => commit(groupConditions(overall, selection, 'and'))}>组合为全部满足</Button>
      <Button size="small" disabled={disabled || selection.length < 2} onClick={() => commit(groupConditions(overall, selection, 'or'))}>组合为任一满足</Button>
      <Button type="link" size="small" onClick={() => setSelection([])}>取消选择</Button>
    </Flex>}
    {condition.type === 'group' && errorPath === '1' && errorMessage && <Alert type="error" showIcon message={errorMessage} style={{ marginBottom: 12 }} />}
    <Table<ConditionRow> size="small" rowKey="key" dataSource={data} pagination={false} scroll={{ x: hierarchyWidth + 480 }}
      locale={{ emptyText: '暂无条件，点击“新建条件”开始配置。' }}
      onRow={row => ({ 'aria-label': `条件项 ${condition.type === 'group' ? row.key : '1'}`,
        style: row.condition.type === 'group' ? { background: token.colorFillAlter } : undefined })}
      rowSelection={{ selectedRowKeys: selection, checkStrictly: true, hideSelectAll: true, columnWidth: 40,
        onChange: keys => setSelection(keys.map(String)),
        getCheckboxProps: row => ({ 'aria-label': `选择条件 ${displayPath(row.key)}`, disabled: disabled ||
          selection.length > 0 && parentConditionPath(row.key) !== parentConditionPath(selection[0]) }) }}
      expandable={{ indentSize: 20, expandedRowKeys: expanded(data).filter(path => !collapsed.includes(path) || errorPath?.startsWith(`${path}.`)),
        onExpand: (open, row) => setCollapsed(current => open ? current.filter(path => path !== row.key) : [...current, row.key]) }}
      columns={[
        { title: '条件', key: 'type', width: hierarchyWidth, onCell: () => ({ style: { whiteSpace: 'nowrap' } }), render: (_, row) => <Space size={6}>
          <Typography.Text type="secondary">{displayPath(row.key)}</Typography.Text>
          <Tag>{row.condition.type === 'group' ? '条件组' : row.condition.type === 'business' ? '业务条件' : '属性条件'}</Tag>
        </Space> },
        { title: '条件内容', key: 'description', render: (_, row) => <>
          {row.condition.type === 'group' ? <Space wrap>
            <Select aria-label={`条件组 ${displayPath(row.key)} 组合方式`} size="small" value={row.condition.operator} options={operators}
              style={{ width: 165 }} disabled={disabled} onChange={operator => commit(replaceCondition(overall, row.key, node => ({ ...node, operator })))} />
            <Typography.Text type="secondary">{row.condition.children?.length ?? 0} 项条件</Typography.Text>
          </Space> : <Typography.Text>{describe(row.condition)}</Typography.Text>}
          {rowError(row) && errorMessage && <Alert showIcon type="error" message={errorMessage} style={{ marginTop: 8 }} />}
        </> },
        { title: '操作', key: 'operations', width: 200, fixed: 'right', render: (_, row) => <Space size={4}>
          {row.condition.type === 'group' ? <Button size="small" type="link" disabled={disabled} onClick={() => add(row.key)}>添加条件</Button>
            : <><Button size="small" type="link" disabled={disabled} onClick={() => open({ path: row.key, value: row.condition, append: false })}>编辑</Button>
              <Popconfirm title="从草稿中删除此条件？" disabled={disabled} onConfirm={() => commit(removeCondition(overall, row.key))} okText="删除" cancelText="取消">
                <Button size="small" type="link" danger disabled={disabled}>删除</Button>
              </Popconfirm></>}
          {(row.condition.type === 'group' || parentConditionPath(row.key) !== '1') && <Dropdown menu={menu(row)} trigger={['click']} disabled={disabled}>
            <Button size="small" type="link" aria-label={`条件 ${displayPath(row.key)} 更多操作`} disabled={disabled}>更多 ▾</Button>
          </Dropdown>}
        </Space> },
      ]} />
    {!!overall.children?.length && <details style={{ marginTop: 16 }}><summary style={{ cursor: 'pointer', color: token.colorTextSecondary }}>完整条件预览</summary>
      <Typography.Paragraph style={{ marginTop: 8, overflowWrap: 'anywhere' }}>{describe(overall)}</Typography.Paragraph>
    </details>}
    <Drawer title={editing?.append ? '新建条件' : '编辑条件'} width={620} open={!!editing} onClose={close} maskClosable={false} destroyOnClose
      extra={<Space><Button onClick={close}>取消</Button><Button type="primary" onClick={confirm} disabled={disabled}>确定</Button></Space>}>
      {editing && <Form layout="vertical" disabled={disabled}>
        <Form.Item label="条件类型" required><Select aria-label="条件类型" value={editing.value.type} onChange={changeType}
          options={[{ value: 'attribute', label: '属性条件' }, { value: 'business', label: '业务条件', disabled: businessTypes.length === 0 }]} /></Form.Item>
        <DataScopeConditionEditor {...props} condition={editing.value} path="1" leafOnly errorPath={undefined} errorMessage={undefined}
          onChange={value => setEditing({ ...editing, value })} />
        <Typography.Paragraph type="secondary">确定后更新草稿，保存根规则后生效。需要分组时，在列表中勾选同层条件进行组合。</Typography.Paragraph>
      </Form>}
    </Drawer>
    <Modal title={proposal?.title} open={!!proposal} onCancel={closeProposal} okText="确认修改" cancelText="取消" okButtonProps={{ disabled }}
      onOk={() => { if (proposal && !disabled) { commit(proposal.next); closeProposal(); } }}>
      {proposal && <><Typography.Paragraph>{proposal.description}</Typography.Paragraph>
        <Typography.Text strong>修改前</Typography.Text><Typography.Paragraph>{describe(overall)}</Typography.Paragraph>
        <Typography.Text strong>修改后</Typography.Text><Typography.Paragraph>{proposal.next.children?.length ? describe(proposal.next) : '无条件（保存前需要添加条件）'}</Typography.Paragraph>
      </>}
    </Modal>
  </>;
}
