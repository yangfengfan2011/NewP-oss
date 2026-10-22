import React from 'react';

export interface InputProps extends React.InputHTMLAttributes<HTMLInputElement> {
  label?: string;
  error?: string;
}

export function Input({ label, error, className = '', id, ...props }: InputProps) {
  const inputId = id || label?.replace(/\s+/g, '-').toLowerCase();

  return (
    <div className="sh-form-group">
      {label && (
        <label className="sh-form-label" htmlFor={inputId}>
          {label}
        </label>
      )}
      <input
        id={inputId}
        className={`sh-form-input ${className}`}
        {...props}
      />
      {error && <div className="sh-form-error">{error}</div>}
    </div>
  );
}
