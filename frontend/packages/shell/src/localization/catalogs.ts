import languagesConfig from '../../../../../resources/localization/languages.json';
import commonZhsDictionary from '../../../../../resources/localization/zhs/dictionary/Common.json';
import commonEnDictionary from '../../../../../resources/localization/en/dictionary/Common.json';
import platformZhsDictionary from '../../../../../resources/localization/zhs/dictionary/Platform.json';
import platformEnDictionary from '../../../../../resources/localization/en/dictionary/Platform.json';
import genderDefinition from '../../../../../resources/enumerations/Gender.json';
import messageStateDefinition from '../../../../../resources/enumerations/MessageState.json';
import selectModeDefinition from '../../../../../resources/enumerations/SelectMode.json';
import genderZhsCatalog from '../../../../../resources/localization/zhs/enumerations/Gender.json';
import genderEnCatalog from '../../../../../resources/localization/en/enumerations/Gender.json';
import messageStateZhsCatalog from '../../../../../resources/localization/zhs/enumerations/MessageState.json';
import messageStateEnCatalog from '../../../../../resources/localization/en/enumerations/MessageState.json';

export const dictionaryLanguageConfig = languagesConfig;

export const dictionaryCatalogs = {
  zhs: {
    ...commonZhsDictionary,
    ...platformZhsDictionary,
  },
  en: {
    ...commonEnDictionary,
    ...platformEnDictionary,
  },
};

export const enumerationDefinitions = {
  Gender: genderDefinition,
  MessageState: messageStateDefinition,
  SelectMode: selectModeDefinition,
};

export const enumerationCatalogs = {
  zhs: {
    ...genderZhsCatalog,
    ...messageStateZhsCatalog,
  },
  en: {
    ...genderEnCatalog,
    ...messageStateEnCatalog,
  },
};
