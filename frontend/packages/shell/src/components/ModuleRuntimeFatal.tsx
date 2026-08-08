export interface ModuleRuntimeFatalProps {
  error: Error;
  onRetry: () => void;
}

export function ModuleRuntimeFatal({ error, onRetry }: ModuleRuntimeFatalProps) {
  return (
    <main className="module-runtime-fatal" role="alert">
      <section>
        <h1>模块运行时加载失败</h1>
        <p>本次模块快照未生效，页面没有进入部分加载状态。</p>
        <pre>{error.message}</pre>
        <button type="button" onClick={onRetry}>
          重试
        </button>
      </section>
    </main>
  );
}
