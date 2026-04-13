import { useState } from 'react';
import {
  Badge,
  Button,
  Checkbox,
  ConfirmDialog,
  Drawer,
  EmptyState,
  ErrorState,
  FilterBar,
  FormInline,
  Input,
  KeyValueEditor,
  LoadingState,
  Modal,
  PageHeader,
  Pagination,
  Panel,
  Radio,
  Select,
  StatusBadge,
  SummaryCard,
  Switch,
  TableWrap,
  Tabs,
  Textarea,
} from '@icp-platform/ui';
import { DataTable } from '../../../ui/dist/components/DataTable.js';
import { Tree, type TreeNode } from '../../../ui/dist/components/Tree.js';
import { TreeSelect } from '../../../ui/dist/components/TreeSelect.js';
import { Uploader, type UploadItem } from '../../../ui/dist/components/Uploader.js';

const treeData: TreeNode[] = [
  {
    key: 'shell',
    label: 'Shell',
    children: [
      { key: 'routing', label: '路由聚合' },
      { key: 'navigation', label: '菜单聚合' },
    ],
  },
  {
    key: 'module',
    label: '模块接入',
    children: [
      { key: 'sdk', label: 'SDK 注册' },
      { key: 'contracts', label: '契约扩展' },
    ],
  },
];

const tableRows = [
  { id: 'row-1', module: 'Shell', owner: '平台前端', status: '进行中' },
  { id: 'row-2', module: 'UI', owner: '设计系统', status: '已完成' },
];

const dataTableRows = [
  { id: 'dt-1', name: '配置中心', status: '启用' },
  { id: 'dt-2', name: '控件展示', status: '草稿' },
];

const filterFields = [
  { key: 'keyword', label: '关键词', placeholder: '输入名称' },
  {
    key: 'status',
    label: '状态',
    type: 'select' as const,
    placeholder: '全部状态',
    options: [
      { label: '启用', value: 'enabled' },
      { label: '草稿', value: 'draft' },
    ],
  },
];

const sectionStyle = {
  display: 'grid',
  gap: '16px',
} satisfies React.CSSProperties;

const gridStyle = {
  display: 'grid',
  gap: '12px',
  gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))',
} satisfies React.CSSProperties;

const inlineStyle = {
  display: 'flex',
  flexWrap: 'wrap',
  gap: '12px',
  alignItems: 'center',
} satisfies React.CSSProperties;

