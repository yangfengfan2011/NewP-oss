import { useLocalization } from '@icp-platform/core';
import { Link } from 'react-router-dom';
import { usePlatform } from '../contexts/PlatformContext';

const businessShortcuts = [
  { label: 'PlatformShell.DemoHome', to: '/demo', note: 'PlatformShell.DemoHomeNote' },
  { label: 'PlatformShell.DemoList', to: '/demo/list', note: 'PlatformShell.DemoListNote' },
  { label: 'PlatformShell.Files', to: '/files', note: 'PlatformShell.FilesNote' },
];

const platformShortcuts = [
  { label: 'PlatformShell.Roles', to: '/authorization/roles', note: 'PlatformShell.RolesNote' },
  { label: 'PlatformShell.Grants', to: '/authorization/workbench', note: 'PlatformShell.GrantsNote' },
  { label: 'PlatformShell.Config', to: '/config/items', note: 'PlatformShell.ConfigNote' },
  { label: 'PlatformShell.ValueLists', to: '/config/value-lists', note: 'PlatformShell.ValueListsNote' },
  { label: 'PlatformShell.Metadata', to: '/metadata-config/modules', note: 'PlatformShell.MetadataNote' },
  { label: 'PlatformShell.AccessLogs', to: '/audit/access-logs', note: 'PlatformShell.AccessLogsNote' },
];

export default function Home() {
  const { t } = useLocalization();
  const { shellView, uiConfig } = usePlatform();

  const experienceMode = uiConfig.shellExperienceMode;
  const showBusiness = experienceMode === 'hybrid' || experienceMode === 'product';
  const showPlatform = experienceMode === 'hybrid' || experienceMode === 'platform';
  const currentViewLabel = shellView === 'business' ? t('PlatformShell.BusinessView', '业务视图') : t('PlatformShell.PlatformView', '平台视图');
  const visiblePortalCount = Number(showBusiness) + Number(showPlatform);

  return (
    <div className="sh-page">
      <div className="sh-section-grid">
        <div className="sh-summary-card">
          <span className="sh-summary-card-label">{t('PlatformShell.ExperienceMode', '体验模式')}</span>
          <span className="sh-summary-card-value">{t(`PlatformShell.Experience_${uiConfig.shellExperienceMode}`)}</span>
          <span className="sh-summary-card-meta">{t('PlatformShell.PortalCount', '当前可见 {count} 个门户入口', { count: visiblePortalCount })}</span>
        </div>
        <div className="sh-summary-card">
          <span className="sh-summary-card-label">{t('PlatformShell.CurrentView', '当前视图')}</span>
          <span className="sh-summary-card-value">{currentViewLabel}</span>
          <span className="sh-summary-card-meta">{t('PlatformShell.SwitchDescription', '可通过顶部切换在业务/平台视图间移动')}</span>
        </div>
      </div>

      <div className="sh-helper-block">
        <h3 className="sh-helper-block-title">{t('PlatformShell.HomeHelp', '入口页说明')}</h3>
        <p>
          {t('PlatformShell.HomeDescription', '第二阶段不仅统一模块页，也要统一入口页的信息结构。首页优先承载入口、导航和示例说明，不提前做品牌化精修。')}
        </p>
      </div>

      <div className="sh-section-grid sh-section-grid-2">
        {showBusiness && (
          <section className="sh-page-panel">
            <div className="sh-page-panel-header">
              <div>
                <h2 className="sh-page-panel-title">{t('PlatformShell.BusinessPortal', '业务门户')}</h2>
                <div className="sh-page-panel-meta">{t('PlatformShell.BusinessDescription', '面向业务团队的日常入口')}</div>
              </div>
            </div>
            <div className="sh-page-panel-body">
              <ul className="sh-feature-list">
                {businessShortcuts.map((shortcut) => (
                  <li key={shortcut.label} className="sh-feature-item">
                    <span className="sh-feature-icon">→</span>
                    <span>
                      <Link to={shortcut.to}>{t(shortcut.label)}</Link>
                      <br />
                      <span className="sh-page-panel-meta">{t(shortcut.note)}</span>
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
                <h2 className="sh-page-panel-title">{t('PlatformShell.PlatformPortal', '平台门户')}</h2>
                <div className="sh-page-panel-meta">{t('PlatformShell.PlatformDescription', '面向治理、配置与审计管理的入口')}</div>
              </div>
            </div>
            <div className="sh-page-panel-body">
              <ul className="sh-feature-list">
                {platformShortcuts.map((shortcut) => (
                  <li key={shortcut.label} className="sh-feature-item">
                    <span className="sh-feature-icon">→</span>
                    <span>
                      <Link to={shortcut.to}>{t(shortcut.label)}</Link>
                      <br />
                      <span className="sh-page-panel-meta">{t(shortcut.note)}</span>
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
            <h2 className="sh-page-panel-title">{t('PlatformShell.Recent', '最近访问')}</h2>
          </div>
          <div className="sh-page-panel-body">
            <div className="sh-page-empty">{t('PlatformShell.RecentEmpty', '后续可接入最近打开的模块、页面与搜索跳转记录。')}</div>
          </div>
        </section>
        <section className="sh-page-panel">
          <div className="sh-page-panel-header">
            <h2 className="sh-page-panel-title">{t('PlatformShell.CommonActions', '常用操作')}</h2>
          </div>
          <div className="sh-page-panel-body">
            <div className="sh-page-empty">{t('PlatformShell.CommonEmpty', '后续可接入快捷操作、个人收藏入口与系统推荐任务。')}</div>
          </div>
        </section>
      </div>
    </div>
  );
}
