import { FormEvent, useState } from 'react';
import { useAuth } from '../contexts/AuthContext';

export default function Login() {
  const [ecode, setEcode] = useState('');
  const [password, setPassword] = useState('');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const auth = useAuth();

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setLoading(true);
    setError('');
    try {
      await auth.login(ecode, password);
      window.location.assign('/');
    } catch (loginError) {
      console.error('Login failed:', loginError);
      const code = loginError instanceof Error ? loginError.message : '';
      setError(code === 'AUTH_ADMISSION_DENIED'
        ? '账号当前不满足登录条件，请联系管理员确认账号和业务身份。'
        : ['Persistence.CacheUnavailable', 'AUTH_SESSION_STORE_UNAVAILABLE', 'AUTH_ADMISSION_UNAVAILABLE'].includes(code)
          ? '登录服务暂时不可用，请稍后重试。'
          : '登录失败，请检查用户名和密码。');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="sh-login-page">
      <div className="sh-login-brand">
        <span className="sh-login-brand-badge">阶段一视觉基础</span>
        <h1>ICP Platform</h1>
        <p>统一业务运营与平台治理入口，提供清晰、稳定、轻量的企业级 Shell 体验。</p>
        <ul className="sh-login-brand-list">
          <li>统一进入业务门户与平台门户</li>
          <li>支持权限治理、配置管理与审计巡检</li>
          <li>为后续模块接入提供稳定的共享视觉基础</li>
        </ul>
      </div>
      <div className="sh-login-panel">
        <div className="sh-login-card">
          <h2>登录平台</h2>
          <p>请使用现有账号凭据登录统一 Shell。</p>
          <form onSubmit={handleSubmit}>
            <div className="sh-form-group">
              <label className="sh-form-label" htmlFor="ecode">
                登录账号（ECODE）
              </label>
              <input
                id="ecode"
                type="text"
                className="sh-form-input"
                value={ecode}
                onChange={(e) => setEcode(e.target.value)}
                required
                placeholder="请输入登录账号"
              />
            </div>
            <div className="sh-form-group sh-form-group-lg">
              <label className="sh-form-label" htmlFor="password">
                密码
              </label>
              <input
                id="password"
                type="password"
                className="sh-form-input"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                required
                placeholder="请输入密码"
              />
            </div>
            {error && <div className="sh-form-error">{error}</div>}
            <button
              type="submit"
              disabled={loading}
              className="sh-btn sh-btn-primary sh-form-submit"
            >
              {loading ? '登录中...' : '登录'}
            </button>
          </form>
        </div>
      </div>
    </div>
  );
}
