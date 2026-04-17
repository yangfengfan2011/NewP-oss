import { Link } from 'react-router-dom';
import { usePlatform } from '../contexts/PlatformContext';

const businessShortcuts = [
  { label: '进入演示首页', to: '/demo', note: '查看业务模块接入后的首页模式' },
  { label: '查看演示列表', to: '/demo/list', note: '参考第二阶段标准列表页结构' },
  { label: '进入文件中心', to: '/files', note: '体验上传、列表与操作区结构' },
];

const platformShortcuts = [
  { label: '角色管理', to: '/authorization/roles', note: '查看权限状态与角色页骨架' },
  { label: '权限列表', to: '/authorization/permissions', note: '查看平台侧目录式列表页结构' },
  { label: '配置管理', to: '/config/items', note: '查看配置页摘要区与表格区组合' },
  { label: '值列表管理', to: '/config/value-lists', note: '管理基础值列表数据与表格操作' },
  { label: '元数据配置中心', to: '/metadata-config/modules', note: '进入 SettingDefinition 元数据定义工作台' },
  { label: '访问日志', to: '/audit/access-logs', note: '查看审计页摘要与分页结构' },
];

export default function Home() {
  const { shellView, uiConfig } = usePlatform();

  const experienceMode = uiConfig.shellExperienceMode;
  const showBusiness = experienceMode === 'hybrid' || experienceMode === 'product';
  const showPlatform = experienceMode === 'hybrid' || experienceMode === 'platform';
  const currentViewLabel = shellView === 'business' ? '业务视图' : '平台视图';
  const visiblePortalCount = Number(showBusiness) + Number(showPlatform);

  return (
    <div className="sh-page">
      <section className="sh-page-header">
        <div className="sh-page-header-main">
          <span className="sh-page-eyebrow">{currentViewLabel}</span>
          <h1 className="sh-page-title">欢迎使用 ICP Platform</h1>
          <p className="sh-page-description">
            在统一 Shell 中快速进入业务门户或平台门户，聚合常用入口、最近动作和代表性模块页面，
            让第二阶段的页面模式从入口层开始保持一致。
          </p>
        </div>
      </section>

      <div className="sh-section-grid">
        <div className="sh-summary-card">
          <span className="sh-summary-card-label">体验模式</span>
          <span className="sh-summary-card-value">{uiConfig.shellExperienceMode}</span>
          <span className="sh-summary-card-meta">当前可见 {visiblePortalCount} 个门户入口</span>
        </div>
        <div className="sh-summary-card">
          <span className="sh-summary-card-label">当前视图</span>
          <span className="sh-summary-card-value">{currentViewLabel}</span>
          <span className="sh-summary-card-meta">可通过顶部切换在业务/平台视图间移动</span>
        </div>
      </div>

      <div className="sh-helper-block">
        <h3 className="sh-helper-block-title">入口页说明</h3>
        <p>
          第二阶段不仅统一模块页，也要统一入口页的信息结构。首页优先承载入口、导航和示例说明，
          不提前做品牌化精修。
        </p>
      </div>

      <div className="sh-section-grid sh-section-grid-2">
        {showBusiness && (
          <section className="sh-page-panel">
            <div className="sh-page-panel-header">
              <div>
                <h2 className="sh-page-panel-title">业务门户</h2>
                <div className="sh-page-panel-meta">面向业务团队的日常入口</div>
              </div>
            </div>
            <div className="sh-page-panel-body">
              <ul className="sh-feature-list">
                {businessShortcuts.map((shortcut) => (
                  <li key={shortcut.label} className="sh-feature-item">
                    <span className="sh-feature-icon">→</span>
                    <span>
                      <Link to={shortcut.to}>{shortcut.label}</Link>
                      <br />
                      <span className="sh-page-panel-meta">{shortcut.note}</span>
                    </span>
                  </li>
                ))}
              </ul>
            </div>
          </section>
        )}

        {showPlatform && (
          <section className="sh-page-panel">
            <div className="sh-page-panel-header">
              <div>
                <h2 className="sh-page-panel-title">平台门户</h2>
                <div className="sh-page-panel-meta">面向治理、配置与审计管理的入口</div>
              </div>
            </div>
            <div className="sh-page-panel-body">
              <ul className="sh-feature-list">
                {platformShortcuts.map((shortcut) => (
                  <li key={shortcut.label} className="sh-feature-item">
                    <span className="sh-feature-icon">→</span>
                    <span>
                      <Link to={shortcut.to}>{shortcut.label}</Link>
                      <br />
                      <span className="sh-page-panel-meta">{shortcut.note}</span>
                    </span>
                  </li>
                ))}
              </ul>
            </div>
          </section>
        )}
      </div>

      <div className="sh-section-grid sh-section-grid-2">
        <section className="sh-page-panel">
          <div className="sh-page-panel-header">
            <h2 className="sh-page-panel-title">最近访问</h2>
          </div>
          <div className="sh-page-panel-body">
            <div className="sh-page-empty">后续可接入最近打开的模块、页面与搜索跳转记录。</div>
          </div>
        </section>
        <section className="sh-page-panel">
          <div className="sh-page-panel-header">
            <h2 className="sh-page-panel-title">常用操作</h2>
          </div>
          <div className="sh-page-panel-body">
            <div className="sh-page-empty">后续可接入快捷操作、个人收藏入口与系统推荐任务。</div>
          </div>
        </section>
      </div>
    </div>
  );
}
