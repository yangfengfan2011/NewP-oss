import { useEffect, useState } from 'react';
import { Button, ErrorState, LoadingState, PageHeader } from '@icp-platform/ui';
import AuthorizationSummaryCard from '../components/AuthorizationSummaryCard';
import PersonEditorForm from '../components/PersonEditorForm';
import PersonUserLinkCard from '../components/PersonUserLinkCard';
import {
  disablePerson,
  getPersonDetail,
  updatePerson,
  type PersonDetailDto,
  type PersonUpsertRequest,
} from '../services/organizationService';

export default function PersonDetailPage() {
  const [detail, setDetail] = useState<PersonDetailDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [disabling, setDisabling] = useState(false);

  useEffect(() => {
    let cancelled = false;
    const personId = getPersonIdFromPath();

    if (!personId) {
      setError('当前地址中没有有效的人员标识。');
      setLoading(false);
      return () => {
        cancelled = true;
      };
    }

    const confirmedPersonId = personId;

    async function load() {
      try {
        setLoading(true);
        setError(null);
        const nextDetail = await getPersonDetail(confirmedPersonId);

        if (!cancelled) {
          setDetail(nextDetail);
        }
      }
      catch (loadError) {
        if (!cancelled) {
          setError(loadError instanceof Error ? loadError.message : '加载人员详情失败');
        }
      }
      finally {
        if (!cancelled) {
          setLoading(false);
        }
      }
    }

    void load();

    return () => {
      cancelled = true;
    };
  }, []);

  async function handleSave(payload: PersonUpsertRequest) {
    if (!detail) {
      throw new Error('当前没有可保存的人员详情。');
    }

    setActionError(null);
    const nextDetail = await updatePerson(detail.id, payload);
    setDetail(nextDetail);
  }

  async function handleDisable() {
    if (!detail) {
      return;
    }

    try {
      setDisabling(true);
      setActionError(null);
      const nextDetail = await disablePerson(detail.id);
      setDetail(nextDetail);
    }
    catch (disableError) {
      setActionError(disableError instanceof Error ? disableError.message : '停用人员失败');
    }
    finally {
      setDisabling(false);
    }
  }

  const headerActions =
    detail && detail.isEnabled ? (
      <Button variant="danger" onClick={() => void handleDisable()} disabled={disabling}>
        {disabling ? '停用中...' : '停用'}
      </Button>
    ) : detail ? (
      <Button disabled>已停用</Button>
    ) : null;

  return (
    <div className="sh-page">
      <PageHeader
        eyebrow="组织结构"
        title="人员详情"
        description="详情页首轮只承接编辑保存、停用和状态化摘要展示，不把绑定账号复杂交互与授权复杂交互拉回主流程。"
        actions={headerActions}
      />

      {loading ? (
        <LoadingState message="正在加载人员详情..." />
      ) : error ? (
        <ErrorState message={error} />
      ) : detail ? (
        <div className="sh-page-stack">
          {actionError ? <ErrorState message={actionError} /> : null}
          <PersonEditorForm person={detail} onSave={handleSave} />
          <PersonUserLinkCard summary={detail.userLinkSummary} />
          <AuthorizationSummaryCard summary={detail.authorizationSummary} />
        </div>
      ) : (
        <ErrorState message="未找到人员详情。" />
      )}
    </div>
  );
}

function getPersonIdFromPath(): string | null {
  const segments = window.location.pathname.split('/').filter(Boolean);
  return segments.length > 0 ? segments[segments.length - 1] : null;
}
