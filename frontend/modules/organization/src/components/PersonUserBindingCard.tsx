import { useEffect, useMemo, useState } from 'react';
import { formatRequestError, LanguageSelect, useLocalization } from '@icp-platform/core';
import Modal from 'antd/es/modal';
import { Button, EmptyState, ErrorState, LoadingState, Panel, StatusBadge } from '@icp-platform/ui/core';
import type { StandardCrudSummaryBlock } from '@icp-platform/sdk';
import {
  bindPersonUser,
  searchPersonUserBindingCandidates,
  type PersonUserBindingCandidateDto,
  type PersonUserBindingSummaryData,
} from '../services/organizationService';

interface PersonUserBindingCardProps {
  summary: StandardCrudSummaryBlock<PersonUserBindingSummaryData>;
  title?: string;
  featureAction?: { position: string; allowed: boolean };
}

export default function PersonUserBindingCard({ summary, title, featureAction }: PersonUserBindingCardProps) {
  const { t } = useLocalization();
  const text = (key: string) => t(`Organization.Person.${key}`);
  const accountStatus = (status: string) => t(`Organization.Person.${status}`, status);
  const canBind = featureAction?.allowed === true;
  const [binding, setBinding] = useState(summary.data);
  // 宿主保留摘要实例时消费新的权威事实；草稿独立保留，成功写入在下一次服务端摘要到达前仍可见。
  useEffect(() => {
    if (summary.status === 'ready') setBinding(summary.data);
  }, [summary.data, summary.status]);
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState('');
  const [candidates, setCandidates] = useState<PersonUserBindingCandidateDto[]>([]);
  const [selectedAccountEid, setSelectedAccountEid] = useState('');
  const [reason, setReason] = useState('');
  const [confirmed, setConfirmed] = useState(false);
  const [loading, setLoading] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<unknown>();
  const selected = useMemo(
    () => candidates.find((candidate) => candidate.accountEid === selectedAccountEid),
    [candidates, selectedAccountEid],
  );

  if (summary.status !== 'ready' || !binding) return null;
  const isBound = Boolean(binding.accountEid);
  const maySubmit = !isBound && binding.isPersonEnabled && canBind;

  async function search() {
    if (!canBind) return;
    try {
      setLoading(true);
      setError(undefined);
      const result = await searchPersonUserBindingCandidates(binding!.personEid, query, featureAction?.position);
      setCandidates(result);
      setSelectedAccountEid('');
    } catch (failure) {
      setCandidates([]);
      setError(failure);
    } finally {
      setLoading(false);
    }
  }

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    if (!selected || !confirmed || reason.trim().length === 0) return;
    if (!canBind) return;
    try {
      setSubmitting(true);
      setError(undefined);
      const result = await bindPersonUser(binding!.personEid, {
        accountEid: selected.accountEid,
        expectedRevision: selected.revision,
        reason,
      }, featureAction?.position);
      setBinding({
        ...binding!,
        accountEid: result.accountEid,
        boundAt: result.boundAt,
        boundBy: result.boundBy,
        reason: result.reason,
        account: result.account ?? selected,
        isAccountAvailable: true,
        isAccountReadable: true,
      });
      close();
    } catch (failure) {
      setError(failure);
    } finally {
      setSubmitting(false);
    }
  }

  function close() {
    setOpen(false);
    setQuery('');
    setCandidates([]);
    setSelectedAccountEid('');
    setReason('');
    setConfirmed(false);
    setError(undefined);
  }

  return (
    <>
      <Panel title={title ?? text('BindingTitle')} meta={text('BindingHelp')}>
        <div className="sh-page-stack">
          <div className="sh-form-inline">
            <StatusBadge
              variant={isBound ? 'success' : binding.isPersonEnabled ? 'neutral' : 'warning'}
              label={isBound ? text('Bound') : binding.isPersonEnabled ? text('Unbound') : text('PersonDisabled')}
            />
            {!isBound && binding.isPersonEnabled && !canBind ? <span className="sh-page-description">{text('NoBindingPermission')}</span> : null}
          </div>

          {isBound ? (
            <dl className="sh-meta-block">
              <Fact label={text('PersonName')} value={binding.personName} />
              <Fact label={text('AccountId')} value={binding.accountEid ?? ''} code />
              <Fact label={text('AccountCode')} value={binding.isAccountReadable ? binding.account?.ecode ?? (binding.isAccountAvailable ? '—' : text('IdentityUnavailable')) : '***'} code />
              <Fact label={text('AccountName')} value={binding.isAccountReadable ? binding.account?.ename ?? (binding.isAccountAvailable ? '—' : text('IdentityUnavailable')) : '***'} />
              <Fact label={text('AccountStatus')} value={binding.isAccountReadable ? binding.account ? accountStatus(binding.account.status) : text('Unavailable') : '***'} />
              <Fact label={text('BoundAt')} value={binding.boundAt ? new Date(binding.boundAt).toLocaleString() : '—'} />
              <Fact label={text('BoundBy')} value={binding.boundBy ?? '—'} code />
              <Fact label={text('BindingReason')} value={binding.reason ?? '—'} />
            </dl>
          ) : (
            <div className="sh-helper-block">
              <strong>{binding.personName}</strong>
              <p>{binding.isPersonEnabled ? text('BindingInstructions') : text('DisabledBindingHelp')}</p>
            </div>
          )}

          {maySubmit ? <Button variant="primary" onClick={() => setOpen(true)}>{text('StartBinding')}</Button> : null}
        </div>
      </Panel>

      <Modal destroyOnHidden open={open && maySubmit} onCancel={close} closable={{ 'aria-label': text('Close') }} title={<div className="sh-form-inline"><span>{text('BindingDialogTitle')}</span><LanguageSelect /></div>} width={800} footer={(
        <><Button onClick={close} disabled={submitting}>{text('Cancel')}</Button><Button variant="primary" type="submit" form="person-user-binding-form" disabled={submitting || !selected || reason.trim().length === 0 || !confirmed}>{submitting ? text('Submitting') : text('ConfirmBinding')}</Button></>
      )}>
        <form id="person-user-binding-form" onSubmit={(event) => void submit(event)} className="sh-page-stack">
          <div className="sh-helper-block"><strong>{binding.personName}</strong><p>{text('SeparateNames')}</p></div>
          <div className="sh-form-inline">
            <label className="sh-form-group sh-form-group-lg" style={{ flex: 1 }}><span className="sh-form-label">{text('SearchLabel')}</span><input className="sh-form-input" value={query} onChange={(event) => setQuery(event.target.value)} /></label>
            <Button type="button" onClick={() => void search()} disabled={loading}>{loading ? text('Searching') : text('SearchCandidates')}</Button>
          </div>
          {loading ? <LoadingState message={text('LoadingCandidates')} /> : null}
          {error ? <ErrorState message={formatRequestError(error, t)} /> : null}
          {!loading && candidates.length === 0 ? <EmptyState message={text('EmptyCandidates')} /> : null}
          {candidates.length > 0 ? <div className="sh-table-wrap"><table className="sh-table"><thead><tr><th>{text('Select')}</th><th>{text('AccountCode')}</th><th>{text('AccountName')}</th><th>{text('Status')}</th><th>{text('Revision')}</th></tr></thead><tbody>{candidates.map((candidate) => <tr key={candidate.accountEid}><td><input type="radio" name="binding-candidate" aria-label={t('Organization.Person.SelectCandidate', '', { code: candidate.ecode })} checked={selectedAccountEid === candidate.accountEid} onChange={() => setSelectedAccountEid(candidate.accountEid)} /></td><td className="sh-code">{candidate.ecode}</td><td>{candidate.ename}</td><td><StatusBadge variant={candidate.status === 'Enabled' ? 'success' : 'warning'} label={accountStatus(candidate.status)} /></td><td>{candidate.revision}</td></tr>)}</tbody></table></div> : null}
          {selected ? <div className="sh-helper-block"><strong>{t('Organization.Person.BindingPreview', '', { person: binding.personName, code: selected.ecode, name: selected.ename })}</strong><p>{text('AccountId')}: <span className="sh-code">{selected.accountEid}</span> · {accountStatus(selected.status)} · {text('Revision')} {selected.revision}</p></div> : null}
          <label className="sh-form-group sh-form-group-lg"><span className="sh-form-label">{text('BindingReason')}</span><textarea className="sh-form-input" rows={4} placeholder={text('BindingReasonPlaceholder')} value={reason} onChange={(event) => setReason(event.target.value)} maxLength={500} required /></label>
          <label className="sh-form-inline"><input type="checkbox" checked={confirmed} onChange={(event) => setConfirmed(event.target.checked)} /><span>{text('BindingConfirmation')}</span></label>
        </form>
      </Modal>
    </>
  );
}

function Fact({ label, value, code }: { label: string; value: string; code?: boolean }) {
  return <div className="sh-meta-row"><dt className="sh-meta-label">{label}</dt><dd className={code ? 'sh-code' : undefined}>{value}</dd></div>;
}
