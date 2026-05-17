import { useMemo, useState } from 'react';
import {
  AlertState,
  LoadingBlock,
  PageHeader,
  SearchTablePage,
  StatusTag,
  SummaryCard,
  createSearchTableRequest,
  type SearchTablePageProps,
} from '@icp-platform/ui';

interface OperationLog {
  id: number;
  operationTime: string;
  userId?: string;
  userName?: string;
  moduleId: string;
  operationType: string;
  operationTarget?: string;
  targetId?: string;
  description?: string;
  result: string;
  errorMessage?: string;
  clientIp?: string;
}

interface AuditListResponse<TItem> {
  items?: TItem[];
  total?: number;
}

export default function OperationLogs() {
  const [logs, setLogs] = useState<OperationLog[]>([]);
  const [total, setTotal] = useState(0);
  const [pageIndex, setPageIndex] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const request = useMemo(
    () =>
      createSearchTableRequest<Record<string, unknown>, OperationLog, OperationLog>({
        buildQuery: () => ({}),
        load: async (query) => {
          try {
            setLoading(true);
            setError(null);
            const nextPageIndex = Number(query.pageIndex ?? 1);
            const nextPageSize = Number(query.pageSize ?? 20);
            const response = await fetch(buildAuditPath('/api/audit/operation-logs', {
              pageIndex: nextPageIndex,
              pageSize: nextPageSize,
            }));

            if (!response.ok) {
              throw new Error('操作日志加载失败');
            }

            const data = await response.json() as AuditListResponse<OperationLog>;
            const items = Array.isArray(data.items) ? data.items : [];
            const nextTotal = typeof data.total === 'number' ? data.total : items.length;

            setLogs(items);
            setTotal(nextTotal);
            setPageIndex(nextPageIndex);
            setPageSize(nextPageSize);

            return { items, total: nextTotal };
          } catch (loadError) {
            const message = loadError instanceof Error ? loadError.message : '操作日志加载失败';
            setLogs([]);
            setTotal(0);
            setError(message);
            return { items: [], total: 0 };
          } finally {
            setLoading(false);
          }
        },
        mapRow: (row) => row,
      }),
    [],
  );

  const successCount = logs.filter((log) => log.result === 'Success').length;
  const failureCount = logs.length - successCount;

  const operationLogColumns: SearchTablePageProps<OperationLog>['columns'] = [
    { key: 'operationTime', title: '时间', dataIndex: 'operationTime', render: (_, log) => new Date(log.operationTime).toLocaleString() },
    { key: 'userName', title: '用户', dataIndex: 'userName', render: (_, log) => log.userName || '-' },
    { key: 'moduleId', title: '模块', dataIndex: 'moduleId' },
    { key: 'operationType', title: '操作类型', dataIndex: 'operationType' },
    { key: 'operationTarget', title: '目标', dataIndex: 'operationTarget', render: (_, log) => log.operationTarget || '-' },
    {
      key: 'result',
      title: '结果',
      dataIndex: 'result',
      render: (_, log) => (
        <StatusTag tone={log.result === 'Success' ? 'success' : 'error'}>
          {log.result === 'Success' ? '成功' : '失败'}
        </StatusTag>
      ),
    },
    { key: 'clientIp', title: 'IP', dataIndex: 'clientIp', render: (_, log) => log.clientIp || '-' },
  ];

  return (
    <div className="sh-page">
      <PageHeader
        eyebrow="审计日志"
        title="操作日志"
        description="查看各模块业务操作审计记录，继续复用统一的摘要区、状态徽标和查询表格结构。"
      />

      <div className="sh-section-grid-3">
        <SummaryCard label="总记录数" value={total} meta={`第 ${pageIndex} 页 / 每页 ${pageSize} 条`} />
        <SummaryCard label="成功操作" value={successCount} meta="当前页统计" />
        <SummaryCard label="失败操作" value={failureCount} meta="当前页统计" />
      </div>

      {loading ? <LoadingBlock message="操作日志加载中..." /> : null}
      {error ? <AlertState message={error} /> : null}
      <SearchTablePage<OperationLog>
        rowKey={(log) => String(log.id)}
        columns={operationLogColumns}
        request={request}
      />
    </div>
  );
}

function buildAuditPath(apiRoute: string, query: Record<string, string | number | boolean | null | undefined>): string {
  const search = new URLSearchParams();

  for (const [key, value] of Object.entries(query)) {
    if (value === null || value === undefined || value === '') {
      continue;
    }

    search.set(key, String(value));
  }

  const suffix = search.toString();
  return suffix ? `${apiRoute}?${suffix}` : apiRoute;
}
