import { useAuth } from '../contexts/AuthContext';

export function Header() {
  const auth = useAuth();

  return (
    <header className="np-app-header">
      <div className="np-header-brand">
        <span className="np-header-logo">ICP</span>
      </div>
      <div className="np-header-actions">
        {auth.isAuthenticated && auth.user && (
          <div className="np-header-session">
            <span className="np-header-avatar" aria-hidden="true">{auth.user.ename.slice(0, 1)}</span>
            <span className="np-header-user">欢迎，{auth.user.ename}</span>
            <button
              type="button"
              className="np-header-logout"
              onClick={() => void Promise.resolve(auth.logout()).catch(() => undefined)}
            >
              退出登录
            </button>
          </div>
        )}
      </div>
    </header>
  );
}
