import { useAuth } from '../contexts/AuthContext';
import { usePlatform } from '../contexts/PlatformContext';

export function Header() {
  const auth = useAuth();
  const { shellView, setShellView, canSwitchShellView } = usePlatform();

  const renderShellViewButton = (view: 'business' | 'platform', label: string) => {
    const isActive = shellView === view;

    return (
      <button
        type="button"
        onClick={() => setShellView(view)}
        disabled={isActive}
        className={`sh-view-switch-btn ${isActive ? 'active' : ''}`}
      >
        {label}
      </button>
    );
  };

  return (
    <header className="sh-header">
      <div className="sh-header-brand">
        <div>
          <div className="sh-header-meta">统一业务与平台工作台</div>
          <h1 className="sh-header-title">ICP Platform</h1>
        </div>
      </div>
      <div className="sh-header-actions">
        {canSwitchShellView && (
          <div className="sh-view-switch">
            {renderShellViewButton('business', '业务视图')}
            {renderShellViewButton('platform', '平台视图')}
          </div>
        )}
        {auth.isAuthenticated && auth.user && (
          <div className="sh-header-user">
            <span className="sh-header-meta">欢迎，{auth.user.displayName}</span>
            <button onClick={() => auth.logout()} className="sh-btn">
              退出登录
            </button>
          </div>
        )}
      </div>
    </header>
  );
}
