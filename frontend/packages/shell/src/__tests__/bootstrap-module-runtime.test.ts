import { describe, expect, it, vi } from 'vitest';
import { bootstrapShell } from '../bootstrap';

describe('Shell module runtime bootstrap', () => {
  const regularUser = {
    id: 'User_1',
    ecode: 'admin',
    ename: 'Administrator',
    permissions: [],
    mustChangePassword: false,
    revision: 1,
    isLocalAdministrator: true,
  };

  it('renders only a retryable fatal state when the active catalog cannot be committed', async () => {
    const failure = new Error('MODULE_PROJECTION_FAILED: Organization');
    const registerModules = vi.fn().mockRejectedValueOnce(failure).mockResolvedValueOnce(undefined);
    const initializeModules = vi.fn().mockResolvedValue(undefined);
    const renderApp = vi.fn();
    const renderFatal = vi.fn();

    const readAuthenticationSnapshot = vi.fn().mockResolvedValue(regularUser);
    await bootstrapShell({ readAuthenticationSnapshot, registerModules, initializeModules, renderApp, renderFatal });

    expect(initializeModules).not.toHaveBeenCalled();
    expect(renderApp).not.toHaveBeenCalled();
    expect(renderFatal).toHaveBeenCalledWith(failure, expect.any(Function));

    const retry = renderFatal.mock.calls[0][1] as () => Promise<void>;
    await retry();

    expect(registerModules).toHaveBeenCalledTimes(2);
    expect(initializeModules).toHaveBeenCalledTimes(1);
    expect(renderApp).toHaveBeenCalledWith(regularUser);
  });

  it.each([
    ['anonymous', undefined],
    ['restricted', { ...regularUser, mustChangePassword: true }],
  ] as const)('renders the verified %s snapshot before loading modules', async (_kind, user) => {
    const registerModules = vi.fn();
    const initializeModules = vi.fn();
    const renderApp = vi.fn();
    const renderFatal = vi.fn();

    await bootstrapShell({
      readAuthenticationSnapshot: vi.fn().mockResolvedValue(user),
      registerModules,
      initializeModules,
      renderApp,
      renderFatal,
    });

    expect(registerModules).not.toHaveBeenCalled();
    expect(initializeModules).not.toHaveBeenCalled();
    expect(renderApp).toHaveBeenCalledWith(user);
    expect(renderFatal).not.toHaveBeenCalled();
  });

  it('waits for the authenticated app chunk and keeps its load failure inside fatal retry', async () => {
    const chunkFailure = new Error('Failed to fetch dynamically imported module: App');
    let finishRender!: () => void;
    const renderApp = vi.fn()
      .mockRejectedValueOnce(chunkFailure)
      .mockImplementationOnce(() => new Promise<void>((resolve) => {
        finishRender = resolve;
      }));
    const renderFatal = vi.fn();

    await bootstrapShell({
      readAuthenticationSnapshot: vi.fn().mockResolvedValue(regularUser),
      registerModules: vi.fn().mockResolvedValue(undefined),
      initializeModules: vi.fn().mockResolvedValue(undefined),
      renderApp,
      renderFatal,
    });

    expect(renderFatal).toHaveBeenCalledWith(chunkFailure, expect.any(Function));
    const retry = renderFatal.mock.calls[0][1] as () => Promise<void>;
    let retryCompleted = false;
    const retryResult = retry().then(() => {
      retryCompleted = true;
    });

    await vi.waitFor(() => expect(renderApp).toHaveBeenCalledTimes(2));
    expect(retryCompleted).toBe(false);
    finishRender();
    await retryResult;
    expect(retryCompleted).toBe(true);
  });
});
