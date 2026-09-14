import type { StandardCrudSummaryBlock } from '@icp-platform/sdk';
import TextArea from 'antd/es/input/TextArea';
import AuthorizationSummaryCard from '../components/AuthorizationSummaryCard';
import PersonUserBindingCard from '../components/PersonUserBindingCard';
import type { PersonUserBindingSummaryData } from '../services/organizationService';

export function PersonUserBindingSummary({
  summary,
  title,
  featureAction,
}: {
  featureAction?: { position: string; allowed: boolean };
  summary: StandardCrudSummaryBlock<PersonUserBindingSummaryData>;
  title: string;
}) {
  return <PersonUserBindingCard summary={summary} title={title} featureAction={featureAction} />;
}

export function AuthorizationSummary({
  summary,
  title,
}: {
  summary: StandardCrudSummaryBlock<string[]>;
  title: string;
}) {
  return <AuthorizationSummaryCard summary={summary} title={title} />;
}

export function DisableReasonInput({
  value,
  onChange,
}: {
  value: unknown;
  onChange: (value: unknown) => void;
}) {
  return (
    <TextArea
      aria-label="停用原因"
      autoSize={{ minRows: 3, maxRows: 6 }}
      maxLength={500}
      showCount
      placeholder="请输入停用原因（必填）"
      value={typeof value === 'string' ? value : ''}
      onChange={(event) => onChange(event.target.value)}
    />
  );
}
