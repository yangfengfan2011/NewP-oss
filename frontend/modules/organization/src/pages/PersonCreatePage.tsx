import { useMemo } from 'react';
import { useLocalization } from '@icp-platform/core';
import { PageHeader } from '@icp-platform/ui';
import PersonEditorForm from '../components/PersonEditorForm';
import { getPersonDetailRuntimeSchema } from '../runtime/personCenterRuntime';
import { createPerson, type PersonDetailDto, type PersonUpsertRequest } from '../services/organizationService';

function createEmptyPerson(): PersonDetailDto {
  return {
    id: '',
    personNo: '',
    name: '',
    companyId: '',
    departmentId: '',
    positionId: null,
    employmentStatus: 'FullTime',
    employmentStatusLabel: '正式',
    isEnabled: true,
    createdAt: '',
    companyName: '',
    departmentName: '',
    positionName: '',
    userLinkSummary: {
      status: 'notConfigured',
      data: null,
    },
    authorizationSummary: {
      status: 'notConfigured',
      data: null,
    },
  };
}

export default function PersonCreatePage() {
  const { t } = useLocalization();
  const schema = useMemo(() => getPersonDetailRuntimeSchema(), []);
  const emptyPerson = useMemo(() => createEmptyPerson(), []);

  function resolveText(localKey: string, fallback: string): string {
    const resolvedKey = localKey.startsWith(`${schema.dicKey}.`)
      ? localKey
      : `${schema.dicKey}.${localKey}`;
    return t(resolvedKey, fallback);
  }

  async function handleCreate(payload: PersonUpsertRequest) {
    const created = await createPerson(payload);
    // 新建成功后立即切到详情页，避免在同一页继续维护“新建态”和“详情态”两套状态机。
    window.location.assign(`/organization/persons/${created.id}`);
  }

  return (
    <div className="sh-page">
      <PageHeader
        eyebrow="组织结构"
        title="新建人员"
        description="新建页沿用人员详情的字段分组约束，字段 section 仍由 metadata runtime 决定。"
      />

      <PersonEditorForm
        person={emptyPerson}
        onSave={handleCreate}
        sections={schema.sections}
        resolveTitle={resolveText}
        mode="create"
        saveLabel="保存并进入详情"
        hideSummaryCards
      />
    </div>
  );
}
