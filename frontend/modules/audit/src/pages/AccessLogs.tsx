import { useEffect, useState } from 'react';
import { PageHeader, SummaryCard, Panel, Badge, Pagination, LoadingState, TableWrap } from '@icp-platform/ui';
import type { TableColumn } from '@icp-platform/ui';

interface AccessLog {
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

export default function AccessLogs() {
  const [logs, setLogs] = useState<AccessLog[]>([]);
  const [loading, setLoading] = useState(true);
  const [total, setTotal] = useState(0);
  const [pageIndex, setPageIndex] = useState(1);
  const [pageSize] = useState(20);

  useEffect(() => {
    void fetchLogs();
  }, [pageIndex]);

  const fetchLogs = async () => {
    setLoading(true);
    try {
      const response = await fetch(
        `/api/audit/access-logs?pageIndex=${pageIndex}&pageSize=${pageSize}`,
      );
      const data = await response.json();
      setLogs(data.items);
      setTotal(data.total);
    } catch (error) {
      console.error('Failed to fetch access logs:', error);
    } finally {
      setLoading(false);
    }
  };

  if (loading) {
    return <LoadingState message="访问日志加载中..." />;
  }

  const successCount = logs.filter((log) => log.statusCode < 400).length;
  const errorCount = logs.length - successCount;
  const avgResponseTime =
    logs.length > 0
      ? Math.round(logs.reduce((sum, log) => sum + log.responseTime, 0) / logs.length)
      : 0;

  const accessLogColumns: TableColumn<AccessLog>[] = [
    { key: 'accessTime', title: '时间', render: (log) => new Date(log.accessTime).toLocaleString() },
    { key: 'userName', title: '用户', render: (log) => log.userName || '-' },
    { key: 'httpMethod', title: '方法', render: (log) => <span className="sh-code">{log.httpMethod}</span> },
    { key: 'requestPath', title: '路径', render: (log) => <span className="sh-code">{log.requestPath}</span> },
    {
      key: 'statusCode',
      title: '状态码',
      render: (log) => (
        <Badge variant={log.statusCode < 400 ? 'success' : 'danger'}>
          {log.statusCode}
        </Badge>
      ),
    },
    { key: 'responseTime', title: '响应时间', render: (log) => `${log.responseTime}ms` },
    { key: 'clientIp', title: 'IP', render: (log) => log.clientIp || '-' },
  ];

  return (
    <div className="sh-page">
      <PageHeader
        eyebrow="审计日志"
        title="访问日志"
        description="查看系统级 API 请求与访问记录，保留原有分页与数据加载逻辑，并统一为第二阶段的中层页面结构。"
      />

      <div className="sh-section-grid">
        <SummaryCard label="总记录数" value={total} meta={`当前页 ${logs.length} 条`} />

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

      <div className="sh-helper-block">
        <h3 className="sh-helper-block-title">查看说明</h3>
        <p>第二阶段优先统一审计页面的信息密度与状态表达，筛选器与高级检索能力在后续按真实需求继续补充。</p>
      </div>

      <Panel title="访问记录" meta={`第 ${pageIndex} 页`}>
        <TableWrap<AccessLog>
          columns={accessLogColumns}
          data={logs}
          rowKey={(log) => String(log.id)}
        />
        <Pagination
          current={pageIndex}
          total={total}
          pageSize={pageSize}
          onPageChange={setPageIndex}
        />
      </Panel>
    </div>
  );
}
