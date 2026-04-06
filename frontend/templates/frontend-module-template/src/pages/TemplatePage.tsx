import { useState, useEffect } from 'react';
import { fetchTemplateStatus, TemplateStatus } from '../services/templateService';

/**
 * 模板页面组件
 *
 * 这是一个示例页面，展示了基本的页面结构和样式类的使用。
 * 使用平台提供的 CSS 类来保持一致的视觉风格。
 */
export default function TemplatePage() {
  const [status, setStatus] = useState<TemplateStatus | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    loadStatus();
  }, []);

  const loadStatus = async () => {
    try {
      setLoading(true);
      setError(null);
      const data = await fetchTemplateStatus();
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
          <span className="sh-page-eyebrow">模板模块</span>
          <h1 className="sh-page-title">模板页面</h1>
          <p className="sh-page-description">
            这是一个前端模块模板页面，用于快速创建新的业务模块。
          </p>
        </div>
      </section>

      <div className="sh-section-grid">
        <div className="sh-summary-card">
          <span className="sh-summary-card-label">模块状态</span>
          <span className="sh-summary-card-value">
            {loading ? '加载中...' : status?.message || 'N/A'}
          </span>
          <span className="sh-summary-card-meta">
            {status?.moduleId || '待定制'}
          </span>
        </div>
        <div className="sh-summary-card">
          <span className="sh-summary-card-label">API 端点</span>
          <span className="sh-summary-card-value">/api/template-module/status</span>
          <span className="sh-summary-card-meta">后端服务接口</span>
        </div>
      </div>

      {error && (
        <div className="sh-helper-block" style={{ borderColor: '#ef4444' }}>
          <h3 className="sh-helper-block-title">错误</h3>
          <p style={{ color: '#ef4444' }}>{error}</p>
        </div>
      )}

      <section className="sh-page-panel">
        <div className="sh-page-panel-header">
          <h2 className="sh-page-panel-title">快速开始</h2>
        </div>
        <div className="sh-page-panel-body">
          <ul className="sh-feature-list">
            <li className="sh-feature-item">
              <span className="sh-feature-icon">1</span>
              <span>复制模板到 frontend/modules/your-module-name</span>
            </li>
            <li className="sh-feature-item">
              <span className="sh-feature-icon">2</span>
              <span>修改 package.json 中的包名和描述</span>
            </li>
            <li className="sh-feature-item">
              <span className="sh-feature-icon">3</span>
              <span>修改 src/index.ts 中的模块配置</span>
            </li>
            <li className="sh-feature-item">
              <span className="sh-feature-icon">4</span>
              <span>实现你的页面组件和服务</span>
            </li>
            <li className="sh-feature-item">
              <span className="sh-feature-icon">5</span>
              <span>在主应用中注册模块</span>
            </li>
          </ul>
        </div>
      </section>

      <div className="sh-helper-block">
        <h3 className="sh-helper-block-title">开发提示</h3>
        <p>
          使用平台提供的 CSS 类（sh-* 前缀）来保持一致的视觉风格。
          参考 demo 模块了解更多页面布局和组件使用示例。
        </p>
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
    </div>
  );
}
