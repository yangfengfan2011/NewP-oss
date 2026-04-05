export default function DemoHome() {
  return (
    <div className="sh-page">
      <section className="sh-page-header">
        <div className="sh-page-header-main">
          <span className="sh-page-eyebrow">演示模块</span>
          <h1 className="sh-page-title">概览</h1>
          <p className="sh-page-description">
            展示如何通过 SDK 接入平台，包括路由、菜单、权限和国际化支持，作为业务模块接入的参考首页。
          </p>
        </div>
      </section>

      <div className="sh-section-grid">
        <div className="sh-summary-card">
          <span className="sh-summary-card-label">模块状态</span>
          <span className="sh-summary-card-value">已激活</span>
          <span className="sh-summary-card-meta">通过 SDK 完整接入</span>
        </div>
        <div className="sh-summary-card">
          <span className="sh-summary-card-label">集成特性</span>
          <span className="sh-summary-card-value">4 项</span>
          <span className="sh-summary-card-meta">路由、菜单、权限、国际化</span>
        </div>
      </div>

      <section className="sh-page-panel">
        <div className="sh-page-panel-header">
          <h2 className="sh-page-panel-title">模块特性</h2>
        </div>
        <div className="sh-page-panel-body">
          <ul className="sh-feature-list">
            <li className="sh-feature-item">
              <span className="sh-feature-icon">✓</span>
              <span>通过 SDK 注册路由</span>
            </li>
            <li className="sh-feature-item">
              <span className="sh-feature-icon">✓</span>
              <span>通过 SDK 注册菜单</span>
            </li>
            <li className="sh-feature-item">
              <span className="sh-feature-icon">✓</span>
              <span>声明权限点</span>
            </li>
            <li className="sh-feature-item">
              <span className="sh-feature-icon">✓</span>
              <span>国际化支持</span>
            </li>
          </ul>
        </div>
      </section>

      <div className="sh-helper-block">
        <h3 className="sh-helper-block-title">开发提示</h3>
        <p>此模块演示了标准的平台接入模式。新模块可以直接参考这套结构继续扩展页面与功能。</p>
      </div>
    </div>
  );
}
