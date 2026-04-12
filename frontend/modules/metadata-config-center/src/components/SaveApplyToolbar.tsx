interface SaveApplyToolbarProps {
  canSave: boolean;
  saving: boolean;
  onSave: () => void;
  statusMessage: string | null;
  hasUnsavedChanges: boolean;
  hasUnappliedChanges: boolean;
  gitSyncPending: boolean;
}

function renderStatusLine(
  hasUnsavedChanges: boolean,
  hasUnappliedChanges: boolean,
  gitSyncPending: boolean,
): string {
  if (hasUnsavedChanges) {
    return '存在未保存修改';
  }

  if (hasUnappliedChanges && gitSyncPending) {
    return '保存成功，尚未应用；Git 未同步';
  }

  if (hasUnappliedChanges) {
    return '已保存，尚未应用';
  }

  if (gitSyncPending) {
    return '已保存，Git 未同步';
  }

  return '当前没有待处理变更';
}

export default function SaveApplyToolbar({
  canSave,
  saving,
  onSave,
  statusMessage,
  hasUnsavedChanges,
  hasUnappliedChanges,
  gitSyncPending,
}: SaveApplyToolbarProps) {
  return (
    <section
      style={{
        border: '1px solid #dbe1ea',
        borderRadius: 14,
        background: 'linear-gradient(135deg, #fffdf4 0%, #ffffff 55%, #f4f8ff 100%)',
        padding: 18,
        display: 'flex',
        justifyContent: 'space-between',
        gap: 16,
        alignItems: 'center',
        flexWrap: 'wrap',
      }}
    >
      <div>
        <div style={{ fontSize: 12, letterSpacing: 1, color: '#6b7280', textTransform: 'uppercase' }}>
          Definition Workflow
        </div>
        <div style={{ marginTop: 6, fontSize: 16, fontWeight: 700, color: '#0f172a' }}>
          {statusMessage ?? renderStatusLine(hasUnsavedChanges, hasUnappliedChanges, gitSyncPending)}
        </div>
        <div style={{ marginTop: 8, fontSize: 13, color: '#475569' }}>
          保存仅回写 metadata 文件；应用和 Git 同步保持分离，并在当前阶段保持禁用。
        </div>
      </div>

      <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap' }}>
        <button
          type="button"
          onClick={onSave}
          disabled={!canSave || saving}
          style={{
            border: 'none',
            borderRadius: 10,
            padding: '12px 18px',
            background: canSave && !saving ? '#2563eb' : '#94a3b8',
            color: '#ffffff',
            cursor: canSave && !saving ? 'pointer' : 'not-allowed',
            fontWeight: 700,
          }}
        >
          {saving ? '保存中...' : '保存'}
        </button>
        <button
          type="button"
          disabled
          title="当前阶段只展示入口，不执行真实应用动作。"
          style={{
            border: '1px solid #d1d5db',
            borderRadius: 10,
            padding: '12px 18px',
            background: '#f8fafc',
            color: '#94a3b8',
            cursor: 'not-allowed',
            fontWeight: 700,
          }}
        >
          应用
        </button>
        <button
          type="button"
          disabled
          title="当前阶段只展示入口，不执行真实 Git 同步动作。"
          style={{
            border: '1px solid #d1d5db',
            borderRadius: 10,
            padding: '12px 18px',
            background: '#f8fafc',
            color: '#94a3b8',
            cursor: 'not-allowed',
            fontWeight: 700,
          }}
        >
          Git 同步
        </button>
      </div>
    </section>
  );
}
