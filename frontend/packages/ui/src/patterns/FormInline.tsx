import React from 'react';

export interface FormInlineProps {
  children: React.ReactNode;
  className?: string;
}

/**
 * FormInline is a structural pattern that arranges form elements
 * in a single horizontal row with consistent spacing.
 */
export function FormInline({ children, className = '' }: FormInlineProps) {
  return (
    <div className={`sh-form-inline ${className}`}>
      {children}
    </div>
  );
}
