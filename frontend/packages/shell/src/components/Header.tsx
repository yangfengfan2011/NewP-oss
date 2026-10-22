import { useAuth } from '../contexts/AuthContext';
import { useLocalization } from '@icp-platform/core';
import { LanguageSelect } from './LanguageSelect';

export function Header() {
  const auth = useAuth();
  const { t } = useLocalization();

  return (
    <header className="np-app-header">
      <div className="np-header-brand">
        <span className="np-header-logo">ICP</span>
      </div>
      <div className="np-header-actions">
        <LanguageSelect />
        {auth.isAuthenticated && auth.user && (
          <div className="np-header-session">
            <span className="np-header-avatar" aria-hidden="true">{auth.user.ename.slice(0, 1)}</span>
            <span className="np-header-user">{t('PlatformShell.Welcome', '欢迎，{name}', { name: auth.user.ename })}</span>
            <button
              type="button"
              className="np-header-logout"
              onClick={() => void Promise.resolve(auth.logout()).catch(() => undefined)}
            >
              {t('PlatformAuthentication.SignOut', '退出登录')}
            </button>
          </div>
        )}
      </div>
    </header>
  );
}
