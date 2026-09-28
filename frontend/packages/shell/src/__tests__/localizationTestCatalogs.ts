import bootstrap from '../localization/bootstrapLocalization.generated.json';
import shellDictionary from '../../../../../modules/Platform/metadata/dictionaries/PlatformShell.dic.json';
import platformDictionary from '../../../../../modules/Platform/metadata/dictionaries/Platform.dic.json';

export const platformTestCatalogs = Object.fromEntries(['zhs', 'en'].map(language => [language,
  Object.fromEntries(Object.entries(platformDictionary.Platform).map(([key, translations]) => [`Platform.${key}`, translations[language as 'zhs' | 'en']])),
]));

export const shellTestCatalogs = Object.fromEntries(bootstrap.supportedLanguages.map(language => [language.code, {
  ...bootstrap.catalogs[language.code as keyof typeof bootstrap.catalogs],
  ...Object.fromEntries(Object.entries(shellDictionary.PlatformShell).map(([key, translations]) => [
    `PlatformShell.${key}`, translations[language.code as keyof typeof translations],
  ])),
}]));
