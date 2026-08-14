import { describe, expect, it, vi } from 'vitest';
import { createRecoverableCapabilityLoader } from '@icp-platform/sdk';

describe('createRecoverableCapabilityLoader', () => {
  it('uses only the primary import on the normal path', async () => {
    const module = { default: () => null };
    const primary = vi.fn().mockResolvedValue(module);
    const recovery = vi.fn().mockResolvedValue(module);
    const load = createRecoverableCapabilityLoader(primary, recovery);

    await expect(load()).resolves.toBe(module);

    expect(primary).toHaveBeenCalledTimes(1);
    expect(recovery).not.toHaveBeenCalled();
  });

  it('switches to only the recovery import after the primary import rejects', async () => {
    const primaryFailure = new Error('primary capability chunk unavailable');
    const recoveredModule = { default: () => null };
    const primary = vi.fn().mockRejectedValue(primaryFailure);
    const recovery = vi.fn().mockResolvedValue(recoveredModule);
    const load = createRecoverableCapabilityLoader(primary, recovery);

    await expect(load()).rejects.toBe(primaryFailure);
    await expect(load()).resolves.toBe(recoveredModule);

    expect(primary).toHaveBeenCalledTimes(1);
    expect(recovery).toHaveBeenCalledTimes(1);
  });
});
