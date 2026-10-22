import React from 'react';

export interface SwitchProps {
  checked: boolean;
  onChange: (checked: boolean) => void;
  label?: string;
  disabled?: boolean;
  id?: string;
}

export function Switch({ checked, onChange, label, disabled, id }: SwitchProps) {
  return (
    <label className={`sh-switch-label${disabled ? ' sh-switch-disabled' : ''}`}>
      <button
        id={id}
        type="button"
        role="switch"
        aria-checked={checked}
        disabled={disabled}
        className={`sh-switch-track${checked ? ' sh-switch-track-on' : ''}`}
        onClick={() => onChange(!checked)}
      >
        <span className="sh-switch-thumb" />
      </button>
      {label && <span>{label}</span>}
    </label>
  );
}
