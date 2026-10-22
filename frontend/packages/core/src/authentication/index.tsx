import { FormEvent, useCallback, useEffect, useRef, useState, type ReactNode } from 'react';
import { readStandardCrudRequestError, StandardCrudRequestError } from '@icp-platform/sdk';
import { useLocalization } from '../localization/LocalizationContext';
import { formatAuthenticationError } from './messages';
import type { AuthUser } from '@icp-platform/sdk';
import { installPlatformAccessRejectionHandler, invalidatePlatformAntiforgeryToken, platformFetch } from '../http';

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
  permissionEpoch: number;
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

export class AuthenticationSessionError extends StandardCrudRequestError {
  constructor(code: string) {
    super(code, 503, {}, { code });
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
  const [permissionEpoch, setPermissionEpoch] = useState(0);
  const rejectionRefresh = useRef<Promise<void>>();
  const refreshGeneration = useRef(0);

  const refresh = useCallback(async () => {
    const generation = ++refreshGeneration.current;
    try {
      const current = await readCurrentUser();
      if (generation === refreshGeneration.current) {
        setUser(current);
        setSessionError(undefined);
        if (!current) invalidatePlatformAntiforgeryToken();
      }
      return current;
    } catch (cause) {
      const error = normalizeSessionError(cause);
      if (generation === refreshGeneration.current) setSessionError(error.message);
      throw error;
    }
  }, []);

  const onAccessRejection = useCallback((isWrite: boolean) => {
    // 读拒绝由该查询的 error 状态清空结果；仅权限真的变化才重建它，避免自动拒绝/刷新循环。
    if (isWrite) setPermissionEpoch(epoch => epoch + 1);
    if (!rejectionRefresh.current) {
      // 同批被拒请求共用一次当前主体读取。
      rejectionRefresh.current = refresh().then(() => undefined, () => undefined)
        .finally(() => { rejectionRefresh.current = undefined; });
    }
    return rejectionRefresh.current;
  }, [refresh]);
  useEffect(() => installPlatformAccessRejectionHandler(onAccessRejection), [onAccessRejection]);

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
    const timeZoneId = Intl.DateTimeFormat().resolvedOptions().timeZone;
    const response = await platformFetch('/api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ ecode, password, timeZoneId: timeZoneId || undefined }),
    });
    if (!response.ok) throw await readStandardCrudRequestError(response);
    refreshGeneration.current += 1;
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

    refreshGeneration.current += 1;
    invalidatePlatformAntiforgeryToken();
    setUser(undefined);
    setSessionError(undefined);
  }, []);

  const retrySessionValidation = useCallback(async () => {
    await refresh();
  }, [refresh]);

  const changePassword = useCallback(async (currentPassword: string, newPassword: string) => {
    if (!user) throw new StandardCrudRequestError('AUTH_SESSION_INVALID', 401, {}, { code: 'AUTH_SESSION_INVALID' });
    const response = await platformFetch('/api/identity/me/password-change', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ currentPassword, newPassword, expectedRevision: user.revision }),
    });
    if (!response.ok) throw await readStandardCrudRequestError(response);
    invalidatePlatformAntiforgeryToken();
    const current = await refresh();
    if (!current || current.mustChangePassword) throw new StandardCrudRequestError('AUTH_SESSION_ROTATION_FAILED', 401, {}, { code: 'AUTH_SESSION_ROTATION_FAILED' });
  }, [refresh, user]);

  return {
    isAuthenticated: Boolean(user),
    isInitialized,
    sessionError,
    permissionEpoch,
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
  const [error, setError] = useState<unknown>();
  const [missingField, setMissingField] = useState<string>();
  const [mismatch, setMismatch] = useState(false);
  const { t } = useLocalization();

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setMissingField(undefined);
    setMismatch(false);
    setError(undefined);
    if (!currentPassword || !newPassword || !confirmation) {
      setMissingField(!currentPassword ? 'CurrentPassword' : !newPassword ? 'NewPassword' : 'ConfirmPassword');
      return;
    }
    if (newPassword !== confirmation) {
      setMismatch(true);
      return;
    }
    setSubmitting(true);
    try {
      await auth.changePassword(currentPassword, newPassword);
      setCurrentPassword('');
      setNewPassword('');
      setConfirmation('');
      window.location.assign('/');
    } catch (cause) {
      setError(cause);
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <main className="sh-login-page" aria-labelledby="password-change-title">
      <section className="sh-login-brand">
        <span className="sh-login-brand-badge">{t('PlatformAuthentication.SecurityStep', '安全步骤')}</span>
        <h1>{t('PlatformAuthentication.FirstPasswordTitle', '先完成首次改密')}</h1>
        <p>{t('PlatformAuthentication.RestrictedDescription', '当前临时会话只允许修改密码、查看当前账号或退出。完成后会自动建立普通会话。')}</p>
      </section>
      <section className="sh-login-panel">
        <div className="sh-login-card">
          <h2 id="password-change-title">{t('PlatformAuthentication.SetPassword', '设置正式密码')}</h2>
          <p>{t('PlatformAuthentication.PasswordRules', '密码长度为 8–64 个字符，不能全部为空白，也不能与当前密码相同。')}</p>
          <form onSubmit={submit} noValidate>
            <PasswordField id="current-password" label={t('PlatformAuthentication.CurrentPassword', '当前临时密码')} value={currentPassword} onChange={setCurrentPassword} autoComplete="current-password" />
            <PasswordField id="new-password" label={t('PlatformAuthentication.NewPassword', '新密码')} value={newPassword} onChange={setNewPassword} autoComplete="new-password" />
            <PasswordField id="confirm-password" label={t('PlatformAuthentication.ConfirmPassword', '确认新密码')} value={confirmation} onChange={setConfirmation} autoComplete="new-password" />
            {missingField && <div className="sh-form-error" role="alert">{t('PlatformAuthentication.RequiredField', undefined, { field: t(`PlatformAuthentication.${missingField}`) })}</div>}
            {(error != null || mismatch) && <div className="sh-form-error" role="alert">{mismatch ? t('PlatformAuthentication.PasswordMismatch', '两次输入的新密码不一致。') : formatAuthenticationError(error, t)}</div>}
            <button className="sh-btn sh-btn-primary sh-form-submit" type="submit" disabled={submitting}>
              {submitting ? t('PlatformAuthentication.UpdatingPassword', '正在更新…') : t('PlatformAuthentication.CompletePassword', '完成改密')}
            </button>
            <button className="sh-btn sh-form-submit" type="button" onClick={() => void auth.logout().catch(() => undefined)} disabled={submitting}>
              {t('PlatformAuthentication.SignOut', '退出登录')}
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
