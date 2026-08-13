import { FormEvent, useCallback, useEffect, useMemo, useState } from 'react';
import {
  Button,
  EmptyState,
  ErrorState,
  LoadingState,
  Modal,
  PageHeader,
  Pagination,
  Panel,
  StatusBadge,
} from '@icp-platform/ui';
import {
  createAccount,
  deleteAccount,
  getAccount,
  IdentityApiError,
  listAccounts,
  resetCredential,
  setAccountStatus,
  updateAccountName,
  type IdentityAccount,
  type IdentityAccountStatus,
  type IdentityListFilters,
  type IdentityAccountDetail,
} from '../api/identityClient';
import { availableIdentityActions, type IdentityAccountAction } from '../state/accountActions';
import { closeOneTimeSecret, revealOneTimeSecret, type OneTimeSecretState } from '../state/oneTimeSecret';

const PAGE_SIZE = 20;

interface FilterDraft {
  query: string;
  status: '' | IdentityAccountStatus;
  isPersonnelBound: '' | 'true' | 'false';
  isLocalAdministrator: '' | 'true' | 'false';
}

type Operation = { type: IdentityAccountAction; account: IdentityAccount } | undefined;

const initialDraft: FilterDraft = {
  query: '',
  status: '',
  isPersonnelBound: '',
  isLocalAdministrator: '',
};

