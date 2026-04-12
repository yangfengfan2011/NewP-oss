import { useState } from 'react';
import { Button, EmptyState, ErrorState, Input, LoadingState, Modal, StatusBadge } from '@icp-platform/ui';
import { searchIdentityUsers, type IdentityUserOptionDto } from '../services/organizationService';

interface BindUserDialogProps {
  open: boolean;
  onClose: () => void;
  onConfirm: (userId: string) => Promise<void>;
}

export default function BindUserDialog({ open, onClose, onConfirm }: BindUserDialogProps) {
  const [keyword, setKeyword] = useState('');
  const [options, setOptions] = useState<IdentityUserOptionDto[]>([]);
  const [selectedUserId, setSelectedUserId] = useState('');
  const [loading, setLoading] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleSearch() {
    try
    {
      setLoading(true);
      setError(null);
      const result = await searchIdentityUsers(keyword);
      setOptions(result);
      setSelectedUserId(result[0]?.userId ?? '');
    }
    catch (searchError)
    {
      setError(searchError instanceof Error ? searchError.message : '加载账号选项失败');
      setOptions([]);
      setSelectedUserId('');
    }
    finally
    {
      setLoading(false);
    }
  }

  async function handleConfirm() {
    if (!selectedUserId) {
      setError('请先选择一个账号。');
      return;
    }

    try
    {
      setSubmitting(true);
      setError(null);
      await onConfirm(selectedUserId);
      onClose();
      setKeyword('');
      setOptions([]);
      setSelectedUserId('');
    }
    catch (confirmError)
    {
      setError(confirmError instanceof Error ? confirmError.message : '绑定账号失败');
    }
    finally
    {
      setSubmitting(false);
    }
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title="绑定账号"
      footer={(
        <>
          <Button onClick={onClose}>取消</Button>
          <Button variant="primary" onClick={() => void handleConfirm()} disabled={submitting}>
            {submitting ? '绑定中...' : '确认绑定'}
          </Button>
        </>
      )}
    >
      <div style={{ display: 'grid', gap: 16 }}>
        <Input
          label="账号关键字"
          value={keyword}
          onChange={(event) => setKeyword(event.target.value)}
          placeholder="输入账号或显示名"
        />

        <Button onClick={() => void handleSearch()} disabled={loading}>
          {loading ? '搜索中...' : '搜索账号'}
        </Button>

        {loading ? <LoadingState message="正在搜索可绑定账号..." /> : null}
        {error ? <ErrorState message={error} /> : null}

        {!loading && options.length === 0 ? (
          <EmptyState message="当前没有可选账号，请先搜索。" />
        ) : null}

        {!loading && options.length > 0 ? (
          <div style={{ display: 'grid', gap: 10 }}>
            {options.map((option) => {
              const selected = selectedUserId === option.userId;
              return (
                <button
                  key={option.userId}
                  type="button"
                  onClick={() => setSelectedUserId(option.userId)}
                  style={{
                    display: 'flex',
                    justifyContent: 'space-between',
                    alignItems: 'center',
                    borderRadius: 12,
                    border: selected ? '1px solid #2563eb' : '1px solid #dbe1ea',
                    background: selected ? '#eff6ff' : '#ffffff',
                    padding: '12px 14px',
                    cursor: 'pointer',
                  }}
                >
                  <span>
                    <strong>{option.displayName}</strong>
                    <span style={{ display: 'block', marginTop: 4, color: '#64748b' }}>{option.userId}</span>
                  </span>
                  <StatusBadge variant={selected ? 'success' : 'neutral'} label={selected ? '已选择' : '可绑定'} />
                </button>
              );
            })}
          </div>
        ) : null}
      </div>
    </Modal>
  );
}
