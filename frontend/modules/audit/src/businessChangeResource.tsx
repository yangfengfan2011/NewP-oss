import { platformFetch } from '@icp-platform/core';
import { createStandardCrudRestDataAdapter, isStandardCrudRecord, type StandardCrudResourceRegistration, type StandardCrudSummaryRenderer, type StandardCrudDetailFieldRenderer } from '@icp-platform/sdk';
import { Alert, Card, Table } from 'antd';

const labels: Record<string, string> = { PersonNo: '工号', ENAME: '姓名', BirthDate: '出生日期', CompanyId: '公司', DepartmentId: '部门', PositionId: '岗位', EmploymentStatus: '任职状态', EmploymentStartedAt: '入职时间', EmploymentEndedAt: '离职时间', IsEnabled: '初始启用状态' };
// 保留原始类型与标识，名称和时间展示值只作为辅助信息。
function display(value: unknown, rendered: unknown): string {
  if (!isStandardCrudRecord(value)) return '—';
  if (value.value === null) return '空值';
  const raw = String(value.value ?? '');
  return typeof rendered === 'string' && rendered !== raw ? `${rendered}（${raw}）` : raw;
}
export const referenceRenderer: StandardCrudDetailFieldRenderer = ({ fieldKey, value, detailFields }) => {
  const name = detailFields[`${fieldKey}_DisplayValue`];
  const raw = detailFields[fieldKey] ?? value;
  return typeof name === 'string' && name !== raw ? `${name}（${String(raw)}）` : String(raw ?? '—');
};

/** 明细沿用共享详情摘要槽；字段事实和空值由服务端协议决定。 */
export const changesRenderer: StandardCrudSummaryRenderer = ({ block, title }) => {
  if (!Array.isArray(block.data)) return <Alert type="error" message="变化明细读取失败" />;
  return <Card size="small" title={title}><Table<Record<string, unknown>> size="small" rowKey="field" pagination={false} dataSource={block.data} columns={[
    { title: '字段', dataIndex: 'field', render: (field: string) => labels[field] ?? field },
    { title: '原值', render: (_, row) => display(row.before, row.beforeDisplay) },
    { title: '新值', render: (_, row) => display(row.after, row.afterDisplay) },
  ]} /></Card>;
};
/** 审计只读资源通过受控标准列表和详情宿主接入。 */
export function createBusinessChangeResource(fetcher: typeof fetch = platformFetch as typeof fetch): StandardCrudResourceRegistration {
  return {
    resourceKey: 'audit.change', listViewKey: 'audit.change.list', detailViewKey: 'audit.change.detail',
    dataAdapter: createStandardCrudRestDataAdapter<Record<string, unknown>>({ apiRoute: '/api/audit/business-changes', fetcher,
      mapListItem: (value: unknown) => {
        if (!isStandardCrudRecord(value) || typeof value.EID !== 'string') throw new Error('业务变更记录响应无效');
        return { ...value, id: value.EID };
      }, getRowId: (row: Record<string, unknown>) => String(row.id),
    }),
    detailFieldRenderers: { 'audit.change.reference': referenceRenderer },
    summaryRenderers: { changes: changesRenderer },
  };
}
export const businessChangeResource = createBusinessChangeResource();
