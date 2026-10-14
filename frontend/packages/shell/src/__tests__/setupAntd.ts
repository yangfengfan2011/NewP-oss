import '../../../../test-support/setupAntd';
import { transferableAbortController } from 'node:util';
import { beforeEach, vi } from 'vitest';

// Data-router 使用 Node Request；jsdom 的 AbortSignal 不属于它的原生 realm。
beforeEach(() => {
  if (typeof window === 'undefined') return;
  vi.stubGlobal('AbortController', class { constructor() { return transferableAbortController(); } });
  vi.stubGlobal('AbortSignal', transferableAbortController().signal.constructor);
});

vi.mock('@icp-platform/core', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@icp-platform/core')>();

  return {
    ...actual,
    platformFetch: (input: RequestInfo | URL, init?: RequestInit) => fetch(input, init),
  };
});
