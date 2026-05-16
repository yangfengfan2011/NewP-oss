import { describe, expect, it } from 'vitest';
import {
  buildStandardCrudI18nLabel,
  normalizeStandardCrudSubmitValues,
  preserveStandardCrudCurrentOption,
  readStandardCrudFieldValue,
  resolveStandardCrudSummaryMessage,
  type StandardCrudFieldDefinition,
  type StandardCrudOption,
} from '@icp-platform/sdk';

describe('standard CRUD phase 2 SDK helpers', () => {
  it('builds i18n labels from dictionary and local keys', () => {
    expect(buildStandardCrudI18nLabel('Person', 'Fields.PersonNo')).toBe('Person.Fields.PersonNo');
    expect(buildStandardCrudI18nLabel('Person', '')).toBe('Person');
    expect(buildStandardCrudI18nLabel('', 'Fields.PersonNo')).toBe('Fields.PersonNo');
  });

  it('reads field values by model field with fallback fields', () => {
    const field: StandardCrudFieldDefinition = {
      key: 'personNo',
      labelKey: 'Fields.PersonNo',
      modelField: 'PERSON_NO',
      fallbackFields: ['PersonNo'],
      kind: 'text',
    };

    expect(readStandardCrudFieldValue({ PERSON_NO: 'P-001' }, field)).toBe('P-001');
    expect(readStandardCrudFieldValue({ PersonNo: 'P-002' }, field)).toBe('P-002');
    expect(readStandardCrudFieldValue({}, field)).toBeNull();
  });

  it('normalizes submit values with trim, emptyAsNull, readOnly skip and default values', () => {
    const fields: StandardCrudFieldDefinition[] = [
      { key: 'personNo', labelKey: 'Fields.PersonNo', modelField: 'PERSON_NO', kind: 'text', trim: true },
      { key: 'displayName', labelKey: 'Fields.DisplayName', modelField: 'DISPLAY_NAME', kind: 'text', trim: true, emptyAsNull: true },
      { key: 'status', labelKey: 'Fields.Status', modelField: 'STATUS', kind: 'enum', defaultValue: 'enabled' },
      { key: 'createdAt', labelKey: 'Fields.CreatedAt', modelField: 'CREATED_AT', kind: 'readonly', readOnly: true },
    ];

    expect(
      normalizeStandardCrudSubmitValues(
        fields,
        {
          personNo: ' P-001 ',
          displayName: '   ',
          createdAt: '2026-05-16',
        },
      ),
    ).toEqual({
      PERSON_NO: 'P-001',
      DISPLAY_NAME: null,
      STATUS: 'enabled',
    });

    expect(normalizeStandardCrudSubmitValues(fields, { status: null })).toEqual({
      PERSON_NO: null,
      DISPLAY_NAME: null,
      STATUS: null,
    });
  });

  it('preserves the current option before provider results when missing', () => {
    const current: StandardCrudOption = { value: 'current', label: '当前值' };
    const options: StandardCrudOption[] = [{ value: 'next', label: '新值' }];

    expect(preserveStandardCrudCurrentOption(options, current)).toEqual([current, ...options]);
    expect(preserveStandardCrudCurrentOption([current, ...options], current)).toEqual([current, ...options]);
  });

  it('resolves standard summary status messages', () => {
    expect(resolveStandardCrudSummaryMessage({ status: 'notConfigured', data: null })).toBe('暂未配置');
    expect(resolveStandardCrudSummaryMessage({ status: 'dependencyUnavailable', data: null })).toBe('依赖不可用');
  });
});
