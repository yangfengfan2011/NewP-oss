import React from 'react';

export interface SummaryCardProps {
  label: string;
  value: React.ReactNode;
  meta?: React.ReactNode;
  className?: string;
}

export function SummaryCard({ label, value, meta, className = '' }: SummaryCardProps) {
  return (
    <div className={`sh-summary-card ${className}`}>
      <span className="sh-summary-card-label">{label}</span>
      <span className="sh-summary-card-value">{value}</span>
      {meta && <span className="sh-summary-card-meta">{meta}</span>}
    </div>
  );
}
