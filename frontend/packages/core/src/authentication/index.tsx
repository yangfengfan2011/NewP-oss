import { FormEvent, useCallback, useEffect, useState, type ReactNode } from 'react';
import type { AuthUser } from '@icp-platform/sdk';
import { invalidatePlatformAntiforgeryToken, platformFetch } from '../http';

const PASSWORD_CHANGE_PATH = '/account/change-password';

export function resolveAuthenticationDestination(
  mustChangePassword: boolean,
  requestedPath: string,
): string {
  if (mustChangePassword) {
    return PASSWORD_CHANGE_PATH;
  }

  return requestedPath === PASSWORD_CHANGE_PATH ? '/' : requestedPath;
}

export interface AuthenticationState {
  isAuthenticated: boolean;
  isInitialized: boolean;
  user?: AuthUser;
  login: (username: string, password: string) => Promise<void>;
  logout: () => Promise<void>;
  changePassword: (currentPassword: string, newPassword: string) => Promise<void>;
}

async function readCurrentUser(): Promise<AuthUser | undefined> {
  const response = await platformFetch('/api/auth/current-user', {
    method: 'GET',
    headers: { Accept: 'application/json' },
  });
  return response.ok ? await response.json() as AuthUser : undefined;
}

async function readProblemCode(response: Response): Promise<string> {
  try {
    const problem = await response.json() as { code?: string };
    return problem.code ?? `HTTP_${response.status}`;
  } catch {
    return `HTTP_${response.status}`;
  }
}

export function useAuthenticationState(): AuthenticationState {
  const [user, setUser] = useState<AuthUser>();
  const [isInitialized, setIsInitialized] = useState(false);

  const refresh = useCallback(async () => {
    const current = await readCurrentUser();
    setUser(current);
    return current;
  }, []);

  useEffect(() => {
    let active = true;
    void readCurrentUser()
      .then((current) => {
        if (active) setUser(current);
      })
      .catch(() => {
        if (active) setUser(undefined);
      })
      .finally(() => {
        if (active) setIsInitialized(true);
      });
    return () => {
      active = false;
    };
  }, []);

  const login = useCallback(async (username: string, password: string) => {
    const response = await platformFetch('/api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username, password }),
    });
    if (!response.ok) throw new Error(await readProblemCode(response));
    invalidatePlatformAntiforgeryToken();
    if (!await refresh()) throw new Error('AUTH_SESSION_INVALID');
  }, [refresh]);

  const logout = useCallback(async () => {
    try {
      await platformFetch('/api/auth/logout', { method: 'POST' });
    } finally {
      invalidatePlatformAntiforgeryToken();
      setUser(undefined);
    }
  }, []);

  const changePassword = useCallback(async (currentPassword: string, newPassword: string) => {
    if (!user) throw new Error('AUTH_SESSION_INVALID');
    const response = await platformFetch('/api/identity/me/password-change', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ currentPassword, newPassword, expectedRevision: user.revision }),
    });
    if (!response.ok) throw new Error(await readProblemCode(response));
    invalidatePlatformAntiforgeryToken();
    const current = await refresh();
    if (!current || current.mustChangePassword) throw new Error('AUTH_SESSION_ROTATION_FAILED');
  }, [refresh, user]);

  return {
    isAuthenticated: Boolean(user),
    isInitialized,
    user,
    login,
    logout,
    changePassword,
  };
}

export function RestrictedSessionGuard({
  mustChangePassword,
  restricted,
  regular,
}: {
  mustChangePassword: boolean;
  restricted: ReactNode;
  regular: ReactNode;
}) {
  return <>{mustChangePassword ? restricted : regular}</>;
}

export function FirstPasswordChangePage({ auth }: { auth: AuthenticationState }) {
  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirmation, setConfirmation] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState('');

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (newPassword !== confirmation) {
      setError('两次输入的新密码不一致。');
      return;
    }
    setSubmitting(true);
    setError('');
    try {
      await auth.changePassword(currentPassword, newPassword);
      setCurrentPassword('');
      setNewPassword('');
      setConfirmation('');
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : '密码修改失败。');
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <main className="sh-login-page" aria-labelledby="password-change-title">
      <section className="sh-login-brand">
        <span className="sh-login-brand-badge">安全步骤</span>
        <h1>先完成首次改密</h1>
        <p>当前临时会话只允许修改密码、查看当前账号或退出。完成后会自动建立普通会话。</p>
      </section>
      <section className="sh-login-panel">
        <div className="sh-login-card">
          <h2 id="password-change-title">设置正式密码</h2>
          <p>密码长度 8–64 个字符，不能包含空白字符，也不能与当前密码相同。</p>
          <form onSubmit={submit}>
            <PasswordField id="current-password" label="当前临时密码" value={currentPassword} onChange={setCurrentPassword} autoComplete="current-password" />
            <PasswordField id="new-password" label="新密码" value={newPassword} onChange={setNewPassword} autoComplete="new-password" />
            <PasswordField id="confirm-password" label="确认新密码" value={confirmation} onChange={setConfirmation} autoComplete="new-password" />
            {error && <div className="sh-form-error" role="alert">{error}</div>}
            <button className="sh-btn sh-btn-primary sh-form-submit" type="submit" disabled={submitting}>
              {submitting ? '正在更新…' : '完成改密'}
            </button>
            <button className="sh-btn sh-form-submit" type="button" onClick={() => void auth.logout()} disabled={submitting}>
              退出登录
            </button>
          </form>
        </div>
      </section>
    </main>
  );
}

function PasswordField({
  id,
  label,
  value,
  onChange,
  autoComplete,
}: {
  id: string;
  label: string;
  value: string;
  onChange: (value: string) => void;
  autoComplete: 'current-password' | 'new-password';
}) {
  return (
    <div className="sh-form-group sh-form-group-lg">
      <label className="sh-form-label" htmlFor={id}>{label}</label>
      <input
        id={id}
        className="sh-form-input"
        type="password"
        value={value}
        onChange={(event) => onChange(event.target.value)}
        required
        autoComplete={autoComplete}
      />
    </div>
  );
}
