import React from 'react';

export type StatusBadgeVariant = 'success' | 'danger' | 'warning' | 'neutral' | 'info';

export interface StatusBadgeProps {
  variant?: StatusBadgeVariant;
  label: string;
  icon?: React.ReactNode;
  className?: string;
}

/**
 * StatusBadge is a structural pattern for displaying entity status.
 * Unlike Badge (inline text emphasis), StatusBadge is designed for
 * entity status display in tables, cards and detail panels.
 */
export function StatusBadge({ variant = 'neutral', label, icon, className = '' }: StatusBadgeProps) {
  return (
    <span className={`sh-status-badge sh-status-badge-${variant} ${className}`}>
      {icon && <span className="sh-status-badge-icon">{icon}</span>}
      {label}
    </span>
  );
}
