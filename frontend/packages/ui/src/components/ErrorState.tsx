import React from 'react';

export interface ErrorStateProps {
  message?: string;
  children?: React.ReactNode;
}

export function ErrorState({ message, children }: ErrorStateProps) {
  return (
    <div className="sh-page-error">
      {children || message || '发生错误'}
    </div>
  );
}
