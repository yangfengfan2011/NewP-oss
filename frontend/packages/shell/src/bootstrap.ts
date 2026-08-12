import type { AuthUser } from '@icp-platform/sdk';

export interface ShellBootstrapDependencies {
  readAuthenticationSnapshot: () => Promise<AuthUser | undefined>;
  registerModules: () => Promise<void>;
  initializeModules: () => Promise<void>;
  renderApp: (user: AuthUser | undefined) => void;
  renderFatal: (error: Error, retry: () => Promise<void>) => void;
}

export async function bootstrapShell(dependencies: ShellBootstrapDependencies): Promise<void> {
  const run = async (): Promise<void> => {
    try {
      const user = await dependencies.readAuthenticationSnapshot();
      if (!user || user.mustChangePassword) {
        dependencies.renderApp(user);
        return;
      }
      await dependencies.registerModules();
      await dependencies.initializeModules();
      dependencies.renderApp(user);
    } catch (error) {
      const fatalError = error instanceof Error ? error : new Error(String(error));
      // 活动目录只允许整批提交；任何失败都停留在此处，重试会重新获取完整快照。
      dependencies.renderFatal(fatalError, run);
    }
  };

  await run();
}
