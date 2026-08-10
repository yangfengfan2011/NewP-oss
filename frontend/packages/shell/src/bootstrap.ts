export interface ShellBootstrapDependencies {
  readAuthenticationState: () => Promise<'anonymous' | 'restricted' | 'regular'>;
  registerModules: () => Promise<void>;
  initializeModules: () => Promise<void>;
  renderApp: (authenticationState: 'anonymous' | 'restricted' | 'regular') => void;
  renderFatal: (error: Error, retry: () => Promise<void>) => void;
}

export async function bootstrapShell(dependencies: ShellBootstrapDependencies): Promise<void> {
  const run = async (): Promise<void> => {
    try {
      const authenticationState = await dependencies.readAuthenticationState();
      if (authenticationState !== 'regular') {
        dependencies.renderApp(authenticationState);
        return;
      }
      await dependencies.registerModules();
      await dependencies.initializeModules();
      dependencies.renderApp(authenticationState);
    } catch (error) {
      const fatalError = error instanceof Error ? error : new Error(String(error));
      // 活动目录只允许整批提交；任何失败都停留在此处，重试会重新获取完整快照。
      dependencies.renderFatal(fatalError, run);
    }
  };

  await run();
}