export default function UiShowcase() {
  const [inputValue, setInputValue] = useState('Shell 页面示例');
  const [textareaValue, setTextareaValue] = useState('用于展示多行输入与说明文本。');
  const [selectValue, setSelectValue] = useState('shell');
  const [checked, setChecked] = useState(true);
  const [enabled, setEnabled] = useState(false);
  const [radioValue, setRadioValue] = useState('grid');
  const [activeTab, setActiveTab] = useState('preview');
  const [page, setPage] = useState(1);
  const [modalOpen, setModalOpen] = useState(false);
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [confirmMessage, setConfirmMessage] = useState('等待确认');
  const [filterValues, setFilterValues] = useState<Record<string, string>>({
    keyword: '控件展示',
    status: 'enabled',
  });
  const [searchSummary, setSearchSummary] = useState('尚未执行查询');
  const [keyValueItems, setKeyValueItems] = useState([
    { key: 'module', value: 'shell' },
    { key: 'scope', value: 'ui-showcase' },
  ]);
  const [selectedTreeLabel, setSelectedTreeLabel] = useState('未选择');
  const [treeSelectValue, setTreeSelectValue] = useState('routing');
  const [selectedTableKeys, setSelectedTableKeys] = useState<string[]>(['dt-1']);
  const [uploadItems, setUploadItems] = useState<UploadItem[]>([
    {
      id: 'upload-1',
      file: new File(['shell'], 'shell-spec.md', { type: 'text/markdown' }),
      status: 'done',
    },
    {
      id: 'upload-2',
      file: new File(['draft'], 'ui-notes.txt', { type: 'text/plain' }),
      status: 'error',
      error: '等待重试',
    },
  ]);

  const pagedRows = tableRows.slice((page - 1) * 1, page * 1);

  return (
    <div style={{ display: 'grid', gap: '20px' }}>
      <PageHeader
        eyebrow="UI Showcase"
        title="控件展示"
        description="面向 Shell 的最小交互示例，使用 @icp-platform/ui 真实组件与本地 mock 数据。"
        actions={
          <div style={inlineStyle}>
            <Button variant="primary" onClick={() => setModalOpen(true)}>
              打开 Modal 示例
            </Button>
            <Button onClick={() => setDrawerOpen(true)}>打开 Drawer 示例</Button>
          </div>
        }
      />

      <section aria-labelledby="ui-showcase-basic">
        <Panel
          title="基础控件"
          meta="Button、Input、Textarea、Select、Checkbox、Switch、Radio、Badge、Tabs、EmptyState、LoadingState、ErrorState、Pagination"
          actions={<Badge variant="success">完整展示</Badge>}
        >
          <div style={sectionStyle}>
            <div style={inlineStyle}>
              <strong>Button</strong>
              <Button variant="primary" onClick={() => setInputValue('点击了主按钮')}>
                主按钮
              </Button>
              <Button onClick={() => setInputValue('点击了默认按钮')}>默认按钮</Button>
            </div>

            <div style={gridStyle}>
              <Input
                label="Input"
                value={inputValue}
                onChange={(event) => setInputValue(event.target.value)}
              />
              <Textarea
                label="Textarea"
                value={textareaValue}
                onChange={(event) => setTextareaValue(event.target.value)}
                rows={3}
              />
              <Select
                label="Select"
                value={selectValue}
                onChange={(event) => setSelectValue(event.target.value)}
                options={[
                  { value: 'shell', label: 'Shell' },
                  { value: 'ui', label: 'UI' },
                ]}
              />
            </div>

            <div style={inlineStyle}>
              <Checkbox checked={checked} onChange={setChecked} label="Checkbox" />
              <Switch checked={enabled} onChange={setEnabled} label="Switch" />
              <span>Radio</span>
              <Radio
                name="showcase-layout"
                value={radioValue}
                onChange={setRadioValue}
                options={[
                  { value: 'grid', label: '网格' },
                  { value: 'list', label: '列表' },
                ]}
              />
            </div>

            <div style={inlineStyle}>
              <strong>Badge</strong>
              <Badge>默认 Badge</Badge>
              <Badge variant="success">成功 Badge</Badge>
              <Badge variant="warning">警告 Badge</Badge>
            </div>

            <div style={sectionStyle}>
              <strong>Tabs</strong>
              <Tabs
                activeKey={activeTab}
                onChange={setActiveTab}
                tabs={[
                  { key: 'preview', label: '预览' },
                  { key: 'code', label: '代码' },
                  { key: 'notes', label: '备注' },
                ]}
              />
              <p>当前标签：{activeTab}</p>
            </div>

            <div style={gridStyle}>
              <div>
                <strong>EmptyState</strong>
                <EmptyState message="暂无控件配置" />
              </div>
              <div>
                <strong>LoadingState</strong>
                <LoadingState message="控件清单加载中..." />
              </div>
              <div>
                <strong>ErrorState</strong>
                <ErrorState message="控件元数据加载失败" />
              </div>
            </div>

            <div style={sectionStyle}>
              <strong>Pagination</strong>
              <TableWrap
                columns={[
                  { key: 'module', title: '模块' },
                  { key: 'owner', title: '归属' },
                  { key: 'status', title: '状态' },
                ]}
                data={pagedRows}
                rowKey={(item) => item.id}
              />
              <Pagination current={page} total={2} pageSize={1} onPageChange={setPage} />
            </div>
          </div>
        </Panel>
      </section>

      <section aria-labelledby="ui-showcase-structural">
        <Panel
          title="结构型页面块"
          meta="PageHeader、Panel、SummaryCard、TableWrap、StatusBadge、FormInline"
        >
          <div style={sectionStyle}>
            <div style={sectionStyle}>
              <strong>PageHeader</strong>
              <PageHeader
                title="PageHeader 示例"
                description="用于承载页面标题、说明和操作区。"
                actions={<Button>次级操作</Button>}
              />
            </div>

            <div style={sectionStyle}>
              <strong>Panel</strong>
              <Panel title="嵌套 Panel 示例" meta="用于块级信息组织">
                <p>当前输入内容：{inputValue}</p>
              </Panel>
            </div>

            <div style={gridStyle}>
              <SummaryCard label="SummaryCard" value="3 个块" meta="结构型页面块" />
              <SummaryCard label="当前选择" value={selectValue} meta="Select 状态" />
              <SummaryCard
                label="开关状态"
                value={enabled ? '已启用' : '未启用'}
                meta="Switch 状态"
              />
            </div>

            <div style={sectionStyle}>
              <strong>TableWrap</strong>
              <TableWrap
                columns={[
                  { key: 'module', title: '模块' },
                  { key: 'owner', title: '负责人' },
                  {
                    key: 'status',
                    title: '状态',
                    render: (item) => (
                      <StatusBadge
                        label={item.status}
                        variant={item.status === '已完成' ? 'success' : 'info'}
                      />
                    ),
                  },
                ]}
                data={tableRows}
                rowKey={(item) => item.id}
              />
            </div>

            <div style={inlineStyle}>
              <strong>StatusBadge</strong>
              <StatusBadge label="运行中" variant="success" />
              <StatusBadge label="待处理" variant="warning" />
              <StatusBadge label="草稿" variant="info" />
            </div>

            <div style={sectionStyle}>
              <strong>FormInline</strong>
              <FormInline>
                <Input
                  label="模块名"
                  value={inputValue}
                  onChange={(event) => setInputValue(event.target.value)}
                />
                <Select
                  label="模式"
                  value={radioValue}
                  onChange={(event) => setRadioValue(event.target.value)}
                  options={[
                    { value: 'grid', label: '网格' },
                    { value: 'list', label: '列表' },
                  ]}
                />
                <Button onClick={() => setSearchSummary(`已保存布局：${radioValue}`)}>保存</Button>
              </FormInline>
            </div>
          </div>
        </Panel>
      </section>

      <section aria-labelledby="ui-showcase-composite">
        <Panel
          title="复合控件"
          meta="Modal、Drawer、ConfirmDialog、FilterBar、KeyValueEditor"
        >
          <div style={sectionStyle}>
            <div style={inlineStyle}>
              <strong>Modal</strong>
              <Button variant="primary" onClick={() => setModalOpen(true)}>
                打开 Modal 示例
              </Button>
            </div>

            <div style={inlineStyle}>
              <strong>Drawer</strong>
              <Button onClick={() => setDrawerOpen(true)}>打开 Drawer 示例</Button>
            </div>

            <div style={inlineStyle}>
              <strong>ConfirmDialog</strong>
              <Button variant="danger" onClick={() => setConfirmOpen(true)}>
                打开 ConfirmDialog 示例
              </Button>
              <span>{confirmMessage}</span>
            </div>

            <div style={sectionStyle}>
              <strong>FilterBar</strong>
              <FilterBar
                fields={filterFields}
                values={filterValues}
                onChange={(key, value) =>
                  setFilterValues((current) => ({
                    ...current,
                    [key]: value,
                  }))
                }
                onSearch={() =>
                  setSearchSummary(
                    `查询条件：${filterValues.keyword || '空'} / ${filterValues.status || '全部'}`,
                  )
                }
                onReset={() => {
                  setFilterValues({ keyword: '', status: '' });
                  setSearchSummary('筛选已重置');
                }}
              />
              <p>{searchSummary}</p>
            </div>

            <div style={sectionStyle}>
              <strong>KeyValueEditor</strong>
              <KeyValueEditor
                items={keyValueItems}
                onChange={setKeyValueItems}
                addLabel="添加字段"
              />
              <p>字段数：{keyValueItems.length}</p>
            </div>
          </div>
        </Panel>
      </section>

      <section aria-labelledby="ui-showcase-candidates">
        <Panel
          title="候选控件"
          meta="候选能力"
          actions={<Badge variant="warning">候选能力</Badge>}
        >
          <div style={sectionStyle}>
            <div style={sectionStyle}>
              <strong>Tree</strong>
              <p>当前选择：{selectedTreeLabel}</p>
              <Tree
                data={treeData}
                defaultExpandedKeys={['shell']}
                onSelect={(_, node) => setSelectedTreeLabel(node.label)}
              />
            </div>

            <div style={sectionStyle}>
              <strong>TreeSelect</strong>
              <TreeSelect
                label="TreeSelect"
                data={treeData}
                value={treeSelectValue}
                onChange={(key, node) => {
                  setTreeSelectValue(key);
                  setSelectedTreeLabel(node.label);
                }}
              />
            </div>

            <div style={sectionStyle}>
              <strong>DataTable</strong>
              <DataTable
                columns={[
                  { key: 'name', title: '名称', sortable: true },
                  {
                    key: 'status',
                    title: '状态',
                    render: (item) => (
                      <StatusBadge
                        label={item.status}
                        variant={item.status === '启用' ? 'success' : 'warning'}
                      />
                    ),
                  },
                ]}
                data={dataTableRows}
                rowKey={(item) => item.id}
                selectable
                selectedKeys={selectedTableKeys}
                onSelectionChange={setSelectedTableKeys}
                toolbar={<Button size="sm">批量操作</Button>}
              />
            </div>

            <div style={sectionStyle}>
              <strong>Uploader</strong>
              <Uploader
                hint="候选能力：适合文件导入、包上传等场景"
                items={uploadItems}
                onAdd={(files) =>
                  setUploadItems((current) => [
                    ...current,
                    ...files.map((file, index) => ({
                      id: `${file.name}-${index}`,
                      file,
                      status: 'pending' as const,
                    })),
                  ])
                }
                onRemove={(id) =>
                  setUploadItems((current) => current.filter((item) => item.id !== id))
                }
                onRetry={(id) =>
                  setUploadItems((current) =>
                    current.map((item) =>
                      item.id === id ? { ...item, status: 'pending', error: undefined } : item,
                    ),
                  )
                }
              />
            </div>
          </div>
        </Panel>
      </section>

      <Modal
        open={modalOpen}
        onClose={() => setModalOpen(false)}
        title="Modal 示例"
        footer={<Button onClick={() => setModalOpen(false)}>关闭 Modal</Button>}
      >
        <p>当前输入：{inputValue}</p>
        <p>Textarea：{textareaValue}</p>
        <p>Select：{selectValue}</p>
      </Modal>

      <Drawer
        open={drawerOpen}
        onClose={() => setDrawerOpen(false)}
        title="Drawer 示例"
        footer={<Button onClick={() => setDrawerOpen(false)}>关闭 Drawer</Button>}
      >
        <p>用于演示侧边编辑或详情抽屉。</p>
        <p>当前筛选：{searchSummary}</p>
      </Drawer>

      <ConfirmDialog
        open={confirmOpen}
        title="ConfirmDialog 示例"
        message="确认删除当前草稿配置吗？"
        description="该示例用于演示危险操作的确认流程。"
        variant="danger"
        onCancel={() => {
          setConfirmOpen(false);
          setConfirmMessage('已取消确认');
        }}
        onConfirm={() => {
          setConfirmOpen(false);
          setConfirmMessage('已确认删除');
        }}
      />
    </div>
  );
}
