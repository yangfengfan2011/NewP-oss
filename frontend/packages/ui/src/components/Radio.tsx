import React from 'react';

export interface RadioOption {
  value: string;
  label: string;
  disabled?: boolean;
}

export interface RadioProps {
  name: string;
  options: RadioOption[];
  value?: string;
  onChange: (value: string) => void;
  direction?: 'horizontal' | 'vertical';
}

export function Radio({ name, options, value, onChange, direction = 'horizontal' }: RadioProps) {
  return (
    <div className={`sh-radio-group sh-radio-group-${direction}`} role="radiogroup">
      {options.map((opt) => (
        <label key={opt.value} className={`sh-radio-label${opt.disabled ? ' sh-radio-disabled' : ''}`}>
          <input
            type="radio"
            name={name}
            className="sh-radio-input"
            value={opt.value}
            checked={value === opt.value}
            disabled={opt.disabled}
            onChange={() => onChange(opt.value)}
          />
          <span>{opt.label}</span>
        </label>
      ))}
    </div>
  );
}
