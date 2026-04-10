import React from 'react';

export type BadgeVariant = 'success' | 'danger' | 'warning' | 'neutral';

export interface BadgeProps {
  variant?: BadgeVariant;
  children: React.ReactNode;
  className?: string;
}

export function Badge({ variant = 'neutral', children, className = '' }: BadgeProps) {
  return (
    <span className={`sh-status-badge sh-status-badge-${variant} ${className}`}>
      {children}
    </span>
  );
}
