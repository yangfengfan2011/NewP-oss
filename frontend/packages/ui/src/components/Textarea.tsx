import React from 'react';

export interface TextareaProps extends React.TextareaHTMLAttributes<HTMLTextAreaElement> {
  label?: string;
}

export function Textarea({ label, className = '', id, ...props }: TextareaProps) {
  const textareaId = id || label?.replace(/\s+/g, '-').toLowerCase();

  return (
    <div className="sh-form-group">
      {label && (
        <label className="sh-form-label" htmlFor={textareaId}>
          {label}
        </label>
      )}
      <textarea
        id={textareaId}
        className={`sh-form-input ${className}`}
        {...props}
      />
    </div>
  );
}
