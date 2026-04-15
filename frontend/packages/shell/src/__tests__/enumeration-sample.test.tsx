// @vitest-environment jsdom

import { afterEach, describe, expect, it } from 'vitest';
import { act } from 'react';
import { createRoot, Root } from 'react-dom/client';
import {
  LocalizationProvider,
  type EnumerationCatalogs,
  getEnumerationItems,
  getEnumerationLabel,
  useEnumeration,
  useLocalization,
} from '@icp-platform/core';
import {
  enumerationCatalogs as platformEnumerationCatalogs,
  enumerationDefinitions as platformEnumerationDefinitions,
} from '../localization/catalogs';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

function EnumerationProbe() {
  const { currentLanguage, setLanguage } = useLocalization();
  const gender = useEnumeration('Gender');
  const selectMode = useEnumeration('SelectMode');
  const mood = useEnumeration('Mood');

  return (
    <div>
      <button type="button" onClick={() => setLanguage('en')}>
        Switch
      </button>
      <div data-testid="language">{currentLanguage}</div>
      <div data-testid="gender">{gender.items.map((item) => item.label).join(',')}</div>
      <div data-testid="select-mode">{selectMode.items.map((item) => item.label).join(',')}</div>
      <div data-testid="mood">{mood.items.map((item) => item.label).join(',')}</div>
    </div>
  );
}

const enumerationDefinitions = {
  Gender: { name: 'Gender', items: [{ name: 'Female' }, { name: 'Male' }] },
  SelectMode: { name: 'SelectMode', items: [{ name: 'MULTI' }, { name: 'SINGLE' }] },
  Mood: { name: 'Mood', items: [{ name: 'Happy' }] },
};

const flatEnumerationCatalogs = {
  zhs: {
    'Gender.Female': '女',
    'Gender.Male': '男',
    'Mood.Happy': '开心',
  },
  en: {
    'Gender.Female': 'Female',
    'Gender.Male': 'Male',
  },
};

const legacyNestedEnumerationCatalogs = {
  zhs: {
    Gender: { Female: '女', Male: '男' },
    Mood: { Happy: '开心' },
  },
  en: {
    Gender: { Female: 'Female', Male: 'Male' },
  },
} as unknown as EnumerationCatalogs;

describe('Enumeration localization sample', () => {
  let container: HTMLDivElement;
  let root: Root;

  afterEach(async () => {
    if (root) {
      await act(async () => {
        root.unmount();
      });
    }

    if (container) {
      container.remove();
    }
  });

  it('resolves current-language labels, default-language labels, and item-name fallbacks', async () => {
    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => {
      root = createRoot(container);
      root.render(
        <LocalizationProvider
          defaultLanguage="zhs"
          initialLanguage="en"
          supportedLanguages={[
            { code: 'zhs', displayName: '简体中文' },
            { code: 'en', displayName: 'English' },
          ]}
          catalogs={{ zhs: {}, en: {} }}
          enumerationDefinitions={enumerationDefinitions}
          enumerationCatalogs={flatEnumerationCatalogs}
        >
          <EnumerationProbe />
        </LocalizationProvider>,
      );
    });

    expect(container.querySelector('[data-testid="language"]')?.textContent).toBe('en');
    expect(container.querySelector('[data-testid="gender"]')?.textContent).toBe('Female,Male');
    expect(container.querySelector('[data-testid="select-mode"]')?.textContent).toBe('MULTI,SINGLE');
    expect(container.querySelector('[data-testid="mood"]')?.textContent).toBe('开心');
    expect(getEnumerationItems(enumerationDefinitions, 'NotExists')).toEqual([]);
    expect(
      getEnumerationLabel(legacyNestedEnumerationCatalogs, 'en', 'zhs', 'Gender', 'Female', 'Female'),
    ).toBe('Female');
  });

  it('reconciles current language when provider props remove the selected language', async () => {
    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => {
      root = createRoot(container);
      root.render(
        <LocalizationProvider
          defaultLanguage="zhs"
          initialLanguage="en"
          supportedLanguages={[
            { code: 'zhs', displayName: '简体中文' },
            { code: 'en', displayName: 'English' },
          ]}
          catalogs={{ zhs: {}, en: {} }}
          enumerationDefinitions={enumerationDefinitions}
          enumerationCatalogs={flatEnumerationCatalogs}
        >
          <EnumerationProbe />
        </LocalizationProvider>,
      );
    });

    expect(container.querySelector('[data-testid="language"]')?.textContent).toBe('en');
    expect(container.querySelector('[data-testid="gender"]')?.textContent).toBe('Female,Male');

    await act(async () => {
      root.render(
        <LocalizationProvider
          defaultLanguage="zhs"
          initialLanguage="en"
          supportedLanguages={[{ code: 'zhs', displayName: '简体中文' }]}
          catalogs={{ zhs: {}, en: {} }}
          enumerationDefinitions={enumerationDefinitions}
          enumerationCatalogs={flatEnumerationCatalogs}
        >
          <EnumerationProbe />
        </LocalizationProvider>,
      );
    });

    expect(container.querySelector('[data-testid="language"]')?.textContent).toBe('zhs');
    expect(container.querySelector('[data-testid="gender"]')?.textContent).toBe('女,男');
  });

  it('loads platform enumerations from modules/Platform/metadata', () => {
    expect(platformEnumerationDefinitions.Gender.name).toBe('Gender');
    expect(platformEnumerationDefinitions.MessageState.items.map((item) => item.name)).toEqual(['Read', 'Unread']);
    expect(platformEnumerationCatalogs.zhs['Gender.Female']).toBe('女');
    expect(platformEnumerationCatalogs.en['MessageState.Read']).toBe('Read');
  });
});
