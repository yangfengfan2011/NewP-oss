import { useLocalization } from '@icp-platform/core';
import { Select } from 'antd';

export function LanguageSelect() {
  const { currentLanguage, supportedLanguages, setLanguage, t } = useLocalization();

  return (
    <Select
      className="np-shell-language-select"
      aria-label={t('Platform.Language', '语言')}
      value={currentLanguage}
      onChange={setLanguage}
      options={supportedLanguages.map(language => ({ value: language.code, label: language.displayName }))}
    />
  );
}
