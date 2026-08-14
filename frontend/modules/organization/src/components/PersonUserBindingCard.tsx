import { useMemo, useState } from 'react';
import { usePermission } from '@icp-platform/core';
import { Button, EmptyState, ErrorState, LoadingState, Modal, Panel, StatusBadge } from '@icp-platform/ui/core';
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
}

export default function PersonUserBindingCard({ summary, title = '账号绑定' }: PersonUserBindingCardProps) {
  const canBind = usePermission('Organization.Person.LinkUser');
  const [binding, setBinding] = useState(summary.data);
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState('');
  const [candidates, setCandidates] = useState<PersonUserBindingCandidateDto[]>([]);
  const [selectedAccountEid, setSelectedAccountEid] = useState('');
  const [reason, setReason] = useState('');
  const [confirmed, setConfirmed] = useState(false);
  const [loading, setLoading] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState('');
  const selected = useMemo(
    () => candidates.find((candidate) => candidate.accountEid === selectedAccountEid),
    [candidates, selectedAccountEid],
  );

  if (summary.status !== 'ready' || !binding) return null;
  const isBound = Boolean(binding.accountEid);
  const maySubmit = !isBound && binding.isPersonEnabled && canBind;

  async function search() {
    try {
      setLoading(true);
      setError('');
      const result = await searchPersonUserBindingCandidates(binding!.personEid, query);
      setCandidates(result);
      setSelectedAccountEid('');
    } catch (failure) {
      setCandidates([]);
      setError(failure instanceof Error ? failure.message : '加载可绑定账号失败');
    } finally {
      setLoading(false);
    }
  }

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    if (!selected || !confirmed || reason.trim().length === 0) return;
    try {
      setSubmitting(true);
      setError('');
      const result = await bindPersonUser(binding!.personEid, {
        accountEid: selected.accountEid,
        expectedRevision: selected.revision,
        reason,
      });
      setBinding({
        ...binding!,
        accountEid: result.accountEid,
        boundAt: result.boundAt,
        boundBy: result.boundBy,
        reason: result.reason,
        account: result.account ?? selected,
        isAccountAvailable: true,
      });
      close();
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : '绑定账号失败');
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
    setError('');
  }

  return (
    <>
      <Panel title={title} meta="永久一对一绑定；提交后不能解绑、改绑或重绑">
        <div className="sh-page-stack">
          <div className="sh-form-inline">
            <StatusBadge
              variant={isBound ? 'success' : binding.isPersonEnabled ? 'neutral' : 'warning'}
              label={isBound ? '已永久绑定' : binding.isPersonEnabled ? '尚未绑定' : '人员已停用'}
            />
            {!isBound && binding.isPersonEnabled && !canBind ? <span className="sh-page-description">当前账号没有绑定权限。</span> : null}
          </div>

          {isBound ? (
            <dl className="sh-meta-block">
              <Fact label="人员姓名" value={binding.personName} />
              <Fact label="User EID" value={binding.accountEid ?? ''} code />
              <Fact label="ECODE" value={binding.account?.ecode ?? (binding.isAccountAvailable ? '—' : 'Identity 暂不可用')} code />
              <Fact label="ENAME" value={binding.account?.ename ?? (binding.isAccountAvailable ? '—' : 'Identity 暂不可用')} />
              <Fact label="账号状态" value={binding.account?.status ?? '暂不可用'} />
              <Fact label="绑定时间" value={binding.boundAt ? new Date(binding.boundAt).toLocaleString() : '—'} />
              <Fact label="绑定操作者" value={binding.boundBy ?? '—'} code />
              <Fact label="绑定原因" value={binding.reason ?? '—'} />
            </dl>
          ) : (
            <div className="sh-helper-block">
              <strong>{binding.personName}</strong>
              <p>{binding.isPersonEnabled ? '搜索一个普通账号，核对 ECODE、ENAME、状态和 Revision 后提交永久绑定。' : '停用人员不能查询候选或提交绑定。'}</p>
            </div>
          )}

          {maySubmit ? <Button variant="primary" onClick={() => setOpen(true)}>建立永久绑定</Button> : null}
        </div>
      </Panel>

      <Modal open={open && maySubmit} onClose={close} title="建立人员账号永久绑定" size="lg" footer={(
        <><Button onClick={close} disabled={submitting}>取消</Button><Button variant="primary" type="submit" form="person-user-binding-form" disabled={submitting || !selected || reason.trim().length === 0 || !confirmed}>{submitting ? '提交中…' : '确认永久绑定'}</Button></>
      )}>
        <form id="person-user-binding-form" onSubmit={(event) => void submit(event)} className="sh-page-stack">
          <div className="sh-helper-block"><strong>{binding.personName}</strong><p>Person.Name 与 User.ENAME 分别维护，绑定不会同步名称。</p></div>
          <div className="sh-form-inline">
            <label className="sh-form-group sh-form-group-lg" style={{ flex: 1 }}><span className="sh-form-label">按 ECODE 或 ENAME 搜索</span><input className="sh-form-input" value={query} onChange={(event) => setQuery(event.target.value)} /></label>
            <Button type="button" onClick={() => void search()} disabled={loading}>{loading ? '搜索中…' : '搜索候选'}</Button>
          </div>
          {loading ? <LoadingState message="正在加载最多 20 个可绑定账号…" /> : null}
          {error ? <ErrorState message={error} /> : null}
          {!loading && candidates.length === 0 ? <EmptyState message="请搜索尚未绑定的普通账号。" /> : null}
          {candidates.length > 0 ? <div className="sh-table-wrap"><table className="sh-table"><thead><tr><th>选择</th><th>ECODE</th><th>ENAME</th><th>状态</th><th>Revision</th></tr></thead><tbody>{candidates.map((candidate) => <tr key={candidate.accountEid}><td><input type="radio" name="binding-candidate" aria-label={`选择 ${candidate.ecode}`} checked={selectedAccountEid === candidate.accountEid} onChange={() => setSelectedAccountEid(candidate.accountEid)} /></td><td className="sh-code">{candidate.ecode}</td><td>{candidate.ename}</td><td><StatusBadge variant={candidate.status === 'Enabled' ? 'success' : 'warning'} label={candidate.status} /></td><td>{candidate.revision}</td></tr>)}</tbody></table></div> : null}
          {selected ? <div className="sh-helper-block"><strong>绑定预览：{binding.personName} → {selected.ecode} · {selected.ename}</strong><p>User EID：<span className="sh-code">{selected.accountEid}</span> · {selected.status} · Revision {selected.revision}</p></div> : null}
          <label className="sh-form-group sh-form-group-lg"><span className="sh-form-label">绑定原因</span><textarea className="sh-form-input" rows={4} value={reason} onChange={(event) => setReason(event.target.value)} maxLength={500} required /></label>
          <label className="sh-form-inline"><input type="checkbox" checked={confirmed} onChange={(event) => setConfirmed(event.target.checked)} /><span>我已核对人员与账号，理解绑定提交后不能解绑、改绑或重绑。</span></label>
        </form>
      </Modal>
    </>
  );
}

function Fact({ label, value, code }: { label: string; value: string; code?: boolean }) {
  return <div className="sh-meta-row"><dt className="sh-meta-label">{label}</dt><dd className={code ? 'sh-code' : undefined}>{value}</dd></div>;
}
