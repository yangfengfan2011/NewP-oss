import { useLocalization } from '@icp-platform/core';
import type { ReactNode } from 'react';

export interface AuthenticationSessionBoundaryState {
  isInitialized: boolean;
  sessionError?: string;
  retrySessionValidation: () => Promise<void>;
}

export function AuthenticationSessionBoundary({
  authentication,
  children,
}: {
  authentication: AuthenticationSessionBoundaryState;
  children: ReactNode;
}) {
  const { t } = useLocalization();
  if (!authentication.isInitialized) {
    return null;
  }

  if (!authentication.sessionError) {
    return <>{children}</>;
  }

  return (
    <main className="module-runtime-fatal" role="alert" data-authentication-state="outage">
      <section>
        <h1>{t('PlatformAuthentication.SessionUnavailable', '认证服务暂时不可用')}</h1>
        <p>{t('PlatformAuthentication.SessionDescription', '当前登录状态无法验证，已保留本地主体；恢复服务后可安全重试。')}</p>
        <pre>{authentication.sessionError}</pre>
        <button type="button" onClick={() => void authentication.retrySessionValidation().catch(() => undefined)}>
          {t('PlatformAuthentication.Retry', '重试')}
        </button>
      </section>
    </main>
  );
}
