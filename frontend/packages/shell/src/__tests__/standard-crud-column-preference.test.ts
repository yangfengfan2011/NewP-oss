import { describe, expect, it } from 'vitest';
import {
  createStandardCrudColumnPreferenceKey,
  loadStandardCrudColumnPreference,
  resetStandardCrudColumnPreference,
  saveStandardCrudColumnPreference,
} from '@icp-platform/ui';

class MemoryStorage implements Storage {
  private readonly values = new Map<string, string>();

  get length(): number {
    return this.values.size;
  }

  clear(): void {
    this.values.clear();
  }

  getItem(key: string): string | null {
    return this.values.get(key) ?? null;
  }

  key(index: number): string | null {
    return [...this.values.keys()][index] ?? null;
  }

  removeItem(key: string): void {
    this.values.delete(key);
  }

  setItem(key: string, value: string): void {
    this.values.set(key, value);
  }
}

describe('standard CRUD column preference', () => {
  it('isolates visible columns and order by current subject plus viewKey', () => {
    const storage = new MemoryStorage();
    const defaults = { personNo: { order: 0, disable: true }, name: { order: 1 }, company: { order: 2 } };

    saveStandardCrudColumnPreference(storage, 'subject-1', 'organization.person.list', {
      personNo: { order: 0, disable: true, show: false },
      name: { order: 2 },
      company: { order: 1, show: false },
    });
    saveStandardCrudColumnPreference(storage, 'subject-1', 'organization.company.list', {
      companyNo: { order: 1 },
      companyName: { order: 0 },
    });

    expect(loadStandardCrudColumnPreference(storage, 'subject-1', 'organization.person.list', defaults)).toEqual({
      personNo: { order: 0, disable: true, show: true },
      name: { order: 2 },
      company: { order: 1, show: false },
    });
    expect(loadStandardCrudColumnPreference(storage, 'subject-1', 'organization.company.list', {
      companyNo: { order: 0 },
      companyName: { order: 1 },
    })).toEqual({
      companyNo: { order: 1 },
      companyName: { order: 0 },
    });
    expect(createStandardCrudColumnPreferenceKey('subject-2', 'organization.person.list'))
      .not.toBe(createStandardCrudColumnPreferenceKey('subject-1', 'organization.person.list'));
  });

  it('restores metadata defaults and removes the persisted override', () => {
    const storage = new MemoryStorage();
    const defaults = { personNo: { order: 0, disable: true }, name: { order: 1 } };
    saveStandardCrudColumnPreference(storage, 'subject-1', 'organization.person.list', {
      personNo: { order: 1, disable: true },
      name: { order: 0, show: false },
    });

    expect(resetStandardCrudColumnPreference(storage, 'subject-1', 'organization.person.list', defaults)).toEqual(defaults);
    expect(loadStandardCrudColumnPreference(storage, 'subject-1', 'organization.person.list', defaults)).toEqual(defaults);
  });
});

it('persists personal widths, rejects invalid widths and resets to current metadata', () => {
  const storage = new MemoryStorage();
  const defaults = { name: { order: 0, width: 180 }, code: { order: 1 } };
  saveStandardCrudColumnPreference(storage, 'one', 'view', { name: { order: 0, width: 260 }, code: { width: -2 } });
  expect(loadStandardCrudColumnPreference(storage, 'one', 'view', defaults).name.width).toBe(260);
  expect(loadStandardCrudColumnPreference(storage, 'two', 'view', defaults).name.width).toBe(180);
  expect(loadStandardCrudColumnPreference(storage, 'one', 'other', defaults).name.width).toBe(180);
  expect(loadStandardCrudColumnPreference(storage, 'one', 'view', defaults).code.width).toBeUndefined();
  expect(resetStandardCrudColumnPreference(storage, 'one', 'view', defaults)).toEqual(defaults);
});
