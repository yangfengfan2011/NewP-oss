import { beforeEach, describe, expect, it, vi } from 'vitest';

const platformFetchMock = vi.hoisted(() => vi.fn());
vi.mock('@icp-platform/core', () => ({ platformFetch: platformFetchMock }));

import { setPersonLifecycleStatus } from '../services/organizationService';

describe('person lifecycle client', () => {
  beforeEach(() => platformFetchMock.mockReset());

  it('uses the dedicated revision-checked lifecycle command', async () => {
    platformFetchMock.mockResolvedValue(new Response('{}', {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    }));

    await setPersonLifecycleStatus('Person/1', {
      isEnabled: false,
      expectedRevision: 4,
      reason: '人员中心停用人员',
    });

    expect(platformFetchMock).toHaveBeenCalledWith(
      '/api/organization/persons/Person%2F1/status',
      expect.objectContaining({
        method: 'PUT',
        body: JSON.stringify({
          isEnabled: false,
          expectedRevision: 4,
          reason: '人员中心停用人员',
        }),
      }),
    );
  });
});
