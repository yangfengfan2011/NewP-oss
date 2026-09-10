import { useEffect, useState } from 'react';
import { Alert, Checkbox, Pagination, Select, Spin, Typography } from 'antd';
import { platformFetch } from '@icp-platform/core';
import { isStandardCrudRecord, type StandardCrudActionInputRenderContext } from '@icp-platform/sdk';
import { requestRole } from './roleResource';

interface Account { id: string; code: string; name: string; status: string }
interface Role { ECODE: string; ENAME: string; IsEnabled: boolean }
function RoleOptions({ selected, onChange }: { selected: string[]; onChange: (codes: string[]) => void }) {
  const [page, setPage] = useState(1);
  const [catalog, setCatalog] = useState<{ items: Role[]; total: number }>();
  const [error, setError] = useState('');
  useEffect(() => {
    const controller = new AbortController();
    setCatalog(undefined);
    setError('');
    void requestRole(platformFetch as typeof fetch, `/api/authorization/roles/query?pageIndex=${page}&pageSize=20`, { signal: controller.signal })
      .then(result => {
        if (controller.signal.aborted) return;
        if (!isStandardCrudRecord(result) || !Array.isArray(result.items) || typeof result.total !== 'number') throw new Error('角色目录响应无效');
        setCatalog({ items: (result.items as Role[]).filter(role => role.ECODE !== 'LocalAdmin'), total: result.total });
      }).catch((failure: unknown) => { if (!controller.signal.aborted) setError(failure instanceof Error ? failure.message : '角色目录读取失败'); });
    return () => controller.abort();
  }, [page]);
  if (error) return <Alert type="error" message={error} />;
  if (!catalog) return <Spin />;
  return <>
    <Checkbox.Group style={{ display: 'flex', flexDirection: 'column', gap: 8, marginTop: 16 }} value={selected}
      options={catalog.items.map(role => ({ value: role.ECODE, label: `${role.ENAME} (${role.ECODE})${role.IsEnabled ? '' : ' · 停用（不生效）'}` }))}
      onChange={codes => onChange([...selected.filter(code => !catalog.items.some(role => role.ECODE === code)), ...codes as string[]])} />
    <Typography.Paragraph>已配置 {selected.length} 个角色</Typography.Paragraph>
    <Pagination current={page} pageSize={20} total={catalog.total} showSizeChanger={false} onChange={setPage} />
  </>;
}
export function UserRoleInput({ value, onChange }: StandardCrudActionInputRenderContext) {
  const [query, setQuery] = useState('');
  const [accountId, setAccountId] = useState<string>();
  const [accounts, setAccounts] = useState<Account[]>([]);
  const [ready, setReady] = useState(false);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);
  useEffect(() => {
    const controller = new AbortController();
    void requestRole(platformFetch as typeof fetch, `/api/authorization/users/candidates?query=${encodeURIComponent(query)}`, { signal: controller.signal })
      .then(result => { if (!controller.signal.aborted) { if (!Array.isArray(result)) throw new Error('账号目录响应无效'); setAccounts(result as Account[]); } })
      .catch((failure: unknown) => { if (!controller.signal.aborted) setError(failure instanceof Error ? failure.message : '账号查询失败'); });
    return () => controller.abort();
  }, [query]);
  useEffect(() => {
    const controller = new AbortController();
    onChange(undefined);
    setError('');
    setReady(false);
    if (!accountId) return () => controller.abort();
    setLoading(true);
    void requestRole(platformFetch as typeof fetch, `/api/authorization/users/${encodeURIComponent(accountId)}/roles`, { signal: controller.signal }).then(current => {
      if (controller.signal.aborted) return;
      if (!isStandardCrudRecord(current) || !Array.isArray(current.roleCodes)) throw new Error('账号角色响应无效');
      setReady(true);
      onChange({ accountId, codes: current.roleCodes });
    }).catch((failure: unknown) => { if (!controller.signal.aborted) setError(failure instanceof Error ? failure.message : '角色读取失败'); })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  // 共享动作持有输入；用户选择不重新触发目录读取，切换账号取消旧结果。
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [accountId]);
  const selected = isStandardCrudRecord(value) && Array.isArray(value.codes) ? value.codes as string[] : [];
  return <div>
    <Typography.Paragraph>选择已有普通账号，完整保存已配置角色。停用角色不产生有效权限。</Typography.Paragraph>
    <Select aria-label="账号" showSearch filterOption={false} style={{ width: '100%' }} placeholder="搜索账号或姓名"
      value={accountId} onSearch={setQuery} onChange={id => { onChange(undefined); setAccountId(id); }}
      options={accounts.map(account => ({ value: account.id, label: `${account.name} (${account.code})${account.status === 'Disabled' ? ' · 停用账号' : ''}` }))} />
    {error && <Alert type="error" message={error} />}
    {loading && <Spin />}
    {ready && !error && <RoleOptions selected={selected} onChange={codes => onChange({ accountId, codes })} />}
  </div>;
}
