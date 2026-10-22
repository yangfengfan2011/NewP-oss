import { FormEvent, useState } from 'react';
import { useLocalization, formatAuthenticationError } from '@icp-platform/core';
import { useAuth } from '../contexts/AuthContext';

export default function Login() {
  const [ecode, setEcode] = useState('');
  const [password, setPassword] = useState('');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<unknown>();
  const { t } = useLocalization();
  const [missingField, setMissingField] = useState<string>();
  const auth = useAuth();

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setMissingField(undefined);
    setError(undefined);
    if (!ecode || !password) {
      setMissingField(!ecode ? 'Account' : 'Password');
      return;
    }
    setLoading(true);
    try {
      await auth.login(ecode, password);
      window.location.assign('/');
    } catch (loginError) {
      console.error('Login failed:', loginError);
      setError(loginError);
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="sh-login-page">
      <div className="sh-login-brand">
        <span className="sh-login-brand-badge">{t('PlatformAuthentication.BrandBadge', '阶段一视觉基础')}</span>
        <h1>ICP Platform</h1>
        <p>{t('PlatformAuthentication.BrandDescription', '统一业务运营与平台治理入口，提供清晰、稳定、轻量的企业级 Shell 体验。')}</p>
        <ul className="sh-login-brand-list">
          <li>{t('PlatformAuthentication.BrandPortal', '统一进入业务门户与平台门户')}</li>
          <li>{t('PlatformAuthentication.BrandGovernance', '支持权限治理、配置管理与审计巡检')}</li>
          <li>{t('PlatformAuthentication.BrandFoundation', '为后续模块接入提供稳定的共享视觉基础')}</li>
        </ul>
      </div>
      <div className="sh-login-panel">
        <div className="sh-login-card">
          <h2>{t('PlatformAuthentication.LoginTitle', '登录平台')}</h2>
          <p>{t('PlatformAuthentication.LoginDescription', '请使用现有账号凭据登录统一 Shell。')}</p>
          <form onSubmit={handleSubmit} noValidate>
            <div className="sh-form-group">
              <label className="sh-form-label" htmlFor="ecode">
                {t('PlatformAuthentication.Account', '登录账号（ECODE）')}
              </label>
              <input
                id="ecode"
                type="text"
                className="sh-form-input"
                value={ecode}
                onChange={(e) => setEcode(e.target.value)}
                required
                placeholder={t('PlatformAuthentication.AccountPlaceholder', '请输入登录账号')}
              />
            </div>
            <div className="sh-form-group sh-form-group-lg">
              <label className="sh-form-label" htmlFor="password">
                {t('PlatformAuthentication.Password', '密码')}
              </label>
              <input
                id="password"
                type="password"
                className="sh-form-input"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                required
                placeholder={t('PlatformAuthentication.PasswordPlaceholder', '请输入密码')}
              />
            </div>
            {missingField && <div className="sh-form-error" role="alert">{t('PlatformAuthentication.RequiredField', undefined, { field: t(`PlatformAuthentication.${missingField}`) })}</div>}
            {error != null && <div className="sh-form-error" role="alert">{formatAuthenticationError(error, t)}</div>}
            <button
              type="submit"
              disabled={loading}
              className="sh-btn sh-btn-primary sh-form-submit"
            >
              {loading ? t('PlatformAuthentication.SigningIn', '登录中...') : t('PlatformAuthentication.SignIn', '登录')}
            </button>
          </form>
        </div>
      </div>
    </div>
  );
}
