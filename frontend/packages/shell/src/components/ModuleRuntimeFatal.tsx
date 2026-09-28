import { useLocalization } from '@icp-platform/core';
export interface ModuleRuntimeFatalProps {
  error: Error;
  onRetry: () => void;
}

export function ModuleRuntimeFatal({ error, onRetry }: ModuleRuntimeFatalProps) {
  const { t } = useLocalization();
  return (
    <main className="module-runtime-fatal" role="alert">
      <section>
        <h1>{t('PlatformAuthentication.RuntimeUnavailable', '模块运行时加载失败')}</h1>
        <p>{t('PlatformAuthentication.RuntimeDescription', '本次模块快照未生效，页面没有进入部分加载状态。')}</p>
        <details><summary>{t('Platform.Diagnostics', '诊断信息')}</summary><pre>{error.message}</pre></details>
        <button type="button" onClick={onRetry}>
          {t('PlatformAuthentication.Retry', '重试')}
        </button>
      </section>
    </main>
  );
}
