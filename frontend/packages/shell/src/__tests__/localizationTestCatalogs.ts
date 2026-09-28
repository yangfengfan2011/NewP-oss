import bootstrap from '../localization/bootstrapLocalization.generated.json';
import shellDictionary from '../../../../../modules/Platform/metadata/dictionaries/PlatformShell.dic.json';

export const shellTestCatalogs = Object.fromEntries(bootstrap.supportedLanguages.map(language => [language.code, {
  ...bootstrap.catalogs[language.code as keyof typeof bootstrap.catalogs],
  ...Object.fromEntries(Object.entries(shellDictionary.PlatformShell).map(([key, translations]) => [
    `PlatformShell.${key}`, translations[language.code as keyof typeof translations],
  ])),
}]));
