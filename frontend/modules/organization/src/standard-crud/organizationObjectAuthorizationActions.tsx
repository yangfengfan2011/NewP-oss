import { lazy, Suspense } from 'react';
import message from 'antd/es/message';
import { platformFetch } from '@icp-platform/core';
import type { StandardCrudActionContext, StandardCrudActionInputRenderContext } from '@icp-platform/sdk';

const LazyObjectAuthorizationInput = lazy(async () => ({
  default: (await import('../components/OrganizationObjectAuthorizationInput')).default,
}));

interface ObjectConfiguration { resourceKey: string; objectId: string; subjects: unknown[] }

/** 三个标准资源共用已登记动作的输入、资格与完整保存；目标从当前行固定。 */
export function createOrganizationObjectAuthorizationActions(resourceKey: string, title: string) {
  const base = (id: string) => `/api/authorization/data-scope/objects/${resourceKey}/${encodeURIComponent(id)}`;
  return {
    actionInputRenderers: {
      objectAuthorization: (context: StandardCrudActionInputRenderContext) => {
        const row = context.row as { id?: string; EID?: string; ENAME?: string } | undefined;
        return <Suspense fallback={null}><LazyObjectAuthorizationInput {...context}
          target={{ resourceKey, title, id: row?.id ?? row?.EID, name: row?.ENAME }} /></Suspense>;
      },
    },
    actionEligibilityResolvers: {
      authorize: async ({ id, featurePosition, signal }: { id?: string; featurePosition: string; signal: AbortSignal }) => {
        if (!id) return { visible: false, available: false };
        const response = await platformFetch(base(id) + '/eligibility',
          { headers: { 'X-NewP-Feature': featurePosition }, signal });
        if (response.status === 403 || response.status === 404) return { visible: false, available: false };
        if (!response.ok) throw new Error('对象授权资格读取失败。');
        return { visible: true, available: true };
      },
    },
    actionHandlers: {
      authorize: async ({ id, input, featurePosition }: StandardCrudActionContext) => {
        if (!id || !featurePosition || !input || typeof input !== 'object')
          throw new Error('对象授权尚未加载，无法保存。');
        const configuration = input as ObjectConfiguration;
        if (configuration.resourceKey !== resourceKey || configuration.objectId !== id
          || !Array.isArray(configuration.subjects))
          throw new Error('对象授权目标已变化，请重新打开。');
        const response = await platformFetch(base(id), {
          method: 'PUT', headers: { 'Content-Type': 'application/json', 'X-NewP-Feature': featurePosition },
          body: JSON.stringify(configuration),
        });
        if (!response.ok) throw new Error(response.status === 403 ? '当前没有对象授权资格。'
          : response.status === 404 ? `${title}已不存在或不可读取。` : '保存对象授权失败。');
        void message.success('授权已保存；后续访问按新配置判定。');
      },
    },
  };
}
