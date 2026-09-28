import { platformFetch, useLocalization } from '@icp-platform/core';
import { createStandardCrudRestDataAdapter, isStandardCrudRecord, type StandardCrudResourceRegistration, type StandardCrudSummaryRenderer, type StandardCrudDetailFieldRenderer } from '@icp-platform/sdk';
import { Alert, Card, Table } from 'antd';

// 保留原始类型与标识，名称和时间展示值只作为辅助信息。
function display(value: unknown, rendered: unknown, t: ReturnType<typeof useLocalization>['t']): string {
  if (!isStandardCrudRecord(value)) return t('BusinessChangeView.NotAvailable');
  if (value.value === null) return t('BusinessChangeView.EmptyValue');
  const raw = String(value.value ?? '');
  return typeof rendered === 'string' && rendered !== raw ? `${rendered}（${raw}）` : raw;
}
export const referenceRenderer: StandardCrudDetailFieldRenderer = ({ fieldKey, value, detailFields }) => {
  const name = detailFields[`${fieldKey}_DisplayValue`];
  const raw = detailFields[fieldKey] ?? value;
  return typeof name === 'string' && name !== raw ? `${name}（${String(raw)}）` : String(raw ?? '—');
};

/** 明细沿用共享详情摘要槽；字段事实和空值由服务端协议决定。 */
function Changes({ block, title }: Parameters<StandardCrudSummaryRenderer>[0]) {
  const { t } = useLocalization();
  if (!Array.isArray(block.data)) return <Alert type="error" message={t('BusinessChangeView.ChangesUnavailable')} />;
  return <Card size="small" title={title}><Table<Record<string, unknown>> size="small" tableLayout="fixed" style={{ overflowWrap: 'anywhere' }} rowKey="field" pagination={false} dataSource={block.data} columns={[
    { title: t('BusinessChangeView.ChangeField'), width: '20%', dataIndex: 'field', render: (field: string) => t(`BusinessChangeView.ChangeField_${field}`, field) },
    { title: t('BusinessChangeView.PreviousValue'), width: '40%', render: (_, row) => display(row.before, row.beforeDisplay, t) },
    { title: t('BusinessChangeView.NewValue'), width: '40%', render: (_, row) => display(row.after, row.afterDisplay, t) },
  ]} /></Card>;
}
export const changesRenderer: StandardCrudSummaryRenderer = (props) => <Changes {...props} />;
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
