import { useState, useEffect } from 'react';
import { fetchModuleStatus, ModuleStatus } from '../services/moduleTemplateSampleService';

export default function ModuleTemplateSamplePage() {
  const [status, setStatus] = useState<ModuleStatus | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    loadStatus();
  }, []);

  const loadStatus = async () => {
    try {
      setLoading(true);
      setError(null);
      const data = await fetchModuleStatus();
      setStatus(data);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unknown error');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="sh-page">
      <section className="sh-page-header">
        <div className="sh-page-header-main">
          <span className="sh-page-eyebrow">Module Template Sample</span>
          <h1 className="sh-page-title">模块模板示例</h1>
          <p className="sh-page-description">
            这是一个前端模块模板示例，展示如何创建和集成新的前端模块。
          </p>
        </div>
      </section>

      <div className="sh-section-grid">
        <div className="sh-summary-card">
          <span className="sh-summary-card-label">模块ID</span>
          <span className="sh-summary-card-value">
            {loading ? '加载中...' : status?.moduleId || 'N/A'}
          </span>
          <span className="sh-summary-card-meta">
            {status?.displayName || '模块名称'}
          </span>
        </div>
        <div className="sh-summary-card">
          <span className="sh-summary-card-label">API 端点</span>
          <span className="sh-summary-card-value">/api/module-template-sample/status</span>
          <span className="sh-summary-card-meta">后端服务接口</span>
        </div>
      </div>

      {error && (
        <div className="sh-helper-block" style={{ borderColor: '#ef4444' }}>
          <h3 className="sh-helper-block-title">错误</h3>
          <p style={{ color: '#ef4444' }}>{error}</p>
        </div>
      )}

      {status && (
        <div className="sh-helper-block">
          <h3 className="sh-helper-block-title">后端消息</h3>
          <p>{status.message}</p>
          <button
            onClick={loadStatus}
            style={{
              marginTop: '1rem',
              padding: '0.5rem 1rem',
              background: '#3b82f6',
              color: 'white',
              border: 'none',
              borderRadius: '4px',
              cursor: 'pointer'
            }}
          >
            刷新状态
          </button>
        </div>
      )}
    </div>
  );
}
