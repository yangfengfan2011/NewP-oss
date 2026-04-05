import { useEffect, useState } from 'react';

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
    return <div className="sh-page-loading">访问日志加载中...</div>;
  }

  const successCount = logs.filter((log) => log.statusCode < 400).length;
  const errorCount = logs.length - successCount;
  const avgResponseTime =
    logs.length > 0
      ? Math.round(logs.reduce((sum, log) => sum + log.responseTime, 0) / logs.length)
      : 0;

  return (
    <div className="sh-page">
      <section className="sh-page-header">
        <div className="sh-page-header-main">
          <span className="sh-page-eyebrow">审计日志</span>
          <h1 className="sh-page-title">访问日志</h1>
          <p className="sh-page-description">
            查看系统级 API 请求与访问记录，保留原有分页与数据加载逻辑，并统一为第二阶段的中层页面结构。
          </p>
        </div>
      </section>

      <div className="sh-section-grid">
        <div className="sh-summary-card">
          <span className="sh-summary-card-label">总记录数</span>
          <span className="sh-summary-card-value">{total}</span>
          <span className="sh-summary-card-meta">当前页 {logs.length} 条</span>
        </div>

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

      <section className="sh-page-panel">
        <div className="sh-page-panel-header">
          <h2 className="sh-page-panel-title">访问记录</h2>
          <div className="sh-page-panel-meta">第 {pageIndex} 页</div>
        </div>
        <div className="sh-page-panel-body">
          <div className="sh-table-wrap">
            <table className="sh-table">
              <thead>
                <tr>
                  <th>时间</th>
                  <th>用户</th>
                  <th>方法</th>
                  <th>路径</th>
                  <th>状态码</th>
                  <th>响应时间</th>
                  <th>IP</th>
                </tr>
              </thead>
              <tbody>
                {logs.map((log) => (
                  <tr key={log.id}>
                    <td>{new Date(log.accessTime).toLocaleString()}</td>
                    <td>{log.userName || '-'}</td>
                    <td>
                      <span className="sh-code">{log.httpMethod}</span>
                    </td>
                    <td>
                      <span className="sh-code">{log.requestPath}</span>
                    </td>
                    <td>
                      <span
                        className={`sh-status-badge ${
                          log.statusCode < 400
                            ? 'sh-status-badge-success'
                            : 'sh-status-badge-danger'
                        }`}
                      >
                        {log.statusCode}
                      </span>
                    </td>
                    <td>{log.responseTime}ms</td>
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
