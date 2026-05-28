import { afterEach, describe, expect, it, vi } from 'vitest';
import { deletePerson } from '../../../../modules/organization/src/services/organizationService';

describe('organizationService error handling', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('throws backend business message from standard crud error response', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({
      code: 'Person.LinkedUser',
      message: '当前人员已绑定账号，不能删除。',
    }), { status: 400, headers: { 'Content-Type': 'application/json' } }));

    await expect(deletePerson('person-1')).rejects.toThrow('当前人员已绑定账号，不能删除。');
  });

  it('falls back to caller message when error body has no readable message', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({
      code: 'Unknown',
    }), { status: 500, headers: { 'Content-Type': 'application/json' } }));

    await expect(deletePerson('person-1')).rejects.toThrow('删除人员失败');
  });
});
