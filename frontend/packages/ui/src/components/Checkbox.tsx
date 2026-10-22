import React from 'react';

export interface CheckboxProps {
  checked: boolean;
  onChange: (checked: boolean) => void;
  label?: string;
  disabled?: boolean;
  id?: string;
}

export function Checkbox({ checked, onChange, label, disabled, id }: CheckboxProps) {
  return (
    <label className="sh-checkbox-label">
      <input
        id={id}
        type="checkbox"
        className="sh-checkbox-input"
        checked={checked}
        disabled={disabled}
        aria-label={label ? undefined : 'checkbox'}
        onChange={(e) => onChange(e.target.checked)}
      />
      {label && <span>{label}</span>}
    </label>
  );
}
