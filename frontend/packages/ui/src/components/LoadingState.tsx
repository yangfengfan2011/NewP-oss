import React from 'react';

export interface LoadingStateProps {
  message?: string;
}

export function LoadingState({ message = '加载中...' }: LoadingStateProps) {
  return (
    <div className="sh-page-loading">
      {message}
    </div>
  );
}
