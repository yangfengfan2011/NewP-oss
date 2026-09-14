import { beforeEach, describe, expect, it, vi } from 'vitest';
import { StandardCrudRequestError } from '@icp-platform/sdk';

const platformFetchMock = vi.hoisted(() => vi.fn());
vi.mock('@icp-platform/core', () => ({ platformFetch: platformFetchMock }));

import { setPersonLifecycleStatus } from '../services/organizationService';

describe('person lifecycle client', () => {
  beforeEach(() => platformFetchMock.mockReset());

  it.each([false, true])('uses the matching lifecycle command and request position for enabled=%s without a person revision', async (isEnabled) => {
    platformFetchMock.mockResolvedValue(new Response('{}', {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    }));

    await setPersonLifecycleStatus('Person/1', {
      isEnabled,
      reason: '人员中心停用人员',
    }, 'declared-action-position');

    expect(platformFetchMock).toHaveBeenCalledWith(
      `/api/organization/persons/Person%2F1/status/${isEnabled ? 'enable' : 'disable'}`,
      expect.objectContaining({
        method: 'PUT',
        headers: expect.objectContaining({ 'X-NewP-Feature': 'declared-action-position' }),
        body: JSON.stringify({
          isEnabled,
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
      reason: '人员中心停用人员',
    }).then(
      () => expect.fail('请求应失败'),
      (error: unknown) => {
        expect(error).toBeInstanceOf(StandardCrudRequestError);
        expect(error).toMatchObject({ message: code, status });
      },
    );
  });
});
