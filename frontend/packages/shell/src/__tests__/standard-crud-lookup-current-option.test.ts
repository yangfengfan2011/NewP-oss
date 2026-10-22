import { expect, it, vi } from 'vitest';
import { loadStandardCrudLookupOptions, loadStandardCrudLookupPage, type StandardCrudLookupProvider, type StandardCrudOption } from '@icp-platform/sdk';

const input = {
  field: { key: 'companyId', labelKey: 'Company', modelField: 'CompanyId', kind: 'lookup' as const, lookup: { providerKey: 'companies' } },
  values: { companyId: 'existing' },
  detailFields: { CompanyName: '不得使用旧名称' },
};

it('marks only the synthesized missing current value while preserving pagination and selection facts', async () => {
  const provider: StandardCrudLookupProvider = { key: 'companies', load: vi.fn(async request => ({
    options: [{ value: 'candidate', label: 'Business name' }], currentOptions: [],
    page: request.page, pageSize: request.pageSize, hasMore: true,
  })) };
  const result = await loadStandardCrudLookupPage({ ...input, providers: [provider], page: 2, pageSize: 5 });
  const unavailable = { value: 'existing', label: '当前值不可用', disabled: true, unavailable: true };
  expect(result.currentOptions).toEqual([unavailable]);
  expect(result.options).toEqual([unavailable, { value: 'candidate', label: 'Business name' }]);
  expect(result).toMatchObject({ page: 2, pageSize: 5, hasMore: true, error: null });
  expect(provider.load).toHaveBeenCalledOnce();
  expect(provider.load).toHaveBeenCalledWith(expect.objectContaining({ currentValueIds: ['existing'], values: input.values, page: 2, pageSize: 5 }));
});

it.each([
  { label: '当前值不可用', disabled: true },
  { label: '当前值不可用', disabled: false },
  { label: '***', disabled: true },
])('does not infer unavailable from an authoritative label/disabled pair: $label/$disabled', async option => {
  const business: StandardCrudOption = { value: 'existing', ...option };
  const provider: StandardCrudLookupProvider = { key: 'companies', load: async () => ({ options: [], currentOptions: [business], page: 1, pageSize: 20, hasMore: false }) };
  const result = await loadStandardCrudLookupPage({ ...input, providers: [provider] });
  expect(result.options).toEqual([business]);
  expect(result.currentOptions).toEqual([business]);
  expect(result.options[0]).toBe(business);
  expect(result.options[0].unavailable).toBeUndefined();
});

it('passes the synthesized marker through the existing options entry point', async () => {
  const provider: StandardCrudLookupProvider = { key: 'companies', load: async () => [] };
  const result = await loadStandardCrudLookupOptions({ ...input, providers: [provider] });
  expect(result).toEqual({ options: [{ value: 'existing', label: '当前值不可用', disabled: true, unavailable: true }], error: null });
});

it('keeps the original provider failure and current identifier with the synthesized unavailable marker', async () => {
  const failure = new Error('original diagnostic');
  const provider: StandardCrudLookupProvider = { key: 'companies', load: vi.fn().mockRejectedValue(failure) };
  const result = await loadStandardCrudLookupPage({ ...input, providers: [provider] });
  expect(result.failure).toBe(failure);
  expect(result.options).toEqual([{ value: 'existing', label: '当前值不可用', disabled: true, unavailable: true }]);
  expect(result.currentOptions).toEqual(result.options);
  expect(result).toMatchObject({ page: 1, pageSize: 20, hasMore: false, error: failure.message });
});
