import { useEffect, useState } from 'react';
import { PageHeader, SummaryCard, Panel, Badge, Pagination, LoadingState, TableWrap } from '@icp-platform/ui';
import type { TableColumn } from '@icp-platform/ui';

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

export default function OperationLogs() {
  const [logs, setLogs] = useState<OperationLog[]>([]);
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
        `/api/audit/operation-logs?pageIndex=${pageIndex}&pageSize=${pageSize}`,
      );
      const data = await response.json();
      setLogs(data.items);
      setTotal(data.total);
    } catch (error) {
      console.error('Failed to fetch operation logs:', error);
    } finally {
      setLoading(false);
    }
  };

  if (loading) {
    return <LoadingState message="操作日志加载中..." />;
  }

  const successCount = logs.filter((log) => log.result === 'Success').length;
  const failureCount = logs.length - successCount;

  const operationLogColumns: TableColumn<OperationLog>[] = [
    { key: 'operationTime', title: '时间', render: (log) => new Date(log.operationTime).toLocaleString() },
    { key: 'userName', title: '用户', render: (log) => log.userName || '-' },
    { key: 'moduleId', title: '模块' },
    { key: 'operationType', title: '操作类型' },
    { key: 'operationTarget', title: '目标', render: (log) => log.operationTarget || '-' },
    {
      key: 'result',
      title: '结果',
      render: (log) => (
        <Badge variant={log.result === 'Success' ? 'success' : 'danger'}>
          {log.result === 'Success' ? '成功' : '失败'}
        </Badge>
      ),
    },
    { key: 'clientIp', title: 'IP', render: (log) => log.clientIp || '-' },
  ];

  return (
    <div className="sh-page">
      <PageHeader
        eyebrow="审计日志"
        title="操作日志"
        description="查看各模块业务操作审计记录，继续复用统一的摘要区、状态徽标和面板化表格结构。"
      />

      <div className="sh-section-grid-3">
        <SummaryCard label="总记录数" value={total} meta={`当前页 ${logs.length} 条`} />
        <SummaryCard label="成功操作" value={successCount} meta="当前页统计" />
        <SummaryCard label="失败操作" value={failureCount} meta="当前页统计" />
      </div>

      <div className="sh-helper-block">
        <h3 className="sh-helper-block-title">查看说明</h3>
        <p>当前阶段优先保证跨模块页面结构一致，操作详情抽屉、组合筛选和导出等能力留到后续再补齐。</p>
      </div>

      <Panel title="操作记录" meta={`第 ${pageIndex} 页`}>
        <TableWrap<OperationLog>
          columns={operationLogColumns}
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
