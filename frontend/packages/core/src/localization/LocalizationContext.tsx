import { createContext, ReactNode, useContext, useEffect, useState } from 'react';

export interface LocalizationLanguage {
  code: string;
  displayName: string;
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
  t: (key: string, fallback?: string) => string;
  getEnumerationItems: (enumerationName: string) => EnumerationDefinitionItem[];
  getEnumerationLabel: (enumerationName: string, itemName: string, fallback?: string) => string;
}

export interface LocalizationProviderProps {
  children: ReactNode;
  defaultLanguage: string;
  supportedLanguages: LocalizationLanguage[];
  catalogs: LocalizationCatalogs;
  initialLanguage?: string;
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
  enumerationDefinitions = {},
  enumerationCatalogs = {},
}: LocalizationProviderProps) {
  const supportedLanguageCodes = new Set(supportedLanguages.map((language) => language.code));
  const normalizedInitialLanguage = initialLanguage && supportedLanguageCodes.has(initialLanguage)
    ? initialLanguage
    : defaultLanguage;
  const [currentLanguage, setCurrentLanguage] = useState(normalizedInitialLanguage);

  useEffect(() => {
    if (!supportedLanguageCodes.has(currentLanguage)) {
      setCurrentLanguage(normalizedInitialLanguage);
    }
  }, [currentLanguage, normalizedInitialLanguage, supportedLanguageCodes]);

  const setLanguage = (languageCode: string) => {
    if (!supportedLanguageCodes.has(languageCode)) {
      return;
    }

    setCurrentLanguage(languageCode);
  };

  const t = (key: string, fallback?: string) => {
    const currentCatalog = catalogs[currentLanguage] ?? {};
    if (currentCatalog[key] !== undefined) {
      return currentCatalog[key];
    }

    const defaultCatalog = catalogs[defaultLanguage] ?? {};
    if (defaultCatalog[key] !== undefined) {
      return defaultCatalog[key];
    }

    return fallback ?? key;
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
