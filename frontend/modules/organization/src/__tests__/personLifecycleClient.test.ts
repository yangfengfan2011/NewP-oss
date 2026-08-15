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

  it.each([
    [400, 'ORGANIZATION_PERSON_LIFECYCLE_REASON_INVALID'],
    [401, 'AUTH_AUTHENTICATION_REQUIRED'],
    [403, 'Authorization.PermissionDenied'],
    [409, 'LIFECYCLE_CONFLICT'],
  ])('preserves the stable server failure code for status %s', async (status, code) => {
    platformFetchMock.mockResolvedValue(new Response(JSON.stringify({
      title: code,
      code,
    }), {
      status,
      headers: { 'Content-Type': 'application/problem+json' },
    }));

    await setPersonLifecycleStatus('Person/1', {
      isEnabled: false,
      expectedRevision: 4,
      reason: '人员中心停用人员',
    }).then(
      () => expect.fail('请求应失败'),
      (error: unknown) => expect(error).toMatchObject({ message: code }),
    );
  });
});
