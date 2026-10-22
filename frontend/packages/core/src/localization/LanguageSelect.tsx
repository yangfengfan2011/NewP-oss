import { useLocalization } from './LocalizationContext';

/** 登录与 Shell 共用支持语言清单和选择行为；不拥有账号或认证状态。 */
export function LanguageSelect({ className }: { className?: string }) {
  const { currentLanguage, supportedLanguages, setLanguage, t } = useLocalization();
  return (
    <label className={className ?? 'np-language-select'}>
      <span>{t('Platform.Language', '语言')}</span>{' '}
      <select aria-label={t('Platform.Language', '语言')} value={currentLanguage}
        onChange={(event) => setLanguage(event.target.value)}>
        {supportedLanguages.map(language => <option key={language.code} value={language.code}>{language.displayName}</option>)}
      </select>
    </label>
  );
}
