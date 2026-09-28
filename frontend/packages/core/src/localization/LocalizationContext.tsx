import { createContext, ReactNode, useContext, useMemo, useState } from 'react';
import { formatTextTemplate } from './userMessage';

export interface LocalizationLanguage {
  code: string;
  displayName: string;
  locale?: string;
}

export type LocalizationCatalog = Record<string, string>;
export type LocalizationCatalogs = Record<string, LocalizationCatalog>;

export interface EnumerationDefinitionItem {
  name: string;
}

export interface EnumerationDefinition {
  name: string;
  items: EnumerationDefinitionItem[];
}

export type EnumerationDefinitions = Record<string, EnumerationDefinition>;
export type EnumerationCatalog = Record<string, string>;
export type EnumerationCatalogs = Record<string, EnumerationCatalog>;

export interface LocalizedEnumerationItem {
  value: string;
  label: string;
}

export interface LocalizedEnumeration {
  name: string;
  items: LocalizedEnumerationItem[];
}

export function getEnumerationItems(
  enumerationDefinitions: EnumerationDefinitions,
  enumerationName: string,
): EnumerationDefinitionItem[] {
  return enumerationDefinitions[enumerationName]?.items ?? [];
}

export function getEnumerationLabel(
  enumerationCatalogs: EnumerationCatalogs,
  currentLanguage: string,
  defaultLanguage: string,
  enumerationName: string,
  itemName: string,
  fallback?: string,
): string {
  // 枚举标签按“当前语言 -> 默认语言 -> 调用方 fallback -> 枚举项名”回退，
  // 保证 catalog 缺项时界面仍能稳定显示，而不是直接出现空白标签。
  const currentLabel = getEnumerationCatalogValue(enumerationCatalogs[currentLanguage], enumerationName, itemName);
  if (currentLabel !== undefined) {
    return currentLabel;
  }

  const defaultLabel = getEnumerationCatalogValue(enumerationCatalogs[defaultLanguage], enumerationName, itemName);
  if (defaultLabel !== undefined) {
    return defaultLabel;
  }

  return fallback ?? itemName;
}

export interface LocalizationContextValue {
  currentLanguage: string;
  defaultLanguage: string;
  supportedLanguages: LocalizationLanguage[];
  setLanguage: (languageCode: string) => void;
  t: (key: string, fallback?: string, args?: Record<string, unknown>) => string;
  getEnumerationItems: (enumerationName: string) => EnumerationDefinitionItem[];
  getEnumerationLabel: (enumerationName: string, itemName: string, fallback?: string) => string;
}

export interface LocalizationProviderProps {
  children: ReactNode;
  defaultLanguage: string;
  supportedLanguages: LocalizationLanguage[];
  catalogs: LocalizationCatalogs;
  initialLanguage?: string;
  storageKey?: string;
  enumerationDefinitions?: EnumerationDefinitions;
  enumerationCatalogs?: EnumerationCatalogs;
}

const LocalizationContext = createContext<LocalizationContextValue | undefined>(undefined);

function getEnumerationCatalogValue(
  languageCatalog: EnumerationCatalog | undefined,
  enumerationName: string,
  itemName: string,
): string | undefined {
  if (!languageCatalog) {
    return undefined;
  }

  const flatKey = `${enumerationName}.${itemName}`;
  const flatValue = languageCatalog[flatKey];
  if (flatValue !== undefined) {
    return flatValue;
  }

  // 这里同时兼容新的扁平 key 和历史嵌套 catalog 结构，
  // 这样静态资源迁移可以逐步推进，而不要求所有枚举字典一次性重写。
  const legacyEnumerationCatalog = (languageCatalog as unknown as Record<string, unknown>)[enumerationName];
  if (!legacyEnumerationCatalog || typeof legacyEnumerationCatalog !== 'object') {
    return undefined;
  }

  const nestedCatalog = legacyEnumerationCatalog as Record<string, string>;
  if (nestedCatalog[flatKey] !== undefined) {
    return nestedCatalog[flatKey];
  }

  return nestedCatalog[itemName];
}

export function LocalizationProvider({
  children,
  defaultLanguage,
  supportedLanguages,
  catalogs,
  initialLanguage,
  storageKey,
  enumerationDefinitions = {},
  enumerationCatalogs = {},
}: LocalizationProviderProps) {
  const supportedLanguageCodes = useMemo(
    () => new Set(supportedLanguages.map((language) => language.code)),
    [supportedLanguages],
  );
  // 保存显式选择而非默认值，正式词典恢复后仍能采用平台更新后的默认语言。
  const [selectedLanguage, setSelectedLanguage] = useState<string | undefined>(() => {
    if (initialLanguage) return initialLanguage;
    try { return storageKey ? localStorage.getItem(storageKey) ?? undefined : undefined; }
    catch { return undefined; }
  });
  const currentLanguage = selectedLanguage && supportedLanguageCodes.has(selectedLanguage)
    ? selectedLanguage : defaultLanguage;

  const setLanguage = (languageCode: string) => {
    if (!supportedLanguageCodes.has(languageCode)) {
      return;
    }

    setSelectedLanguage(languageCode);
    try { if (storageKey) localStorage.setItem(storageKey, languageCode); }
    catch { /* 浏览器禁止持久化时仍保留当前页面的选择。 */ }
  };

  const t = (key: string, fallback?: string, args?: Record<string, unknown>) => {
    // 文案解析只在当前语言和默认语言之间回退；
    // 这里故意不做多级链式猜测，避免 catalog 问题被过度隐藏。
    const currentCatalog = catalogs[currentLanguage] ?? {};
    if (currentCatalog[key] !== undefined) {
      return formatTextTemplate(currentCatalog[key], args);
    }

    const defaultCatalog = catalogs[defaultLanguage] ?? {};
    if (defaultCatalog[key] !== undefined) {
      return formatTextTemplate(defaultCatalog[key], args);
    }

    return formatTextTemplate(fallback ?? key, args);
  };

  return (
    <LocalizationContext.Provider
      value={{
        currentLanguage,
        defaultLanguage,
        supportedLanguages,
        setLanguage,
        t,
        getEnumerationItems: (enumerationName: string) =>
          getEnumerationItems(enumerationDefinitions, enumerationName),
        getEnumerationLabel: (enumerationName: string, itemName: string, fallback?: string) =>
          getEnumerationLabel(
            enumerationCatalogs,
            currentLanguage,
            defaultLanguage,
            enumerationName,
            itemName,
            fallback,
          ),
      }}
    >
      {children}
    </LocalizationContext.Provider>
  );
}

export function useLocalization(): LocalizationContextValue {
  const context = useContext(LocalizationContext);
  if (!context) {
    throw new Error('useLocalization must be used within LocalizationProvider');
  }

  return context;
}

export function useEnumeration(enumerationName: string): LocalizedEnumeration {
  const { getEnumerationItems, getEnumerationLabel } = useLocalization();
  const items = getEnumerationItems(enumerationName).map((item) => ({
    value: item.name,
    label: getEnumerationLabel(enumerationName, item.name, item.name),
  }));

  return {
    name: enumerationName,
    items,
  };
}
