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

interface AccessLog {
  [key: string]: unknown;
  id: number;
  accessTime: string;
  userId?: string;
  userName?: string;
  requestPath: string;
  httpMethod: string;
  statusCode: number;
  responseTime: number;
  clientIp?: string;
  userAgent?: string;
}

interface AuditListResponse<TItem> {
  items?: TItem[];
  total?: number;
}

export default function AccessLogs() {
  const [logs, setLogs] = useState<AccessLog[]>([]);
  const [total, setTotal] = useState(0);
  const [pageIndex, setPageIndex] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const request = useMemo(
    () =>
      createSearchTableRequest<Record<string, unknown>, AccessLog, AccessLog>({
        buildQuery: () => ({}),
        load: async (query) => {
          try {
            setLoading(true);
            setError(null);
            const nextPageIndex = Number(query.pageIndex ?? 1);
            const nextPageSize = Number(query.pageSize ?? 20);
            const response = await fetch(buildAuditPath('/api/audit/access-logs', {
              pageIndex: nextPageIndex,
              pageSize: nextPageSize,
            }));

            if (!response.ok) {
              throw new Error('访问日志加载失败');
            }

            const data = await response.json() as AuditListResponse<AccessLog>;
            const items = Array.isArray(data.items) ? data.items : [];
            const nextTotal = typeof data.total === 'number' ? data.total : items.length;

            setLogs(items);
            setTotal(nextTotal);
            setPageIndex(nextPageIndex);
            setPageSize(nextPageSize);

            return { items, total: nextTotal };
          } catch (loadError) {
            const message = loadError instanceof Error ? loadError.message : '访问日志加载失败';
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

  const successCount = logs.filter((log) => log.statusCode < 400).length;
  const errorCount = logs.length - successCount;
  const avgResponseTime =
    logs.length > 0
      ? Math.round(logs.reduce((sum, log) => sum + log.responseTime, 0) / logs.length)
      : 0;

  const accessLogColumns: SearchTablePageProps<AccessLog>['columns'] = [
    { key: 'accessTime', title: '时间', dataIndex: 'accessTime', render: (_, log) => new Date(log.accessTime).toLocaleString() },
    { key: 'userName', title: '用户', dataIndex: 'userName', render: (_, log) => log.userName || '-' },
    { key: 'httpMethod', title: '方法', dataIndex: 'httpMethod', render: (_, log) => <span className="sh-code">{log.httpMethod}</span> },
    { key: 'requestPath', title: '路径', dataIndex: 'requestPath', render: (_, log) => <span className="sh-code">{log.requestPath}</span> },
    {
      key: 'statusCode',
      title: '状态码',
      dataIndex: 'statusCode',
      render: (_, log) => (
        <StatusTag tone={log.statusCode < 400 ? 'success' : 'error'}>
          {log.statusCode}
        </StatusTag>
      ),
    },
    { key: 'responseTime', title: '响应时间', dataIndex: 'responseTime', render: (_, log) => `${log.responseTime}ms` },
    { key: 'clientIp', title: 'IP', dataIndex: 'clientIp', render: (_, log) => log.clientIp || '-' },
  ];

  return (
    <div className="sh-page">
      <PageHeader
        eyebrow="审计日志"
        title="访问日志"
        description="查看系统级 API 请求与访问记录，保留原有分页与数据加载逻辑，并统一为查询表格结构。"
      />

      <div className="sh-section-grid">
        <SummaryCard label="总记录数" value={total} meta={`第 ${pageIndex} 页 / 每页 ${pageSize} 条`} />

        <div className="sh-meta-block">
          <div className="sh-meta-row">
            <span className="sh-meta-label">成功请求</span>
            <span className="sh-meta-value">{successCount}</span>
          </div>
          <div className="sh-meta-row">
            <span className="sh-meta-label">失败请求</span>
            <span className="sh-meta-value">{errorCount}</span>
          </div>
          <div className="sh-meta-row">
            <span className="sh-meta-label">平均响应</span>
            <span className="sh-meta-value">{avgResponseTime}ms</span>
          </div>
        </div>
      </div>

      {loading ? <LoadingBlock message="访问日志加载中..." /> : null}
      {error ? <AlertState message={error} /> : null}
      <SearchTablePage<AccessLog>
        rowKey={(log) => String(log.id)}
        columns={accessLogColumns}
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