export default function IdentityAccountsPage() {
  const [draft, setDraft] = useState<FilterDraft>(initialDraft);
  const [filters, setFilters] = useState<IdentityListFilters>({ page: 1, pageSize: PAGE_SIZE });
  const [items, setItems] = useState<IdentityAccount[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [detail, setDetail] = useState<IdentityAccountDetail>();
  const [detailLoading, setDetailLoading] = useState(false);
  const [createOpen, setCreateOpen] = useState(false);
  const [createECODE, setCreateECODE] = useState('');
  const [createENAME, setCreateENAME] = useState('');
  const [operation, setOperation] = useState<Operation>();
  const [operationReason, setOperationReason] = useState('');
  const [deleteConfirmation, setDeleteConfirmation] = useState('');
  const [editName, setEditName] = useState('');
  const [operationError, setOperationError] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [secret, setSecret] = useState<OneTimeSecretState>({ value: undefined });
  const [copyStatus, setCopyStatus] = useState('');

  const load = useCallback(async (nextFilters = filters) => {
    setLoading(true);
    setError('');
    try {
      const page = await listAccounts(nextFilters);
      setItems(page.items);
      setTotal(page.totalCount);
    } catch (cause) {
      setError(toMessage(cause));
    } finally {
      setLoading(false);
    }
  }, [filters]);

  useEffect(() => {
    void load(filters);
  }, [filters, load]);

  const loadDetail = useCallback(async (eid: string, eventPage = 1) => {
    setDetailLoading(true);
    setOperationError('');
    try {
      setDetail(await getAccount(eid, eventPage));
    } catch (cause) {
      setOperationError(toMessage(cause));
    } finally {
      setDetailLoading(false);
    }
  }, []);

  const confirmFilters = (event: FormEvent) => {
    event.preventDefault();
    setFilters({
      query: draft.query.trim() || undefined,
      status: draft.status || undefined,
      isPersonnelBound: parseBooleanFilter(draft.isPersonnelBound),
      isLocalAdministrator: parseBooleanFilter(draft.isLocalAdministrator),
      page: 1,
      pageSize: PAGE_SIZE,
    });
  };

  const resetFilters = () => {
    setDraft(initialDraft);
    setFilters({ page: 1, pageSize: PAGE_SIZE });
  };

  const showDetail = (account: IdentityAccount) => {
    setDetail(undefined);
    void loadDetail(account.eid);
  };

  const startOperation = (type: IdentityAccountAction, account: IdentityAccount) => {
    setOperation({ type, account });
    setOperationReason('');
    setDeleteConfirmation('');
    setEditName(account.ename);
    setOperationError('');
  };

  const closeOperation = () => {
    setOperation(undefined);
    setOperationReason('');
    setDeleteConfirmation('');
    setEditName('');
    setOperationError('');
  };

  const submitCreate = async (event: FormEvent) => {
    event.preventDefault();
    setSubmitting(true);
    setOperationError('');
    try {
      const issuance = await createAccount({ ecode: createECODE, ename: createENAME });
      setCreateOpen(false);
      setCreateECODE('');
      setCreateENAME('');
      setSecret(revealOneTimeSecret(issuance.temporaryCredential));
      await load();
    } catch (cause) {
      setOperationError(toMessage(cause));
    } finally {
      setSubmitting(false);
    }
  };

  const submitOperation = async (event: FormEvent) => {
    event.preventDefault();
    if (!operation) return;
    setSubmitting(true);
    setOperationError('');
    try {
      const { account, type } = operation;
      if (type === 'edit') {
        await updateAccountName(account.eid, { ename: editName, expectedRevision: account.revision });
      } else if (type === 'enable' || type === 'disable') {
        await setAccountStatus(account.eid, {
          targetStatus: type === 'enable' ? 'Enabled' : 'Disabled',
          expectedRevision: account.revision,
          reason: operationReason,
        });
      } else if (type === 'reset') {
        const issuance = await resetCredential(account.eid, {
          expectedRevision: account.revision,
          reason: operationReason,
        });
        setSecret(revealOneTimeSecret(issuance.temporaryCredential));
      } else {
        await deleteAccount(account.eid, {
          expectedRevision: account.revision,
          reason: operationReason,
        });
      }
      closeOperation();
      await load();
      if (detail?.account.eid === account.eid) await loadDetail(account.eid);
    } catch (cause) {
      if (cause instanceof IdentityApiError
          && cause.code === 'IDENTITY_REVISION_CONFLICT'
          && cause.current) {
        // 冲突只刷新权威快照；操作仍保持打开并要求操作者重新确认，绝不自动重放写命令。
        setOperation({ ...operation, account: cause.current });
        setOperationReason('');
        setDeleteConfirmation('');
        setOperationError('账号已被其他操作更新。已刷新当前状态，请重新填写并确认。');
        await load();
        if (detail?.account.eid === cause.current.eid) await loadDetail(cause.current.eid);
      } else {
        setOperationError(toMessage(cause));
      }
    } finally {
      setSubmitting(false);
    }
  };

  const closeSecret = () => {
    setSecret((current) => closeOneTimeSecret(current));
    setCopyStatus('');
  };

  const copySecret = async () => {
    if (!secret.value) return;
    try {
      await navigator.clipboard.writeText(secret.value);
      setCopyStatus('已复制。关闭后将无法再次查看。');
    } catch {
      setCopyStatus('复制失败，请现在手动复制。');
    }
  };

  const operationTitle = useMemo(() => operation ? operationLabel(operation.type) : '', [operation]);

  return (
    <div className="sh-page" data-testid="identity-users-page">
      <PageHeader
        eyebrow="身份基础"
        title="账号管理"
        description="管理本地账号的显示名称、状态、凭据和不可恢复删除。所有敏感操作均基于最新 Revision 明确确认。"
        actions={<Button variant="primary" onClick={() => { setCreateOpen(true); setOperationError(''); }}>创建账号</Button>}
      />

      <Panel title="账号目录" meta={`共 ${total} 个符合条件的账号`}>
        <form className="sh-filter-bar" onSubmit={confirmFilters} aria-label="账号筛选">
          <FilterField label="ECODE / ENAME">
            <input className="sh-form-input" value={draft.query} onChange={(event) => setDraft({ ...draft, query: event.target.value })} placeholder="搜索登录账号或显示名称" />
          </FilterField>
          <FilterField label="状态">
            <select className="sh-form-input" value={draft.status} onChange={(event) => setDraft({ ...draft, status: event.target.value as FilterDraft['status'] })}>
              <option value="">Enabled + Disabled</option>
              <option value="Enabled">Enabled</option>
              <option value="Disabled">Disabled</option>
              <option value="Deleted">Deleted</option>
            </select>
          </FilterField>
          <FilterField label="人员绑定">
            <BooleanSelect value={draft.isPersonnelBound} onChange={(value) => setDraft({ ...draft, isPersonnelBound: value })} />
          </FilterField>
          <FilterField label="本地管理员">
            <BooleanSelect value={draft.isLocalAdministrator} onChange={(value) => setDraft({ ...draft, isLocalAdministrator: value })} />
          </FilterField>
          <div className="sh-filter-bar-actions">
            <Button type="submit" variant="primary">应用筛选</Button>
            <Button type="button" onClick={resetFilters}>重置</Button>
          </div>
        </form>

        {loading ? <LoadingState message="账号目录加载中…" /> : error ? <ErrorState message={error} /> : items.length === 0 ? (
          <EmptyState message="没有符合当前筛选条件的账号。" />
        ) : (
          <div className="sh-table-wrap">
            <table className="sh-table">
              <thead><tr><th>ECODE</th><th>ENAME</th><th>状态</th><th>约束</th><th>Revision</th><th>操作</th></tr></thead>
              <tbody>{items.map((account) => (
                <tr key={account.eid}>
                  <td className="sh-code">{account.ecode}</td>
                  <td>{account.ename}</td>
                  <td><AccountStatus status={account.status} /></td>
                  <td>{account.isLocalAdministrator ? '本地管理员' : account.isPersonnelBound ? '已绑定人员' : account.personnelDisableHold ? '人员停用阻断' : '普通账号'}</td>
                  <td>{account.revision}</td>
                  <td><div className="sh-form-inline"><Button size="sm" onClick={() => showDetail(account)}>详情</Button>{availableIdentityActions(account).map((action) => (
                    <Button key={action} size="sm" variant={action === 'delete' ? 'danger' : 'default'} onClick={() => startOperation(action, account)}>{operationLabel(action)}</Button>
                  ))}</div></td>
                </tr>
              ))}</tbody>
            </table>
          </div>
        )}
        {total > 0 && <Pagination current={filters.page} total={total} pageSize={PAGE_SIZE} onPageChange={(page) => setFilters({ ...filters, page })} />}
      </Panel>

      <Modal open={Boolean(detail) || detailLoading} onClose={() => setDetail(undefined)} title="账号详情与安全事件" size="lg">
        {detailLoading && !detail ? <LoadingState message="详情加载中…" /> : detail && (
          <div className="sh-page-stack">
            <Panel title={`${detail.account.ecode} · ${detail.account.ename}`} meta={`Revision ${detail.account.revision}`} compact>
              <dl className="sh-meta-block">
                <DetailFact label="状态" value={detail.account.status} />
                <DetailFact label="本地管理员" value={detail.account.isLocalAdministrator ? '是' : '否'} />
                <DetailFact label="人员绑定" value={detail.account.isPersonnelBound ? '是（永久）' : '否'} />
                <DetailFact label="人员停用阻断" value={detail.account.personnelDisableHold ? '是' : '否'} />
                <DetailFact label="最近更新" value={formatDate(detail.account.updatedAt)} />
              </dl>
            </Panel>
            <Panel title="身份安全事件" meta={`共 ${detail.events.totalCount} 条`} compact>
              {detail.events.items.length === 0 ? <EmptyState message="暂无安全事件。" /> : (
                <div className="sh-table-wrap"><table className="sh-table"><thead><tr><th>时间</th><th>类型</th><th>原因</th><th>操作者</th><th>TraceId</th></tr></thead><tbody>
                  {detail.events.items.map((event) => <tr key={event.eid}><td>{formatDate(event.occurredAt)}</td><td>{event.eventType}</td><td>{event.reason}</td><td>{event.actorKind}</td><td className="sh-code">{event.traceId}</td></tr>)}
                </tbody></table></div>
              )}
              {detail.events.totalCount > 0 && <Pagination current={detail.events.page} total={detail.events.totalCount} pageSize={detail.events.pageSize} onPageChange={(page) => void loadDetail(detail.account.eid, page)} />}
            </Panel>
          </div>
        )}
      </Modal>

      <Modal open={createOpen} onClose={() => { setCreateOpen(false); setOperationError(''); }} title="创建普通账号" footer={(
        <><Button onClick={() => setCreateOpen(false)} disabled={submitting}>取消</Button><Button variant="primary" type="submit" form="identity-create-form" disabled={submitting}>{submitting ? '创建中…' : '创建并签发临时密码'}</Button></>
      )}>
        <form id="identity-create-form" onSubmit={submitCreate}>
          <TextField id="create-ecode" label="ECODE" value={createECODE} onChange={setCreateECODE} required />
          <TextField id="create-ename" label="ENAME" value={createENAME} onChange={setCreateENAME} required />
          <p className="sh-page-description">ECODE 创建后不可修改，账号删除后也不会释放。</p>
          {operationError && <div className="sh-form-error" role="alert">{operationError}</div>}
        </form>
      </Modal>

      <Modal open={Boolean(operation)} onClose={closeOperation} title={operationTitle} footer={operation && (
        <><Button onClick={closeOperation} disabled={submitting}>取消</Button><Button variant={operation.type === 'delete' ? 'danger' : 'primary'} type="submit" form="identity-operation-form" disabled={submitting || !canSubmitOperation(operation, operationReason, deleteConfirmation, editName)}>{submitting ? '提交中…' : '确认提交'}</Button></>
      )}>
        {operation && <form id="identity-operation-form" onSubmit={submitOperation}>
          <div className="sh-helper-block"><strong>{operation.account.ecode}</strong><p>当前状态：{operation.account.status} · Revision {operation.account.revision}</p></div>
          {operation.type === 'edit' ? <TextField id="edit-ename" label="新的 ENAME" value={editName} onChange={setEditName} required /> : (
            <TextField id="operation-reason" label="操作原因" value={operationReason} onChange={setOperationReason} required multiline />
          )}
          {operation.type === 'delete' && <TextField id="delete-confirmation" label={`完整输入 ${operation.account.ecode} 确认不可恢复删除`} value={deleteConfirmation} onChange={setDeleteConfirmation} required />}
          {operation.type === 'reset' && <p className="sh-page-description">重置会撤销目标账号的全部旧会话；临时密码只显示一次。</p>}
          {operationError && <div className="sh-form-error" role="alert">{operationError}</div>}
        </form>}
      </Modal>

      <Modal open={Boolean(secret.value)} onClose={closeSecret} title="一次性临时密码" footer={<Button variant="primary" onClick={closeSecret}>我已安全保存，关闭</Button>}>
        <p>这是唯一一次显示机会。关闭后页面状态会立即销毁该秘密，无法查询或恢复。</p>
        <div className="sh-helper-block"><code className="sh-code" aria-label="临时密码">{secret.value}</code></div>
        <Button onClick={() => void copySecret()}>复制临时密码</Button>
        {copyStatus && <p role="status">{copyStatus}</p>}
      </Modal>
    </div>
  );
}

function FilterField({ label, children }: { label: string; children: React.ReactNode }) {
  return <label className="sh-filter-bar-field"><span className="sh-filter-bar-label">{label}</span>{children}</label>;
}

function BooleanSelect({ value, onChange }: { value: '' | 'true' | 'false'; onChange: (value: '' | 'true' | 'false') => void }) {
  return <select className="sh-form-input" value={value} onChange={(event) => onChange(event.target.value as '' | 'true' | 'false')}><option value="">全部</option><option value="true">是</option><option value="false">否</option></select>;
}

function TextField({ id, label, value, onChange, required, multiline }: { id: string; label: string; value: string; onChange: (value: string) => void; required?: boolean; multiline?: boolean }) {
  return <div className="sh-form-group sh-form-group-lg"><label className="sh-form-label" htmlFor={id}>{label}</label>{multiline ? <textarea id={id} className="sh-form-input" value={value} onChange={(event) => onChange(event.target.value)} required={required} rows={4} /> : <input id={id} className="sh-form-input" value={value} onChange={(event) => onChange(event.target.value)} required={required} />}</div>;
}

function DetailFact({ label, value }: { label: string; value: string }) {
  return <div className="sh-meta-row"><dt className="sh-meta-label">{label}</dt><dd>{value}</dd></div>;
}

function AccountStatus({ status }: { status: IdentityAccountStatus }) {
  const variant = status === 'Enabled' ? 'success' : status === 'Disabled' ? 'warning' : 'neutral';
  return <StatusBadge label={status} variant={variant} />;
}

function parseBooleanFilter(value: '' | 'true' | 'false'): boolean | undefined {
  return value === '' ? undefined : value === 'true';
}

function operationLabel(action: IdentityAccountAction): string {
  return ({ edit: '修改 ENAME', enable: '启用', disable: '停用', reset: '重置凭据', delete: '删除' })[action];
}

function canSubmitOperation(operation: NonNullable<Operation>, reason: string, confirmation: string, editName: string): boolean {
  if (operation.type === 'edit') return editName.trim().length > 0;
  if (!reason.trim()) return false;
  return operation.type !== 'delete' || confirmation === operation.account.ecode;
}

function formatDate(value: string | undefined): string {
  if (!value) return '—';
  return new Intl.DateTimeFormat('zh-CN', { dateStyle: 'medium', timeStyle: 'medium' }).format(new Date(value));
}

function toMessage(cause: unknown): string {
  if (cause instanceof IdentityApiError) return cause.code;
  return cause instanceof Error ? cause.message : '请求失败，请稍后重试。';
}
