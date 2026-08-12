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
  sessionError?: string;
  user?: AuthUser;
  login: (ecode: string, password: string) => Promise<void>;
  logout: () => Promise<void>;
  retrySessionValidation: () => Promise<void>;
  changePassword: (currentPassword: string, newPassword: string) => Promise<void>;
}

export interface AuthenticationBootstrapSnapshot {
  user?: AuthUser;
}

export async function readCurrentUser(): Promise<AuthUser | undefined> {
  let response: Response;
  try {
    response = await platformFetch('/api/auth/current-user', {
      method: 'GET',
      headers: { Accept: 'application/json' },
    });
  } catch (cause) {
    throw normalizeSessionError(cause);
  }

  if (response.status === 401) {
    return undefined;
  }
  if (!response.ok) {
    throw new AuthenticationSessionError(await readProblemCode(response));
  }
  return await response.json() as AuthUser;
}

export class AuthenticationSessionError extends Error {
  constructor(code: string) {
    super(code);
    this.name = 'AuthenticationSessionError';
  }
}

function normalizeSessionError(cause: unknown): AuthenticationSessionError {
  return cause instanceof AuthenticationSessionError
    ? cause
    : new AuthenticationSessionError('AUTH_NETWORK_UNAVAILABLE');
}

async function readProblemCode(response: Response): Promise<string> {
  try {
    const problem = await response.json() as { code?: string };
    return problem.code ?? `HTTP_${response.status}`;
  } catch {
    return `HTTP_${response.status}`;
  }
}

export function useAuthenticationState(
  initialSnapshot?: AuthenticationBootstrapSnapshot,
): AuthenticationState {
  const hasInitialSnapshot = initialSnapshot !== undefined;
  const [user, setUser] = useState<AuthUser | undefined>(() => initialSnapshot?.user);
  const [isInitialized, setIsInitialized] = useState(hasInitialSnapshot);
  const [sessionError, setSessionError] = useState<string>();

  const refresh = useCallback(async () => {
    try {
      const current = await readCurrentUser();
      setUser(current);
      setSessionError(undefined);
      if (!current) invalidatePlatformAntiforgeryToken();
      return current;
    } catch (cause) {
      const error = normalizeSessionError(cause);
      setSessionError(error.message);
      throw error;
    }
  }, []);

  useEffect(() => {
    if (hasInitialSnapshot) {
      return;
    }

    let active = true;
    void readCurrentUser()
      .then((current) => {
        if (active) {
          setUser(current);
          setSessionError(undefined);
          if (!current) invalidatePlatformAntiforgeryToken();
        }
      })
      .catch((cause) => {
        if (active) setSessionError(normalizeSessionError(cause).message);
      })
      .finally(() => {
        if (active) setIsInitialized(true);
      });
    return () => {
      active = false;
    };
  }, [hasInitialSnapshot]);

  const login = useCallback(async (ecode: string, password: string) => {
    const response = await platformFetch('/api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ ecode, password }),
    });
    if (!response.ok) throw new Error(await readProblemCode(response));
    invalidatePlatformAntiforgeryToken();
    const authenticatedUser = await response.json() as AuthUser;
    setUser(authenticatedUser);
    setSessionError(undefined);
    setIsInitialized(true);
  }, []);

  const logout = useCallback(async () => {
    let response: Response;
    try {
      response = await platformFetch('/api/auth/logout', { method: 'POST' });
    } catch (cause) {
      const error = normalizeSessionError(cause);
      setSessionError(error.message);
      throw error;
    }

    if (!response.ok && response.status !== 401) {
      const error = new AuthenticationSessionError(await readProblemCode(response));
      setSessionError(error.message);
      throw error;
    }

    invalidatePlatformAntiforgeryToken();
    setUser(undefined);
    setSessionError(undefined);
  }, []);

  const retrySessionValidation = useCallback(async () => {
    await refresh();
  }, [refresh]);

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
    sessionError,
    user,
    login,
    logout,
    retrySessionValidation,
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
      window.location.assign('/');
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
            <button className="sh-btn sh-form-submit" type="button" onClick={() => void auth.logout().catch(() => undefined)} disabled={submitting}>
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
