import { useState, useEffect } from 'react';

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
    fetchLogs();
  }, [pageIndex]);

  const fetchLogs = async () => {
    setLoading(true);
    try {
      const response = await fetch(
        `/api/audit/operation-logs?pageIndex=${pageIndex}&pageSize=${pageSize}`
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
    return <div>加载中...</div>;
  }

  return (
    <div>
      <h1>操作日志</h1>
      <div>
        <table style={{ width: '100%', borderCollapse: 'collapse' }}>
          <thead>
            <tr style={{ borderBottom: '2px solid #ddd' }}>
              <th style={{ padding: '8px', textAlign: 'left' }}>时间</th>
              <th style={{ padding: '8px', textAlign: 'left' }}>用户</th>
              <th style={{ padding: '8px', textAlign: 'left' }}>模块</th>
              <th style={{ padding: '8px', textAlign: 'left' }}>操作类型</th>
              <th style={{ padding: '8px', textAlign: 'left' }}>目标</th>
              <th style={{ padding: '8px', textAlign: 'left' }}>结果</th>
              <th style={{ padding: '8px', textAlign: 'left' }}>IP</th>
            </tr>
          </thead>
          <tbody>
            {logs.map((log) => (
              <tr key={log.id} style={{ borderBottom: '1px solid #eee' }}>
                <td style={{ padding: '8px' }}>
                  {new Date(log.operationTime).toLocaleString()}
                </td>
                <td style={{ padding: '8px' }}>{log.userName || '-'}</td>
                <td style={{ padding: '8px' }}>{log.moduleId}</td>
                <td style={{ padding: '8px' }}>{log.operationType}</td>
                <td style={{ padding: '8px' }}>{log.operationTarget || '-'}</td>
                <td style={{ padding: '8px' }}>
                  <span style={{ color: log.result === 'Success' ? 'green' : 'red' }}>
                    {log.result}
                  </span>
                </td>
                <td style={{ padding: '8px' }}>{log.clientIp || '-'}</td>
              </tr>
            ))}
          </tbody>
        </table>
        <div style={{ marginTop: '16px' }}>
          <button
            onClick={() => setPageIndex(p => Math.max(1, p - 1))}
            disabled={pageIndex === 1}
          >
            上一页
          </button>
          <span style={{ margin: '0 16px' }}>
            第 {pageIndex} 页，共 {Math.ceil(total / pageSize)} 页
          </span>
          <button
            onClick={() => setPageIndex(p => p + 1)}
            disabled={pageIndex >= Math.ceil(total / pageSize)}
          >
            下一页
          </button>
        </div>
      </div>
    </div>
  );
}
