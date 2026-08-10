import { describe, expect, it, vi } from 'vitest';
import { bootstrapShell } from '../bootstrap';

describe('Shell module runtime bootstrap', () => {
  it('renders only a retryable fatal state when the active catalog cannot be committed', async () => {
    const failure = new Error('MODULE_PROJECTION_FAILED: Organization');
    const registerModules = vi.fn().mockRejectedValueOnce(failure).mockResolvedValueOnce(undefined);
    const initializeModules = vi.fn().mockResolvedValue(undefined);
    const renderApp = vi.fn();
    const renderFatal = vi.fn();

    const readAuthenticationState = vi.fn().mockResolvedValue('regular' as const);
    await bootstrapShell({ readAuthenticationState, registerModules, initializeModules, renderApp, renderFatal });

    expect(initializeModules).not.toHaveBeenCalled();
    expect(renderApp).not.toHaveBeenCalled();
    expect(renderFatal).toHaveBeenCalledWith(failure, expect.any(Function));

    const retry = renderFatal.mock.calls[0][1] as () => Promise<void>;
    await retry();

    expect(registerModules).toHaveBeenCalledTimes(2);
    expect(initializeModules).toHaveBeenCalledTimes(1);
    expect(renderApp).toHaveBeenCalledTimes(1);
  });

  it.each(['anonymous', 'restricted'] as const)(
    'renders core authentication before loading modules for %s sessions',
    async (authenticationState) => {
      const registerModules = vi.fn();
      const initializeModules = vi.fn();
      const renderApp = vi.fn();
      const renderFatal = vi.fn();

      await bootstrapShell({
        readAuthenticationState: vi.fn().mockResolvedValue(authenticationState),
        registerModules,
        initializeModules,
        renderApp,
        renderFatal,
      });

      expect(registerModules).not.toHaveBeenCalled();
      expect(initializeModules).not.toHaveBeenCalled();
      expect(renderApp).toHaveBeenCalledOnce();
      expect(renderFatal).not.toHaveBeenCalled();
    },
  );
});
