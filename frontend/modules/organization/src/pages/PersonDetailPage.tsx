import { useEffect, useState } from 'react';
import { ErrorState, LoadingState, PageHeader } from '@icp-platform/ui';
import AuthorizationSummaryCard from '../components/AuthorizationSummaryCard';
import PersonEditorForm from '../components/PersonEditorForm';
import PersonUserLinkCard from '../components/PersonUserLinkCard';
import { getPersonDetail, type PersonDetailDto } from '../services/organizationService';

export default function PersonDetailPage() {
  const [detail, setDetail] = useState<PersonDetailDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

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
      try
      {
        setLoading(true);
        setError(null);
        const nextDetail = await getPersonDetail(confirmedPersonId);

        if (!cancelled) {
          setDetail(nextDetail);
        }
      }
      catch (loadError)
      {
        if (!cancelled) {
          setError(loadError instanceof Error ? loadError.message : '加载人员详情失败');
        }
      }
      finally
      {
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

  return (
    <div className="sh-page">
      <PageHeader
        eyebrow="组织结构"
        title="人员详情"
        description="人员详情工作台首版集中展示基础信息、账号绑定状态和授权摘要，不在本页复制其他模块的写操作。"
      />

      {loading ? (
        <LoadingState message="正在加载人员详情..." />
      ) : error ? (
        <ErrorState message={error} />
      ) : detail ? (
        <div className="sh-page-stack">
          <PersonEditorForm person={detail} />
          <PersonUserLinkCard link={detail.userLink} />
          <AuthorizationSummaryCard items={detail.authorizationSummary} />
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
