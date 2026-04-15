import languagesConfig from '../../../../../modules/Platform/metadata/languages.json';
import commonDictionaryDocument from '../../../../../modules/Platform/metadata/dictionaries/Common.dic.json';
import platformDictionaryDocument from '../../../../../modules/Platform/metadata/dictionaries/Platform.dic.json';
import configCenterDictionaryDocument from '../../../../../modules/Platform/metadata/dictionaries/ConfigCenter.dic.json';
import genderDictionaryDocument from '../../../../../modules/Platform/metadata/dictionaries/Gender.dic.json';
import messageStateDictionaryDocument from '../../../../../modules/Platform/metadata/dictionaries/MessageState.dic.json';
import genderDefinitionDocument from '../../../../../modules/Platform/metadata/enumerations/Gender.json';
import messageStateDefinitionDocument from '../../../../../modules/Platform/metadata/enumerations/MessageState.json';
import selectModeDefinitionDocument from '../../../../../modules/Platform/metadata/enumerations/SelectMode.json';

type SupportedLanguage = {
  code: string;
  displayName: string;
};

type LanguageConfig = {
  defaultLanguage: string;
  supportedLanguages: SupportedLanguage[];
};

type DictionaryEntry = Record<string, string>;
type DictionaryBusinessRoot = Record<string, DictionaryEntry>;
type DictionaryDocument = {
  schemaVersion: string;
  type: 'dic';
} & Record<string, unknown>;

type EnumerationDefinition = {
  name: string;
  items: Array<{ name: string }>;
};

type EnumerationDocument = {
  schemaVersion: string;
  type: 'enum';
} & Record<string, unknown>;

const commonDictionaryMetadata = commonDictionaryDocument as DictionaryDocument;
const platformDictionaryMetadata = platformDictionaryDocument as DictionaryDocument;
const configCenterDictionaryMetadata = configCenterDictionaryDocument as DictionaryDocument;
const genderDictionaryMetadata = genderDictionaryDocument as DictionaryDocument;
const messageStateDictionaryMetadata = messageStateDictionaryDocument as DictionaryDocument;
const genderEnumerationMetadata = genderDefinitionDocument as EnumerationDocument;
const messageStateEnumerationMetadata = messageStateDefinitionDocument as EnumerationDocument;
const selectModeEnumerationMetadata = selectModeDefinitionDocument as EnumerationDocument;

const METADATA_HEADER_KEYS = new Set(['schemaVersion', 'type']);

function getBusinessRoot<T>(document: Record<string, unknown>, expectedType: string): [string, T] {
  if (document.type !== expectedType) {
    throw new Error(`Unsupported metadata document type: ${String(document.type)}`);
  }

  const businessEntries = Object.entries(document).filter(([key]) => !METADATA_HEADER_KEYS.has(key));
  if (businessEntries.length !== 1) {
    throw new Error(`Metadata document must contain exactly one business root, received ${businessEntries.length}.`);
  }

  return [businessEntries[0][0], businessEntries[0][1] as T];
}

export function mergeDicEntries(
  currentEntries: DictionaryBusinessRoot,
  incomingEntries: DictionaryBusinessRoot,
): DictionaryBusinessRoot {
  const mergedEntries: DictionaryBusinessRoot = { ...currentEntries };

  for (const [entryKey, localizedValues] of Object.entries(incomingEntries)) {
    // `dic-entry` 的覆盖边界只到词条级，同词条必须整组语言一起替换。
    mergedEntries[entryKey] = { ...localizedValues };
  }

  return mergedEntries;
}

function validateEntryLanguages(
  dictionaryName: string,
  entryKey: string,
  localizedValues: DictionaryEntry,
  supportedLanguageCodes: string[],
) {
  const unknownLanguages = Object.keys(localizedValues).filter((languageCode) => !supportedLanguageCodes.includes(languageCode));
  if (unknownLanguages.length > 0) {
    throw new Error(
      `Dictionary '${dictionaryName}' entry '${entryKey}' contains unknown languages: ${unknownLanguages.join(', ')}.`,
    );
  }

  for (const languageCode of supportedLanguageCodes) {
    if (!(languageCode in localizedValues)) {
      throw new Error(`Dictionary '${dictionaryName}' entry '${entryKey}' is missing language '${languageCode}'.`);
    }
  }
}

export function buildLanguageCatalogs(
  languageConfig: LanguageConfig,
  ...documents: DictionaryDocument[]
): Record<string, Record<string, string>> {
  const supportedLanguageCodes = languageConfig.supportedLanguages.map((language) => language.code);
  const catalogs = Object.fromEntries(supportedLanguageCodes.map((languageCode) => [languageCode, {} as Record<string, string>]));

  for (const document of documents) {
    const [dictionaryName, entries] = getBusinessRoot<DictionaryBusinessRoot>(document, 'dic');
    const mergedEntries = mergeDicEntries({}, entries);

    for (const [entryKey, localizedValues] of Object.entries(mergedEntries)) {
      validateEntryLanguages(dictionaryName, entryKey, localizedValues, supportedLanguageCodes);
      for (const languageCode of supportedLanguageCodes) {
        catalogs[languageCode][entryKey] = localizedValues[languageCode];
      }
    }
  }

  return catalogs;
}

export function buildEnumerationDefinition(document: EnumerationDocument): EnumerationDefinition {
  const [name, definition] = getBusinessRoot<{ items: Array<{ name: string }> }>(document, 'enum');
  return {
    name,
    items: definition.items,
  };
}

export const dictionaryLanguageConfig = languagesConfig;

export const dictionaryCatalogs = buildLanguageCatalogs(
  dictionaryLanguageConfig,
  commonDictionaryMetadata,
  platformDictionaryMetadata,
  configCenterDictionaryMetadata,
);

export const enumerationDefinitions = {
  Gender: buildEnumerationDefinition(genderEnumerationMetadata),
  MessageState: buildEnumerationDefinition(messageStateEnumerationMetadata),
  SelectMode: buildEnumerationDefinition(selectModeEnumerationMetadata),
};

export const enumerationCatalogs = buildLanguageCatalogs(
  dictionaryLanguageConfig,
  genderDictionaryMetadata,
  messageStateDictionaryMetadata,
);
