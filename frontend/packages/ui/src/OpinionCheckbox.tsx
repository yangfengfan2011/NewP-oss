import { useLocalization } from '@icp-platform/core';
import { Button, Tooltip, theme } from 'antd';

const opinions = {
  unset: { key: 'DataScope.Unset', label: '不选', mark: '', next: 'allow' },
  allow: { key: 'DataScope.Allow', label: '同意', mark: '✓', next: 'deny' },
  deny: { key: 'DataScope.Deny', label: '拒绝', mark: '✕', next: 'unset' },
} as const;

/** 三种显式意见独立于最终权限；叉号避免与普通 checkbox 的部分选中混淆。 */
export default function OpinionCheckbox({ value, label, disabled, onChange }: {
  value: string; label: string; disabled?: boolean; onChange: (value: string) => void;
}) {
  const { t } = useLocalization();
  const { token } = theme.useToken();
  const state = opinions[value as keyof typeof opinions] ?? opinions.unset;
  return <Tooltip title={t('DataScope.OpinionCycle', '{current}；点击切换为{next}', { current: t(state.key, state.label), next: t(opinions[state.next].key, opinions[state.next].label) })}>
    <Button role="checkbox" aria-label={t('DataScope.OpinionLabel', '{action}：{opinion}', { action: label, opinion: t(state.key, state.label) })} aria-checked={value === 'allow' ? true : value === 'deny' ? 'mixed' : false}
      disabled={disabled} onClick={() => onChange(state.next)} size="small"
      style={{ width: 24, minWidth: 24, height: 24, padding: 0, fontWeight: 700,
        color: value === 'deny' ? token.colorError : value === 'allow' ? token.colorPrimary : token.colorText,
        borderColor: value === 'deny' ? token.colorError : value === 'allow' ? token.colorPrimary : token.colorBorder,
        background: value === 'deny' ? token.colorErrorBg : value === 'allow' ? token.colorPrimaryBg : token.colorBgContainer }}>
      <span aria-hidden="true">{state.mark}</span>
    </Button>
  </Tooltip>;
}
