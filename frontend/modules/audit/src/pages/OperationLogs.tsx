import { useEffect, useState } from 'react';

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
    return <div className="sh-page-loading">操作日志加载中...</div>;
  }

  const successCount = logs.filter((log) => log.result === 'Success').length;
  const failureCount = logs.length - successCount;

  return (
    <div className="sh-page">
      <section className="sh-page-header">
        <div className="sh-page-header-main">
          <span className="sh-page-eyebrow">审计日志</span>
          <h1 className="sh-page-title">操作日志</h1>
          <p className="sh-page-description">
            查看各模块业务操作审计记录，继续复用统一的摘要区、状态徽标和面板化表格结构。
          </p>
        </div>
      </section>

      <div className="sh-section-grid-3">
        <div className="sh-summary-card">
          <span className="sh-summary-card-label">总记录数</span>
          <span className="sh-summary-card-value">{total}</span>
          <span className="sh-summary-card-meta">当前页 {logs.length} 条</span>
        </div>

        <div className="sh-summary-card">
          <span className="sh-summary-card-label">成功操作</span>
          <span className="sh-summary-card-value">{successCount}</span>
          <span className="sh-summary-card-meta">当前页统计</span>
        </div>

        <div className="sh-summary-card">
          <span className="sh-summary-card-label">失败操作</span>
          <span className="sh-summary-card-value">{failureCount}</span>
          <span className="sh-summary-card-meta">当前页统计</span>
        </div>
      </div>

      <div className="sh-helper-block">
        <h3 className="sh-helper-block-title">查看说明</h3>
        <p>当前阶段优先保证跨模块页面结构一致，操作详情抽屉、组合筛选和导出等能力留到后续再补齐。</p>
      </div>

      <section className="sh-page-panel">
        <div className="sh-page-panel-header">
          <h2 className="sh-page-panel-title">操作记录</h2>
          <div className="sh-page-panel-meta">第 {pageIndex} 页</div>
        </div>
        <div className="sh-page-panel-body">
          <div className="sh-table-wrap">
            <table className="sh-table">
              <thead>
                <tr>
                  <th>时间</th>
                  <th>用户</th>
                  <th>模块</th>
                  <th>操作类型</th>
                  <th>目标</th>
                  <th>结果</th>
                  <th>IP</th>
                </tr>
              </thead>
              <tbody>
                {logs.map((log) => (
                  <tr key={log.id}>
                    <td>{new Date(log.operationTime).toLocaleString()}</td>
                    <td>{log.userName || '-'}</td>
                    <td>{log.moduleId}</td>
                    <td>{log.operationType}</td>
                    <td>{log.operationTarget || '-'}</td>
                    <td>
                      <span
                        className={`sh-status-badge ${
                          log.result === 'Success'
                            ? 'sh-status-badge-success'
                            : 'sh-status-badge-danger'
                        }`}
                      >
                        {log.result === 'Success' ? '成功' : '失败'}
                      </span>
                    </td>
                    <td>{log.clientIp || '-'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <div className="sh-pagination">
            <div className="sh-pagination-info">共 {total} 条记录</div>
            <div className="sh-pagination-actions">
              <button
                className="sh-btn"
                onClick={() => setPageIndex((page) => Math.max(1, page - 1))}
                disabled={pageIndex === 1}
              >
                上一页
              </button>
              <span className="sh-pagination-info">
                第 {pageIndex} / {Math.ceil(total / pageSize) || 1} 页
              </span>
              <button
                className="sh-btn"
                onClick={() => setPageIndex((page) => page + 1)}
                disabled={pageIndex >= Math.ceil(total / pageSize)}
              >
                下一页
              </button>
            </div>
          </div>
        </div>
      </section>
    </div>
  );
}
